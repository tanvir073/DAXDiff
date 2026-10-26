using DaxDiff.Application;
using DaxDiff.Domain;
using static DaxDiff.Tests.Unit.Fixtures.Fixture;

namespace DaxDiff.Tests.Unit;

public class ProjectStoreTests
{
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
        Config = YearRegionSales() with
        {
            Tolerance = new ToleranceSettings { Mode = ToleranceMode.Tolerance, Absolute = 0.01m, Relative = 0.001m },
            NullPolicy = NullPolicy.BlankEqualsZero,
            Filters = [new FilterPair { LeftColumn = Col("Date", "Year"), RightColumn = Col("Date", "Year"), Values = ["2025"] }],
        },
    };

    [Fact]
    public void AT13_RoundTrip_PreservesMappingsAndOptions()
    {
        var loaded = ProjectStore.Deserialize(ProjectStore.Serialize(Sample()));

        Assert.Equal("Prod vs Dev", loaded.Name);
        Assert.Equal(Sample().Config.FieldPairs, loaded.Config.FieldPairs);
        Assert.Equal(0.01m, loaded.Config.Tolerance.Absolute);
        Assert.Equal(NullPolicy.BlankEqualsZero, loaded.Config.NullPolicy);
        Assert.Equal(["2025"], loaded.Config.Filters.Single().Values);
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
        Assert.Contains("\"schemaVersion\": 1", json);
        Assert.Contains("\"BlankEqualsZero\"", json);
    }

    [Fact]
    public void NFR08_LegacySchema_IsMigrated()
    {
        const string legacy = """
            { "name": "Old", "config": { "limits": { "maxRowCount": 500, "timeoutSeconds": 30 } } }
            """;

        var project = ProjectStore.Deserialize(legacy);

        Assert.Equal(1, project.SchemaVersion);
        Assert.Equal(500, project.Config.Limits.MaxRows);
        Assert.Equal(30, project.Config.Limits.TimeoutSeconds);
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
            Assert.Equal(Sample().Config.FieldPairs.Count, loaded.Config.FieldPairs.Count);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
