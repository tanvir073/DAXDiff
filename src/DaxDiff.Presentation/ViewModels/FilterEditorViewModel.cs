using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DaxDiff.Comparison;
using DaxDiff.Connectors;
using DaxDiff.Domain;
using DaxDiff.Presentation.Resources;

namespace DaxDiff.Presentation.ViewModels;

/// <summary>What the filter editor can see: both models' metadata and connections, and the comparison's synonym pairs.</summary>
public sealed record FilterEditorContext(
    ModelMetadata? MetadataA,
    ModelMetadata? MetadataB,
    IModelConnector? ConnectorA,
    IModelConnector? ConnectorB,
    IReadOnlyList<FieldPair> Pairs);

/// <summary>One value in a basic filter's list.</summary>
public sealed partial class FilterValueItem : ObservableObject
{
    private readonly Action<FilterValueItem> _changed;

    public FilterValueItem(string? value, bool isSelected, Action<FilterValueItem> changed)
    {
        Value = value;
        _isSelected = isSelected;
        _changed = changed;
    }

    /// <summary>The value as an invariant typed literal; null is BLANK.</summary>
    public string? Value { get; }

    public string Display => Value ?? Strings.Blank;

    [ObservableProperty]
    private bool _isSelected;

    partial void OnIsSelectedChanged(bool value) => _changed(this);
}

/// <summary>An entry in an operator drop-down; <see cref="Operator"/> null means "no second condition".</summary>
public sealed record OperatorOption(ConditionOperator? Operator, string Text)
{
    public override string ToString() => Text;
}

/// <summary>One filter being edited: the column on each side, then basic (pick values) or advanced (conditions) rules.</summary>
public sealed partial class FilterItemViewModel : ObservableObject
{
    private const int MaxValues = 1000;
    private readonly FilterEditorContext _context;
    private readonly HashSet<string> _selected = new(StringComparer.Ordinal);
    private bool _blankSelected;
    private CancellationTokenSource? _loadCts;

    public FilterItemViewModel(FilterEditorContext context, FilterPair? filter = null)
    {
        _context = context;
        ColumnsA = context.MetadataA?.AllColumns.Select(c => c.Ref).OrderBy(c => c.Table).ThenBy(c => c.Name).ToList() ?? [];
        ColumnsB = context.MetadataB?.AllColumns.Select(c => c.Ref).OrderBy(c => c.Table).ThenBy(c => c.Name).ToList() ?? [];
        RefreshOperators();
        if (filter is null)
            return;

        _leftColumn = filter.LeftColumn;
        _rightColumn = filter.RightColumn;
        _isAdvanced = filter.Kind == FilterKind.Advanced;
        _exclude = filter.Operator == FilterOperator.NotIn;
        _selected.UnionWith(filter.Values);
        _blankSelected = filter.IncludeBlank;
        _meaning = filter.SemanticMeaning;
        _joinOr = filter.Join == ConditionJoin.Or;
        RefreshOperators();
        _condition1 = Option(filter.Conditions.ElementAtOrDefault(0)?.Operator) ?? Condition1Options[0];
        _condition1Value = filter.Conditions.ElementAtOrDefault(0)?.Value ?? "";
        _condition2 = filter.Conditions.Count > 1 ? Option(filter.Conditions[1].Operator) ?? Condition2Options[0] : Condition2Options[0];
        _condition2Value = filter.Conditions.ElementAtOrDefault(1)?.Value ?? "";
        RebuildValueList([]);
    }

    public IReadOnlyList<FieldRef> ColumnsA { get; }
    public IReadOnlyList<FieldRef> ColumnsB { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary), nameof(Issues), nameof(LeftKind))]
    private FieldRef? _leftColumn;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary), nameof(Issues))]
    private FieldRef? _rightColumn;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBasic), nameof(Summary), nameof(Issues))]
    private bool _isAdvanced;

    public bool IsBasic { get => !IsAdvanced; set => IsAdvanced = !value; }

    /// <summary>Basic: "is not" (remove the selected values) instead of "is".</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary), nameof(Issues))]
    private bool _exclude;

    public ObservableCollection<FilterValueItem> Values { get; } = [];

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private string _valuesStatus = "";

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _manualValue = "";

    [ObservableProperty]
    private string _meaning = "";

    [ObservableProperty]
    private IReadOnlyList<OperatorOption> _condition1Options = [];

    [ObservableProperty]
    private IReadOnlyList<OperatorOption> _condition2Options = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary), nameof(Issues), nameof(Condition1NeedsValue))]
    private OperatorOption? _condition1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary), nameof(Issues))]
    private string _condition1Value = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary), nameof(Issues), nameof(Condition2NeedsValue), nameof(HasCondition2))]
    private OperatorOption? _condition2;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary), nameof(Issues))]
    private string _condition2Value = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(JoinAnd), nameof(Summary))]
    private bool _joinOr;

    public bool JoinAnd { get => !JoinOr; set => JoinOr = !value; }

    public bool Condition1NeedsValue => Condition1?.Operator is { } op && FilterPair.NeedsValue(op);
    public bool HasCondition2 => Condition2?.Operator is not null;
    public bool Condition2NeedsValue => Condition2?.Operator is { } op && FilterPair.NeedsValue(op);

    public DataKind LeftKind => LeftColumn is null ? DataKind.Unknown : _context.MetadataA?.KindOf(LeftColumn) ?? DataKind.Unknown;

    public string SelectedCountText => Localized.Format(Strings.Filter_SelectedCount, _selected.Count + (_blankSelected ? 1 : 0));

    public string Summary => ToFilter()?.Describe() ?? Strings.Filter_ChooseColumn;

    /// <summary>Problems with this filter as it stands; empty when it is valid.</summary>
    public string Issues
    {
        get
        {
            if (LeftColumn is null || RightColumn is null)
                return LeftColumn is null ? Strings.Filter_ChooseColumnA : Strings.Filter_ChooseColumnB;
            var issues = MappingValidator.ValidateFilter(ToFilter()!, _context.MetadataA, _context.MetadataB).Select(i => i.Message);
            return string.Join(Environment.NewLine, issues);
        }
    }

    public FilterPair? ToFilter()
    {
        if (LeftColumn is null || RightColumn is null)
            return null;
        var conditions = new List<FilterCondition>();
        if (Condition1?.Operator is { } op1)
            conditions.Add(new FilterCondition(op1, FilterPair.NeedsValue(op1) ? Condition1Value.Trim() : ""));
        if (Condition2?.Operator is { } op2)
            conditions.Add(new FilterCondition(op2, FilterPair.NeedsValue(op2) ? Condition2Value.Trim() : ""));
        return new FilterPair
        {
            LeftColumn = LeftColumn,
            RightColumn = RightColumn,
            Kind = IsAdvanced ? FilterKind.Advanced : FilterKind.Basic,
            Operator = Exclude ? FilterOperator.NotIn : FilterOperator.In,
            Values = IsAdvanced ? [] : _selected.Order(StringComparer.Ordinal).ToList(),
            IncludeBlank = !IsAdvanced && _blankSelected,
            Conditions = IsAdvanced ? conditions : [],
            Join = JoinOr ? ConditionJoin.Or : ConditionJoin.And,
            SemanticMeaning = Meaning.Trim(),
        };
    }

    partial void OnLeftColumnChanged(FieldRef? value)
    {
        RightColumn = Counterpart(value) ?? RightColumn;
        _selected.Clear();
        _blankSelected = false;
        RefreshOperators();
        _ = LoadValuesAsync();
    }

    partial void OnRightColumnChanged(FieldRef? value) => _ = LoadValuesAsync();

    partial void OnIsAdvancedChanged(bool value)
    {
        if (!value)
            _ = LoadValuesAsync();
    }

    partial void OnSearchTextChanged(string value) => _ = LoadValuesAsync(debounce: true);

    /// <summary>The same column in model B, or the B column that shares its synonym in this comparison.</summary>
    private FieldRef? Counterpart(FieldRef? left)
    {
        if (left is null)
            return null;
        if (_context.MetadataB?.FindColumn(left.Table, left.Name) is { } same)
            return same.Ref;
        return _context.Pairs.FirstOrDefault(p => p.Left == left && p.Right.Kind == FieldKind.Column)?.Right;
    }

    private void RefreshOperators()
    {
        Condition1Options = FilterPair.OperatorsFor(LeftKind).Select(o => new OperatorOption(o, FilterPair.OperatorText(o))).ToList();
        Condition2Options = [new OperatorOption(null, Strings.Filter_NoSecondCondition), .. Condition1Options];
        Condition1 = Option(Condition1?.Operator) ?? Condition1Options.FirstOrDefault();
        Condition2 = Option(Condition2?.Operator) ?? Condition2Options[0];
    }

    private OperatorOption? Option(ConditionOperator? op) =>
        op is null ? null : Condition1Options.FirstOrDefault(o => o.Operator == op);

    /// <summary>Loads the distinct values of the chosen columns from both models (merged), optionally narrowed by the search text.</summary>
    public async Task LoadValuesAsync(bool debounce = false)
    {
        _loadCts?.Cancel();
        var cts = _loadCts = new CancellationTokenSource();
        if (IsAdvanced || LeftColumn is null)
            return;

        try
        {
            if (debounce)
                await Task.Delay(350, cts.Token);

            var sources = new List<(IModelConnector Connector, FieldRef Column, DataKind Kind)>();
            if (_context.ConnectorA is { } a)
                sources.Add((a, LeftColumn, LeftKind));
            if (_context.ConnectorB is { } b && RightColumn is not null)
                sources.Add((b, RightColumn, _context.MetadataB?.KindOf(RightColumn) ?? DataKind.Unknown));
            if (sources.Count == 0)
            {
                RebuildValueList([]);
                ValuesStatus = Strings.Filter_NotConnected;
                return;
            }

            IsLoading = true;
            ValuesStatus = Strings.Filter_LoadingValues;
            var values = new SortedSet<string>(StringComparer.Ordinal);
            var hasBlank = false;
            var more = false;
            foreach (var (connector, column, kind) in sources)
            {
                var dax = DaxQueryBuilder.BuildDistinctValuesQuery(column, kind, SearchText, MaxValues + 1);
                var result = await connector.ExecuteDaxAsync(dax, new QueryOptions(MaxValues + 1, TimeSpan.FromSeconds(60)), cts.Token);
                more |= result.Rows.Count > MaxValues || result.IsTruncated;
                foreach (var row in result.Rows.Take(MaxValues))
                {
                    var text = Format(row[0]);
                    if (text is null)
                        hasBlank = true;
                    else
                        values.Add(text);
                }
            }

            if (cts.IsCancellationRequested)
                return;
            RebuildValueList(values, hasBlank || string.IsNullOrWhiteSpace(SearchText));
            ValuesStatus = more
                ? Localized.Format(Strings.Filter_ShowingFirst, MaxValues)
                : Localized.Format(sources.Count == 2 ? Strings.Filter_DistinctValuesAB : Strings.Filter_DistinctValuesA, values.Count + (hasBlank ? 1 : 0));
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            ValuesStatus = Localized.Format(Strings.Filter_LoadValuesFailed, ex is DaxDiffException d ? d.Message : ex.Message);
        }
        finally
        {
            if (_loadCts == cts)
                IsLoading = false;
        }
    }

    /// <summary>Shows loaded values plus any selected values that the current search did not return, so selections are never hidden.</summary>
    private void RebuildValueList(IEnumerable<string> loaded, bool showBlank = true)
    {
        var all = new SortedSet<string>(loaded, StringComparer.Ordinal);
        all.UnionWith(_selected);
        Values.Clear();
        if (showBlank || _blankSelected)
            Values.Add(new FilterValueItem(null, _blankSelected, OnValueToggled));
        foreach (var value in all)
            Values.Add(new FilterValueItem(value, _selected.Contains(value), OnValueToggled));
        OnPropertyChanged(nameof(SelectedCountText));
    }

    private void OnValueToggled(FilterValueItem item)
    {
        if (item.Value is null)
            _blankSelected = item.IsSelected;
        else if (item.IsSelected)
            _selected.Add(item.Value);
        else
            _selected.Remove(item.Value);
        OnPropertyChanged(nameof(SelectedCountText));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(Issues));
    }

    [RelayCommand]
    private void SelectAllShown()
    {
        foreach (var item in Values)
            item.IsSelected = true;
    }

    [RelayCommand]
    private void ClearSelection()
    {
        _selected.Clear();
        _blankSelected = false;
        foreach (var item in Values)
            item.IsSelected = false;
    }

    /// <summary>Adds a typed value by hand, e.g. when the models are not connected.</summary>
    [RelayCommand]
    private void AddManualValue()
    {
        var value = ManualValue.Trim();
        if (value.Length == 0)
            return;
        _selected.Add(value);
        ManualValue = "";
        RebuildValueList(Values.Where(v => v.Value is not null).Select(v => v.Value!));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(Issues));
    }

    /// <summary>Invariant text that <see cref="DaxQueryBuilder.TypedLiteral"/> parses back; null for BLANK.</summary>
    public static string? Format(object? value) => value switch
    {
        null or DBNull => null,
        DateTime dt => dt.TimeOfDay == TimeSpan.Zero
            ? dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        bool b => b ? "true" : "false",
        string s => s,
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };
}

/// <summary>The filter pop-up for one comparison: a list of filters on the left, the selected filter's editor on the right (FR-020).</summary>
public sealed partial class FilterEditorViewModel : ObservableObject
{
    private readonly FilterEditorContext _context;

    public FilterEditorViewModel(string comparisonName, FilterEditorContext context, IEnumerable<FilterPair> filters)
    {
        _context = context;
        Title = Localized.Format(Strings.Filter_WindowTitle, comparisonName);
        foreach (var filter in filters)
            Filters.Add(new FilterItemViewModel(context, filter));
        Selected = Filters.FirstOrDefault();
    }

    public string Title { get; }

    public bool ModelsConnected => _context.MetadataA is not null && _context.MetadataB is not null;

    public ObservableCollection<FilterItemViewModel> Filters { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyCanExecuteChangedFor(nameof(RemoveFilterCommand))]
    private FilterItemViewModel? _selected;

    public bool HasSelection => Selected is not null;

    partial void OnSelectedChanged(FilterItemViewModel? value)
    {
        if (value is { IsBasic: true, LeftColumn: not null })
            _ = value.LoadValuesAsync();
    }

    [RelayCommand]
    private void AddFilter()
    {
        var item = new FilterItemViewModel(_context);
        Filters.Add(item);
        Selected = item;
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void RemoveFilter()
    {
        if (Selected is null)
            return;
        var index = Filters.IndexOf(Selected);
        Filters.Remove(Selected);
        Selected = Filters.Count == 0 ? null : Filters[Math.Min(index, Filters.Count - 1)];
    }

    /// <summary>The filters to keep, or the problems that stop them being applied.</summary>
    public (IReadOnlyList<FilterPair> Filters, string Problems) Collect()
    {
        var problems = Filters
            .Select((f, i) => (f, i))
            .Where(x => x.f.Issues.Length > 0)
            .Select(x => Localized.Format(Strings.Filter_Problem, x.i + 1, x.f.Summary, x.f.Issues))
            .ToList();
        return (Filters.Select(f => f.ToFilter()).OfType<FilterPair>().ToList(), string.Join(Environment.NewLine, problems));
    }
}
