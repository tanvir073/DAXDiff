using System.Net;
using System.Text;
using System.Text.Json;
using DaxDiff.Connectors;
using DaxDiff.Connectors.Service;
using DaxDiff.Domain;

namespace DaxDiff.Tests.Unit;

public class ConnectorParsingTests
{
    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void RestParse_PreservesColumnOrderAndTypes()
    {
        var result = ServiceRestConnector.Parse(Json("""
            { "results": [ { "tables": [ { "rows": [
                { "Date[Year]": 2025, "Geo[Region]": "North", "[M0]": 1200.50, "[M1]": null, "Date[Date]": "2025-01-31T00:00:00" }
            ] } ] } ] }
            """), 1000);

        Assert.Equal(["Date[Year]", "Geo[Region]", "[M0]", "[M1]", "Date[Date]"], result.ColumnNames);
        var row = result.Rows.Single();
        Assert.Equal(2025L, row[0]);
        Assert.Equal("North", row[1]);
        Assert.Equal(1200.50m, row[2]);
        Assert.Null(row[3]);
        Assert.Equal(new DateTime(2025, 1, 31), row[4]);
        Assert.False(result.IsTruncated);
    }

    [Fact]
    public void AT07_RestRowLimit_FlagsTruncation()
    {
        var sb = new StringBuilder("""{ "results": [ { "tables": [ { "rows": [""");
        for (var i = 0; i < ServiceRestConnector.MaxRowsPerQuery; i++)
            sb.Append(i == 0 ? "" : ",").Append("{\"[K]\":").Append(i).Append('}');
        sb.Append("] } ] } ] }");

        var result = ServiceRestConnector.Parse(Json(sb.ToString()), int.MaxValue);

        Assert.True(result.IsTruncated);
        Assert.Contains("100,000", result.TruncationReason);
    }

    [Fact]
    public void RestParse_RowCap_FlagsTruncation()
    {
        var result = ServiceRestConnector.Parse(Json("""{ "results": [ { "tables": [ { "rows": [ {"a":1}, {"a":2}, {"a":3} ] } ] } ] }"""), 2);
        Assert.True(result.IsTruncated);
        Assert.Equal(2, result.Rows.Count);
    }

    [Fact]
    public void RestParse_QueryError_IsClassified()
    {
        var ex = Assert.Throws<DaxDiffException>(() => ServiceRestConnector.Parse(Json("""
            { "results": [ { "error": { "code": "DatasetExecuteQueriesError", "message": "Column 'X' cannot be found" } } ] }
            """), 10));
        Assert.Equal("REST-QUERY", ex.DiagnosticCode);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, ErrorCategory.Authorization)]
    [InlineData(HttpStatusCode.Unauthorized, ErrorCategory.Authentication)]
    [InlineData(HttpStatusCode.TooManyRequests, ErrorCategory.Throttled)]
    [InlineData(HttpStatusCode.ServiceUnavailable, ErrorCategory.Network)]
    public void HttpStatus_MapsToActionableErrors(HttpStatusCode status, ErrorCategory expected)
    {
        var ex = ErrorClassifier.FromHttpStatus(status, "{}");
        Assert.Equal(expected, ex.Category);
        Assert.False(string.IsNullOrWhiteSpace(ex.Remedy));
    }

    [Theory]
    [InlineData("XMLA endpoint is disabled for this capacity", "CON-XMLA-DISABLED")]
    [InlineData("Either the user does not exist or does not have permission", "CON-PERMISSION")]
    [InlineData("No connection could be made because the target machine actively refused it", "CON-NETWORK")]
    [InlineData("Query (1, 10) The column 'Foo' cannot be found", "QRY-INVALID")]
    [InlineData("The specified Power BI workspace ('Scandi%20-%20Power%20BI%20data%20sets') is not found.", "CON-WORKSPACE-NOT-FOUND")]
    public void AdomdMessages_AreClassified(string message, string code)
    {
        Assert.Equal(code, ErrorClassifier.Classify(new InvalidOperationException(message), "XMLA").DiagnosticCode);
    }

    [Fact]
    public void TmschemaReader_BuildsTreesSkipsRowNumberAndWarns()
    {
        var tables = new Rowset(["ID", "Name", "IsHidden"], [[1L, "Sales", false], [2L, "Date", false], [3L, "Time Intel", false]]);
        var columns = new Rowset(
            ["ID", "TableID", "ExplicitName", "InferredName", "ExplicitDataType", "IsHidden", "Type"],
            [
                [10L, 1L, null, "RowNumber-2662979B", 6L, true, 3L],
                [11L, 1L, "Amount", null, 10L, false, 1L],
                [12L, 2L, "Year", null, 6L, false, 1L],
                [13L, 1L, "DateKey", null, 9L, true, 1L],
            ]);
        var measures = new Rowset(["TableID", "Name", "IsHidden", "DisplayFolder", "DataType"], [[1L, "Total Sales", false, "KPIs", 10L]]);
        var relationships = new Rowset(
            ["FromColumnID", "ToColumnID", "IsActive", "CrossFilteringBehavior", "FromCardinality", "ToCardinality"],
            [[13L, 12L, false, 1L, 2L, 1L]]);
        var calc = new Rowset(["TableID"], [[3L]]);

        var meta = TmschemaMetadataReader.Build("M", tables, columns, measures, relationships, calc, null);

        Assert.Equal(["Date", "Sales", "Time Intel"], meta.Tables.Select(t => t.Name));
        Assert.Equal(["Amount", "DateKey"], meta.Tables.Single(t => t.Name == "Sales").Columns.Select(c => c.Name));
        Assert.Equal(DataKind.Decimal, meta.FindColumn("sales", "amount")!.DataKind);
        Assert.Equal("KPIs", meta.FindMeasure("total sales")!.DisplayFolder);
        Assert.False(meta.Relationships.Single().IsActive);
        Assert.Contains(meta.Warnings, w => w.Contains("Calculation groups"));
        Assert.Contains(meta.Warnings, w => w.Contains("inactive"));
    }

    [Fact]
    public void InfoViewReader_ReadsBracketedColumnNames()
    {
        var tables = new Rowset(["[ID]", "[Name]", "[IsHidden]"], [[1L, "Sales", false]]);
        var columns = new Rowset(["[Table]", "[Name]", "[DataType]", "[IsHidden]"], [["Sales", "Region", "String", false]]);
        var measures = new Rowset(["[Table]", "[Name]", "[DataType]", "[IsHidden]", "[DisplayFolder]"], [["Sales", "Total", "Decimal", false, null]]);

        var meta = InfoViewMetadataReader.Build("M", tables, columns, measures, null);

        Assert.Equal(DataKind.Text, meta.FindColumn("Sales", "Region")!.DataKind);
        Assert.Equal(DataKind.Decimal, meta.FindMeasure("Total")!.DataKind);
    }
}
