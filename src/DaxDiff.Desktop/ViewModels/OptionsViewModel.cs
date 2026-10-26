using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DaxDiff.Application;
using DaxDiff.Comparison;
using DaxDiff.Desktop.Services;
using DaxDiff.Domain;

namespace DaxDiff.Desktop.ViewModels;

public sealed partial class FilterRowViewModel : ObservableObject
{
    [ObservableProperty]
    private FieldRef? _leftColumn;

    [ObservableProperty]
    private FieldRef? _rightColumn;

    [ObservableProperty]
    private FilterOperator _operator = FilterOperator.In;

    /// <summary>Values separated by semicolons or new lines; commas are allowed inside values.</summary>
    [ObservableProperty]
    private string _valuesText = "";

    [ObservableProperty]
    private string _meaning = "";

    public static IReadOnlyList<FilterOperator> Operators { get; } = Enum.GetValues<FilterOperator>();

    public FilterPair? ToFilter() => LeftColumn is null || RightColumn is null
        ? null
        : new FilterPair
        {
            LeftColumn = LeftColumn,
            RightColumn = RightColumn,
            Operator = Operator,
            Values = ValuesText.Split([';', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries),
            SemanticMeaning = Meaning,
        };

    public static FilterRowViewModel From(FilterPair filter) => new()
    {
        LeftColumn = filter.LeftColumn,
        RightColumn = filter.RightColumn,
        Operator = filter.Operator,
        ValuesText = string.Join("; ", filter.Values),
        Meaning = filter.SemanticMeaning,
    };
}

/// <summary>Filters, tolerances, normalization, limits and query preview (FR-008, FR-009).</summary>
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
        _sourceA.MetadataChanged += (_, _) => OnPropertyChanged(nameof(ColumnsA));
        _sourceB.MetadataChanged += (_, _) => OnPropertyChanged(nameof(ColumnsB));
    }

    /// <summary>Provides the current configuration for previews; set by the shell.</summary>
    public Func<ComparisonConfig>? ConfigProvider { get; set; }

    public IReadOnlyList<FieldRef> ColumnsA => _sourceA.Metadata?.AllColumns.Select(c => c.Ref).ToList() ?? [];
    public IReadOnlyList<FieldRef> ColumnsB => _sourceB.Metadata?.AllColumns.Select(c => c.Ref).ToList() ?? [];

    public ObservableCollection<FilterRowViewModel> Filters { get; } = [];

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

    [ObservableProperty]
    private string _queryPreviewA = "";

    [ObservableProperty]
    private string _queryPreviewB = "";

    [ObservableProperty]
    private string _estimateText = "";

    [RelayCommand]
    private void AddFilter() => Filters.Add(new FilterRowViewModel());

    [RelayCommand]
    private void RemoveFilter(FilterRowViewModel? row)
    {
        if (row is not null)
            Filters.Remove(row);
    }

    /// <summary>Applies these options to a configuration that already holds the field pairs.</summary>
    public ComparisonConfig ApplyTo(ComparisonConfig config) => config with
    {
        Filters = Filters.Select(f => f.ToFilter()).OfType<FilterPair>().ToList(),
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
    };

    public void Load(ComparisonConfig config)
    {
        Filters.Clear();
        foreach (var filter in config.Filters)
            Filters.Add(FilterRowViewModel.From(filter));
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
    }

    /// <summary>Tolerances are entered with invariant culture; invalid input becomes -1 so validation reports it.</summary>
    private static decimal ParseDecimal(string text) =>
        decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : -1;

    [RelayCommand]
    private void PreviewQueries()
    {
        if (ConfigProvider?.Invoke() is not { } config)
            return;
        try
        {
            QueryPreviewA = DaxQueryBuilder.BuildComparisonQuery(config, Side.A, _sourceA.Metadata);
            QueryPreviewB = DaxQueryBuilder.BuildComparisonQuery(config, Side.B, _sourceB.Metadata);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            QueryPreviewA = QueryPreviewB = "Cannot generate a query yet: " + ex.Message;
        }
    }

    [RelayCommand]
    private async Task EstimateRowsAsync()
    {
        if (ConfigProvider?.Invoke() is not { } config)
            return;
        if (_sourceA.Connector is null || _sourceB.Connector is null)
        {
            _dialogs.ShowInfo("Connect both models first.");
            return;
        }

        var issues = MappingValidator.Validate(config, _sourceA.Metadata, _sourceB.Metadata);
        if (MappingValidator.HasErrors(issues))
        {
            _dialogs.ShowInfo("Fix the mapping errors first:" + Environment.NewLine + string.Join(Environment.NewLine, issues.Where(i => i.Severity == IssueSeverity.Error)));
            return;
        }

        EstimateText = "Estimating…";
        try
        {
            var a = _runner.EstimateRowsAsync(_sourceA.Connector, config, Side.A, _sourceA.Metadata, CancellationToken.None);
            var b = _runner.EstimateRowsAsync(_sourceB.Connector, config, Side.B, _sourceB.Metadata, CancellationToken.None);
            await Task.WhenAll(a, b);
            EstimateText = $"Estimated keys – A: {a.Result:N0}, B: {b.Result:N0}." +
                           (a.Result > MaxRows || b.Result > MaxRows ? $" Exceeds the row cap ({MaxRows:N0}): the run would be Partial. Add filters or raise the cap." : "");
        }
        catch (Exception ex)
        {
            EstimateText = "Estimate failed.";
            _dialogs.ShowError(ex);
        }
    }
}
