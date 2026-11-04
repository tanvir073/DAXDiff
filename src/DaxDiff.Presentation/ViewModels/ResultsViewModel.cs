using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DaxDiff.Comparison;
using DaxDiff.Desktop.Services;
using DaxDiff.Domain;
using DaxDiff.Export;
using Microsoft.Extensions.Logging;
using DaxDiff.Desktop.Resources;

namespace DaxDiff.Desktop.ViewModels;

public enum ResultFilter
{
    All,
    Match,
    Mismatch,
    OnlyInA,
    OnlyInB,
    InvalidKey,
}

public enum ResultColumnKind
{
    Status,
    Key,
    Value,
    Delta,
    Percent,
    Text,
}

/// <param name="Measure">The compared value (alias) the column belongs to; null for status, key and reason columns.</param>
public sealed record ResultColumn(string Header, int Index, ResultColumnKind Kind, string? Measure = null);

/// <summary>Grid row: raw values (for correct sorting) addressed by column index.</summary>
public sealed class ResultRowView
{
    public ResultRowView(ComparisonRow row, object?[] cells)
    {
        Row = row;
        Cells = cells;
        SearchText = string.Join("\u001f", cells.Select(c => c is null ? "" : ValueCanonicalizer.Display(c)));
    }

    public ComparisonRow Row { get; }
    public object?[] Cells { get; }
    public string SearchText { get; }
    public RowStatus Status => Row.Status;
}

/// <summary>KPI header, discrepancy filters, search, virtualized grid, drilldown and export (FR-011..FR-013).</summary>
public sealed partial class ResultsViewModel : ObservableObject
{
    /// <summary>Grid text for a value the other model did not return at all.</summary>
    public const string Absent = "—";

    private readonly IDialogService _dialogs;
    private readonly ILogger<ResultsViewModel> _logger;
    private List<ResultRowView> _rows = [];

    public ResultsViewModel(IDialogService dialogs, ILogger<ResultsViewModel> logger)
    {
        _dialogs = dialogs;
        _logger = logger;
    }

    /// <summary>Raised when columns change so the view can rebuild grid columns.</summary>
    public event EventHandler? ColumnsChanged;

    /// <summary>The latest result of each comparison run in this session (results are never saved in the project).</summary>
    public ObservableCollection<ComparisonResult> Results { get; } = [];

    /// <summary>Adds a result, replacing the previous result of the same comparison, and shows it.</summary>
    public void Show(ComparisonResult result)
    {
        var previous = Results.FirstOrDefault(r => r.ComparisonName == result.ComparisonName);
        if (previous is null)
            Results.Add(result);
        else
            Results[Results.IndexOf(previous)] = result;
        Result = result;
    }

    public void Clear()
    {
        Result = null;
        Results.Clear();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResult), nameof(StatusBanner), nameof(IsIncomplete), nameof(Kpis), nameof(SourceText), nameof(MeasureStats))]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    private ComparisonResult? _result;

    [ObservableProperty]
    private IListView? _rowsView;

    [ObservableProperty]
    private IReadOnlyList<ResultColumn> _columns = [];

    [ObservableProperty]
    private ResultFilter _filter = ResultFilter.All;

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Details))]
    private ResultRowView? _selectedRow;

    [ObservableProperty]
    private bool _exportAllData;

    [ObservableProperty]
    private bool _exportQueryText;

    [ObservableProperty]
    private string _visibleCountText = "";

    public bool HasResult => Result is not null;
    public bool IsIncomplete => Result is { IsComplete: false };

    public string StatusBanner => Result?.Status switch
    {
        null => "",
        RunStatus.Complete => Localized.Format(Strings.Results_BannerComplete, Result.ComparisonName),
        RunStatus.Partial => Localized.Format(Strings.Results_BannerPartial, Result.ComparisonName),
        RunStatus.Cancelled => Localized.Format(Strings.Results_BannerCancelled, Result.ComparisonName),
        _ => Localized.Format(Strings.Results_BannerFailed, Result.ComparisonName, Result.FailureMessage),
    };

    public string SourceText => Result is null
        ? ""
        : $"A: {Result.SourceA.Describe()}  [{Result.SecurityContextA}]{Environment.NewLine}" +
          $"B: {Result.SourceB.Describe()}  [{Result.SecurityContextB}]{Environment.NewLine}" +
          Localized.Format(Strings.Results_SourceDetails, Result.FinishedUtc, Result.RunId,
              EnumText.Of(Result.Config.Options.Tolerance.Mode), Result.Config.Options.Tolerance.Absolute, Result.Config.Options.Tolerance.Relative,
              string.Join(", ", Result.Queries.Select(q => $"{q.Side}:{q.QueryHash[..12]}")));

    public IReadOnlyList<KeyValuePair<string, string>> Kpis => Result is null
        ? []
        :
        [
            // Ui labels: the KPI tiles in ResultsView colour themselves by comparing with them.
            new(Strings.Results_KpiKeys, Result.Summary.TotalKeys.ToString("N0")),
            new(Ui.Results_KpiMatchRate, Result.Summary.MatchRate.ToString("P2")),
            new(Ui.Results_KpiMatches, Result.Summary.Matches.ToString("N0")),
            new(Ui.Results_KpiMismatches, Result.Summary.Mismatches.ToString("N0")),
            new(Ui.Results_KpiOnlyInA, Result.Summary.OnlyInA.ToString("N0")),
            new(Ui.Results_KpiOnlyInB, Result.Summary.OnlyInB.ToString("N0")),
            new(Strings.Results_KpiInvalidKeys, Result.Summary.InvalidKeys.ToString("N0")),
        ];

    public IReadOnlyList<string> MeasureStats => Result?.Summary.Measures
        .Select(m => Localized.Format(Strings.Results_MeasureStats, m.Alias, m.Mismatches, m.Compared, m.TotalA, m.TotalB, m.MaxAbsDelta))
        .ToList() ?? [];

    public string Details
    {
        get
        {
            if (SelectedRow is null || Result is null)
                return Strings.Results_SelectRow;
            var row = SelectedRow.Row;
            var sb = new StringBuilder();
            sb.AppendLine(Localized.Format(Strings.Results_DetailStatus, ExcelReportWriter.StatusText(row.Status)));
            if (row.Reason is not null)
                sb.AppendLine(Localized.Format(Strings.Results_DetailReason, row.Reason));
            var dims = Result.Config.Keys;
            for (var i = 0; i < dims.Count; i++)
            {
                var a = row.KeyA is null ? "—" : ValueCanonicalizer.Display(row.KeyA[i]);
                var b = row.KeyB is null ? "—" : ValueCanonicalizer.Display(row.KeyB[i]);
                sb.AppendLine($"{dims[i].Alias}: A = {a}   B = {b}");
            }

            foreach (var cell in row.Measures)
            {
                sb.AppendLine($"{cell.Alias}: A = {Format(cell.A)}   B = {Format(cell.B)}   Δ = {Format(cell.Delta)}   Δ% = {(cell.DeltaPercent is { } p ? p.ToString("P4") : Strings.Results_NotApplicable)}" +
                              (cell.IsMatch ? "   ✓" : $"   ✗ {cell.Reason}"));
            }

            sb.AppendLine(Localized.Format(Strings.Results_DetailNormalizedKey, row.NormalizedKey));
            return sb.ToString();
        }
    }

    private static string Format(object? value) => value switch
    {
        null => Strings.Blank,
        decimal d => d.ToString("#,##0.##########", CultureInfo.CurrentCulture),
        double d => d.ToString("#,##0.##########", CultureInfo.CurrentCulture),
        _ => ValueCanonicalizer.Display(value),
    };

    partial void OnResultChanged(ComparisonResult? value)
    {
        SelectedRow = null;
        if (value is null)
        {
            _rows = [];
            Columns = [];
            FailedColumns = [];
            _shownColumns = [];
            RowsView = null;
            ColumnsChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        var dims = value.Config.Keys;
        var measures = value.Config.Values;
        var columns = new List<ResultColumn> { new(Strings.Results_ColumnStatus, 0, ResultColumnKind.Status) };
        foreach (var d in dims)
            columns.Add(new ResultColumn(d.Alias, columns.Count, ResultColumnKind.Key));
        foreach (var m in measures)
        {
            columns.Add(new ResultColumn($"{m.Alias} A", columns.Count, ResultColumnKind.Value, m.Alias));
            columns.Add(new ResultColumn($"{m.Alias} B", columns.Count, ResultColumnKind.Value, m.Alias));
            columns.Add(new ResultColumn($"{m.Alias} Δ", columns.Count, ResultColumnKind.Delta, m.Alias));
            columns.Add(new ResultColumn($"{m.Alias} Δ%", columns.Count, ResultColumnKind.Percent, m.Alias));
        }

        // The summary counts every row, also those the result settings left out of the listing.
        FailedColumns = value.Summary.Measures.Where(m => m.Mismatches > 0).Select(m => m.Alias).ToList();
        _shownColumns = [];
        OnPropertyChanged(nameof(ColumnPickerText));

        columns.Add(new ResultColumn(Strings.Results_ColumnReason, columns.Count, ResultColumnKind.Text));

        _rows = value.Rows.Select(r =>
        {
            var cells = new object?[columns.Count];
            cells[0] = ExcelReportWriter.StatusText(r.Status);
            var key = r.DisplayKey;
            for (var i = 0; i < dims.Count; i++)
                cells[1 + i] = i < key.Length ? key[i] : null;
            var c = 1 + dims.Count;
            foreach (var cell in r.Measures)
            {
                // A side that did not return the key is "absent", which is different from a BLANK value.
                cells[c++] = r.Status == RowStatus.OnlyInB ? Absent : cell.A;
                cells[c++] = r.Status == RowStatus.OnlyInA ? Absent : cell.B;
                cells[c++] = cell.Delta;
                cells[c++] = cell.DeltaPercent;
            }

            cells[^1] = r.Reason;
            return new ResultRowView(r, cells);
        }).ToList();

        Columns = columns;
        var view = ListViews.Create(_rows);
        view.Filter = Matches;
        RowsView = view;
        Filter = ResultFilter.All;
        UpdateCount();
        ColumnsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Compared values with at least one mismatch, offered in the column picker.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFailedColumns))]
    private IReadOnlyList<string> _failedColumns = [];

    public bool HasFailedColumns => FailedColumns.Count > 0;

    private IReadOnlyList<string> _shownColumns = [];

    /// <summary>The compared values chosen in the column picker; empty when every column is shown.</summary>
    public IReadOnlyList<string> ShownColumns => _shownColumns;

    /// <summary>
    /// Limits the grid to the chosen compared values (their A, B, Δ and Δ% columns) plus status, key and reason
    /// columns. An empty list shows every column.
    /// </summary>
    public void ShowOnlyColumns(IReadOnlyList<string> measures)
    {
        _shownColumns = measures;
        OnPropertyChanged(nameof(ColumnPickerText));
        ColumnVisibilityChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Raised when the column picker changes which columns are shown.</summary>
    public event EventHandler? ColumnVisibilityChanged;

    public bool IsColumnShown(ResultColumn column) =>
        column.Measure is null || _shownColumns.Count == 0 || _shownColumns.Contains(column.Measure);

    public string ColumnPickerText => _shownColumns.Count == 0
        ? Localized.Format(Strings.Results_ColumnsAll, FailedColumns.Count)
        : Localized.Format(Strings.Results_ColumnsSome, string.Join(", ", _shownColumns));

    partial void OnFilterChanged(ResultFilter value) => RefreshView();

    partial void OnSearchTextChanged(string value) => RefreshView();

    private void RefreshView()
    {
        RowsView?.Refresh();
        UpdateCount();
    }

    private void UpdateCount() =>
        VisibleCountText = RowsView is null
            ? ""
            : Localized.Format(Strings.Results_VisibleCount, RowsView.Count, _rows.Count) +
              (Result?.Summary.RowsOmitted is > 0 and var omitted ? " " + Localized.Format(Strings.Results_RowsOmitted, omitted) : "");

    private bool Matches(object item)
    {
        var row = (ResultRowView)item;
        var statusOk = Filter switch
        {
            ResultFilter.Match => row.Status == RowStatus.Match,
            ResultFilter.Mismatch => row.Status == RowStatus.Mismatch,
            ResultFilter.OnlyInA => row.Status == RowStatus.OnlyInA,
            ResultFilter.OnlyInB => row.Status == RowStatus.OnlyInB,
            ResultFilter.InvalidKey => row.Status == RowStatus.InvalidKey,
            _ => true,
        };
        return statusOk && (string.IsNullOrWhiteSpace(SearchText) || row.SearchText.Contains(SearchText.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Sorts by a column using a type-aware comparer (numbers numerically, text case-insensitively).</summary>
    public void Sort(int columnIndex, ListSortDirection direction)
    {
        if (RowsView is { } view)
            view.Sort = new CellComparer(columnIndex, direction);
    }

    private bool CanExport() => Result is not null;

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportAsync()
    {
        if (Result is not { } result)
            return;

        var path = _dialogs.SaveFile(Strings.Results_ExcelFilter, $"DAXDiff_{SafeFileName(result.ComparisonName)}_{result.FinishedUtc:yyyyMMdd_HHmm}.xlsx", Strings.Results_ExportDialogTitle);
        if (path is null)
            return;

        var nl = Environment.NewLine;
        var notice = Strings.Export_Sensitive + nl + nl +
                     Localized.Format(Strings.Export_Destination, path) + nl +
                     Localized.Format(Strings.Export_AllDataRows, ExportAllData ? Strings.Export_Included : Strings.Export_AllDataOmitted) + nl +
                     Localized.Format(Strings.Export_DaxText, ExportQueryText ? Strings.Export_Included : Strings.Export_DaxOmitted) + nl +
                     (result.IsComplete ? "" : nl + Localized.Format(Strings.Export_IncompleteRun, EnumText.Of(result.Status).ToUpper(CultureInfo.CurrentCulture)) + nl) +
                     nl + Strings.Export_Continue;
        if (!_dialogs.Confirm(notice, Strings.Export_NoticeTitle))
            return;

        try
        {
            var options = new ExcelExportOptions { IncludeAllData = ExportAllData, IncludeQueryText = ExportQueryText };
            await Task.Run(() => ExcelReportWriter.Write(result, path, options));
            _logger.LogInformation("Exported run {RunId} to workbook", result.RunId);
            if (_dialogs.Confirm(Localized.Format(Strings.Export_Done, path) + Environment.NewLine + Strings.Export_OpenNow, Strings.Export_DoneTitle))
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _dialogs.ShowError(new DaxDiffException(ErrorCategory.Export, "EXP-IO",
                Strings.Export_WriteFailed, Strings.Export_WriteFailedRemedy, inner: ex));
        }
    }

    private static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(c => invalid.Contains(c) || c == ' ' ? '_' : c).ToArray());
        return cleaned.Length == 0 ? Strings.Results_DefaultFileName : cleaned;
    }

    private sealed class CellComparer(int index, ListSortDirection direction) : System.Collections.IComparer
    {
        public int Compare(object? x, object? y)
        {
            var a = ((ResultRowView)x!).Cells[index];
            var b = ((ResultRowView)y!).Cells[index];
            var result = CompareValues(a, b);
            return direction == ListSortDirection.Ascending ? result : -result;
        }

        private static int CompareValues(object? a, object? b)
        {
            if (a is null || b is null)
                return a is null ? (b is null ? 0 : -1) : 1;
            if (ValueCanonicalizer.TryToDecimal(a, out var da) && ValueCanonicalizer.TryToDecimal(b, out var db))
                return da.CompareTo(db);
            if (a is DateTime ta && b is DateTime tb)
                return ta.CompareTo(tb);
            return string.Compare(ValueCanonicalizer.Display(a), ValueCanonicalizer.Display(b), StringComparison.CurrentCultureIgnoreCase);
        }
    }
}
