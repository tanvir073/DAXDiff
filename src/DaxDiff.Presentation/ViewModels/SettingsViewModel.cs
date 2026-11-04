using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DaxDiff.Application;
using DaxDiff.Connectors.Auth;
using DaxDiff.Desktop.Resources;
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
        _selectedTheme = ThemeCatalog.Current;
        _selectedLanguage = LanguageService.Languages.First(l => l.Code == LanguageService.Current);
    }

    public IReadOnlyList<AppLanguage> Languages => LanguageService.Languages;

    /// <summary>User-interface language; saved at once, and shown after a restart (offered straight away).</summary>
    [ObservableProperty]
    private AppLanguage _selectedLanguage;

    private string _savedLanguage = LanguageService.Current;

    partial void OnSelectedLanguageChanged(AppLanguage value)
    {
        if (value is null || value.Code == _savedLanguage)
            return;
        try
        {
            LanguageService.Save(value.Code);
            _savedLanguage = value.Code;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _dialogs.ShowInfo(Localized.Format(Strings.Language_SaveFailed, ex.Message));
            return;
        }

        // Back to the language on screen: nothing to restart for.
        if (value.Code != LanguageService.Current && _dialogs.Confirm(Strings.Language_RestartQuestion, Strings.Language_RestartTitle))
            RestartRequested?.Invoke();
    }

    public Action? ResetSession { get; set; }

    /// <summary>Restarts the app (offering to save the project first) and reopens the current project.</summary>
    public Action? RestartRequested { get; set; }

    public IReadOnlyList<AppTheme> Themes => ThemeCatalog.Themes;

    /// <summary>Colour theme; applied immediately and remembered in the user settings.</summary>
    [ObservableProperty]
    private AppTheme _selectedTheme;

    partial void OnSelectedThemeChanged(AppTheme value)
    {
        if (value is null || value == ThemeCatalog.Current)
            return;
        ThemeCatalog.Apply(value.Id);
        try
        {
            ThemeCatalog.Save(value.Id);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _dialogs.ShowInfo(Localized.Format(Strings.Settings_ThemeSaveFailed, ex.Message));
        }
    }

    [ObservableProperty]
    private string _clientId;

    [ObservableProperty]
    private string _tenantId;

    public string Version => ComparisonRunner.AppVersion;
    public string Runtime => System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;
    public string LogFolder => AppPaths.Logs;
    public string SignInStatus => _auth.IsConfigured
        ? Strings.Settings_SignInConfigured
        : Strings.Settings_SignInNotConfigured;

    public string PrivacyText =>
        Strings.Settings_Privacy;

    [RelayCommand]
    private void Save()
    {
        if (!string.IsNullOrWhiteSpace(ClientId) && !Guid.TryParse(ClientId, out _))
        {
            _dialogs.ShowInfo(Strings.Settings_ClientIdInvalid);
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
        _dialogs.ShowInfo(Strings.Settings_SavedRestart);
    }

    [RelayCommand]
    private async Task SignOutAsync()
    {
        await _auth.SignOutEverywhereAsync();
        OnPropertyChanged(nameof(SignInStatus));
        _dialogs.ShowInfo(Strings.Settings_SignedOutAll);
    }

    [RelayCommand]
    private void OpenLogs()
    {
        Directory.CreateDirectory(AppPaths.Logs);
        Process.Start(new ProcessStartInfo(AppPaths.Logs) { UseShellExecute = true });
    }

    [RelayCommand]
    private void OpenUserGuide() => OpenUrl(ProductLinks.UserGuide);

    [RelayCommand]
    private void OpenReleases() => OpenUrl(ProductLinks.Releases);

    /// <summary>Opens the GitHub bug-report form with the version and operating system filled in.</summary>
    [RelayCommand]
    private void ReportBug() => OpenUrl(ProductLinks.NewBugReport("Desktop", ProductLinks.CurrentOs));

    private void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // No default browser: show the address so it can be copied.
            _dialogs.ShowInfo(Localized.Format(Strings.Settings_OpenUrlFailed, url));
        }
    }

    [RelayCommand]
    private void Reset()
    {
        if (_dialogs.Confirm(Strings.Settings_ResetQuestion))
            ResetSession?.Invoke();
    }
}
