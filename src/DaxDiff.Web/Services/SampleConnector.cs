using DaxDiff.Connectors;
using DaxDiff.Domain;

namespace DaxDiff.Web.Services;

/// <summary>
/// An in-memory model for the sample project, so the web app can be tried without signing in. It answers every query
/// with the shape of the sample comparison (Region, Year keys; summed Amount and Units), whatever the DAX says.
/// </summary>
public sealed class SampleConnector : IModelConnector
{
    private static readonly string[] Regions = ["North", "South", "East", "West", "Central"];
    private static readonly int[] Years = [2023, 2024, 2025];

    private readonly List<object?[]> _rows;

    private SampleConnector(Side side)
    {
        Reference = new ModelReference
        {
            Kind = SourceKind.ServiceRest,
            DisplayName = $"Sales (sample {side})",
            WorkspaceName = "Sample workspace",
            DatasetName = $"Sales (sample {side})",
        };
        _rows = side == Side.A ? RowsA() : RowsB();
    }

    public static SampleConnector For(Side side) => new(side);

    public ModelReference Reference { get; }

    public ModelMetadata Metadata { get; } = new()
    {
        ModelName = "Sales (sample)",
        Tables =
        [
            new TableInfo("Sales", false, false,
            [
                new ColumnInfo("Sales", "Region", DataKind.Text, false),
                new ColumnInfo("Sales", "Year", DataKind.Integer, false),
                new ColumnInfo("Sales", "Amount", DataKind.Decimal, false),
                new ColumnInfo("Sales", "Units", DataKind.Integer, false),
            ]),
        ],
        Measures = [],
        Warnings = ["Sample data generated in the browser; no Power BI model is queried."],
    };

    public ConnectorCapabilities Capabilities { get; } = new()
    {
        TransportName = "Sample data (in browser)",
        CanQuery = true,
        Metadata = MetadataSupport.Full,
        SecurityContext = "None (sample)",
    };

    public string SecurityContext => "Sample data, no sign-in";

    /// <summary>The sample comparison: Region and Year as keys, Amount and Units summed.</summary>
    public static ComparisonDefinition Comparison()
    {
        ComparisonField Key(string name) => new() { Field = new FieldRef("Sales", name, FieldKind.Column), IsKey = true, Synonym = name };
        ComparisonField Sum(string name) => new() { Field = new FieldRef("Sales", name, FieldKind.Column), Aggregation = Aggregation.Sum, Synonym = name };
        IReadOnlyList<ComparisonField> fields = [Key("Region"), Key("Year"), Sum("Amount"), Sum("Units")];
        return new ComparisonDefinition { Name = "Sales by region and year", FieldsA = fields, FieldsB = fields };
    }

    public Task ConnectAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<ModelMetadata> DiscoverMetadataAsync(CancellationToken cancellationToken) => Task.FromResult(Metadata);

    public async Task<QueryResult> ExecuteDaxAsync(string dax, QueryOptions options, CancellationToken cancellationToken)
    {
        await Task.Delay(300, cancellationToken);
        var rows = _rows.Take(options.MaxRows).ToList();
        return new QueryResult
        {
            ColumnNames = ["Sales[Region]", "Sales[Year]", "[V0]", "[V1]"],
            Rows = rows,
            IsTruncated = rows.Count < _rows.Count,
            TruncationReason = rows.Count < _rows.Count ? $"Result exceeded the row cap of {options.MaxRows:N0}." : null,
        };
    }

    private static List<object?[]> RowsA() =>
        (from region in Regions
         from year in Years
         let i = Array.IndexOf(Regions, region) + 1
         select new object?[] { region, (long)year, 1000m * i + 37.25m * (year - 2020), (long)(40 * i + year % 7) }).ToList();

    /// <summary>Model A's rows with a few planted differences: changed values, a missing key and an extra key.</summary>
    private static List<object?[]> RowsB()
    {
        var rows = RowsA().Where(r => !(Equals(r[0], "Central") && Equals(r[1], 2023L))).ToList();
        foreach (var row in rows)
        {
            if (Equals(row[0], "East") && Equals(row[1], 2024L))
                row[2] = (decimal)row[2]! + 125.50m;
            if (Equals(row[0], "West") && Equals(row[1], 2025L))
                row[3] = (long)row[3]! - 3;
        }

        rows.Add(["Nordic", 2025L, 812.40m, 18L]);
        return rows;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
