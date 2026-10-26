using System.Text.Json.Serialization;

namespace DaxDiff.Domain;

public enum FieldRole
{
    /// <summary>Part of the composite key at the comparison grain.</summary>
    Dimension,

    /// <summary>A value evaluated on both sides per key and compared.</summary>
    Measure,
}

/// <summary>A pairing of a model A field with a model B field (FR-006).</summary>
public sealed record FieldPair
{
    public required FieldRef Left { get; init; }
    public required FieldRef Right { get; init; }
    public required FieldRole Role { get; init; }

    /// <summary>User-friendly label shown in the grid and in the workbook.</summary>
    public required string Alias { get; init; }

    /// <summary>Auto-match suggestions start unconfirmed and never run until accepted (FR-007).</summary>
    public bool IsConfirmed { get; init; } = true;
}

public enum FilterOperator
{
    In,
    NotIn,
}

/// <summary>
/// A governed filter applied symmetrically: the same literal values are applied to a mapped column on each side.
/// Values are typed literals, never raw DAX (FR-009).
/// </summary>
public sealed record FilterPair
{
    public required FieldRef LeftColumn { get; init; }
    public required FieldRef RightColumn { get; init; }
    public FilterOperator Operator { get; init; } = FilterOperator.In;
    public required IReadOnlyList<string> Values { get; init; }
    public string SemanticMeaning { get; init; } = "";
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

public sealed record ExecutionLimits
{
    /// <summary>Maximum rows retrieved per side. Exceeding it marks the run Partial (NFR-05).</summary>
    public int MaxRows { get; init; } = 100_000;

    public int TimeoutSeconds { get; init; } = 120;
    public int RetryCount { get; init; } = 2;
}

/// <summary>Everything that determines a comparison run, apart from source data and security context (FR-008).</summary>
public sealed record ComparisonConfig
{
    public IReadOnlyList<FieldPair> FieldPairs { get; init; } = [];
    public IReadOnlyList<FilterPair> Filters { get; init; } = [];
    public ToleranceSettings Tolerance { get; init; } = new();
    public KeyNormalization KeyNormalization { get; init; } = new();
    public NullPolicy NullPolicy { get; init; } = NullPolicy.Strict;
    public TextComparison TextComparison { get; init; } = TextComparison.Ordinal;
    public DuplicateKeyPolicy DuplicateKeyPolicy { get; init; } = DuplicateKeyPolicy.Fail;
    public PercentBaseline PercentBaseline { get; init; } = PercentBaseline.A;
    public ExecutionLimits Limits { get; init; } = new();

    [JsonIgnore]
    public IReadOnlyList<FieldPair> Dimensions => FieldPairs.Where(p => p.Role == FieldRole.Dimension).ToList();
    [JsonIgnore]
    public IReadOnlyList<FieldPair> Measures => FieldPairs.Where(p => p.Role == FieldRole.Measure).ToList();
}
