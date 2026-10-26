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

public sealed record NavItem(string Title, string Hint, ObservableObject Page);

/// <summary>Shell: navigation, project lifecycle and the glue that assembles a configuration.</summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly IDialogService _dialogs;
    private readonly RecentProjects _recent;
    private readonly ILogger<MainViewModel> _logger;

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
        Mapping = new MappingViewModel(SourceA, SourceB, matcher, dialogs);
        Options = new OptionsViewModel(SourceA, SourceB, runner, dialogs) { ConfigProvider = BuildConfig };
        Run = new RunViewModel(SourceA, SourceB, runner, dialogs) { ConfigProvider = BuildConfig };
        Results = new ResultsViewModel(dialogs, loggerFactory.CreateLogger<ResultsViewModel>());
        Settings = new SettingsViewModel(authOptions, auth, dialogs) { ResetSession = NewComparison };
        Home = new HomeViewModel(this);

        Mapping.Changed += (_, _) => Revalidate();
        Options.PropertyChanged += (_, _) => Revalidate();
        Options.Filters.CollectionChanged += (_, _) => Revalidate();
        SourceA.MetadataChanged += (_, _) => Revalidate();
        SourceB.MetadataChanged += (_, _) => Revalidate();
        Run.Completed += (_, result) =>
        {
            Results.Result = result;
            Navigate(Results);
            StatusMessage = $"Run {result.Status}: {result.Summary.Mismatches:N0} mismatches, {result.Summary.OnlyInA:N0} only in A, {result.Summary.OnlyInB:N0} only in B.";
        };

        NavItems =
        [
            new NavItem("Home", "Projects and prerequisites", Home),
            new NavItem("Sources", "Choose and connect models A and B", Sources),
            new NavItem("Field mapping", "Pair dimensions and measures", Mapping),
            new NavItem("Options", "Filters, tolerances, rules, query preview", Options),
            new NavItem("Run", "Execute the comparison", Run),
            new NavItem("Results", "Inspect differences and export", Results),
            new NavItem("Settings", "Sign-in, privacy, diagnostics", Settings),
        ];
        _currentPage = Home;
        _selectedNav = NavItems[0];
    }

    public SourcePanelViewModel SourceA { get; }
    public SourcePanelViewModel SourceB { get; }
    public SourcesViewModel Sources { get; }
    public MappingViewModel Mapping { get; }
    public OptionsViewModel Options { get; }
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
    [NotifyPropertyChangedFor(nameof(WindowTitle))]
    private string? _projectPath;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowTitle))]
    private string _projectName = "Untitled comparison";

    [ObservableProperty]
    private string _statusMessage = "Ready.";

    [ObservableProperty]
    private int _errorCount;

    public string WindowTitle => $"{ProjectName}{(ProjectPath is null ? "" : $" ({Path.GetFileName(ProjectPath)})")} – DAXDiff";

    partial void OnSelectedNavChanged(NavItem value)
    {
        if (value is not null)
            CurrentPage = value.Page;
    }

    public void Navigate(ObservableObject page) => SelectedNav = NavItems.First(n => n.Page == page);

    public ComparisonConfig BuildConfig() => Options.ApplyTo(new ComparisonConfig { FieldPairs = Mapping.ToPairs() });

    private void Revalidate()
    {
        var issues = MappingValidator.Validate(BuildConfig(), SourceA.Metadata, SourceB.Metadata);
        Mapping.ApplyIssues(issues);
        Run.Refresh(issues);
        ErrorCount = issues.Count(i => i.Severity == IssueSeverity.Error);
    }

    [RelayCommand]
    public void NewComparison()
    {
        SourceA.Apply(null);
        SourceB.Apply(null);
        Mapping.Load([]);
        Options.Load(new ComparisonConfig());
        Results.Result = null;
        ProjectPath = null;
        ProjectName = "Untitled comparison";
        Navigate(Sources);
        StatusMessage = "New comparison. Start by choosing the two models.";
    }

    [RelayCommand]
    private async Task OpenProjectAsync()
    {
        var path = _dialogs.OpenFile($"DAXDiff project (*{ProjectFile.Extension})|*{ProjectFile.Extension}", "Open comparison project");
        if (path is not null)
            await OpenProjectPathAsync(path);
    }

    public async Task OpenProjectPathAsync(string path)
    {
        try
        {
            var project = await ProjectStore.LoadAsync(path);
            SourceA.Apply(project.SourceA);
            SourceB.Apply(project.SourceB);
            Mapping.Load(project.Config.FieldPairs);
            Options.Load(project.Config);
            Results.Result = null;
            Results.ExportAllData = project.Ui.ExportAllData;
            ProjectPath = path;
            ProjectName = project.Name;
            Home.SetRecent(_recent.Add(path));
            Navigate(Sources);
            StatusMessage = "Project loaded. Reconnect both models; mappings are validated against fresh metadata.";
        }
        catch (DaxDiffException ex)
        {
            _dialogs.ShowError(ex);
        }
        catch (IOException ex)
        {
            _dialogs.ShowError(new DaxDiffException(ErrorCategory.Configuration, "PRJ-IO", "The project file could not be read.", "Check the file path and permissions.", inner: ex));
        }
    }

    [RelayCommand]
    private Task SaveProjectAsync() => SaveAsync(ProjectPath);

    [RelayCommand]
    private Task SaveProjectAsAsync() => SaveAsync(null);

    private async Task SaveAsync(string? path)
    {
        path ??= _dialogs.SaveFile($"DAXDiff project (*{ProjectFile.Extension})|*{ProjectFile.Extension}", ProjectName + ProjectFile.Extension, "Save comparison project");
        if (path is null)
            return;

        var project = new ProjectFile
        {
            Name = ProjectPath == path || ProjectName != "Untitled comparison" ? ProjectName : Path.GetFileNameWithoutExtension(path),
            SourceA = SourceA.BuildReference(),
            SourceB = SourceB.BuildReference(),
            Config = BuildConfig(),
            Ui = new ProjectUiPreferences { ExportAllData = Results.ExportAllData },
        };

        try
        {
            await ProjectStore.SaveAsync(project, path);
            ProjectPath = path;
            ProjectName = project.Name;
            Home.SetRecent(_recent.Add(path));
            StatusMessage = "Project saved (settings only: no credentials, tokens or results).";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _dialogs.ShowError(new DaxDiffException(ErrorCategory.Configuration, "PRJ-IO", "The project could not be saved.", "Choose a folder you can write to.", inner: ex));
        }
    }

    [RelayCommand]
    private void SwapSources()
    {
        var a = SourceA.BuildReference();
        var b = SourceB.BuildReference();
        SourceA.Apply(b);
        SourceB.Apply(a);
        Mapping.Load(Mapping.ToPairs().Select(p => p with { Left = p.Right, Right = p.Left }).ToList());
        var options = Options.ApplyTo(new ComparisonConfig());
        Options.Load(options with
        {
            Filters = options.Filters.Select(f => f with { LeftColumn = f.RightColumn, RightColumn = f.LeftColumn }).ToList(),
        });
        StatusMessage = "Sides swapped. Test both connections again.";
    }

    public void ApplyLaunchArguments(LaunchArguments args)
    {
        if (args.Server is not null)
        {
            SourceA.AttachExternal(args.Server, args.Database);
            Navigate(Sources);
        }
        else if (args.ProjectPath is not null)
        {
            _ = OpenProjectPathAsync(args.ProjectPath);
        }
    }
}

/// <summary>Sources page: two cards plus swap (FR-001).</summary>
public sealed class SourcesViewModel(MainViewModel main) : ObservableObject
{
    public SourcePanelViewModel SourceA => main.SourceA;
    public SourcePanelViewModel SourceB => main.SourceB;
    public IRelayCommand SwapCommand => main.SwapSourcesCommand;
}

/// <summary>Home page: new/open, recent projects and prerequisites.</summary>
public sealed partial class HomeViewModel : ObservableObject
{
    private readonly MainViewModel _main;

    public HomeViewModel(MainViewModel main)
    {
        _main = main;
        Recent = new ObservableCollection<string>(new RecentProjects().Load());
    }

    public ObservableCollection<string> Recent { get; }
    public IRelayCommand NewCommand => _main.NewComparisonCommand;
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
