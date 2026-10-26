using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DaxDiff.Application;
using DaxDiff.Connectors.Auth;
using DaxDiff.Desktop.Services;

namespace DaxDiff.Desktop.ViewModels;

/// <summary>Privacy, sign-in configuration, diagnostics location, version and session reset.</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly IAuthService _auth;
    private readonly IDialogService _dialogs;

    public SettingsViewModel(AuthOptions options, IAuthService auth, IDialogService dialogs)
    {
        _auth = auth;
        _dialogs = dialogs;
        _clientId = options.ClientId;
        _tenantId = options.TenantId;
    }

    public Action? ResetSession { get; set; }

    [ObservableProperty]
    private string _clientId;

    [ObservableProperty]
    private string _tenantId;

    public string Version => ComparisonRunner.AppVersion;
    public string Runtime => System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;
    public string LogFolder => AppPaths.Logs;
    public string SignInStatus => _auth.IsConfigured
        ? "Configured. Each source signs in separately on the Sources page (own tenant and account)."
        : "No client ID: XMLA uses the built-in sign-in; workspace browsing and REST are unavailable.";

    public string PrivacyText =>
        "DAXDiff runs locally and sends no telemetry. Queries are read-only and run with your own identity. " +
        "Sign-in tokens are kept only in your Windows-encrypted token cache and never written to projects, logs or exports. " +
        "Logs contain no measure values, key values or DAX text; queries are identified by SHA-256 hash.";

    [RelayCommand]
    private void Save()
    {
        if (!string.IsNullOrWhiteSpace(ClientId) && !Guid.TryParse(ClientId, out _))
        {
            _dialogs.ShowInfo("The client ID must be a GUID (Application ID of an Entra public-client app registration).");
            return;
        }

        JsonObject root;
        try
        {
            root = File.Exists(AppPaths.UserSettings) ? JsonNode.Parse(File.ReadAllText(AppPaths.UserSettings)) as JsonObject ?? [] : [];
        }
        catch (JsonException)
        {
            root = [];
        }

        root["AzureAd"] = new JsonObject { ["ClientId"] = ClientId.Trim(), ["TenantId"] = string.IsNullOrWhiteSpace(TenantId) ? "organizations" : TenantId.Trim() };
        Directory.CreateDirectory(AppPaths.Root);
        File.WriteAllText(AppPaths.UserSettings, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        _dialogs.ShowInfo("Saved. Restart DAXDiff for the sign-in settings to take effect.");
    }

    [RelayCommand]
    private async Task SignOutAsync()
    {
        await _auth.SignOutEverywhereAsync();
        OnPropertyChanged(nameof(SignInStatus));
        _dialogs.ShowInfo("Signed out of every account and cleared the token cache. Sign in again on the Sources page.");
    }

    [RelayCommand]
    private void OpenLogs()
    {
        Directory.CreateDirectory(AppPaths.Logs);
        Process.Start(new ProcessStartInfo(AppPaths.Logs) { UseShellExecute = true });
    }

    [RelayCommand]
    private void Reset()
    {
        if (_dialogs.Confirm("Discard the current comparison (sources, mappings, options and results)?"))
            ResetSession?.Invoke();
    }
}
