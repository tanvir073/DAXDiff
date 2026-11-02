using DaxDiff.Comparison;
using DaxDiff.Domain;
using static DaxDiff.Tests.Unit.Fixtures.Fixture;

namespace DaxDiff.Tests.Unit;

public class DaxQueryBuilderTests
{
    [Fact]
    public void GeneratesModelSpecificSummarizeColumns()
    {
        var config = YearRegionSales();

        var daxA = DaxQueryBuilder.BuildComparisonQuery(config, Side.A);
        var daxB = DaxQueryBuilder.BuildComparisonQuery(config, Side.B);

        Assert.Contains("SUMMARIZECOLUMNS(", daxA);
        Assert.Contains("'Date'[Year]", daxA);
        Assert.Contains("'Geography'[Region]", daxA);
        Assert.Contains("\"V0\", [Total Sales]", daxA);
        Assert.Contains("\"V0\", [Sales Amount]", daxB);
        Assert.Contains("ORDER BY 'Date'[Year], 'Geography'[Region]", daxA);
        Assert.DoesNotContain("DEFINE", daxA);
    }

    [Fact]
    public void EscapesIdentifiers()
    {
        var config = new ComparisonConfig
        {
            FieldPairs =
            [
                Key("K", Col("O'Brien Table", "Col]umn")),
                Value("V", Measure("T", "Sales [Net]"), Measure("T", "x")),
            ],
        };

        var dax = DaxQueryBuilder.BuildComparisonQuery(config, Side.A);

        Assert.Contains("'O''Brien Table'[Col]]umn]", dax);
        Assert.Contains("[Sales [Net]]]", dax);
    }

    [Fact]
    public void FilterValues_AreTypedLiterals_AndCannotInjectDax()
    {
        var config = YearRegionSales().With(o => o with
        {
            Filters =
            [
                new FilterPair
                {
                    LeftColumn = Col("Geography", "Region"),
                    RightColumn = Col("Geography", "Region"),
                    Values = ["North", "x\") , EVALUATE Secrets //"],
                },
                new FilterPair
                {
                    LeftColumn = Col("Date", "Year"),
                    RightColumn = Col("Date", "Year"),
                    Operator = FilterOperator.NotIn,
                    Values = ["2020"],
                },
            ],
        });

        var dax = DaxQueryBuilder.BuildComparisonQuery(config, Side.A, ModelA());

        Assert.Contains("VAR __DAXDiffFilter0 = TREATAS({ \"North\", \"x\"\") , EVALUATE Secrets //\" }, 'Geography'[Region])", dax);
        Assert.Contains("VAR __DAXDiffFilter1 = FILTER(ALL('Date'[Year]), NOT ('Date'[Year] IN { 2020 }))", dax);
        Assert.Contains("    __DAXDiffFilter0,", dax);
    }

    [Theory]
    [InlineData("42", DataKind.Integer, "42")]
    [InlineData("3.5", DataKind.Decimal, "3.5")]
    [InlineData("true", DataKind.Boolean, "TRUE()")]
    [InlineData("2025-03-01", DataKind.DateTime, "DATE(2025, 3, 1)")]
    [InlineData("2025-03-01 13:30:05", DataKind.DateTime, "DATE(2025, 3, 1) + TIME(13, 30, 5)")]
    [InlineData("0012", DataKind.Text, "\"0012\"")]
    public void TypedLiteral(string input, DataKind kind, string expected) =>
        Assert.Equal(expected, DaxQueryBuilder.TypedLiteral(input, kind));

    [Theory]
    [InlineData("12; DROP", DataKind.Integer)]
    [InlineData("1) + EVIL(", DataKind.Decimal)]
    [InlineData("yes", DataKind.Boolean)]
    public void TypedLiteral_RejectsInvalidInput(string input, DataKind kind) =>
        Assert.Throws<FormatException>(() => DaxQueryBuilder.TypedLiteral(input, kind));

    [Fact]
    public void RowEstimateQuery_CountsSummarizedRows()
    {
        var dax = DaxQueryBuilder.BuildRowEstimateQuery(YearRegionSales(), Side.B);
        Assert.Contains("ROW(\"Rows\", COUNTROWS(SUMMARIZECOLUMNS('Date'[Year], 'Geography'[Region], \"V0\", [Sales Amount])))", dax);
    }

    [Fact]
    public void RequiresAField() =>
        Assert.Throws<ArgumentException>(() => DaxQueryBuilder.BuildComparisonQuery(new ComparisonConfig(), Side.A));

    [Fact]
    public void KeysOnly_ReturnsKeyCombinations()
    {
        var config = new ComparisonConfig { FieldPairs = [Key("Y", Col("Date", "Year"))] };

        var dax = DaxQueryBuilder.BuildComparisonQuery(config, Side.A);

        Assert.Contains($"SUMMARIZECOLUMNS({Environment.NewLine}    'Date'[Year]{Environment.NewLine})", dax);
    }

    [Theory]
    [InlineData(Aggregation.Sum, "SUM('Fact'[Amount])")]
    [InlineData(Aggregation.Average, "AVERAGE('Fact'[Amount])")]
    [InlineData(Aggregation.Min, "MIN('Fact'[Amount])")]
    [InlineData(Aggregation.Max, "MAX('Fact'[Amount])")]
    [InlineData(Aggregation.Count, "COUNTA('Fact'[Amount])")]
    [InlineData(Aggregation.DistinctCount, "DISTINCTCOUNT('Fact'[Amount])")]
    [InlineData(Aggregation.Concatenate, "CONCATENATEX(VALUES('Fact'[Amount]), 'Fact'[Amount], \", \", 'Fact'[Amount], ASC)")]
    public void FR017_ColumnAggregations(Aggregation aggregation, string expected) =>
        Assert.Equal(expected, DaxQueryBuilder.ValueExpression(Col("Fact", "Amount"), aggregation));

    [Fact]
    public void AggregatedColumns_UseEachSidesOwnAggregation()
    {
        var config = new ComparisonConfig
        {
            FieldPairs =
            [
                Key("Year", Col("Date", "Year")),
                Value("Amount", Col("Sales", "Amount"), Col("Fact", "Amt"), Aggregation.Sum, Aggregation.Average),
            ],
        };

        Assert.Contains("\"V0\", SUM('Sales'[Amount])", DaxQueryBuilder.BuildComparisonQuery(config, Side.A));
        Assert.Contains("\"V0\", AVERAGE('Fact'[Amt])", DaxQueryBuilder.BuildComparisonQuery(config, Side.B));
    }

    [Fact]
    public void UnaggregatedValueColumn_IsRejected() =>
        Assert.Throws<ArgumentException>(() => DaxQueryBuilder.ValueExpression(Col("Fact", "Amount"), Aggregation.None));

    [Theory]
    [InlineData(Aggregation.Max, "MAXX")]
    [InlineData(Aggregation.Min, "MINX")]
    public void FR017_BooleanMinMax_EvaluatesThroughOneAndZero(Aggregation aggregation, string function) =>
        Assert.Equal($"VAR __v = {function}(VALUES('Fact'[Active]), IF('Fact'[Active], 1, 0)) RETURN IF(NOT ISBLANK(__v), __v = 1)",
            DaxQueryBuilder.ValueExpression(Col("Fact", "Active"), aggregation, DataKind.Boolean));
}
