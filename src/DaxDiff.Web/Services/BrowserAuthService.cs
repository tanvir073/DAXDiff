using DaxDiff.Connectors;
using DaxDiff.Connectors.Auth;
using DaxDiff.Domain;
using Microsoft.JSInterop;

namespace DaxDiff.Web.Services;

/// <summary>
/// Sign-in for one source through MSAL.js (<c>wwwroot/js/auth.js</c>). Same contract as the desktop
/// <c>MsalAuthService</c>: each source has its own tenant and account (FR-002). Tokens stay in the browser's session
/// storage and are never sent anywhere but Microsoft Entra ID and the Power BI service.
/// </summary>
public sealed class BrowserAuthService : IAuthService
{
    private readonly IJSRuntime _js;
    private readonly AuthOptions _options;
    private Task<IJSObjectReference>? _module;
    private string _tenant;
    private string? _homeAccountId;

    public BrowserAuthService(IJSRuntime js, AuthOptions options)
    {
        _js = js;
        _options = options;
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
            _homeAccountId = null;
            SignedInAccount = null;
            SignedInTenant = null;
        }
    }

    public string? AccountHint { get; set; }
    public string? SignedInAccount { get; private set; }
    public string? SignedInTenant { get; private set; }

    public Task<AuthToken> SignInAsync(CancellationToken cancellationToken) => AcquireAsync("signIn", cancellationToken);

    public Task<AuthToken> GetTokenAsync(CancellationToken cancellationToken) => AcquireAsync("getToken", cancellationToken);

    private async Task<AuthToken> AcquireAsync(string function, CancellationToken cancellationToken)
    {
        if (!IsConfigured)
            throw new DaxDiffException(ErrorCategory.Configuration, "AUTH-NO-CLIENT",
                "No Entra ID client ID is configured, so DAXDiff cannot sign in to the Power BI service.",
                "Enter the client ID of a DAXDiff single-page app registration in Settings (see docs/web.md).");

        try
        {
            var module = await ModuleAsync();
            var token = await module.InvokeAsync<JsToken>(function, cancellationToken, Request());
            _homeAccountId = token.HomeAccountId;
            SignedInAccount = token.Account;
            SignedInTenant = token.TenantId;
            AccountHint = token.Account;
            var expires = DateTimeOffset.TryParse(token.ExpiresOn, out var e) ? e : DateTimeOffset.UtcNow.AddMinutes(5);
            return new AuthToken(token.AccessToken, expires, token.Account, token.TenantId);
        }
        catch (JSException ex)
        {
            throw Classify(ex);
        }
    }

    public async Task SignOutAsync()
    {
        if (IsConfigured && (_homeAccountId is not null || AccountHint is not null))
            await (await ModuleAsync()).InvokeVoidAsync("signOut", Request());
        Forget();
    }

    public async Task SignOutEverywhereAsync()
    {
        if (IsConfigured)
            await (await ModuleAsync()).InvokeVoidAsync("signOutAll", Request());
        Forget();
    }

    private void Forget()
    {
        _homeAccountId = null;
        SignedInAccount = null;
        SignedInTenant = null;
        AccountHint = null;
    }

    private object Request() => new
    {
        clientId = _options.ClientId,
        tenant = _tenant,
        redirectUri = _options.RedirectUri,
        scopes = _options.Scopes,
        homeAccountId = _homeAccountId,
        loginHint = AccountHint,
    };

    private Task<IJSObjectReference> ModuleAsync() =>
        _module ??= _js.InvokeAsync<IJSObjectReference>("import", "./js/auth.js").AsTask();

    /// <summary>MSAL.js error codes appear at the start of the message, e.g. "user_cancelled: …".</summary>
    private static DaxDiffException Classify(JSException ex)
    {
        var message = ex.Message;
        if (message.Contains("user_cancelled", StringComparison.OrdinalIgnoreCase))
            return new DaxDiffException(ErrorCategory.Authentication, "AUTH-CANCELLED", "Sign-in was cancelled.",
                "Sign in again to continue.", inner: ex);
        if (message.Contains("popup_window_error", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("empty_window_error", StringComparison.OrdinalIgnoreCase))
            return new DaxDiffException(ErrorCategory.Authentication, "AUTH-POPUP-BLOCKED", "The sign-in pop-up was blocked.",
                "Allow pop-ups for this site, then sign in again.", inner: ex);
        if (message.Contains("interaction_in_progress", StringComparison.OrdinalIgnoreCase))
            return new DaxDiffException(ErrorCategory.Authentication, "AUTH-BUSY", "Another sign-in is still open.",
                "Finish or close the other sign-in window, then try again.", inner: ex);
        var line = message.Split('\n')[0];
        return line.Contains("AADSTS", StringComparison.OrdinalIgnoreCase)
            ? new DaxDiffException(ErrorCategory.Authentication, "AUTH-ENTRA", $"Sign-in failed: {line}",
                "Check the tenant and client ID in Settings, and that the app registration lists this site's redirect URI " +
                "as a single-page application. Your administrator may need to grant consent.", inner: ex)
            : ErrorClassifier.Classify(ex, "Sign-in");
    }

    private sealed record JsToken(string AccessToken, string? ExpiresOn, string Account, string TenantId, string? HomeAccountId);
}
