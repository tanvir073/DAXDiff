using DaxDiff.Connectors.Service;
using DaxDiff.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Extensions.Msal;

namespace DaxDiff.Connectors.Auth;

/// <summary>
/// Delegated interactive sign-in via the system browser. Tokens live only in the DPAPI-encrypted MSAL cache,
/// shared by all sources, and never in projects, logs or exports (§9).
/// </summary>
public sealed class MsalAuthService : IAuthService
{
    private static readonly SemaphoreSlim CacheGate = new(1, 1);
    private static MsalCacheHelper? _cacheHelper;

    private readonly AuthOptions _options;
    private readonly ILogger<MsalAuthService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IPublicClientApplication? _app;
    private string _tenant;
    private string? _accountId;

    public MsalAuthService(AuthOptions options, ILogger<MsalAuthService> logger)
    {
        _options = options;
        _logger = logger;
        _tenant = string.IsNullOrWhiteSpace(options.TenantId) ? "organizations" : options.TenantId.Trim();
    }

    public bool IsConfigured => Guid.TryParse(_options.ClientId, out _);

    public string Tenant
    {
        get => _tenant;
        set
        {
            var tenant = string.IsNullOrWhiteSpace(value) ? "organizations" : value.Trim();
            if (string.Equals(tenant, _tenant, StringComparison.OrdinalIgnoreCase))
                return;
            _tenant = tenant;
            _app = null;
            _accountId = null;
            SignedInAccount = null;
            SignedInTenant = null;
        }
    }

    public string? AccountHint { get; set; }
    public string? SignedInAccount { get; private set; }
    public string? SignedInTenant { get; private set; }

    /// <summary>Optional window handle provider so the browser prompt is parented to the app window.</summary>
    public Func<IntPtr>? ParentWindow { get; set; }

    public Task<AuthToken> SignInAsync(CancellationToken cancellationToken) => AcquireAsync(forcePicker: true, cancellationToken);

    public Task<AuthToken> GetTokenAsync(CancellationToken cancellationToken) => AcquireAsync(forcePicker: false, cancellationToken);

    private async Task<AuthToken> AcquireAsync(bool forcePicker, CancellationToken cancellationToken)
    {
        if (!IsConfigured)
            throw new DaxDiffException(ErrorCategory.Configuration, "AUTH-NO-CLIENT",
                "No Entra ID client ID is configured, so DAXDiff cannot sign in to the Power BI service itself.",
                "Ask your administrator for the client ID of a DAXDiff app registration and enter it in Settings (see docs/setup.md). " +
                "XMLA connections still work without it: type the workspace and model names and Test; Microsoft's sign-in appears then.");

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var app = await GetAppAsync();
            AuthenticationResult result;
            var account = forcePicker ? null : await FindAccountAsync(app);
            try
            {
                if (account is null)
                    throw new MsalUiRequiredException("no_account", "No cached account for this source.");
                result = await app.AcquireTokenSilent(_options.Scopes, account).ExecuteAsync(cancellationToken);
            }
            catch (MsalUiRequiredException)
            {
                var builder = app.AcquireTokenInteractive(_options.Scopes).WithUseEmbeddedWebView(false);
                if (forcePicker)
                    builder = builder.WithPrompt(Prompt.SelectAccount);
                else if (!string.IsNullOrWhiteSpace(AccountHint))
                    builder = builder.WithLoginHint(AccountHint);
                if (ParentWindow?.Invoke() is { } hwnd && hwnd != IntPtr.Zero)
                    builder = builder.WithParentActivityOrWindow(hwnd);
                result = await builder.ExecuteAsync(cancellationToken);
            }

            _accountId = result.Account?.HomeAccountId?.Identifier;
            SignedInAccount = result.Account?.Username;
            SignedInTenant = result.TenantId;
            AccountHint = SignedInAccount ?? AccountHint;
            _logger.LogInformation("Acquired Power BI token for tenant {TenantId}", result.TenantId);
            return new AuthToken(result.AccessToken, result.ExpiresOn, result.Account?.Username ?? "unknown", result.TenantId);
        }
        catch (MsalException ex)
        {
            throw ex.ErrorCode == MsalError.AuthenticationCanceledError
                ? new DaxDiffException(ErrorCategory.Authentication, "AUTH-CANCELLED", "Sign-in was cancelled.",
                    "Sign in again to continue.", inner: ex)
                : new DaxDiffException(ErrorCategory.Authentication, "AUTH-" + ex.ErrorCode.ToUpperInvariant(),
                    $"Sign-in failed: {ex.Message}",
                    "Check the tenant and client ID in Settings, then sign in again. Your administrator may need to grant consent.", inner: ex);
        }
        catch (Exception ex) when (ex is not DaxDiffException and not OperationCanceledException)
        {
            throw ErrorClassifier.Classify(ex, "Sign-in");
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>This source's account: the one it signed in with, else the remembered user name. Never another source's account.</summary>
    private async Task<IAccount?> FindAccountAsync(IPublicClientApplication app)
    {
        var accounts = (await app.GetAccountsAsync()).ToList();
        if (_accountId is not null)
            return accounts.FirstOrDefault(a => a.HomeAccountId?.Identifier == _accountId);
        if (!string.IsNullOrWhiteSpace(AccountHint))
            return accounts.FirstOrDefault(a => string.Equals(a.Username, AccountHint, StringComparison.OrdinalIgnoreCase));
        return null;
    }

    public async Task SignOutAsync()
    {
        if (IsConfigured && _accountId is not null)
        {
            var app = await GetAppAsync();
            if ((await app.GetAccountsAsync()).FirstOrDefault(a => a.HomeAccountId?.Identifier == _accountId) is { } account)
                await app.RemoveAsync(account);
        }

        _accountId = null;
        SignedInAccount = null;
        SignedInTenant = null;
        AccountHint = null;
    }

    public async Task SignOutEverywhereAsync()
    {
        if (IsConfigured)
        {
            var app = await GetAppAsync();
            foreach (var account in await app.GetAccountsAsync())
                await app.RemoveAsync(account);
        }

        _accountId = null;
        SignedInAccount = null;
        SignedInTenant = null;
        AccountHint = null;
    }

    private async Task<IPublicClientApplication> GetAppAsync()
    {
        if (_app is not null)
            return _app;

        var app = PublicClientApplicationBuilder.Create(_options.ClientId)
            .WithAuthority(AzureCloudInstance.AzurePublic, _tenant)
            .WithRedirectUri(_options.RedirectUri)
            .Build();
        (await GetCacheHelperAsync()).RegisterCache(app.UserTokenCache);
        _app = app;
        return app;
    }

    /// <summary>One cache helper for the whole process; every source's client registers with the same encrypted cache file.</summary>
    private async Task<MsalCacheHelper> GetCacheHelperAsync()
    {
        await CacheGate.WaitAsync();
        try
        {
            if (_cacheHelper is null)
            {
                Directory.CreateDirectory(_options.CacheDirectory);
                var storage = new StorageCreationPropertiesBuilder("msal.cache", _options.CacheDirectory).Build();
                _cacheHelper = await MsalCacheHelper.CreateAsync(storage);
            }

            return _cacheHelper;
        }
        finally
        {
            CacheGate.Release();
        }
    }
}

/// <summary>Creates an independent sign-in (tenant, account, REST client) per source.</summary>
public sealed class SourceSessionFactory
{
    private readonly HttpClient _http;
    private readonly AuthOptions _options;
    private readonly ILoggerFactory _loggerFactory;

    public SourceSessionFactory(HttpClient http, AuthOptions options, ILoggerFactory loggerFactory)
    {
        _http = http;
        _options = options;
        _loggerFactory = loggerFactory;
    }

    /// <summary>Set by the app so sign-in prompts are parented to its window.</summary>
    public Func<IntPtr>? ParentWindow { get; set; }

    public SourceSession Create()
    {
        var auth = new MsalAuthService(_options, _loggerFactory.CreateLogger<MsalAuthService>()) { ParentWindow = () => ParentWindow?.Invoke() ?? IntPtr.Zero };
        var rest = new PowerBiRestClient(_http, auth, _options, _loggerFactory.CreateLogger<Service.PowerBiRestClient>());
        return new SourceSession(auth, rest, new PowerBiDiscoveryService(rest));
    }
}
