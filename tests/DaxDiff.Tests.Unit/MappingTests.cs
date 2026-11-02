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
    public void EmptyConfiguration_IsBlocked() =>
        Assert.Contains(MappingValidator.Validate(new ComparisonConfig(), null, null), i => i.Code == "MAP-EMPTY");

    [Fact]
    public void FR006_DuplicateFields_AreBlocked()
    {
        var config = YearRegionSales() with
        {
            FieldPairs = [.. YearRegionSales().FieldPairs, Key("Year again", Col("Date", "Year"))],
        };

        var issues = MappingValidator.Validate(config, ModelA(), ModelB());

        Assert.Contains(issues, i => i.Code == "MAP-DUP-LEFT" && i.Message.Contains("'Date'[Year]"));
        Assert.Contains(issues, i => i.Code == "MAP-DUP-RIGHT");
    }

    [Fact]
    public void AT14_RenamedOrDeletedFields_InvalidateComparison()
    {
        var config = new ComparisonConfig
        {
            FieldPairs =
            [
                Key("Year", Col("Date", "Year")),
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
                Key("Country", Col("Geography", "Country Code"), Col("Geography", "CountryCode")),
                Value("Sales", Measure("Sales", "Total Sales"), Measure("Fact", "Sales Amount")),
            ],
        };
        Assert.Contains(MappingValidator.Validate(config, ModelA(), ModelB()), i => i.Code == "MAP-TYPE" && i.Severity == IssueSeverity.Error);
    }

    [Fact]
    public void FR017_AggregationChangesTheComparedType()
    {
        // Text column vs integer column: incompatible as-is, comparable once both are counted.
        var raw = new ComparisonConfig
        {
            FieldPairs = [Value("Countries", Col("Geography", "Country Code"), Col("Geography", "CountryCode"), Aggregation.Max)],
        };
        var counted = new ComparisonConfig
        {
            FieldPairs = [Value("Countries", Col("Geography", "Country Code"), Col("Geography", "CountryCode"), Aggregation.DistinctCount)],
        };

        Assert.Contains(MappingValidator.Validate(raw, ModelA(), ModelB()), i => i.Code == "MAP-TYPE");
        Assert.DoesNotContain(MappingValidator.Validate(counted, ModelA(), ModelB()), i => i.Code == "MAP-TYPE");
    }

    [Fact]
    public void MeasureKey_IsError()
    {
        var pair = Key("Sales", Measure("Sales", "Total Sales"), Measure("Fact", "Sales Amount"));
        Assert.Contains(MappingValidator.Validate(new ComparisonConfig { FieldPairs = [pair] }, null, null), i => i.Code == "MAP-ROLE");
    }

    [Theory]
    [InlineData(FieldKind.Column, Aggregation.None)]
    [InlineData(FieldKind.Measure, Aggregation.Sum)]
    public void ValueAggregation_MustFitTheFieldKind(FieldKind kind, Aggregation aggregation)
    {
        var field = new FieldRef("Sales", kind == FieldKind.Column ? "Amount" : "Total Sales", kind);
        var config = new ComparisonConfig { FieldPairs = [Value("V", field, field, aggregation)] };
        Assert.Contains(MappingValidator.Validate(config, null, null), i => i.Code == "MAP-AGG");
    }

    [Fact]
    public void InvalidFilterValues_AreReported()
    {
        var config = YearRegionSales().With(o => o with
        {
            Filters = [new FilterPair { LeftColumn = Col("Date", "Year"), RightColumn = Col("Date", "Year"), Values = ["twenty"] }],
        });
        Assert.Contains(MappingValidator.Validate(config, ModelA(), ModelB()), i => i.Code == "FLT-VALUE");
    }

    [Fact]
    public void AutoMatch_NormalizedMeasureName()
    {
        var suggestion = new AutoMatcher().SuggestMeasure(Measure("Sales", "Order Count"), ModelB());
        Assert.Equal("order_count", suggestion.Right!.Name);
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
        Assert.Null(suggestion.Right);
        Assert.Equal(2, suggestion.Candidates.Count);
    }

    [Fact]
    public void FR018_SynonymSuggestions_AlignModelBWithModelA()
    {
        var matcher = new AutoMatcher([["Revenue", "Turnover"]]);
        IReadOnlyList<ComparisonField> a =
        [
            Field(Col("Date", "Year"), "Year", isKey: true),
            Field(Measure("Sales", "Revenue"), "Revenue"),
            Field(Measure("Sales", "Order Count"), "Orders"),
        ];
        IReadOnlyList<ComparisonField> b =
        [
            Field(Col("Calendar", "Year"), "Year", isKey: true),
            Field(Measure("Fact", "Turnover")),
            Field(Measure("Fact", "order_count")),
        ];

        var result = matcher.SuggestSynonyms(a, b);

        Assert.Empty(result.Ambiguous);
        Assert.Collection(result.Matches,
            m => Assert.Equal((1, "Revenue"), (m.IndexB, m.Synonym)),
            m => Assert.Equal((2, "Orders"), (m.IndexB, m.Synonym)));
    }

    [Fact]
    public void FR018_SynonymSuggestions_ReportAmbiguity()
    {
        IReadOnlyList<ComparisonField> a = [Field(Col("Sales", "Amount"), "Gross"), Field(Col("Returns", "Amount"), "Returned")];
        IReadOnlyList<ComparisonField> b = [Field(Col("Fact", "Amount"))];

        var result = new AutoMatcher().SuggestSynonyms(a, b);

        Assert.Empty(result.Matches);
        Assert.Single(result.Ambiguous);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData(500, false)]
    [InlineData(0, true)]
    public void ResultRowLimit_MustBePositive(int? limit, bool expectError)
    {
        var config = YearRegionSales().With(o => o with { Results = new ResultSettings { MaxRows = limit } });

        var issues = MappingValidator.Validate(config, ModelA(), ModelB());

        Assert.Equal(expectError, issues.Any(i => i.Code == "OPT-RESULT-ROWS"));
    }
}
