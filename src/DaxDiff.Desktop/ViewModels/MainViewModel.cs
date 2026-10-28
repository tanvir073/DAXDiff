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
    public NavItem(string title, string hint, ObservableObject page, bool requiresProject, int? step = null)
    {
        Title = title;
        Hint = hint;
        Page = page;
        RequiresProject = requiresProject;
        Step = step;
        _status = hint;
        _isEnabled = !requiresProject;
    }

    public string Title { get; }
    public string Hint { get; }
    public ObservableObject Page { get; }
    public bool RequiresProject { get; }

    /// <summary>Position in the project workflow (1–5); null for Home and Settings.</summary>
    public int? Step { get; }

    /// <summary>Second line in the menu: the hint, or the step's live state ("1 of 2 connected").</summary>
    [ObservableProperty]
    private string _status;

    [ObservableProperty]
    private bool _isEnabled;

    [ObservableProperty]
    private bool _isSelected;
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
        SourceSessionFactory sessions,
        AuthOptions authOptions,
        ComparisonRunner runner,
        AutoMatcher matcher,
        IDialogService dialogs,
        IFilterEditorService filterEditor,
        RecentProjects recent,
        ILoggerFactory loggerFactory)
    {
        _dialogs = dialogs;
        _recent = recent;
        _logger = loggerFactory.CreateLogger<MainViewModel>();

        // Each source signs in on its own, so the two models can be in different tenants (FR-002).
        var sessionA = sessions.Create();
        var sessionB = sessions.Create();
        SourceA = new SourcePanelViewModel(Side.A, factory, locator, sessionA, dialogs, _logger);
        SourceB = new SourcePanelViewModel(Side.B, factory, locator, sessionB, dialogs, _logger);
        Sources = new SourcesViewModel(this);
        Comparisons = new ComparisonsViewModel(SourceA, SourceB, matcher, dialogs, (definition, isSaved) =>
        {
            var editor = new ComparisonEditorViewModel(definition, isSaved, SourceA, SourceB, new OptionsViewModel(SourceA, SourceB, runner, dialogs));
            editor.Options.EditFiltersRequested = () => filterEditor.Edit(editor);
            return editor;
        });
        Options = new OptionsPageViewModel(Comparisons);
        Run = new RunViewModel(SourceA, SourceB, Comparisons, runner, dialogs);
        Results = new ResultsViewModel(dialogs, loggerFactory.CreateLogger<ResultsViewModel>());
        Settings = new SettingsViewModel(authOptions, sessionA.Auth, dialogs) { ResetSession = () => CloseProject() };
        Home = new HomeViewModel(this, recent, Guid.TryParse(authOptions.ClientId, out _));

        Comparisons.Changed += (_, _) => OnProjectEdited();
        Comparisons.PersistRequested += (_, _) => PersistComparisons();
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
            new NavItem("Sources", "Connect model A and B", Sources, requiresProject: true, step: 1),
            new NavItem("Comparisons", "Fields, keys, synonyms", Comparisons, requiresProject: true, step: 2),
            new NavItem("Options", "Filters and tolerances", Options, requiresProject: true, step: 3),
            new NavItem("Run", "Execute comparisons", Run, requiresProject: true, step: 4),
            new NavItem("Results", "Inspect and export", Results, requiresProject: true, step: 5),
            new NavItem("Settings", "Sign-in, privacy, diagnostics", Settings, requiresProject: false),
        ];
        _currentPage = Home;
        _selectedNav = NavItems[0];
        _selectedNav.IsSelected = true;
        Results.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ResultsViewModel.HasResult))
                UpdateStepStatus();
        };
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
    public NavItem HomeNav => NavItems[0];
    public IEnumerable<NavItem> WorkflowNav => NavItems.Where(n => n.Step is not null);
    public NavItem SettingsNav => NavItems[^1];

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
    [NotifyPropertyChangedFor(nameof(WindowTitle), nameof(SaveStateText))]
    private bool _isDirty;

    public string SaveStateText => IsDirty ? "Unsaved changes" : "All changes saved";

    [ObservableProperty]
    private string _statusMessage = "Create a new project or open an existing one to start.";

    [ObservableProperty]
    private int _errorCount;

    public bool HasProject => ProjectPath is not null;

    public string WindowTitle => HasProject ? $"{ProjectName}{(IsDirty ? " *" : "")} – DAXDiff" : "DAXDiff";

    partial void OnSelectedNavChanged(NavItem? oldValue, NavItem newValue)
    {
        if (oldValue is not null)
            oldValue.IsSelected = false;
        if (newValue is not null)
        {
            newValue.IsSelected = true;
            CurrentPage = newValue.Page;
        }
        if (newValue?.Page == Run)
            Run.Refresh();
    }

    partial void OnProjectPathChanged(string? value)
    {
        foreach (var item in NavItems.Where(n => n.RequiresProject))
            item.IsEnabled = value is not null;
        UpdateStepStatus();
    }

    public void Navigate(ObservableObject page) => SelectedNav = NavItems.First(n => n.Page == page);

    [RelayCommand]
    private void SelectNav(NavItem? item)
    {
        if (item is { IsEnabled: true })
            SelectedNav = item;
    }

    /// <summary>Live second lines for the workflow steps in the menu.</summary>
    private void UpdateStepStatus()
    {
        var connected = (SourceA.State == ConnectionState.Connected ? 1 : 0) + (SourceB.State == ConnectionState.Connected ? 1 : 0);
        NavItems[1].Status = HasProject ? $"{connected} of 2 connected" : NavItems[1].Hint;
        NavItems[2].Status = HasProject
            ? Comparisons.Comparisons.Count switch { 0 => "None yet", 1 => "1 comparison", var n => $"{n} comparisons" }
            : NavItems[2].Hint;
        NavItems[5].Status = Results.HasResult ? $"{Results.Results.Count} result(s)" : NavItems[5].Hint;
    }

    // ---- Project lifecycle ----------------------------------------------------------------------

    public ProjectFile BuildProject() => new()
    {
        Name = ProjectName,
        SourceA = SourceA.BuildReference(),
        SourceB = SourceB.BuildReference(),
        Comparisons = Comparisons.SavedDefinitions(),
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
        if (ProjectPath is null || !ResolveUnsavedComparisons())
            return;
        if (TrySave(ProjectPath))
            StatusMessage = "Project saved (sources and saved comparisons; no credentials, tokens or results).";
    }

    /// <summary>
    /// Comparison drafts are saved with the comparison's own Save button. When the whole project is saved, offer to
    /// save the drafts too. False when the user cancels.
    /// </summary>
    private bool ResolveUnsavedComparisons()
    {
        var unsaved = Comparisons.UnsavedComparisons;
        if (unsaved.Count == 0)
            return true;
        var answer = _dialogs.AskYesNoCancel(
            $"These comparisons have unsaved changes:{Environment.NewLine}  {string.Join(Environment.NewLine + "  ", unsaved.Select(c => c.Name))}" +
            $"{Environment.NewLine}{Environment.NewLine}Save them too? (No keeps their last saved versions in the project.)", "Unsaved comparisons");
        if (answer is null)
            return false;
        if (answer == true)
            Comparisons.CommitAll();
        return true;
    }

    /// <summary>A comparison was saved or deleted: write the project file so the change is kept (FR-021).</summary>
    private void PersistComparisons()
    {
        if (ProjectPath is not null && TrySave(ProjectPath))
            StatusMessage = $"Saved to {Path.GetFileName(ProjectPath)}. Runs use the saved version of each comparison.";
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void SaveProjectAs()
    {
        var path = _dialogs.SaveFile(ProjectFilter, ProjectName + ProjectFile.Extension, "Save project as");
        if (path is null || !ResolveUnsavedComparisons() || !TrySave(path, Path.GetFileNameWithoutExtension(path)))
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
            IsDirty = Comparisons.UnsavedComparisons.Count > 0; // other comparisons may still have drafts
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
        var drafts = Comparisons.UnsavedComparisons;
        var detail = drafts.Count == 0 ? "" : $"{Environment.NewLine}{Environment.NewLine}Includes unsaved changes to: {string.Join(", ", drafts.Select(c => c.Name))}.";
        switch (_dialogs.AskYesNoCancel($"Save changes to the project '{ProjectName}'?{detail}", "Unsaved changes"))
        {
            case true:
                Comparisons.CommitAll();
                return TrySave(ProjectPath!);
            case false:
                return true;
            default:
                return false;
        }
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
        UpdateStepStatus();
    }

    private void OnProjectEdited()
    {
        ErrorCount = Comparisons.Selected?.ErrorCount ?? 0;
        UpdateDirty();
        UpdateStepStatus();
    }

    private void OnSourceChanged()
    {
        UpdateDirty();
        UpdateStepStatus();
    }

    private void UpdateDirty()
    {
        if (HasProject && _savedFingerprint is not null)
            IsDirty = Comparisons.UnsavedComparisons.Count > 0 || ProjectStore.Fingerprint(BuildProject()) != _savedFingerprint;
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
    public IRelayCommand NextCommand { get; } = new RelayCommand(() => main.Navigate(main.Comparisons));
}

/// <summary>Options page: the options of the comparison selected on the Comparisons page (each comparison has its own).</summary>
public sealed class OptionsPageViewModel(ComparisonsViewModel comparisons) : ObservableObject
{
    public ComparisonsViewModel Comparisons => comparisons;
}

/// <summary>A recent project file as shown on Home.</summary>
public sealed record RecentProject(string Path)
{
    public string Name => System.IO.Path.GetFileNameWithoutExtension(Path);
    public string Folder => System.IO.Path.GetDirectoryName(Path) ?? "";
}

/// <summary>Home page: the two ways in, New project and Open project (FR-019), plus recent projects.</summary>
public sealed partial class HomeViewModel : ObservableObject
{
    private readonly MainViewModel _main;

    public HomeViewModel(MainViewModel main, RecentProjects recent, bool clientIdConfigured)
    {
        _main = main;
        ClientIdConfigured = clientIdConfigured;
        Recent = new ObservableCollection<RecentProject>(recent.Load().Select(p => new RecentProject(p)));
    }

    public ObservableCollection<RecentProject> Recent { get; }
    public IRelayCommand NewCommand => _main.NewProjectCommand;
    public IAsyncRelayCommand OpenCommand => _main.OpenProjectCommand;

    /// <summary>Workspace browsing and REST need an Entra client ID (Settings).</summary>
    public bool ClientIdConfigured { get; }

    public void SetRecent(IEnumerable<string> paths)
    {
        Recent.Clear();
        foreach (var path in paths)
            Recent.Add(new RecentProject(path));
    }

    [RelayCommand]
    private Task OpenRecentAsync(string? path) => path is null ? Task.CompletedTask : _main.OpenProjectPathAsync(path);
}
