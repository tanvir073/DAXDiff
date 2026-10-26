using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DaxDiff.Application;
using DaxDiff.Comparison;
using DaxDiff.Connectors;
using DaxDiff.Connectors.Auth;
using DaxDiff.Connectors.Desktop;
using DaxDiff.Connectors.Service;
using DaxDiff.Desktop.Services;
using DaxDiff.Domain;
using Microsoft.Extensions.Logging;

namespace DaxDiff.Desktop.ViewModels;

/// <summary>A step in the left-hand menu. Steps that edit a project are disabled until one is open.</summary>
public sealed partial class NavItem : ObservableObject
{
    public NavItem(string title, string hint, ObservableObject page, bool requiresProject)
    {
        Title = title;
        Hint = hint;
        Page = page;
        RequiresProject = requiresProject;
        _isEnabled = !requiresProject;
    }

    public string Title { get; }
    public string Hint { get; }
    public ObservableObject Page { get; }
    public bool RequiresProject { get; }

    [ObservableProperty]
    private bool _isEnabled;
}

/// <summary>
/// Shell: navigation and the project lifecycle. Everything the user sets up (sources and all comparisons)
/// lives in one project file (FR-014, FR-019); results stay in the session.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    private const string ProjectFilter = "DAXDiff project (*.pbicomp)|*.pbicomp";

    private readonly IDialogService _dialogs;
    private readonly RecentProjects _recent;
    private readonly ILogger<MainViewModel> _logger;
    private string? _savedFingerprint;
    private LaunchArguments? _pendingAttach;

    public MainViewModel(
        IConnectorFactory factory,
        DesktopSessionLocator locator,
        PowerBiDiscoveryService discovery,
        IAuthService auth,
        AuthOptions authOptions,
        ComparisonRunner runner,
        AutoMatcher matcher,
        IDialogService dialogs,
        RecentProjects recent,
        ILoggerFactory loggerFactory)
    {
        _dialogs = dialogs;
        _recent = recent;
        _logger = loggerFactory.CreateLogger<MainViewModel>();

        SourceA = new SourcePanelViewModel(Side.A, factory, locator, discovery, auth, dialogs, _logger);
        SourceB = new SourcePanelViewModel(Side.B, factory, locator, discovery, auth, dialogs, _logger);
        Sources = new SourcesViewModel(this);
        Comparisons = new ComparisonsViewModel(SourceA, SourceB, matcher, dialogs,
            definition => new ComparisonEditorViewModel(definition, SourceA, SourceB, new OptionsViewModel(SourceA, SourceB, runner, dialogs)));
        Options = new OptionsPageViewModel(Comparisons);
        Run = new RunViewModel(SourceA, SourceB, Comparisons, runner, dialogs);
        Results = new ResultsViewModel(dialogs, loggerFactory.CreateLogger<ResultsViewModel>());
        Settings = new SettingsViewModel(authOptions, auth, dialogs) { ResetSession = () => CloseProject() };
        Home = new HomeViewModel(this, recent);

        Comparisons.Changed += (_, _) => OnProjectEdited();
        Comparisons.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ComparisonsViewModel.Selected))
                OnProjectEdited();
        };
        SourceA.PropertyChanged += (_, _) => OnSourceChanged();
        SourceB.PropertyChanged += (_, _) => OnSourceChanged();
        Run.Completed += (_, result) => Results.Show(result);
        Run.Finished += (_, _) =>
        {
            Navigate(Results);
            StatusMessage = $"Run finished: {string.Join(", ", Results.Results.Select(r => $"{r.ComparisonName} {r.Status}"))}.";
        };

        NavItems =
        [
            new NavItem("Home", "New or open a project", Home, requiresProject: false),
            new NavItem("Sources", "Choose and connect models A and B", Sources, requiresProject: true),
            new NavItem("Comparisons", "Fields, keys, aggregations, synonyms", Comparisons, requiresProject: true),
            new NavItem("Options", "Filters, tolerances, rules, query preview", Options, requiresProject: true),
            new NavItem("Run", "Execute comparisons", Run, requiresProject: true),
            new NavItem("Results", "Inspect differences and export", Results, requiresProject: true),
            new NavItem("Settings", "Sign-in, privacy, diagnostics", Settings, requiresProject: false),
        ];
        _currentPage = Home;
        _selectedNav = NavItems[0];
    }

    public SourcePanelViewModel SourceA { get; }
    public SourcePanelViewModel SourceB { get; }
    public SourcesViewModel Sources { get; }
    public ComparisonsViewModel Comparisons { get; }
    public OptionsPageViewModel Options { get; }
    public RunViewModel Run { get; }
    public ResultsViewModel Results { get; }
    public SettingsViewModel Settings { get; }
    public HomeViewModel Home { get; }
    public IReadOnlyList<NavItem> NavItems { get; }

    [ObservableProperty]
    private ObservableObject _currentPage;

    [ObservableProperty]
    private NavItem _selectedNav;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowTitle), nameof(HasProject))]
    [NotifyCanExecuteChangedFor(nameof(SaveProjectCommand), nameof(SaveProjectAsCommand), nameof(CloseProjectCommand))]
    private string? _projectPath;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowTitle))]
    private string _projectName = "No project";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowTitle))]
    private bool _isDirty;

    [ObservableProperty]
    private string _statusMessage = "Create a new project or open an existing one to start.";

    [ObservableProperty]
    private int _errorCount;

    public bool HasProject => ProjectPath is not null;

    public string WindowTitle => HasProject ? $"{ProjectName}{(IsDirty ? " *" : "")} – DAXDiff" : "DAXDiff";

    partial void OnSelectedNavChanged(NavItem value)
    {
        if (value is not null)
            CurrentPage = value.Page;
        if (value?.Page == Run)
            Run.Refresh();
    }

    partial void OnProjectPathChanged(string? value)
    {
        foreach (var item in NavItems.Where(n => n.RequiresProject))
            item.IsEnabled = value is not null;
    }

    public void Navigate(ObservableObject page) => SelectedNav = NavItems.First(n => n.Page == page);

    // ---- Project lifecycle ----------------------------------------------------------------------

    public ProjectFile BuildProject() => new()
    {
        Name = ProjectName,
        SourceA = SourceA.BuildReference(),
        SourceB = SourceB.BuildReference(),
        Comparisons = Comparisons.ToDefinitions(),
        Ui = new ProjectUiPreferences { ExportAllData = Results.ExportAllData },
    };

    private bool CanSave() => HasProject;

    /// <summary>Creates a project file first, so everything from then on is saved to it (FR-019).</summary>
    [RelayCommand]
    public void NewProject()
    {
        if (!ConfirmDiscardChanges())
            return;
        var path = _dialogs.SaveFile(ProjectFilter, "New project" + ProjectFile.Extension, "Create a new project");
        if (path is null)
            return;

        ResetState();
        ProjectName = Path.GetFileNameWithoutExtension(path);
        Comparisons.Load([new ComparisonDefinition { Name = "Comparison 1" }]);
        if (!TrySave(path))
        {
            ResetState();
            return;
        }

        ProjectPath = path;
        Home.SetRecent(_recent.Add(path));
        ApplyPendingAttach();
        Navigate(Sources);
        StatusMessage = $"Project created at {path}. Choose the two models, then define comparisons.";
    }

    [RelayCommand]
    private async Task OpenProjectAsync()
    {
        if (!ConfirmDiscardChanges())
            return;
        var path = _dialogs.OpenFile(ProjectFilter, "Open project");
        if (path is not null)
            await LoadProjectAsync(path);
    }

    public async Task OpenProjectPathAsync(string path)
    {
        if (ConfirmDiscardChanges())
            await LoadProjectAsync(path);
    }

    private async Task LoadProjectAsync(string path)
    {
        ProjectFile project;
        try
        {
            project = await ProjectStore.LoadAsync(path);
        }
        catch (DaxDiffException ex)
        {
            _dialogs.ShowError(ex);
            return;
        }
        catch (IOException ex)
        {
            _dialogs.ShowError(new DaxDiffException(ErrorCategory.Configuration, "PRJ-IO", "The project file could not be read.", "Check the file path and permissions.", inner: ex));
            return;
        }

        ResetState();
        ProjectName = Path.GetFileNameWithoutExtension(path);
        SourceA.Apply(project.SourceA);
        SourceB.Apply(project.SourceB);
        Comparisons.Load(project.Comparisons);
        Results.ExportAllData = project.Ui.ExportAllData;
        ProjectPath = path;
        _savedFingerprint = ProjectStore.Fingerprint(BuildProject());
        IsDirty = false;
        Home.SetRecent(_recent.Add(path));
        ApplyPendingAttach();
        Navigate(Sources);
        StatusMessage = project.SchemaVersion < ProjectFile.CurrentSchemaVersion
            ? "Project upgraded from an older format; save it to keep the new format. Reconnect both models to validate the comparisons."
            : "Project opened. Reconnect both models; comparisons are validated against fresh metadata.";
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void SaveProject()
    {
        if (ProjectPath is not null && TrySave(ProjectPath))
            StatusMessage = "Project saved (sources, comparisons and settings; no credentials, tokens or results).";
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void SaveProjectAs()
    {
        var path = _dialogs.SaveFile(ProjectFilter, ProjectName + ProjectFile.Extension, "Save project as");
        if (path is null || !TrySave(path, Path.GetFileNameWithoutExtension(path)))
            return;
        ProjectPath = path;
        ProjectName = Path.GetFileNameWithoutExtension(path);
        Home.SetRecent(_recent.Add(path));
        StatusMessage = $"Project saved as {path}.";
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void CloseProject()
    {
        if (!ConfirmDiscardChanges())
            return;
        ResetState();
        Navigate(Home);
        StatusMessage = "Project closed.";
    }

    /// <summary>Called when the window closes: offers to save unsaved changes. False cancels the close.</summary>
    public bool ConfirmClose() => ConfirmDiscardChanges();

    private bool TrySave(string path, string? name = null)
    {
        var project = BuildProject() with { Name = name ?? ProjectName };
        try
        {
            ProjectStore.Save(project, path);
            _savedFingerprint = ProjectStore.Fingerprint(project);
            IsDirty = false;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _dialogs.ShowError(new DaxDiffException(ErrorCategory.Configuration, "PRJ-IO", "The project could not be saved.", "Choose a folder you can write to.", inner: ex));
            return false;
        }
    }

    /// <summary>True when it is fine to drop the current project: nothing unsaved, saved now, or discarded.</summary>
    private bool ConfirmDiscardChanges()
    {
        if (!HasProject || !IsDirty)
            return true;
        return _dialogs.AskYesNoCancel($"Save changes to the project '{ProjectName}'?", "Unsaved changes") switch
        {
            true => TrySave(ProjectPath!),
            false => true,
            null => false,
        };
    }

    private void ResetState()
    {
        ProjectPath = null;
        ProjectName = "No project";
        SourceA.Apply(null);
        SourceB.Apply(null);
        Comparisons.Load([]);
        Results.Clear();
        _savedFingerprint = null;
        IsDirty = false;
    }

    private void OnProjectEdited()
    {
        ErrorCount = Comparisons.Selected?.ErrorCount ?? 0;
        UpdateDirty();
    }

    private void OnSourceChanged() => UpdateDirty();

    private void UpdateDirty()
    {
        if (HasProject && _savedFingerprint is not null)
            IsDirty = ProjectStore.Fingerprint(BuildProject()) != _savedFingerprint;
    }

    [RelayCommand]
    private void SwapSources()
    {
        var a = SourceA.BuildReference();
        var b = SourceB.BuildReference();
        SourceA.Apply(b);
        SourceB.Apply(a);
        Comparisons.SwapSides();
        StatusMessage = "Sides swapped, including every comparison's fields and filters. Test both connections again.";
    }

    // ---- External Tools launch ------------------------------------------------------------------

    public void ApplyLaunchArguments(LaunchArguments args)
    {
        if (args.ProjectPath is not null)
        {
            _pendingAttach = args.Server is null ? null : args;
            _ = OpenProjectPathAsync(args.ProjectPath);
        }
        else if (args.Server is not null)
        {
            _pendingAttach = args;
            StatusMessage = $"Launched from Power BI Desktop ({args.Server}). Create or open a project; model A will be attached to that session.";
        }
    }

    private void ApplyPendingAttach()
    {
        if (_pendingAttach is not { Server: { } server } args)
            return;
        _pendingAttach = null;
        SourceA.AttachExternal(server, args.Database);
    }
}

/// <summary>Sources page: two cards plus swap (FR-001).</summary>
public sealed class SourcesViewModel(MainViewModel main) : ObservableObject
{
    public SourcePanelViewModel SourceA => main.SourceA;
    public SourcePanelViewModel SourceB => main.SourceB;
    public IRelayCommand SwapCommand => main.SwapSourcesCommand;
}

/// <summary>Options page: the options of the comparison selected on the Comparisons page (each comparison has its own).</summary>
public sealed class OptionsPageViewModel(ComparisonsViewModel comparisons) : ObservableObject
{
    public ComparisonsViewModel Comparisons => comparisons;
}

/// <summary>Home page: the two ways in, New project and Open project (FR-019), plus recent projects.</summary>
public sealed partial class HomeViewModel : ObservableObject
{
    private readonly MainViewModel _main;

    public HomeViewModel(MainViewModel main, RecentProjects recent)
    {
        _main = main;
        Recent = new ObservableCollection<string>(recent.Load());
    }

    public ObservableCollection<string> Recent { get; }
    public IRelayCommand NewCommand => _main.NewProjectCommand;
    public IAsyncRelayCommand OpenCommand => _main.OpenProjectCommand;

    public void SetRecent(IEnumerable<string> paths)
    {
        Recent.Clear();
        foreach (var path in paths)
            Recent.Add(path);
    }

    [RelayCommand]
    private Task OpenRecentAsync(string? path) => path is null ? Task.CompletedTask : _main.OpenProjectPathAsync(path);
}
