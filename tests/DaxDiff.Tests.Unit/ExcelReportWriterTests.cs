using ClosedXML.Excel;
using DaxDiff.Comparison;
using DaxDiff.Domain;
using DaxDiff.Export;
using static DaxDiff.Tests.Unit.Fixtures.Fixture;

namespace DaxDiff.Tests.Unit;

public class ExcelReportWriterTests
{
    private static ComparisonResult Run(QueryResult a, QueryResult b, RunStatus status = RunStatus.Complete, ComparisonConfig? config = null)
    {
        config ??= YearRegionSales();
        var output = ComparisonEngine.Compare(a, b, config);
        return new ComparisonResult
        {
            RunId = Guid.NewGuid(),
            Status = status,
            SourceA = new ModelReference { Kind = SourceKind.DesktopSession, DisplayName = "A.pbix", DesktopServer = "localhost:1" },
            SourceB = new ModelReference { Kind = SourceKind.ServiceXmla, DisplayName = "B", WorkspaceName = "WS", DatasetName = "B" },
            Config = config,
            Rows = output.Rows,
            Summary = output.Summary,
            Queries =
            [
                new QueryAudit { Side = Side.A, Transport = "Fake", ModelIdentity = "A", QueryHash = new string('a', 64), Dax = "EVALUATE {1}" },
            ],
            MetadataA = ModelA(),
            MetadataB = ModelB(),
        };
    }

    private static XLWorkbook RoundTrip(ComparisonResult result, ExcelExportOptions? options = null)
    {
        var stream = new MemoryStream();
        ExcelReportWriter.Write(result, stream, options);
        stream.Position = 0;
        return new XLWorkbook(stream);
    }

    [Fact]
    public void FR013_WorkbookContainsContractSheets()
    {
        using var wb = RoundTrip(Run(Result(3, Row(2025L, "N", 1m)), Result(3, Row(2025L, "N", 2m))));

        Assert.Equal(
            ["Summary", "Differences", "All Data", "Field Mapping", "Settings", "Execution Log"],
            wb.Worksheets.Select(w => w.Name));
        var diff = wb.Worksheet("Differences");
        Assert.Equal("Status", diff.Cell(1, 1).GetString());
        Assert.Equal("Mismatch", diff.Cell(2, 1).GetString());
        Assert.Equal(1d, diff.Cell(2, 6).GetDouble());
        Assert.Equal(2d, diff.Cell(2, 7).GetDouble());
        Assert.Equal(-1d, diff.Cell(2, 8).GetDouble());
        Assert.NotNull(diff.AutoFilter);
        Assert.Equal(1, diff.SheetView.SplitRow);
    }

    [Fact]
    public void ValuesMatchOnScreenResult()
    {
        var result = Run(Result(3, Row(2025L, "North", 1200m), Row(2025L, "East", 300m)), Result(3, Row(2025L, "North", 1198m)));
        using var wb = RoundTrip(result, new ExcelExportOptions { IncludeAllData = true });

        var all = wb.Worksheet("All Data");
        Assert.Equal(result.Rows.Count + 1, all.LastRowUsed()!.RowNumber());
        Assert.Equal(2d, all.Cell(2, 8).GetDouble());
        Assert.Equal("Only in A", all.Cell(3, 1).GetString());
        Assert.Equal(2025d, all.Cell(2, 2).GetDouble());
        Assert.True(all.Cell(3, 3).IsEmpty()); // key B blank for Only in A
    }

    [Fact]
    public void AT11_LargeExports_SplitAcrossSheetsPreservingCounts()
    {
        var a = Result(3, Enumerable.Range(0, 25).Select(i => Row((long)i, "N", 1m)).ToArray());
        var b = Result(3);
        using var wb = RoundTrip(Run(a, b), new ExcelExportOptions { MaxRowsPerSheet = 10 });

        var sheets = wb.Worksheets.Where(w => w.Name.StartsWith("Differences")).ToList();
        Assert.Equal(["Differences", "Differences (2)", "Differences (3)"], sheets.Select(s => s.Name));
        Assert.Equal(25, sheets.Sum(s => s.LastRowUsed()!.RowNumber() - 1));
        Assert.True(sheets.All(s => s.Cell(1, 1).GetString() == "Status"));
    }

    [Fact]
    public void AT12_FormulaLikeText_IsStoredAsText()
    {
        var config = new ComparisonConfig
        {
            FieldPairs = [Dim("Name", Col("C", "Name")), Value("V", Measure("S", "V"), Measure("S", "V"))],
        };
        var a = Result(2, Row("=HYPERLINK(\"http://evil\")", 1m), Row("+1+1", 1m), Row("@SUM(A1)", 1m), Row("-2", 1m));
        using var wb = RoundTrip(Run(a, Result(2), config: config));

        var ws = wb.Worksheet("Differences");
        for (var r = 2; r <= 5; r++)
        {
            var cell = ws.Cell(r, 2);
            Assert.False(cell.HasFormula);
            Assert.Equal(XLDataType.Text, cell.DataType);
            Assert.True(cell.Style.IncludeQuotePrefix);
        }

        Assert.Equal("=HYPERLINK(\"http://evil\")", ws.Cell(2, 2).GetString());
    }

    [Fact]
    public void DatesAndNumbers_KeepNativeTypes()
    {
        var config = new ComparisonConfig
        {
            FieldPairs = [Dim("Date", Col("D", "Date")), Value("V", Measure("S", "V"), Measure("S", "V"))],
        };
        using var wb = RoundTrip(Run(Result(2, Row(new DateTime(2025, 5, 1), 1.5m)), Result(2), config: config));

        var ws = wb.Worksheet("Differences");
        Assert.Equal(XLDataType.DateTime, ws.Cell(2, 2).DataType);
        Assert.Equal(XLDataType.Number, ws.Cell(2, 4).DataType);
    }

    [Theory]
    [InlineData(RunStatus.Partial, "PARTIAL")]
    [InlineData(RunStatus.Cancelled, "CANCELLED")]
    public void IncompleteRuns_CarryProminentNotice(RunStatus status, string expected)
    {
        using var wb = RoundTrip(Run(Result(3), Result(3), status));
        Assert.StartsWith(expected, wb.Worksheet("Summary").Cell(2, 1).GetString());
    }

    [Fact]
    public void AllData_IsOmittedWithNoteByDefault()
    {
        using var wb = RoundTrip(Run(Result(3, Row(1L, "N", 1m)), Result(3, Row(1L, "N", 1m))));
        Assert.Contains("omitted", wb.Worksheet("All Data").Cell(1, 1).GetString());
    }

    [Fact]
    public void ExecutionLog_OmitsDaxUnlessRequested()
    {
        var result = Run(Result(3), Result(3));
        using (var wb = RoundTrip(result))
            Assert.DoesNotContain(wb.Worksheet("Execution Log").CellsUsed(), c => c.GetString() == "EVALUATE {1}");
        using (var wb = RoundTrip(result, new ExcelExportOptions { IncludeQueryText = true }))
            Assert.Contains(wb.Worksheet("Execution Log").CellsUsed(), c => c.GetString() == "EVALUATE {1}");
    }
}
