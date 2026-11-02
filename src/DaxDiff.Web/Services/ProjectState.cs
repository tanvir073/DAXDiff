using DaxDiff.Application;
using DaxDiff.Comparison;
using DaxDiff.Connectors.Auth;
using DaxDiff.Connectors.Service;
using DaxDiff.Domain;
using Microsoft.JSInterop;

namespace DaxDiff.Web.Services;

public sealed record RunLogEntry(TimeSpan At, string Text, bool IsError = false);

/// <summary>
/// The open project and everything done with it in this browser tab: the two sources, which comparisons to run, the
/// run itself and the latest result per comparison. Results stay in memory only; projects never hold them (FR-019).
/// </summary>
public sealed class ProjectState
{
    private readonly BrowserFiles _files;
    private readonly ComparisonRunner _runner;
    private string _savedFingerprint = "";
    private CancellationTokenSource? _runCts;

    public ProjectState(IJSRuntime js, HttpClient http, WebSettings settings, BrowserFiles files, ComparisonRunner runner, ILoggerFactory loggerFactory)
    {
        _files = files;
        _runner = runner;
        SourceState Source(Side side)
        {
            var auth = new BrowserAuthService(js, settings.Auth);
            var rest = new PowerBiRestClient(http, auth, settings.Auth, loggerFactory.CreateLogger<PowerBiRestClient>());
            var source = new SourceState(side, new SourceSession(auth, rest, new PowerBiDiscoveryService(rest)), loggerFactory);
            source.Changed += () => _ = NotifyAsync();
            return source;
        }

        SourceA = Source(Side.A);
        SourceB = Source(Side.B);
    }

    public event Action? Changed;

    public ProjectFile? Project { get; private set; }
    public string? FileName { get; private set; }
    public bool IsSample { get; private set; }
    public SourceState SourceA { get; }
    public SourceState SourceB { get; }

    public IReadOnlyList<ComparisonDefinition> Comparisons => Project?.Comparisons ?? [];
    public HashSet<Guid> IncludedInRun { get; } = [];
    public Dictionary<Guid, ComparisonResult> Results { get; } = [];
    public Guid? SelectedResultId { get; set; }

    public bool IsRunning { get; private set; }
    public List<RunLogEntry> RunLog { get; } = [];
    public string? RunStep { get; private set; }

    /// <summary>The project as it would be saved: the loaded file with the sources picked in this tab.</summary>
    public ProjectFile? Current => Project is null ? null : Project with { SourceA = SourceA.Reference, SourceB = SourceB.Reference };

    public bool IsDirty => Current is { } current && ProjectStore.Fingerprint(current) != _savedFingerprint;

    public bool BothConnected => SourceA.IsConnected && SourceB.IsConnected;

    public async Task NewAsync(string name)
    {
        await _files.ForgetProjectHandleAsync();
        await LoadAsync(new ProjectFile { Name = string.IsNullOrWhiteSpace(name) ? "Untitled project" : name.Trim() }, null, markSaved: false);
    }

    /// <summary>Opens a .pbicomp chosen by the user. Returns false when cancelled; throws on an unreadable file.</summary>
    public async Task<bool> OpenAsync()
    {
        var file = await _files.OpenProjectAsync();
        if (file is null)
            return false;
        await LoadAsync(ProjectStore.Deserialize(file.Text), file.Name, markSaved: true);
        return true;
    }

    public async Task OpenSampleAsync()
    {
        await _files.ForgetProjectHandleAsync();
        await LoadAsync(new ProjectFile { Name = "Sample project", Comparisons = [SampleConnector.Comparison()] }, null, markSaved: false);
        IsSample = true;
        await SourceA.UseSampleAsync(SampleConnector.For(Side.A));
        await SourceB.UseSampleAsync(SampleConnector.For(Side.B));
    }

    private async Task LoadAsync(ProjectFile project, string? fileName, bool markSaved)
    {
        Cancel();
        Project = project;
        FileName = fileName;
        IsSample = false;
        await SourceA.LoadAsync(project.SourceA);
        await SourceB.LoadAsync(project.SourceB);
        IncludedInRun.Clear();
        IncludedInRun.UnionWith(project.Comparisons.Select(c => c.Id));
        Results.Clear();
        SelectedResultId = null;
        RunLog.Clear();
        _savedFingerprint = markSaved ? ProjectStore.Fingerprint(Current!) : "";
        await NotifyAsync();
    }

    /// <summary>Saves to the opened file (Chromium) or asks where to save; returns false when cancelled.</summary>
    public async Task<bool> SaveAsync(bool saveAs = false)
    {
        if (Current is not { } current)
            return false;
        var project = current with { SavedUtc = DateTimeOffset.UtcNow };
        var name = await _files.SaveProjectAsync(ProjectStore.Serialize(project), SuggestedFileName(project), saveAs);
        if (name is null)
            return false;
        Project = project;
        FileName = name;
        _savedFingerprint = ProjectStore.Fingerprint(Current!);
        await NotifyAsync();
        return true;
    }

    public async Task CloseAsync()
    {
        Cancel();
        await SourceA.LoadAsync(null);
        await SourceB.LoadAsync(null);
        await _files.ForgetProjectHandleAsync();
        Project = null;
        FileName = null;
        IsSample = false;
        Results.Clear();
        RunLog.Clear();
        _savedFingerprint = "";
        await NotifyAsync();
    }

    public async Task RenameAsync(string name)
    {
        if (Project is null || string.IsNullOrWhiteSpace(name))
            return;
        Project = Project with { Name = name.Trim() };
        await NotifyAsync();
    }

    private static string SuggestedFileName(ProjectFile project)
    {
        var name = string.Concat(project.Name.Split(Path.GetInvalidFileNameChars())).Trim();
        return (name.Length == 0 ? "project" : name) + ProjectFile.Extension;
    }

    public CompiledComparison Compile(ComparisonDefinition definition) =>
        ComparisonCompiler.CompileAndValidate(definition, SourceA.Metadata, SourceB.Metadata);

    /// <summary>Runs the ticked comparisons one after another (FR-010); each result replaces that comparison's previous one.</summary>
    public async Task RunAsync()
    {
        if (IsRunning || !BothConnected)
            return;
        var selected = Comparisons.Where(c => IncludedInRun.Contains(c.Id)).ToList();
        if (selected.Count == 0)
            return;

        IsRunning = true;
        RunLog.Clear();
        _runCts = new CancellationTokenSource();
        var token = _runCts.Token;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        void Log(string text, bool error = false) => RunLog.Add(new RunLogEntry(clock.Elapsed, text, error));
        await NotifyAsync();

        try
        {
            foreach (var definition in selected)
            {
                if (token.IsCancellationRequested)
                    break;
                Log($"{definition.Name}: started");
                var progress = new Progress<RunProgress>(p =>
                {
                    RunStep = $"{definition.Name}: {(p.Side is { } side ? $"model {side}: " : "")}{p.Step}";
                    _ = NotifyAsync();
                });
                var request = RunRequest.For(SourceA.Connector!, SourceB.Connector!, definition, SourceA.Metadata, SourceB.Metadata);
                var result = await _runner.RunAsync(request, progress, token);
                Results[definition.Id] = result;
                SelectedResultId = definition.Id;
                Log($"{definition.Name}: {result.Status}" + (result.FailureMessage is null ? "" : " – " + result.FailureMessage),
                    result.Status is RunStatus.Failed);
                await NotifyAsync();
            }
        }
        finally
        {
            IsRunning = false;
            RunStep = null;
            _runCts.Dispose();
            _runCts = null;
            Log($"Finished in {clock.Elapsed:mm\\:ss}");
            await NotifyAsync();
        }
    }

    public void Cancel() => _runCts?.Cancel();

    private async Task NotifyAsync()
    {
        Changed?.Invoke();
        await _files.SetDirtyAsync(Project is not null && !IsSample && IsDirty);
    }
}
