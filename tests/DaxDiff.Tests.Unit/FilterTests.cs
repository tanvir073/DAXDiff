using DaxDiff.Comparison;
using DaxDiff.Connectors.Adomd;
using DaxDiff.Domain;
using static DaxDiff.Tests.Unit.Fixtures.Fixture;

namespace DaxDiff.Tests.Unit;

public class FilterTests
{
    private static readonly FieldRef Region = Col("Geography", "Region");
    private static readonly FieldRef Year = Col("Date", "Year");

    private static FilterPair Basic(FieldRef col, FilterOperator op, bool blank, params string[] values) =>
        new() { LeftColumn = col, RightColumn = col, Operator = op, Values = values, IncludeBlank = blank };

    private static FilterPair Advanced(FieldRef col, ConditionJoin join, params FilterCondition[] conditions) =>
        new() { LeftColumn = col, RightColumn = col, Kind = FilterKind.Advanced, Conditions = conditions, Join = join };

    [Fact]
    public void FR020_BasicFilter_IncludesBlankWhenSelected()
    {
        Assert.Equal("TREATAS({ \"North\", BLANK() }, 'Geography'[Region])",
            DaxQueryBuilder.FilterExpression(Basic(Region, FilterOperator.In, true, "North"), Region, DataKind.Text));
        Assert.Equal("FILTER(ALL('Date'[Year]), NOT ('Date'[Year] IN { 2020, 2021 }))",
            DaxQueryBuilder.FilterExpression(Basic(Year, FilterOperator.NotIn, false, "2020", "2021"), Year, DataKind.Integer));
    }

    [Fact]
    public void FR020_AdvancedFilter_JoinsTypedConditions()
    {
        var filter = Advanced(Year, ConditionJoin.And,
            new FilterCondition(ConditionOperator.GreaterThanOrEqual, "2020"),
            new FilterCondition(ConditionOperator.LessThan, "2025"));

        Assert.Equal("FILTER(ALL('Date'[Year]), ('Date'[Year] >= 2020) && ('Date'[Year] < 2025))",
            DaxQueryBuilder.FilterExpression(filter, Year, DataKind.Integer));
    }

    [Theory]
    [InlineData(ConditionOperator.Is, "N'th", "'Geography'[Region] == \"N'th\"")]
    [InlineData(ConditionOperator.IsNot, "North", "NOT ('Geography'[Region] == \"North\")")]
    [InlineData(ConditionOperator.Contains, "or\"th", "CONTAINSSTRING('Geography'[Region], \"or\"\"th\")")]
    [InlineData(ConditionOperator.DoesNotContain, "x", "NOT CONTAINSSTRING('Geography'[Region], \"x\")")]
    [InlineData(ConditionOperator.StartsWith, "No", "LEFT('Geography'[Region], 2) = \"No\"")]
    [InlineData(ConditionOperator.DoesNotStartWith, "So", "NOT (LEFT('Geography'[Region], 2) = \"So\")")]
    [InlineData(ConditionOperator.IsBlank, "", "ISBLANK('Geography'[Region])")]
    [InlineData(ConditionOperator.IsNotBlank, "", "NOT ISBLANK('Geography'[Region])")]
    public void FR020_TextConditions(ConditionOperator op, string value, string expected) =>
        Assert.Equal(expected, DaxQueryBuilder.ConditionExpression(new FilterCondition(op, value), "'Geography'[Region]", DataKind.Text));

    [Fact]
    public void FR020_OrJoin_AndDateLiterals()
    {
        var date = Col("Date", "Date");
        var filter = Advanced(date, ConditionJoin.Or,
            new FilterCondition(ConditionOperator.LessThan, "2024-01-01"),
            new FilterCondition(ConditionOperator.IsBlank));

        Assert.Equal("FILTER(ALL('Date'[Date]), ('Date'[Date] < DATE(2024, 1, 1)) || (ISBLANK('Date'[Date])))",
            DaxQueryBuilder.FilterExpression(filter, date, DataKind.DateTime));
    }

    [Fact]
    public void FR020_FiltersAreAppliedPerSideInTheQuery()
    {
        var config = YearRegionSales().With(o => o with
        {
            Filters = [Advanced(Region, ConditionJoin.And, new FilterCondition(ConditionOperator.StartsWith, "N")) with { RightColumn = Col("Geo", "Region Name") }],
        });

        Assert.Contains("VAR __DAXDiffFilter0 = FILTER(ALL('Geography'[Region]), (LEFT('Geography'[Region], 1) = \"N\"))",
            DaxQueryBuilder.BuildComparisonQuery(config, Side.A, ModelA()));
        Assert.Contains("FILTER(ALL('Geo'[Region Name])", DaxQueryBuilder.BuildComparisonQuery(config, Side.B));
    }

    [Fact]
    public void DistinctValuesQuery_SortsLimitsAndSearches()
    {
        Assert.Equal($"EVALUATE{Environment.NewLine}TOPN(1001, VALUES('Geography'[Region]), 'Geography'[Region], ASC){Environment.NewLine}ORDER BY 'Geography'[Region]",
            DaxQueryBuilder.BuildDistinctValuesQuery(Region, DataKind.Text, null, 1001));
        Assert.Contains("FILTER(VALUES('Date'[Year]), CONTAINSSTRING(FORMAT('Date'[Year], \"\"), \"20\"\"\"))",
            DaxQueryBuilder.BuildDistinctValuesQuery(Year, DataKind.Integer, " 20\" ", 50));
    }

    [Fact]
    public void Validation_BasicNeedsValuesOrBlank()
    {
        Assert.Contains(MappingValidator.ValidateFilter(Basic(Region, FilterOperator.In, false), 1, ModelA(), null), i => i.Code == "FLT-EMPTY");
        Assert.Empty(MappingValidator.ValidateFilter(Basic(Region, FilterOperator.In, true), 1, ModelA(), null));
    }

    [Fact]
    public void Validation_AdvancedChecksValuesOperatorsAndTypes()
    {
        var missingValue = Advanced(Region, ConditionJoin.And, new FilterCondition(ConditionOperator.Is));
        var textOpOnNumber = Advanced(Year, ConditionJoin.And, new FilterCondition(ConditionOperator.Contains, "20"));
        var badNumber = Advanced(Year, ConditionJoin.And, new FilterCondition(ConditionOperator.GreaterThan, "twenty"));
        var tooMany = Advanced(Year, ConditionJoin.And,
            new FilterCondition(ConditionOperator.IsBlank), new FilterCondition(ConditionOperator.IsBlank), new FilterCondition(ConditionOperator.IsBlank));

        Assert.Contains(MappingValidator.ValidateFilter(missingValue, 1, ModelA(), null), i => i.Code == "FLT-VALUE");
        Assert.Contains(MappingValidator.ValidateFilter(textOpOnNumber, 1, ModelA(), null), i => i.Code == "FLT-OPERATOR");
        Assert.Contains(MappingValidator.ValidateFilter(badNumber, 1, ModelA(), null), i => i.Code == "FLT-VALUE");
        Assert.Contains(MappingValidator.ValidateFilter(tooMany, 1, ModelA(), null), i => i.Code == "FLT-CONDITIONS");
    }

    [Fact]
    public void Describe_ReadsLikeThePowerBiFilterPane()
    {
        Assert.Equal("Region is North, South, (Blank)", Basic(Region, FilterOperator.In, true, "North", "South").Describe());
        Assert.Equal("Region is not A, B, C, D, E and 2 more", Basic(Region, FilterOperator.NotIn, false, "A", "B", "C", "D", "E", "F", "G").Describe());
        Assert.Equal("Year is greater than 2020 or is blank",
            Advanced(Year, ConditionJoin.Or, new FilterCondition(ConditionOperator.GreaterThan, "2020"), new FilterCondition(ConditionOperator.IsBlank)).Describe());
    }

    [Theory]
    [InlineData(null, "powerbi://api.powerbi.com/v1.0/myorg/Sales")]
    [InlineData("organizations", "powerbi://api.powerbi.com/v1.0/myorg/Sales")]
    [InlineData("contoso.com", "powerbi://api.powerbi.com/v1.0/contoso.com/Sales")]
    [InlineData(" 72f988bf-86f1-41af-91ab-2d7cd011db47 ", "powerbi://api.powerbi.com/v1.0/72f988bf-86f1-41af-91ab-2d7cd011db47/Sales")]
    public void FR002_XmlaAddressesTheSourcesTenant(string? tenant, string expected) =>
        Assert.Equal(expected, ServiceXmlaConnector.DataSource("powerbi://api.powerbi.com/v1.0/myorg/", tenant, "Sales"));
}
