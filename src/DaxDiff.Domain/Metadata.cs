using System.Text.Json.Serialization;

namespace DaxDiff.Domain;

/// <summary>Normalized data type of a column or measure result.</summary>
public enum DataKind
{
    Unknown,
    Text,
    Integer,
    Decimal,
    Double,
    Boolean,
    DateTime,
}

public enum FieldKind
{
    Column,
    Measure,
}

/// <summary>A qualified reference to a model column or measure.</summary>
public sealed record FieldRef(string Table, string Name, FieldKind Kind)
{
    /// <summary>Display name such as <c>'Date'[Year]</c> or <c>[Total Sales]</c>.</summary>
    [JsonIgnore]
    public string QualifiedName => Kind == FieldKind.Measure
        ? $"[{Name}]"
        : $"'{Table}'[{Name}]";

    public override string ToString() => Kind == FieldKind.Measure ? $"{Table}.[{Name}]" : QualifiedName;
}

public sealed record ColumnInfo(string Table, string Name, DataKind DataKind, bool IsHidden)
{
    public FieldRef Ref => new(Table, Name, FieldKind.Column);
}

public sealed record MeasureInfo(string Table, string Name, DataKind DataKind, bool IsHidden, string? DisplayFolder)
{
    public FieldRef Ref => new(Table, Name, FieldKind.Measure);
}

public sealed record TableInfo(string Name, bool IsHidden, bool IsCalculationGroup, IReadOnlyList<ColumnInfo> Columns);

public sealed record RelationshipInfo(
    string FromTable,
    string FromColumn,
    string ToTable,
    string ToColumn,
    bool IsActive,
    string CrossFilter,
    string Cardinality);

/// <summary>Discovered model metadata for one side (FR-005).</summary>
public sealed record ModelMetadata
{
    public required string ModelName { get; init; }
    public required IReadOnlyList<TableInfo> Tables { get; init; }
    public required IReadOnlyList<MeasureInfo> Measures { get; init; }
    public IReadOnlyList<RelationshipInfo> Relationships { get; init; } = [];
    public DateTimeOffset RetrievedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Last data refresh, if the transport exposes it.</summary>
    public DateTimeOffset? LastRefreshUtc { get; init; }

    /// <summary>Notes about the model that can legitimately produce differences (§4.4).</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    public IEnumerable<ColumnInfo> AllColumns => Tables.SelectMany(t => t.Columns);

    public ColumnInfo? FindColumn(string table, string name) =>
        AllColumns.FirstOrDefault(c =>
            string.Equals(c.Table, table, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Measure names are unique per model, so the table is informational only.</summary>
    public MeasureInfo? FindMeasure(string name) =>
        Measures.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));

    public bool Contains(FieldRef field) => field.Kind == FieldKind.Measure
        ? FindMeasure(field.Name) is not null
        : FindColumn(field.Table, field.Name) is not null;

    public DataKind KindOf(FieldRef field) => field.Kind == FieldKind.Measure
        ? FindMeasure(field.Name)?.DataKind ?? DataKind.Unknown
        : FindColumn(field.Table, field.Name)?.DataKind ?? DataKind.Unknown;
}
