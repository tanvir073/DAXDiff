using DaxDiff.Application;
using DaxDiff.Domain;
using static DaxDiff.Tests.Unit.Fixtures.Fixture;

namespace DaxDiff.Tests.Unit;

public class ProjectStoreTests
{
    private static ComparisonDefinition SalesByRegion() => new()
    {
        Name = "Sales by region",
        FieldsA =
        [
            Field(Col("Date", "Year"), isKey: true),
            Field(Col("Geography", "Region"), isKey: true),
            Field(Col("Sales", "Amount"), "Amount", aggregation: Aggregation.Sum),
        ],
        FieldsB =
        [
            Field(Col("Date", "Year"), isKey: true),
            Field(Col("Geography", "Region"), isKey: true),
            Field(Col("Fact", "Amount"), "Amount", aggregation: Aggregation.Sum),
        ],
        Options = new ComparisonOptions
        {
            Tolerance = new ToleranceSettings { Mode = ToleranceMode.Tolerance, Absolute = 0.01m, Relative = 0.001m },
            NullPolicy = NullPolicy.BlankEqualsZero,
            Filters = [new FilterPair { LeftColumn = Col("Date", "Year"), RightColumn = Col("Date", "Year"), Values = ["2025"] }],
        },
    };

    private static ProjectFile Sample() => new()
    {
        Name = "Prod vs Dev",
        SourceA = new ModelReference
        {
            Kind = SourceKind.DesktopSession,
            DisplayName = "Sales.pbix",
            LocalPbixPath = @"C:\models\Sales.pbix",
            DesktopServer = "localhost:51234",
            DesktopDatabase = "f3a1-guid",
        },
        SourceB = new ModelReference
        {
            Kind = SourceKind.ServiceXmla,
            DisplayName = "Sales",
            WorkspaceId = "ws-id",
            WorkspaceName = "Finance",
            DatasetId = "ds-id",
            DatasetName = "Sales",
        },
        Comparisons =
        [
            SalesByRegion(),
            new ComparisonDefinition { Name = "Customers", FieldsA = [Field(Col("Customer", "Id"), isKey: true)], FieldsB = [Field(Col("Customer", "Id"), isKey: true)] },
        ],
    };

    [Fact]
    public void AT13_RoundTrip_PreservesComparisonsAndOptions()
    {
        var saved = Sample();
        var loaded = ProjectStore.Deserialize(ProjectStore.Serialize(saved));

        Assert.Equal("Prod vs Dev", loaded.Name);
        Assert.Equal(["Sales by region", "Customers"], loaded.Comparisons.Select(c => c.Name));
        var sales = loaded.Comparisons[0];
        Assert.Equal(saved.Comparisons[0].Id, sales.Id);
        Assert.Equal(saved.Comparisons[0].FieldsA, sales.FieldsA);
        Assert.Equal(saved.Comparisons[0].FieldsB, sales.FieldsB);
        Assert.Equal(Aggregation.Sum, sales.FieldsA[2].Aggregation);
        Assert.True(sales.FieldsB[0].IsKey);
        Assert.Equal(0.01m, sales.Options.Tolerance.Absolute);
        Assert.Equal(NullPolicy.BlankEqualsZero, sales.Options.NullPolicy);
        Assert.Equal(["2025"], sales.Options.Filters.Single().Values);
        Assert.Equal("Finance", loaded.SourceB!.WorkspaceName);
        Assert.Equal(@"C:\models\Sales.pbix", loaded.SourceA!.LocalPbixPath);
    }

    [Fact]
    public void AT13_SessionDetailsTokensAndResults_AreNotPersisted()
    {
        var json = ProjectStore.Serialize(Sample());

        Assert.DoesNotContain("51234", json);
        Assert.DoesNotContain("f3a1-guid", json);
        Assert.DoesNotContain("token", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("qualifiedName", json);
        Assert.Contains("\"schemaVersion\": 2", json);
        Assert.Contains("\"BlankEqualsZero\"", json);
        Assert.Contains("\"aggregation\": \"Sum\"", json);
    }

    [Fact]
    public void NFR08_V0Schema_IsMigrated()
    {
        const string legacy = """
            { "name": "Old", "config": { "limits": { "maxRowCount": 500, "timeoutSeconds": 30 } } }
            """;

        var project = ProjectStore.Deserialize(legacy);

        Assert.Equal(ProjectFile.CurrentSchemaVersion, project.SchemaVersion);
        var options = Assert.Single(project.Comparisons).Options;
        Assert.Equal(500, options.Limits.MaxRows);
        Assert.Equal(30, options.Limits.TimeoutSeconds);
    }

    [Fact]
    public void NFR08_V1PairsBecomeOneComparisonPairedBySynonym()
    {
        const string v1 = """
            {
              "schemaVersion": 1,
              "name": "Migrated",
              "config": {
                "fieldPairs": [
                  { "left": { "table": "Date", "name": "Year", "kind": "Column" }, "right": { "table": "Calendar", "name": "Yr", "kind": "Column" }, "role": "Dimension", "alias": "Year" },
                  { "left": { "table": "Sales", "name": "Total Sales", "kind": "Measure" }, "right": { "table": "Fact", "name": "Sales Amount", "kind": "Measure" }, "role": "Measure", "alias": "Sales", "isConfirmed": true }
                ],
                "nullPolicy": "BlankEqualsZero"
              }
            }
            """;

        var project = ProjectStore.Deserialize(v1);

        var comparison = Assert.Single(project.Comparisons);
        Assert.Equal("Comparison 1", comparison.Name);
        Assert.Equal(NullPolicy.BlankEqualsZero, comparison.Options.NullPolicy);
        Assert.Equal([true, false], comparison.FieldsA.Select(f => f.IsKey));
        Assert.Equal(["Year", "Sales"], comparison.FieldsB.Select(f => f.Synonym));
        Assert.Equal(Col("Calendar", "Yr"), comparison.FieldsB[0].Field);
        Assert.All(comparison.FieldsA, f => Assert.Equal(Aggregation.None, f.Aggregation));

        var compiled = Comparison.ComparisonCompiler.Compile(comparison);
        Assert.False(compiled.HasErrors);
        Assert.Equal(2, compiled.Config.FieldPairs.Count);
    }

    [Fact]
    public void NewerSchema_IsRejected()
    {
        var ex = Assert.Throws<DaxDiffException>(() => ProjectStore.Deserialize("""{ "schemaVersion": 99 }"""));
        Assert.Equal("PRJ-NEWER", ex.DiagnosticCode);
    }

    [Fact]
    public void Garbage_IsRejectedWithDiagnostic()
    {
        var ex = Assert.Throws<DaxDiffException>(() => ProjectStore.Deserialize("not json"));
        Assert.Equal("PRJ-INVALID", ex.DiagnosticCode);
    }

    [Fact]
    public async Task SaveAndLoad_File()
    {
        var path = Path.Combine(Path.GetTempPath(), $"daxdiff-{Guid.NewGuid():N}{ProjectFile.Extension}");
        try
        {
            await ProjectStore.SaveAsync(Sample(), path);
            var loaded = await ProjectStore.LoadAsync(path);
            Assert.Equal(2, loaded.Comparisons.Count);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
