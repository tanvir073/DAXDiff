using DaxDiff.Connectors.Auth;
using Microsoft.Extensions.Configuration;

namespace DaxDiff.Web.Services;

/// <summary>
/// Sign-in settings: defaults from <c>wwwroot/appsettings.json</c> (set by whoever hosts the site), overridden per
/// browser in Settings (stored in localStorage). The redirect URI is always this site's <c>redirect.html</c>.
/// </summary>
public sealed class WebSettings
{
    private const string ClientIdKey = "daxdiff.clientId";
    private const string TenantKey = "daxdiff.tenant";

    private readonly BrowserFiles _files;
    private readonly string _defaultClientId;
    private readonly string _defaultTenant;

    public WebSettings(IConfiguration configuration, BrowserFiles files, string baseAddress)
    {
        _files = files;
        _defaultClientId = configuration["Auth:ClientId"] ?? "";
        _defaultTenant = configuration["Auth:TenantId"] is { Length: > 0 } t ? t : "organizations";
        Auth = new AuthOptions
        {
            ClientId = _defaultClientId,
            TenantId = _defaultTenant,
            RedirectUri = new Uri(new Uri(baseAddress), "redirect.html").ToString(),
        };
    }

    /// <summary>Shared by every source's sign-in; edited in place so open sources see changes.</summary>
    public AuthOptions Auth { get; }

    public bool HostProvidesClientId => Guid.TryParse(_defaultClientId, out _);

    public async Task LoadAsync()
    {
        if (await _files.GetSettingAsync(ClientIdKey) is { Length: > 0 } clientId)
            Auth.ClientId = clientId;
        if (await _files.GetSettingAsync(TenantKey) is { Length: > 0 } tenant)
            Auth.TenantId = tenant;
    }

    public async Task SaveAsync(string clientId, string tenant)
    {
        Auth.ClientId = clientId.Trim();
        Auth.TenantId = string.IsNullOrWhiteSpace(tenant) ? "organizations" : tenant.Trim();
        await _files.SetSettingAsync(ClientIdKey, Auth.ClientId == _defaultClientId ? null : Auth.ClientId);
        await _files.SetSettingAsync(TenantKey, Auth.TenantId == _defaultTenant ? null : Auth.TenantId);
    }
}
