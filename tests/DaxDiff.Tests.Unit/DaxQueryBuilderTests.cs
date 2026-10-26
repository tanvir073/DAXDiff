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
        Assert.Contains("\"M0\", [Total Sales]", daxA);
        Assert.Contains("\"M0\", [Sales Amount]", daxB);
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
                Dim("K", Col("O'Brien Table", "Col]umn")),
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
        var config = YearRegionSales() with
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
        };

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
        Assert.Contains("ROW(\"Rows\", COUNTROWS(SUMMARIZECOLUMNS('Date'[Year], 'Geography'[Region], \"M0\", [Sales Amount])))", dax);
    }

    [Fact]
    public void RequiresAMeasure()
    {
        var config = new ComparisonConfig { FieldPairs = [Dim("Y", Col("Date", "Year"))] };
        Assert.Throws<ArgumentException>(() => DaxQueryBuilder.BuildComparisonQuery(config, Side.A));
    }
}
