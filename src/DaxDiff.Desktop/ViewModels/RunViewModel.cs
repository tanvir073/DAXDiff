using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DaxDiff.Application;
using DaxDiff.Comparison;
using DaxDiff.Desktop.Services;
using DaxDiff.Domain;

namespace DaxDiff.Desktop.ViewModels;

/// <summary>Execution with per-model progress, cancel, elapsed time and warnings (FR-010, FR-016).</summary>
public sealed partial class RunViewModel : ObservableObject
{
    private readonly SourcePanelViewModel _sourceA;
    private readonly SourcePanelViewModel _sourceB;
    private readonly ComparisonRunner _runner;
    private readonly IDialogService _dialogs;
    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _stopwatch = new();

    public RunViewModel(SourcePanelViewModel sourceA, SourcePanelViewModel sourceB, ComparisonRunner runner, IDialogService dialogs)
    {
        _sourceA = sourceA;
        _sourceB = sourceB;
        _runner = runner;
        _dialogs = dialogs;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick += (_, _) => Elapsed = _stopwatch.Elapsed.ToString(@"mm\:ss\.f");
    }

    public Func<ComparisonConfig>? ConfigProvider { get; set; }

    /// <summary>Raised with the finished result; the shell navigates to Results.</summary>
    public event EventHandler<ComparisonResult>? Completed;

    public ObservableCollection<string> Log { get; } = [];

    [ObservableProperty]
    private string _statusA = "Waiting";

    [ObservableProperty]
    private string _statusB = "Waiting";

    [ObservableProperty]
    private string _elapsed = "00:00.0";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    private bool _isRunning;

    /// <summary>The user must acknowledge that results reflect each caller's security view (FR-016).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    private bool _acknowledgedScope;

    [ObservableProperty]
    private IReadOnlyList<string> _preflightWarnings = [];

    public string ScopeA => $"{_sourceA.Identity} — {_sourceA.SecurityContext ?? "not connected"}";
    public string ScopeB => $"{_sourceB.Identity} — {_sourceB.SecurityContext ?? "not connected"}";

    public void Refresh(IReadOnlyList<ValidationIssue> issues)
    {
        PreflightWarnings = issues.Select(i => i.ToString()).ToList();
        OnPropertyChanged(nameof(ScopeA));
        OnPropertyChanged(nameof(ScopeB));
    }

    private bool CanRun() => !IsRunning && AcknowledgedScope;

    [RelayCommand(CanExecute = nameof(CanRun), IncludeCancelCommand = true)]
    private async Task RunAsync(CancellationToken cancellationToken)
    {
        if (ConfigProvider?.Invoke() is not { } config)
            return;
        if (_sourceA.Connector is null || _sourceB.Connector is null)
        {
            _dialogs.ShowInfo("Connect and test both models in the Sources step first.");
            return;
        }

        IsRunning = true;
        Log.Clear();
        StatusA = StatusB = "Queued";
        _stopwatch.Restart();
        _timer.Start();
        try
        {
            var progress = new Progress<RunProgress>(p =>
            {
                var line = $"[{_stopwatch.Elapsed:mm\\:ss}] {(p.Side is { } s ? $"Model {s}: " : "")}{p.Step}";
                Log.Add(line);
                if (p.Side == Side.A)
                    StatusA = p.Step;
                else if (p.Side == Side.B)
                    StatusB = p.Step;
            });

            var request = new RunRequest(_sourceA.Connector, _sourceB.Connector, config, _sourceA.Metadata, _sourceB.Metadata);
            var result = await _runner.RunAsync(request, progress, cancellationToken);
            Log.Add($"[{_stopwatch.Elapsed:mm\\:ss}] Run {result.Status}" + (result.FailureMessage is null ? "" : ": " + result.FailureMessage));
            Completed?.Invoke(this, result);
        }
        finally
        {
            _timer.Stop();
            Elapsed = _stopwatch.Elapsed.ToString(@"mm\:ss\.f");
            IsRunning = false;
        }
    }
}
