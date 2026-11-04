using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DaxDiff.Application;
using DaxDiff.Comparison;
using DaxDiff.Desktop.Services;
using DaxDiff.Domain;
using DaxDiff.Desktop.Resources;

namespace DaxDiff.Desktop.ViewModels;

/// <summary>One comparison's filters, tolerances, normalization, limits and query preview (FR-008, FR-009).</summary>
public sealed partial class OptionsViewModel : ObservableObject
{
    private readonly SourcePanelViewModel _sourceA;
    private readonly SourcePanelViewModel _sourceB;
    private readonly ComparisonRunner _runner;
    private readonly IDialogService _dialogs;

    public OptionsViewModel(SourcePanelViewModel sourceA, SourcePanelViewModel sourceB, ComparisonRunner runner, IDialogService dialogs)
    {
        _sourceA = sourceA;
        _sourceB = sourceB;
        _runner = runner;
        _dialogs = dialogs;
        Filters.CollectionChanged += (_, _) => OnPropertyChanged(nameof(FiltersSummary));
    }

    /// <summary>Provides the comparison's compiled configuration for previews; set by its editor.</summary>
    public Func<CompiledComparison>? CompiledProvider { get; set; }

    /// <summary>Opens the filter editor for this comparison; set by its editor.</summary>
    public Action? EditFiltersRequested { get; set; }

    /// <summary>This comparison's filters, edited in the filter pop-up (FR-020).</summary>
    public ObservableCollection<FilterPair> Filters { get; } = [];

    public string FiltersSummary => Filters.Count == 0
        ? Strings.Options_NoFilters
        : Localized.Format(Strings.Options_FiltersSummary, Filters.Count, string.Join("; ", Filters.Select(f => f.Describe())));

    public static IReadOnlyList<ToleranceMode> ToleranceModes { get; } = Enum.GetValues<ToleranceMode>();
    public static IReadOnlyList<NullPolicy> NullPolicies { get; } = Enum.GetValues<NullPolicy>();
    public static IReadOnlyList<TextComparison> TextComparisons { get; } = Enum.GetValues<TextComparison>();
    public static IReadOnlyList<UnicodeNormalization> UnicodeForms { get; } = Enum.GetValues<UnicodeNormalization>();
    public static IReadOnlyList<DuplicateKeyPolicy> DuplicatePolicies { get; } = Enum.GetValues<DuplicateKeyPolicy>();
    public static IReadOnlyList<PercentBaseline> PercentBaselines { get; } = Enum.GetValues<PercentBaseline>();

    [ObservableProperty]
    private ToleranceMode _toleranceMode;

    [ObservableProperty]
    private string _absoluteTolerance = "0";

    [ObservableProperty]
    private string _relativeTolerance = "0";

    [ObservableProperty]
    private NullPolicy _nullPolicy;

    [ObservableProperty]
    private TextComparison _textComparison;

    [ObservableProperty]
    private DuplicateKeyPolicy _duplicateKeyPolicy;

    [ObservableProperty]
    private PercentBaseline _percentBaseline;

    [ObservableProperty]
    private bool _trimWhitespace;

    [ObservableProperty]
    private bool _caseInsensitiveKeys;

    [ObservableProperty]
    private UnicodeNormalization _unicode;

    [ObservableProperty]
    private bool _blankEqualsEmpty;

    [ObservableProperty]
    private bool _dateOnly;

    [ObservableProperty]
    private int _maxRows = new ExecutionLimits().MaxRows;

    [ObservableProperty]
    private int _timeoutSeconds = new ExecutionLimits().TimeoutSeconds;

    [ObservableProperty]
    private int _retryCount = new ExecutionLimits().RetryCount;

    /// <summary>True when the result keeps only differences; false keeps matches and differences (the default).</summary>
    [ObservableProperty]
    private bool _differencesOnly;

    [ObservableProperty]
    private bool _limitResultRows;

    [ObservableProperty]
    private int _maxResultRows = 10_000;

    [ObservableProperty]
    private string _queryPreviewA = "";

    [ObservableProperty]
    private string _queryPreviewB = "";

    [ObservableProperty]
    private string _estimateText = "";

    [RelayCommand]
    private void EditFilters() => EditFiltersRequested?.Invoke();

    /// <summary>Replaces the filters, e.g. when the filter pop-up is confirmed.</summary>
    public void SetFilters(IEnumerable<FilterPair> filters)
    {
        Filters.Clear();
        foreach (var filter in filters)
            Filters.Add(filter);
    }

    public ComparisonOptions ToOptions() => new()
    {
        Filters = Filters.ToList(),
        Tolerance = new ToleranceSettings
        {
            Mode = ToleranceMode,
            Absolute = ParseDecimal(AbsoluteTolerance),
            Relative = ParseDecimal(RelativeTolerance),
        },
        NullPolicy = NullPolicy,
        TextComparison = TextComparison,
        DuplicateKeyPolicy = DuplicateKeyPolicy,
        PercentBaseline = PercentBaseline,
        KeyNormalization = new KeyNormalization
        {
            TrimWhitespace = TrimWhitespace,
            CaseInsensitive = CaseInsensitiveKeys,
            Unicode = Unicode,
            BlankEqualsEmpty = BlankEqualsEmpty,
            DateOnly = DateOnly,
        },
        Limits = new ExecutionLimits { MaxRows = MaxRows, TimeoutSeconds = TimeoutSeconds, RetryCount = RetryCount },
        Results = new ResultSettings
        {
            Rows = DifferencesOnly ? ResultRows.DifferencesOnly : ResultRows.All,
            MaxRows = LimitResultRows ? MaxResultRows : null,
        },
    };

    public void Load(ComparisonOptions config)
    {
        SetFilters(config.Filters);
        ToleranceMode = config.Tolerance.Mode;
        AbsoluteTolerance = config.Tolerance.Absolute.ToString(CultureInfo.InvariantCulture);
        RelativeTolerance = config.Tolerance.Relative.ToString(CultureInfo.InvariantCulture);
        NullPolicy = config.NullPolicy;
        TextComparison = config.TextComparison;
        DuplicateKeyPolicy = config.DuplicateKeyPolicy;
        PercentBaseline = config.PercentBaseline;
        TrimWhitespace = config.KeyNormalization.TrimWhitespace;
        CaseInsensitiveKeys = config.KeyNormalization.CaseInsensitive;
        Unicode = config.KeyNormalization.Unicode;
        BlankEqualsEmpty = config.KeyNormalization.BlankEqualsEmpty;
        DateOnly = config.KeyNormalization.DateOnly;
        MaxRows = config.Limits.MaxRows;
        TimeoutSeconds = config.Limits.TimeoutSeconds;
        RetryCount = config.Limits.RetryCount;
        DifferencesOnly = config.Results.Rows == ResultRows.DifferencesOnly;
        LimitResultRows = config.Results.MaxRows is not null;
        MaxResultRows = config.Results.MaxRows ?? 10_000;
    }

    /// <summary>Tolerances are entered with invariant culture; invalid input becomes -1 so validation reports it.</summary>
    private static decimal ParseDecimal(string text) =>
        decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : -1;

    [RelayCommand]
    private void PreviewQueries()
    {
        if (CompiledProvider?.Invoke().Config is not { } config)
            return;
        try
        {
            QueryPreviewA = DaxQueryBuilder.BuildComparisonQuery(config, Side.A, _sourceA.Metadata);
            QueryPreviewB = DaxQueryBuilder.BuildComparisonQuery(config, Side.B, _sourceB.Metadata);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            QueryPreviewA = QueryPreviewB = Localized.Format(Strings.Options_CannotGenerateQuery, ex.Message);
        }
    }

    [RelayCommand]
    private async Task EstimateRowsAsync()
    {
        if (CompiledProvider?.Invoke() is not { } compiled)
            return;
        var config = compiled.Config;
        if (_sourceA.Connector is null || _sourceB.Connector is null)
        {
            _dialogs.ShowInfo(Strings.Options_ConnectBothFirst);
            return;
        }

        if (compiled.HasErrors)
        {
            _dialogs.ShowInfo(Strings.Options_FixErrorsFirst + Environment.NewLine + string.Join(Environment.NewLine, compiled.Issues.Where(i => i.Severity == IssueSeverity.Error)));
            return;
        }

        EstimateText = Strings.Options_Estimating;
        try
        {
            var a = _runner.EstimateRowsAsync(_sourceA.Connector, config, Side.A, _sourceA.Metadata, CancellationToken.None);
            var b = _runner.EstimateRowsAsync(_sourceB.Connector, config, Side.B, _sourceB.Metadata, CancellationToken.None);
            await Task.WhenAll(a, b);
            EstimateText = Localized.Format(Strings.Options_Estimate, a.Result, b.Result) +
                           (a.Result > MaxRows || b.Result > MaxRows ? " " + Localized.Format(Strings.Options_EstimateExceedsCap, MaxRows) : "");
        }
        catch (Exception ex)
        {
            EstimateText = Strings.Options_EstimateFailed;
            _dialogs.ShowError(ex);
        }
    }
}
