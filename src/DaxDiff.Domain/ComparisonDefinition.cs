namespace DaxDiff.Domain;

/// <summary>How a column is reduced to one value per key (FR-017).</summary>
public enum Aggregation
{
    /// <summary>No aggregation: the field is a key, or a measure evaluated as defined in the model.</summary>
    None,
    Sum,
    Average,
    Min,
    Max,
    Count,
    DistinctCount,

    /// <summary>Distinct values joined with ", " in ascending order.</summary>
    Concatenate,
}

/// <summary>One field chosen for a comparison on one side (FR-006).</summary>
public sealed record ComparisonField
{
    public required FieldRef Field { get; init; }

    /// <summary>Part of the comparison grain. Keys must be columns and are never aggregated.</summary>
    public bool IsKey { get; init; }

    public Aggregation Aggregation { get; init; }

    /// <summary>
    /// The name this field is compared under. A model A field and a model B field with the same synonym
    /// (case-insensitive, trimmed) are compared with each other (FR-018). Defaults to the field name.
    /// </summary>
    public required string Synonym { get; init; }
}

/// <summary>A named, saved comparison: the fields picked on each side and the rules to compare them by.</summary>
public sealed record ComparisonDefinition
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = "Comparison 1";
    public IReadOnlyList<ComparisonField> FieldsA { get; init; } = [];
    public IReadOnlyList<ComparisonField> FieldsB { get; init; } = [];
    public ComparisonOptions Options { get; init; } = new();
}

/// <summary>Which aggregations are offered for which field, and what type they return.</summary>
public static class AggregationRules
{
    // None comes first for non-key columns: it is the starting value, and must be replaced before the column can be compared.
    private static readonly Aggregation[] Numeric =
        [Aggregation.None, Aggregation.Sum, Aggregation.Average, Aggregation.Min, Aggregation.Max, Aggregation.Count, Aggregation.DistinctCount];

    private static readonly Aggregation[] Text =
        [Aggregation.None, Aggregation.Concatenate, Aggregation.Min, Aggregation.Max, Aggregation.Count, Aggregation.DistinctCount];

    private static readonly Aggregation[] Dates =
        [Aggregation.None, Aggregation.Min, Aggregation.Max, Aggregation.Count, Aggregation.DistinctCount];

    private static readonly Aggregation[] Counts = [Aggregation.None, Aggregation.Count, Aggregation.DistinctCount];

    /// <summary>Type not known (metadata not loaded or not reported): nothing can be ruled out.</summary>
    private static readonly Aggregation[] Unknown =
        [Aggregation.None, Aggregation.Sum, Aggregation.Average, Aggregation.Min, Aggregation.Max, Aggregation.Count, Aggregation.DistinctCount, Aggregation.Concatenate];

    /// <summary>
    /// Aggregations a field may select. Keys and measures only allow <see cref="Aggregation.None"/>; non-key columns
    /// also list <see cref="Aggregation.None"/> (their starting value), which validation rejects until one is chosen.
    /// </summary>
    public static IReadOnlyList<Aggregation> Allowed(FieldKind kind, DataKind dataKind, bool isKey)
    {
        if (isKey || kind == FieldKind.Measure)
            return [Aggregation.None];
        return dataKind switch
        {
            DataKind.Integer or DataKind.Decimal or DataKind.Double => Numeric,
            DataKind.Text => Text,
            DataKind.DateTime => Dates,
            DataKind.Boolean => Counts,
            _ => Unknown,
        };
    }

    /// <summary>
    /// Inserted fields start with no aggregation: the user decides for each non-key column (FR-017). Nothing is
    /// summed or concatenated without being asked for.
    /// </summary>
    public static Aggregation Default(FieldKind kind, DataKind dataKind, bool isKey) => Aggregation.None;

    /// <summary>True when the field is compared as a value and still needs an aggregation chosen.</summary>
    public static bool NeedsAggregation(FieldKind kind, bool isKey, Aggregation aggregation) =>
        !isKey && kind == FieldKind.Column && aggregation == Aggregation.None;

    public static bool IsAllowed(Aggregation aggregation, FieldKind kind, DataKind dataKind, bool isKey) =>
        Allowed(kind, dataKind, isKey).Contains(aggregation);

    /// <summary>The data type of the aggregated value, used to check that paired values are comparable.</summary>
    public static DataKind ResultKind(Aggregation aggregation, DataKind dataKind) => aggregation switch
    {
        Aggregation.Count or Aggregation.DistinctCount => DataKind.Integer,
        Aggregation.Concatenate => DataKind.Text,
        Aggregation.Average => DataKind.Double,
        _ => dataKind,
    };
}
