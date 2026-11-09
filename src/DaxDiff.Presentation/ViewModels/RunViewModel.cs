using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DaxDiff.Application;
using DaxDiff.Comparison;
using DaxDiff.Presentation.Services;
using DaxDiff.Domain;
using DaxDiff.Presentation.Resources;

namespace DaxDiff.Presentation.ViewModels;

/// <summary>
/// Runs the comparisons ticked for the run, one after another, with per-model progress, cancel, elapsed time
/// and warnings (FR-010, FR-016).
/// </summary>
public sealed partial class RunViewModel : ObservableObject
{
    private readonly SourcePanelViewModel _sourceA;
    private readonly SourcePanelViewModel _sourceB;
    private readonly ComparisonsViewModel _comparisons;
    private readonly ComparisonRunner _runner;
    private readonly IDialogService _dialogs;
    private readonly Stopwatch _stopwatch = new();

    public RunViewModel(SourcePanelViewModel sourceA, SourcePanelViewModel sourceB, ComparisonsViewModel comparisons, ComparisonRunner runner, IDialogService dialogs)
    {
        _sourceA = sourceA;
        _sourceB = sourceB;
        _comparisons = comparisons;
        _runner = runner;
        _dialogs = dialogs;
    }

    /// <summary>Updates <see cref="Elapsed"/> until cancelled. Awaited from the UI thread, so each update runs there.</summary>
    private async Task TickAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
                Elapsed = _stopwatch.Elapsed.ToString(@"mm\:ss\.f");
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <summary>Raised with each finished result as soon as it is available.</summary>
    public event EventHandler<ComparisonResult>? Completed;

    /// <summary>Raised once the whole batch has finished; the shell then shows the results.</summary>
    public event EventHandler? Finished;

    public ObservableCollection<ComparisonEditorViewModel> Comparisons => _comparisons.Comparisons;

    public ObservableCollection<string> Log { get; } = [];

    [ObservableProperty]
    private string _current = "";

    [ObservableProperty]
    private string _statusA = Strings.Run_Waiting;

    [ObservableProperty]
    private string _statusB = Strings.Run_Waiting;

    [ObservableProperty]
    private string _elapsed = "00:00.0";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    private bool _isRunning;

    /// <summary>The user must acknowledge that results reflect each caller's security view (FR-016).</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    private bool _acknowledgedScope;

    public string ScopeA => $"{_sourceA.Identity} — {_sourceA.SecurityContext ?? Strings.Run_NotConnected}";
    public string ScopeB => $"{_sourceB.Identity} — {_sourceB.SecurityContext ?? Strings.Run_NotConnected}";

    public void Refresh()
    {
        OnPropertyChanged(nameof(ScopeA));
        OnPropertyChanged(nameof(ScopeB));
    }

    [RelayCommand]
    private void IncludeAll() => SetIncluded(true);

    [RelayCommand]
    private void IncludeNone() => SetIncluded(false);

    private void SetIncluded(bool include)
    {
        foreach (var comparison in Comparisons)
            comparison.IncludeInRun = include;
    }

    private bool CanRun() => !IsRunning && AcknowledgedScope;

    [RelayCommand(CanExecute = nameof(CanRun), IncludeCancelCommand = true)]
    private async Task RunAsync(CancellationToken cancellationToken)
    {
        // Only saved versions run; drafts stay on the Comparisons page until saved (FR-021).
        var selected = Comparisons.Where(c => c.IncludeInRun && c.SavedDefinition is not null).ToList();
        if (selected.Count == 0)
        {
            _dialogs.ShowInfo(Strings.Run_TickOne);
            return;
        }

        if (_sourceA.Connector is null || _sourceB.Connector is null)
        {
            _dialogs.ShowInfo(Strings.Run_ConnectBothFirst);
            return;
        }

        IsRunning = true;
        Log.Clear();
        _stopwatch.Restart();
        using var ticking = new CancellationTokenSource();
        var ticker = TickAsync(ticking.Token);
        try
        {
            for (var i = 0; i < selected.Count && !cancellationToken.IsCancellationRequested; i++)
            {
                var definition = selected[i].SavedDefinition!;
                Current = Localized.Format(Strings.Run_CurrentOf, definition.Name, i + 1, selected.Count);
                StatusA = StatusB = Strings.Run_Queued;
                var name = definition.Name;
                var progress = new Progress<RunProgress>(p =>
                {
                    Log.Add($"[{_stopwatch.Elapsed:mm\\:ss}] {name}: {(p.Side is { } s ? Localized.Format(Strings.Source_Title, s) + ": " : "")}{p.Step}");
                    if (p.Side == Side.A)
                        StatusA = p.Step;
                    else if (p.Side == Side.B)
                        StatusB = p.Step;
                });

                var request = RunRequest.For(_sourceA.Connector, _sourceB.Connector, definition, _sourceA.Metadata, _sourceB.Metadata);
                var result = await _runner.RunAsync(request, progress, cancellationToken);
                Log.Add($"[{_stopwatch.Elapsed:mm\\:ss}] {name}: {EnumText.Of(result.Status)}" + (result.FailureMessage is null ? "" : " – " + result.FailureMessage));
                Completed?.Invoke(this, result);
            }

            Current = cancellationToken.IsCancellationRequested ? Strings.Run_Cancelled : Strings.Run_Finished;
            Finished?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            await ticking.CancelAsync();
            await ticker;
            Elapsed = _stopwatch.Elapsed.ToString(@"mm\:ss\.f");
            IsRunning = false;
        }
    }
}
