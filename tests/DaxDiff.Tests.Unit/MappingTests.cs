using DaxDiff.Comparison;
using DaxDiff.Domain;
using static DaxDiff.Tests.Unit.Fixtures.Fixture;

namespace DaxDiff.Tests.Unit;

public class MappingTests
{
    [Fact]
    public void ValidConfiguration_HasNoErrors()
    {
        var issues = MappingValidator.Validate(YearRegionSales(), ModelA(), ModelB());
        Assert.False(MappingValidator.HasErrors(issues));
    }

    [Fact]
    public void FR006_DuplicateMappings_AreBlocked()
    {
        var config = YearRegionSales() with
        {
            FieldPairs = [.. YearRegionSales().FieldPairs, Dim("Year again", Col("Date", "Year"))],
        };

        var issues = MappingValidator.Validate(config, ModelA(), ModelB());

        Assert.Contains(issues, i => i.Code == "MAP-DUP-LEFT" && i.Message.Contains("'Date'[Year]"));
        Assert.Contains(issues, i => i.Code == "MAP-DUP-RIGHT");
    }

    [Fact]
    public void FR006_DuplicateAliases_AreBlocked()
    {
        var config = new ComparisonConfig
        {
            FieldPairs =
            [
                Value("Sales", Measure("Sales", "Total Sales"), Measure("Fact", "Sales Amount")),
                Value("sales", Measure("Sales", "Revenue"), Measure("Fact", "Turnover")),
            ],
        };
        Assert.Contains(MappingValidator.Validate(config, ModelA(), ModelB()), i => i.Code == "MAP-DUP-ALIAS");
    }

    [Fact]
    public void FR007_UnconfirmedSuggestions_DoNotRun()
    {
        var pair = Value("Sales", Measure("Sales", "Total Sales"), Measure("Fact", "Sales Amount")) with { IsConfirmed = false };
        var issues = MappingValidator.Validate(new ComparisonConfig { FieldPairs = [pair] }, ModelA(), ModelB());
        Assert.Contains(issues, i => i.Code == "MAP-UNCONFIRMED");
    }

    [Fact]
    public void AT14_RenamedOrDeletedFields_InvalidateMapping()
    {
        var config = new ComparisonConfig
        {
            FieldPairs =
            [
                Dim("Year", Col("Date", "Year")),
                Value("Margin", Measure("Sales", "Gross Margin"), Measure("Fact", "Sales Amount")),
            ],
        };

        var issues = MappingValidator.Validate(config, ModelA(), ModelB());

        var missing = Assert.Single(issues, i => i.Code == "MAP-MISSING");
        Assert.Equal("Margin", missing.Alias);
        Assert.Contains("model A", missing.Message);
    }

    [Fact]
    public void IncompatibleKeyTypes_AreErrors()
    {
        var config = new ComparisonConfig
        {
            FieldPairs =
            [
                Dim("Country", Col("Geography", "Country Code"), Col("Geography", "CountryCode")),
                Value("Sales", Measure("Sales", "Total Sales"), Measure("Fact", "Sales Amount")),
            ],
        };
        Assert.Contains(MappingValidator.Validate(config, ModelA(), ModelB()), i => i.Code == "MAP-TYPE" && i.Severity == IssueSeverity.Error);
    }

    [Fact]
    public void RoleMismatch_IsError()
    {
        var pair = new FieldPair { Left = Col("Date", "Year"), Right = Col("Date", "Year"), Role = FieldRole.Measure, Alias = "Y" };
        Assert.Contains(MappingValidator.Validate(new ComparisonConfig { FieldPairs = [pair] }, null, null), i => i.Code == "MAP-ROLE");
    }

    [Fact]
    public void InvalidFilterValues_AreReported()
    {
        var config = YearRegionSales() with
        {
            Filters = [new FilterPair { LeftColumn = Col("Date", "Year"), RightColumn = Col("Date", "Year"), Values = ["twenty"] }],
        };
        Assert.Contains(MappingValidator.Validate(config, ModelA(), ModelB()), i => i.Code == "FLT-VALUE");
    }

    [Fact]
    public void AutoMatch_NormalizedMeasureName()
    {
        var suggestion = new AutoMatcher().SuggestMeasure(Measure("Sales", "Order Count"), ModelB());

        Assert.Equal("order_count", suggestion.Right!.Name);
        var pair = suggestion.ToUnconfirmedPair()!;
        Assert.False(pair.IsConfirmed);
    }

    [Fact]
    public void AutoMatch_UsesSynonymDictionary()
    {
        var matcher = new AutoMatcher([["Revenue", "Turnover"], ["Total Sales", "Sales Amount"]]);

        Assert.Equal("Turnover", matcher.SuggestMeasure(Measure("Sales", "Revenue"), ModelB()).Right!.Name);
        Assert.Equal(MatchConfidence.Synonym, matcher.SuggestMeasure(Measure("Sales", "Total Sales"), ModelB()).Confidence);
        Assert.Null(new AutoMatcher().SuggestMeasure(Measure("Sales", "Revenue"), ModelB()).Right);
    }

    [Fact]
    public void AutoMatch_PrefersQualifiedColumnMatch()
    {
        var suggestion = new AutoMatcher().SuggestColumn(Col("Date", "Year"), ModelA(), ModelB());
        Assert.Equal(Col("Date", "Year"), suggestion.Right);
        Assert.Equal(MatchConfidence.QualifiedName, suggestion.Confidence);
    }

    [Fact]
    public void AutoMatch_SurfacesAmbiguityInsteadOfGuessing()
    {
        var b = ModelB() with { Tables = ModelB().Tables.Where(t => t.Name != "Date").ToList() };
        var a = ModelA() with
        {
            Tables = [.. ModelA().Tables, new TableInfo("Fiscal", false, false, [new ColumnInfo("Fiscal", "Year", DataKind.Integer, false)])],
        };
        b = b with
        {
            Tables = [.. b.Tables, new TableInfo("Period", false, false, [new ColumnInfo("Period", "Year", DataKind.Integer, false)])],
        };

        var suggestion = new AutoMatcher().SuggestColumn(Col("Fiscal", "Year"), a, b);

        Assert.True(suggestion.IsAmbiguous);
        Assert.Null(suggestion.ToUnconfirmedPair());
        Assert.Equal(2, suggestion.Candidates.Count);
    }
}
