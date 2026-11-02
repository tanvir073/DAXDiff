using DaxDiff.Connectors.Service;

namespace DaxDiff.Connectors.Auth;

public sealed class AuthOptions
{
    /// <summary>Entra ID public-client application ID. When empty, XMLA falls back to ADOMD's built-in sign-in and REST/discovery are unavailable.</summary>
    public string ClientId { get; set; } = "";

    /// <summary>Default tenant for new sources: a tenant ID or domain; "organizations" lets the user pick any work account.</summary>
    public string TenantId { get; set; } = "organizations";

    public string RedirectUri { get; set; } = "http://localhost";
    public string PowerBiApiBase { get; set; } = "https://api.powerbi.com/v1.0/myorg";
    public string XmlaEndpointBase { get; set; } = "powerbi://api.powerbi.com/v1.0/myorg";
    public string[] Scopes { get; set; } = ["https://analysis.windows.net/powerbi/api/.default"];

    /// <summary>Directory for the encrypted MSAL token cache.</summary>
    public string CacheDirectory { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DAXDiff");

    /// <summary>True for the tenant values that mean "the user's own tenant" rather than a specific one.</summary>
    public static bool IsHomeTenant(string? tenant) =>
        string.IsNullOrWhiteSpace(tenant) || tenant.Trim().ToLowerInvariant() is "organizations" or "common" or "myorg";
}

public sealed record AuthToken(string AccessToken, DateTimeOffset ExpiresOn, string Account, string TenantId);

/// <summary>
/// Sign-in for one source. Each source has its own instance, tenant and account, so model A and model B can be
/// in the same tenant or in different tenants, signed in as the same or different users (FR-002).
/// </summary>
public interface IAuthService
{
    bool IsConfigured { get; }

    /// <summary>Tenant ID or domain; "organizations" for the user's home tenant. Changing it signs this source out.</summary>
    string Tenant { get; set; }

    /// <summary>User name to prefer when signing in silently (e.g. remembered from the project).</summary>
    string? AccountHint { get; set; }

    string? SignedInAccount { get; }
    string? SignedInTenant { get; }

    /// <summary>Interactive sign-in with an account picker, so a different account can be chosen per source.</summary>
    Task<AuthToken> SignInAsync(CancellationToken cancellationToken);

    /// <summary>Returns a token silently if possible, otherwise prompts interactively.</summary>
    Task<AuthToken> GetTokenAsync(CancellationToken cancellationToken);

    /// <summary>Forgets this source's account (other sources keep theirs).</summary>
    Task SignOutAsync();

    /// <summary>Removes every account from the shared token cache.</summary>
    Task SignOutEverywhereAsync();
}

/// <summary>Everything a source needs to reach the Power BI service as its own signed-in user.</summary>
public sealed record SourceSession(IAuthService Auth, PowerBiRestClient Rest, PowerBiDiscoveryService Discovery);
