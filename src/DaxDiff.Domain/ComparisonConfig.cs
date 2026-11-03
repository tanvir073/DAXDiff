using System.Text.Json.Serialization;
using System.Globalization;
using DaxDiff.Domain.Resources;

namespace DaxDiff.Domain;

public enum FieldRole
{
    /// <summary>Part of the composite key at the comparison grain.</summary>
    Key,

    /// <summary>A value evaluated on both sides per key and compared.</summary>
    Value,
}

/// <summary>A compiled pairing of a model A field with a model B field that share a synonym (FR-006).</summary>
public sealed record FieldPair
{
    public required FieldRef Left { get; init; }
    public required FieldRef Right { get; init; }
    public required FieldRole Role { get; init; }

    /// <summary>The shared synonym; used as the label in the grid and in the workbook.</summary>
    public required string Alias { get; init; }

    /// <summary>How the A column is aggregated per key. <see cref="Aggregation.None"/> for keys and measures.</summary>
    public Aggregation LeftAggregation { get; init; }

    public Aggregation RightAggregation { get; init; }
}

public enum FilterOperator
{
    In,
    NotIn,
}

/// <summary>The two filter styles of the Power BI filter pane (FR-020).</summary>
public enum FilterKind
{
    /// <summary>Pick values from a list: the column is (or is not) one of them.</summary>
    Basic,

    /// <summary>One or two conditions joined by And/Or.</summary>
    Advanced,
}

public enum ConditionOperator
{
    Is,
    IsNot,
    LessThan,
    LessThanOrEqual,
    GreaterThan,
    GreaterThanOrEqual,
    Contains,
    DoesNotContain,
    StartsWith,
    DoesNotStartWith,
    IsBlank,
    IsNotBlank,
}

public enum ConditionJoin
{
    And,
    Or,
}

/// <summary>One condition of an advanced filter. <see cref="Value"/> is a typed literal as text; unused for blank checks.</summary>
public sealed record FilterCondition(ConditionOperator Operator, string Value = "");

/// <summary>
/// A governed filter applied symmetrically: the same rule is applied to a mapped column on each side.
/// Values are typed literals, never raw DAX (FR-009, FR-020).
/// </summary>
public sealed record FilterPair
{
    public required FieldRef LeftColumn { get; init; }
    public required FieldRef RightColumn { get; init; }
    public FilterKind Kind { get; init; } = FilterKind.Basic;

    /// <summary>Basic: keep (<see cref="FilterOperator.In"/>) or remove (<see cref="FilterOperator.NotIn"/>) the listed values.</summary>
    public FilterOperator Operator { get; init; } = FilterOperator.In;

    /// <summary>Basic: the selected values.</summary>
    public IReadOnlyList<string> Values { get; init; } = [];

    /// <summary>Basic: BLANK is one of the selected values.</summary>
    public bool IncludeBlank { get; init; }

    /// <summary>Advanced: one or two conditions.</summary>
    public IReadOnlyList<FilterCondition> Conditions { get; init; } = [];

    public ConditionJoin Join { get; init; } = ConditionJoin.And;
    public string SemanticMeaning { get; init; } = "";

    /// <summary>Readable summary, e.g. "Region is North, South" or "Amount is greater than 100 and is less than 500".</summary>
    public string Describe()
    {
        if (Kind == FilterKind.Advanced)
        {
            var parts = Conditions.Select(c => c.Operator is ConditionOperator.IsBlank or ConditionOperator.IsNotBlank
                ? OperatorText(c.Operator)
                : $"{OperatorText(c.Operator)} {c.Value}");
            return $"{LeftColumn.Name} {string.Join($" {(Join == ConditionJoin.And ? Strings.Filter_JoinAnd : Strings.Filter_JoinOr)} ", parts)}";
        }

        var values = Values.Concat(IncludeBlank ? [Strings.Filter_Blank] : []).ToList();
        var shown = values.Count <= 5 ? string.Join(", ", values) : string.Format(CultureInfo.CurrentCulture, Strings.Filter_MoreValues, string.Join(", ", values.Take(5)), values.Count - 5);
        return $"{LeftColumn.Name} {(Operator == FilterOperator.In ? Strings.Filter_Is : Strings.Filter_IsNot)} {shown}";
    }

    public static string OperatorText(ConditionOperator op) => op switch
    {
        ConditionOperator.Is => Strings.Filter_Is,
        ConditionOperator.IsNot => Strings.Filter_IsNot,
        ConditionOperator.LessThan => Strings.Filter_LessThan,
        ConditionOperator.LessThanOrEqual => Strings.Filter_LessThanOrEqual,
        ConditionOperator.GreaterThan => Strings.Filter_GreaterThan,
        ConditionOperator.GreaterThanOrEqual => Strings.Filter_GreaterThanOrEqual,
        ConditionOperator.Contains => Strings.Filter_Contains,
        ConditionOperator.DoesNotContain => Strings.Filter_DoesNotContain,
        ConditionOperator.StartsWith => Strings.Filter_StartsWith,
        ConditionOperator.DoesNotStartWith => Strings.Filter_DoesNotStartWith,
        ConditionOperator.IsBlank => Strings.Filter_IsBlank,
        _ => Strings.Filter_IsNotBlank,
    };

    /// <summary>Operators offered for a column type, as in Power BI's advanced filtering.</summary>
    public static IReadOnlyList<ConditionOperator> OperatorsFor(DataKind kind) => kind switch
    {
        DataKind.Text => [ConditionOperator.Contains, ConditionOperator.DoesNotContain, ConditionOperator.StartsWith, ConditionOperator.DoesNotStartWith,
            ConditionOperator.Is, ConditionOperator.IsNot, ConditionOperator.IsBlank, ConditionOperator.IsNotBlank],
        DataKind.Boolean => [ConditionOperator.Is, ConditionOperator.IsNot, ConditionOperator.IsBlank, ConditionOperator.IsNotBlank],
        DataKind.Unknown => Enum.GetValues<ConditionOperator>(),
        _ => [ConditionOperator.Is, ConditionOperator.IsNot, ConditionOperator.LessThan, ConditionOperator.LessThanOrEqual,
            ConditionOperator.GreaterThan, ConditionOperator.GreaterThanOrEqual, ConditionOperator.IsBlank, ConditionOperator.IsNotBlank],
    };

    public static bool NeedsValue(ConditionOperator op) => op is not (ConditionOperator.IsBlank or ConditionOperator.IsNotBlank);
}

public enum ToleranceMode
{
    Exact,
    Tolerance,
}

/// <summary>abs(A−B) ≤ max(Absolute, Relative × max(|A|, |B|)) (§4.3).</summary>
public sealed record ToleranceSettings
{
    public ToleranceMode Mode { get; init; } = ToleranceMode.Exact;
    public decimal Absolute { get; init; }
    public decimal Relative { get; init; }
}

public enum UnicodeNormalization
{
    None,
    FormC,
    FormKC,
}

/// <summary>Explicit key normalization; nothing is implicit (§4.4).</summary>
public sealed record KeyNormalization
{
    public bool TrimWhitespace { get; init; }
    public bool CaseInsensitive { get; init; }
    public UnicodeNormalization Unicode { get; init; } = UnicodeNormalization.None;

    /// <summary>Treat BLANK and empty string as the same key member.</summary>
    public bool BlankEqualsEmpty { get; init; }

    /// <summary>Compare date keys on date only, discarding time of day.</summary>
    public bool DateOnly { get; init; }
}

public enum NullPolicy
{
    /// <summary>BLANK and zero are different values (default).</summary>
    Strict,

    /// <summary>BLANK on one side equals zero on the other.</summary>
    BlankEqualsZero,
}

public enum TextComparison
{
    Ordinal,
    OrdinalIgnoreCase,
}

public enum DuplicateKeyPolicy
{
    /// <summary>Duplicate normalized keys fail the run (default, fail safe).</summary>
    Fail,

    /// <summary>Duplicate keys are reported as a diagnostic category and excluded from matching.</summary>
    Diagnose,
}

public enum PercentBaseline
{
    A,
    B,
}

/// <summary>Which compared rows a run keeps for the Results page and the workbook.</summary>
public enum ResultRows
{
    /// <summary>Matches and differences.</summary>
    All,

    /// <summary>Mismatches, one-sided keys and invalid keys; matches are counted but not kept.</summary>
    DifferencesOnly,
}

/// <summary>
/// What a run keeps of its compared rows. The summary always counts every row; these settings only decide which
/// rows are listed. The defaults keep every row.
/// </summary>
public sealed record ResultSettings
{
    public ResultRows Rows { get; init; } = ResultRows.All;

    /// <summary>Most rows kept, differences before matches; null keeps every row.</summary>
    public int? MaxRows { get; init; }
}

public sealed record ExecutionLimits
{
    /// <summary>Maximum rows retrieved per side. Exceeding it marks the run Partial (NFR-05).</summary>
    public int MaxRows { get; init; } = 100_000;

    public int TimeoutSeconds { get; init; } = 120;
    public int RetryCount { get; init; } = 2;
}

/// <summary>Per-comparison rules: filters, tolerances, normalization and limits (FR-008).</summary>
public sealed record ComparisonOptions
{
    public IReadOnlyList<FilterPair> Filters { get; init; } = [];
    public ToleranceSettings Tolerance { get; init; } = new();
    public KeyNormalization KeyNormalization { get; init; } = new();
    public NullPolicy NullPolicy { get; init; } = NullPolicy.Strict;
    public TextComparison TextComparison { get; init; } = TextComparison.Ordinal;
    public DuplicateKeyPolicy DuplicateKeyPolicy { get; init; } = DuplicateKeyPolicy.Fail;
    public PercentBaseline PercentBaseline { get; init; } = PercentBaseline.A;
    public ExecutionLimits Limits { get; init; } = new();
    public ResultSettings Results { get; init; } = new();
}

/// <summary>
/// The compiled input of one run: field pairs resolved from a <see cref="ComparisonDefinition"/> by synonym,
/// plus its options. Determines the run apart from source data and security context (FR-008).
/// </summary>
public sealed record ComparisonConfig
{
    public IReadOnlyList<FieldPair> FieldPairs { get; init; } = [];
    public ComparisonOptions Options { get; init; } = new();

    [JsonIgnore]
    public IReadOnlyList<FieldPair> Keys => FieldPairs.Where(p => p.Role == FieldRole.Key).ToList();

    [JsonIgnore]
    public IReadOnlyList<FieldPair> Values => FieldPairs.Where(p => p.Role == FieldRole.Value).ToList();
}
