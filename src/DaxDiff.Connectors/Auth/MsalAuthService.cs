using DaxDiff.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Extensions.Msal;

namespace DaxDiff.Connectors.Auth;

public sealed class AuthOptions
{
    /// <summary>Entra ID public-client application ID. When empty, XMLA falls back to ADOMD's built-in sign-in and REST/discovery are unavailable.</summary>
    public string ClientId { get; set; } = "";

    /// <summary>Tenant ID or domain; "organizations" lets the user pick any work account.</summary>
    public string TenantId { get; set; } = "organizations";

    public string RedirectUri { get; set; } = "http://localhost";
    public string PowerBiApiBase { get; set; } = "https://api.powerbi.com/v1.0/myorg";
    public string XmlaEndpointBase { get; set; } = "powerbi://api.powerbi.com/v1.0/myorg";
    public string[] Scopes { get; set; } = ["https://analysis.windows.net/powerbi/api/.default"];

    /// <summary>Directory for the encrypted MSAL token cache.</summary>
    public string CacheDirectory { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DAXDiff");
}

public sealed record AuthToken(string AccessToken, DateTimeOffset ExpiresOn, string Account);

public interface IAuthService
{
    bool IsConfigured { get; }
    string? SignedInAccount { get; }

    /// <summary>Returns a token silently if possible, otherwise prompts interactively.</summary>
    Task<AuthToken> GetTokenAsync(CancellationToken cancellationToken);

    Task SignOutAsync();
}

/// <summary>
/// Delegated interactive sign-in via the system browser. Tokens live only in the DPAPI-encrypted MSAL cache,
/// never in projects, logs or exports (§9).
/// </summary>
public sealed class MsalAuthService : IAuthService
{
    private readonly AuthOptions _options;
    private readonly ILogger<MsalAuthService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IPublicClientApplication? _app;

    public MsalAuthService(AuthOptions options, ILogger<MsalAuthService> logger)
    {
        _options = options;
        _logger = logger;
    }

    public bool IsConfigured => Guid.TryParse(_options.ClientId, out _);

    public string? SignedInAccount { get; private set; }

    /// <summary>Optional window handle provider so the browser prompt is parented to the app window.</summary>
    public Func<IntPtr>? ParentWindow { get; set; }

    public async Task<AuthToken> GetTokenAsync(CancellationToken cancellationToken)
    {
        if (!IsConfigured)
            throw new DaxDiffException(ErrorCategory.Configuration, "AUTH-NO-CLIENT",
                "No Entra ID client ID is configured.",
                "Set AzureAd:ClientId in appsettings.json (see docs/setup.md). XMLA connections can still use the built-in sign-in.");

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var app = await GetAppAsync();
            var account = (await app.GetAccountsAsync()).FirstOrDefault();
            AuthenticationResult result;
            try
            {
                if (account is null)
                    throw new MsalUiRequiredException("no_account", "No cached account.");
                result = await app.AcquireTokenSilent(_options.Scopes, account).ExecuteAsync(cancellationToken);
            }
            catch (MsalUiRequiredException)
            {
                var builder = app.AcquireTokenInteractive(_options.Scopes).WithUseEmbeddedWebView(false);
                if (ParentWindow?.Invoke() is { } hwnd && hwnd != IntPtr.Zero)
                    builder = builder.WithParentActivityOrWindow(hwnd);
                result = await builder.ExecuteAsync(cancellationToken);
            }

            SignedInAccount = result.Account?.Username;
            _logger.LogInformation("Acquired Power BI token for tenant {TenantId}", result.TenantId);
            return new AuthToken(result.AccessToken, result.ExpiresOn, result.Account?.Username ?? "unknown");
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

    public async Task SignOutAsync()
    {
        if (!IsConfigured)
            return;
        var app = await GetAppAsync();
        foreach (var account in await app.GetAccountsAsync())
            await app.RemoveAsync(account);
        SignedInAccount = null;
    }

    private async Task<IPublicClientApplication> GetAppAsync()
    {
        if (_app is not null)
            return _app;

        var app = PublicClientApplicationBuilder.Create(_options.ClientId)
            .WithAuthority(AzureCloudInstance.AzurePublic, _options.TenantId)
            .WithRedirectUri(_options.RedirectUri)
            .Build();

        Directory.CreateDirectory(_options.CacheDirectory);
        var storage = new StorageCreationPropertiesBuilder("msal.cache", _options.CacheDirectory).Build();
        var cacheHelper = await MsalCacheHelper.CreateAsync(storage);
        cacheHelper.RegisterCache(app.UserTokenCache);

        _app = app;
        return app;
    }
}
