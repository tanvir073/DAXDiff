using DaxDiff.Domain;

namespace DaxDiff.Tests.Unit.Fixtures;

/// <summary>Synthetic models and configurations shared by the tests.</summary>
internal static class Fixture
{
    public static FieldRef Col(string table, string name) => new(table, name, FieldKind.Column);

    public static FieldRef Measure(string table, string name) => new(table, name, FieldKind.Measure);

    public static FieldPair Key(string alias, FieldRef left, FieldRef? right = null) =>
        new() { Left = left, Right = right ?? left, Role = FieldRole.Key, Alias = alias };

    public static FieldPair Value(string alias, FieldRef left, FieldRef right, Aggregation leftAgg = Aggregation.None, Aggregation? rightAgg = null) =>
        new() { Left = left, Right = right, Role = FieldRole.Value, Alias = alias, LeftAggregation = leftAgg, RightAggregation = rightAgg ?? leftAgg };

    public static ComparisonField Field(FieldRef field, string? synonym = null, bool isKey = false, Aggregation aggregation = Aggregation.None) =>
        new() { Field = field, Synonym = synonym ?? field.Name, IsKey = isKey, Aggregation = aggregation };

    /// <summary>Changes a configuration's options, e.g. <c>config.With(o => o with { NullPolicy = ... })</c>.</summary>
    public static ComparisonConfig With(this ComparisonConfig config, Func<ComparisonOptions, ComparisonOptions> change) =>
        config with { Options = change(config.Options) };

    /// <summary>Year + Region grain, one measure named differently on each side (§4.5).</summary>
    public static ComparisonConfig YearRegionSales() => new()
    {
        FieldPairs =
        [
            Key("Year", Col("Date", "Year")),
            Key("Region", Col("Geography", "Region")),
            Value("Sales", Measure("Sales", "Total Sales"), Measure("Fact", "Sales Amount")),
        ],
    };

    public static QueryResult Result(int columns, params object?[][] rows) => new()
    {
        ColumnNames = Enumerable.Range(0, columns).Select(i => $"C{i}").ToList(),
        Rows = rows,
    };

    public static object?[] Row(params object?[] values) => values;

    public static ModelMetadata ModelA() => new()
    {
        ModelName = "Sales A",
        Tables =
        [
            new TableInfo("Date", false, false, [new ColumnInfo("Date", "Year", DataKind.Integer, false), new ColumnInfo("Date", "Date", DataKind.DateTime, false)]),
            new TableInfo("Geography", false, false, [new ColumnInfo("Geography", "Region", DataKind.Text, false), new ColumnInfo("Geography", "Country Code", DataKind.Text, false)]),
            new TableInfo("Sales", false, false, [new ColumnInfo("Sales", "Amount", DataKind.Decimal, true)]),
        ],
        Measures =
        [
            new MeasureInfo("Sales", "Total Sales", DataKind.Decimal, false, null),
            new MeasureInfo("Sales", "Order Count", DataKind.Integer, false, null),
            new MeasureInfo("Sales", "Revenue", DataKind.Decimal, false, null),
        ],
    };

    public static ModelMetadata ModelB() => new()
    {
        ModelName = "Sales B",
        Tables =
        [
            new TableInfo("Calendar", false, false, [new ColumnInfo("Calendar", "Year", DataKind.Integer, false)]),
            new TableInfo("Date", false, false, [new ColumnInfo("Date", "Year", DataKind.Integer, false)]),
            new TableInfo("Geography", false, false, [new ColumnInfo("Geography", "Region", DataKind.Text, false), new ColumnInfo("Geography", "CountryCode", DataKind.Integer, false)]),
            new TableInfo("Fact", false, false, [new ColumnInfo("Fact", "Amount", DataKind.Decimal, true)]),
        ],
        Measures =
        [
            new MeasureInfo("Fact", "Sales Amount", DataKind.Decimal, false, null),
            new MeasureInfo("Fact", "order_count", DataKind.Integer, false, null),
            new MeasureInfo("Fact", "Turnover", DataKind.Decimal, false, null),
        ],
    };
}
