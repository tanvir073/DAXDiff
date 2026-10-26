using System.Globalization;
using ClosedXML.Excel;
using DaxDiff.Domain;

namespace DaxDiff.Export;

public sealed record ExcelExportOptions
{
    /// <summary>Write every compared row to "All Data" (otherwise only a note is written).</summary>
    public bool IncludeAllData { get; init; }

    /// <summary>Include generated DAX text in the Execution Log. Off by default: DAX may contain sensitive filter literals.</summary>
    public bool IncludeQueryText { get; init; }

    /// <summary>Data rows per worksheet before splitting; Excel's limit is 1,048,576 rows including the header.</summary>
    public int MaxRowsPerSheet { get; init; } = ExcelReportWriter.ExcelMaxDataRows;
}

/// <summary>
/// Writes the audit workbook: Summary, Differences, All Data, Field Mapping, Settings, Execution Log (§10, FR-013).
/// Contains no credentials or tokens.
/// </summary>
public static class ExcelReportWriter
{
    public const int ExcelMaxDataRows = 1_048_575;

    private const string NumberFormat = "#,##0.00########";
    private const string PercentFormat = "0.00%";
    private const string DateFormat = "yyyy-mm-dd hh:mm:ss";

    private static readonly IReadOnlyDictionary<RowStatus, XLColor> StatusColors = new Dictionary<RowStatus, XLColor>
    {
        [RowStatus.Match] = XLColor.FromHtml("#E2F0D9"),
        [RowStatus.Mismatch] = XLColor.FromHtml("#F8CBAD"),
        [RowStatus.OnlyInA] = XLColor.FromHtml("#FFE699"),
        [RowStatus.OnlyInB] = XLColor.FromHtml("#BDD7EE"),
        [RowStatus.InvalidKey] = XLColor.FromHtml("#D9C3E9"),
    };

    public static void Write(ComparisonResult result, string path, ExcelExportOptions? options = null)
    {
        using var workbook = Build(result, options ?? new ExcelExportOptions());
        workbook.SaveAs(path);
    }

    public static void Write(ComparisonResult result, Stream stream, ExcelExportOptions? options = null)
    {
        using var workbook = Build(result, options ?? new ExcelExportOptions());
        workbook.SaveAs(stream);
    }

    public static XLWorkbook Build(ComparisonResult result, ExcelExportOptions options)
    {
        if (options.MaxRowsPerSheet is < 1 or > ExcelMaxDataRows)
            throw new ArgumentOutOfRangeException(nameof(options), "MaxRowsPerSheet must be between 1 and 1,048,575.");

        var workbook = new XLWorkbook();
        workbook.Properties.Title = "DAXDiff comparison";
        workbook.Properties.Comments = $"Run {result.RunId} – {result.Status}";

        var differences = result.Rows.Where(r => r.Status != RowStatus.Match).ToList();
        var diffSheets = Math.Max(1, (int)Math.Ceiling(differences.Count / (double)options.MaxRowsPerSheet));
        WriteSummary(workbook.AddWorksheet("Summary"), result, differences.Count, diffSheets);
        WriteRows(workbook, "Differences", differences, result.Config, options.MaxRowsPerSheet);

        if (options.IncludeAllData)
        {
            WriteRows(workbook, "All Data", result.Rows, result.Config, options.MaxRowsPerSheet);
        }
        else
        {
            var note = workbook.AddWorksheet("All Data");
            note.Cell(1, 1).Value = "All compared rows were omitted from this workbook by export settings (data minimization).";
            note.Cell(2, 1).Value = $"Total compared keys: {result.Summary.TotalKeys:N0}. Re-export with 'Include all data' to add them.";
        }

        WriteMapping(workbook.AddWorksheet("Field Mapping"), result);
        WriteSettings(workbook.AddWorksheet("Settings"), result.Config);
        WriteExecutionLog(workbook.AddWorksheet("Execution Log"), result, options);
        return workbook;
    }

    private static void WriteSummary(IXLWorksheet ws, ComparisonResult result, int differenceCount, int differenceSheets)
    {
        ws.Cell(1, 1).Value = "DAXDiff comparison summary";
        ws.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(14);

        var row = 2;
        if (!result.IsComplete)
        {
            var banner = ws.Range(row, 1, row, 6).Merge();
            banner.Value = result.Status switch
            {
                RunStatus.Partial => "PARTIAL RESULT – the comparison is incomplete. Do not treat missing differences as equality.",
                RunStatus.Cancelled => "CANCELLED – this run was cancelled before completion. Results are not complete.",
                _ => "FAILED – the comparison did not complete. " + result.FailureMessage,
            };
            banner.Style.Fill.SetBackgroundColor(XLColor.FromHtml("#C00000")).Font.SetFontColor(XLColor.White).Font.SetBold();
            row++;
        }

        void Pair(string label, object? value)
        {
            ws.Cell(row, 1).Value = label;
            ws.Cell(row, 1).Style.Font.SetBold();
            SetValue(ws.Cell(row, 2), value);
            row++;
        }

        row++;
        Pair("Comparison", result.ComparisonName);
        Pair("Run ID", result.RunId.ToString());
        Pair("Status", result.Status.ToString());
        Pair("Started (UTC)", result.StartedUtc.UtcDateTime);
        Pair("Finished (UTC)", result.FinishedUtc.UtcDateTime);
        Pair("Model A", result.SourceA.Describe());
        Pair("Model A source type", result.SourceA.Kind.ToString());
        Pair("Model A identity", result.SecurityContextA);
        Pair("Model A last refresh (UTC)", result.MetadataA?.LastRefreshUtc?.UtcDateTime);
        Pair("Model B", result.SourceB.Describe());
        Pair("Model B source type", result.SourceB.Kind.ToString());
        Pair("Model B identity", result.SecurityContextB);
        Pair("Model B last refresh (UTC)", result.MetadataB?.LastRefreshUtc?.UtcDateTime);
        Pair("Scope notice", "Results are as observed by the current caller on each side; row-level and object-level security may hide data.");
        row++;
        Pair("Rows retrieved A", result.Summary.RowsA);
        Pair("Rows retrieved B", result.Summary.RowsB);
        Pair("Total keys", result.Summary.TotalKeys);
        Pair("Matches", result.Summary.Matches);
        Pair("Value mismatches", result.Summary.Mismatches);
        Pair("Only in A", result.Summary.OnlyInA);
        Pair("Only in B", result.Summary.OnlyInB);
        Pair("Invalid / duplicate keys", result.Summary.InvalidKeys);
        Pair("Difference rows exported", differenceCount);
        Pair("Difference sheets", differenceSheets);
        Pair("Match rate", result.Summary.MatchRate);
        ws.Cell(row - 1, 2).Style.NumberFormat.Format = PercentFormat;
        row++;

        var header = new[] { "Value", "Compared keys", "Mismatches", "Total A", "Total B", "Total A − B", "Max |Δ|" };
        for (var c = 0; c < header.Length; c++)
            ws.Cell(row, c + 1).Value = header[c];
        StyleHeader(ws.Range(row, 1, row, header.Length));
        var tableStart = row;
        foreach (var m in result.Summary.Measures)
        {
            row++;
            SetValue(ws.Cell(row, 1), m.Alias);
            ws.Cell(row, 2).Value = m.Compared;
            ws.Cell(row, 3).Value = m.Mismatches;
            SetValue(ws.Cell(row, 4), m.TotalA);
            SetValue(ws.Cell(row, 5), m.TotalB);
            SetValue(ws.Cell(row, 6), m.TotalA is { } a && m.TotalB is { } b ? a - b : null);
            SetValue(ws.Cell(row, 7), m.MaxAbsDelta);
            ws.Range(row, 4, row, 7).Style.NumberFormat.Format = NumberFormat;
        }

        if (result.Summary.Measures.Count > 0)
            ws.Range(tableStart, 1, row, header.Length).SetAutoFilter();

        if (result.Warnings.Count > 0)
        {
            row += 2;
            ws.Cell(row, 1).Value = "Warnings";
            ws.Cell(row, 1).Style.Font.SetBold();
            foreach (var warning in result.Warnings)
                SetValue(ws.Cell(++row, 1), warning);
        }

        ws.Column(1).Width = 30;
        ws.Column(2).Width = 60;
        ws.Columns(3, 7).Width = 18;
    }

    /// <summary>Writes rows across as many sheets as needed, each with headers, filters and frozen panes (AT-11).</summary>
    private static void WriteRows(XLWorkbook workbook, string baseName, IReadOnlyList<ComparisonRow> rows, ComparisonConfig config, int maxRowsPerSheet)
    {
        var dims = config.Keys;
        var measures = config.Values;
        var headers = new List<string> { "Status" };
        foreach (var d in dims)
        {
            headers.Add($"{d.Alias} (A)");
            headers.Add($"{d.Alias} (B)");
        }

        foreach (var m in measures)
        {
            headers.Add($"{m.Alias} A");
            headers.Add($"{m.Alias} B");
            headers.Add($"{m.Alias} Δ (A−B)");
            headers.Add($"{m.Alias} Δ%");
        }

        headers.Add("Reason");
        headers.Add("Normalized key");

        var sheetCount = Math.Max(1, (int)Math.Ceiling(rows.Count / (double)maxRowsPerSheet));
        for (var s = 0; s < sheetCount; s++)
        {
            var ws = workbook.AddWorksheet(s == 0 ? baseName : $"{baseName} ({s + 1})");
            for (var c = 0; c < headers.Count; c++)
                ws.Cell(1, c + 1).Value = headers[c];
            StyleHeader(ws.Range(1, 1, 1, headers.Count));

            var chunk = rows.Skip(s * maxRowsPerSheet).Take(maxRowsPerSheet).ToList();
            var r = 1;
            foreach (var row in chunk)
            {
                r++;
                var c = 1;
                ws.Cell(r, c++).Value = StatusText(row.Status);
                for (var i = 0; i < dims.Count; i++)
                {
                    SetValue(ws.Cell(r, c++), row.KeyA is null ? null : row.KeyA[i]);
                    SetValue(ws.Cell(r, c++), row.KeyB is null ? null : row.KeyB[i]);
                }

                for (var i = 0; i < measures.Count; i++)
                {
                    var cell = i < row.Measures.Count ? row.Measures[i] : null;
                    SetValue(ws.Cell(r, c), cell?.A);
                    SetValue(ws.Cell(r, c + 1), cell?.B);
                    SetValue(ws.Cell(r, c + 2), cell?.Delta);
                    SetValue(ws.Cell(r, c + 3), cell?.DeltaPercent);
                    if (cell is not null && !cell.IsMatch && row.Status == RowStatus.Mismatch)
                        ws.Range(r, c, r, c + 3).Style.Font.SetBold();
                    c += 4;
                }

                SetValue(ws.Cell(r, c++), row.Reason);
                SetValue(ws.Cell(r, c), row.NormalizedKey);
            }

            var firstMeasureColumn = 2 + dims.Count * 2;
            for (var i = 0; i < measures.Count; i++)
            {
                var col = firstMeasureColumn + i * 4;
                ws.Range(2, col, Math.Max(2, r), col + 2).Style.NumberFormat.Format = NumberFormat;
                ws.Range(2, col + 3, Math.Max(2, r), col + 3).Style.NumberFormat.Format = PercentFormat;
            }

            var statusRange = ws.Range(2, 1, Math.Max(2, r), 1);
            foreach (var (status, color) in StatusColors)
                statusRange.AddConditionalFormat().WhenEquals($"\"{StatusText(status)}\"").Fill.SetBackgroundColor(color);

            ws.Range(1, 1, Math.Max(1, r), headers.Count).SetAutoFilter();
            ws.SheetView.FreezeRows(1);
            ws.Columns(1, headers.Count).Width = 16;
            ws.Column(headers.Count - 1).Width = 50;
        }
    }

    private static void WriteMapping(IXLWorksheet ws, ComparisonResult result)
    {
        var headers = new[] { "Role", "Synonym", "A table", "A field", "A aggregation", "A type", "B table", "B field", "B aggregation", "B type" };
        for (var c = 0; c < headers.Length; c++)
            ws.Cell(1, c + 1).Value = headers[c];
        StyleHeader(ws.Range(1, 1, 1, headers.Length));

        var r = 1;
        foreach (var pair in result.Config.FieldPairs)
        {
            r++;
            ws.Cell(r, 1).Value = pair.Role.ToString();
            SetValue(ws.Cell(r, 2), pair.Alias);
            SetValue(ws.Cell(r, 3), pair.Left.Table);
            SetValue(ws.Cell(r, 4), Describe(pair.Left));
            ws.Cell(r, 5).Value = pair.LeftAggregation.ToString();
            ws.Cell(r, 6).Value = (result.MetadataA?.KindOf(pair.Left) ?? DataKind.Unknown).ToString();
            SetValue(ws.Cell(r, 7), pair.Right.Table);
            SetValue(ws.Cell(r, 8), Describe(pair.Right));
            ws.Cell(r, 9).Value = pair.RightAggregation.ToString();
            ws.Cell(r, 10).Value = (result.MetadataB?.KindOf(pair.Right) ?? DataKind.Unknown).ToString();
        }

        ws.Range(1, 1, r, headers.Length).SetAutoFilter();
        ws.SheetView.FreezeRows(1);

        r += 2;
        ws.Cell(r, 1).Value = "Active filters";
        ws.Cell(r, 1).Style.Font.SetBold();
        if (result.Config.Options.Filters.Count == 0)
            ws.Cell(++r, 1).Value = "None";
        foreach (var filter in result.Config.Options.Filters)
        {
            r++;
            SetValue(ws.Cell(r, 1), filter.LeftColumn.QualifiedName);
            ws.Cell(r, 2).Value = filter.Operator.ToString();
            SetValue(ws.Cell(r, 3), string.Join(", ", filter.Values));
            SetValue(ws.Cell(r, 4), filter.RightColumn.QualifiedName);
            SetValue(ws.Cell(r, 5), filter.SemanticMeaning);
        }

        ws.Columns(1, headers.Length).Width = 20;
    }

    private static string Describe(FieldRef field) => field.Kind == FieldKind.Measure ? $"[{field.Name}] (measure)" : field.Name;

    private static void WriteSettings(IXLWorksheet ws, ComparisonConfig compiled)
    {
        var config = compiled.Options;
        var settings = new (string, object?)[]
        {
            ("Tolerance mode", config.Tolerance.Mode.ToString()),
            ("Absolute tolerance", config.Tolerance.Absolute),
            ("Relative tolerance", config.Tolerance.Relative),
            ("Tolerance predicate", "abs(A−B) ≤ max(absolute, relative × max(|A|, |B|))"),
            ("Null policy", config.NullPolicy.ToString()),
            ("Text comparison", config.TextComparison.ToString()),
            ("Percent baseline", config.PercentBaseline.ToString()),
            ("Key: trim whitespace", config.KeyNormalization.TrimWhitespace),
            ("Key: case-insensitive", config.KeyNormalization.CaseInsensitive),
            ("Key: Unicode normalization", config.KeyNormalization.Unicode.ToString()),
            ("Key: blank equals empty text", config.KeyNormalization.BlankEqualsEmpty),
            ("Key: date only", config.KeyNormalization.DateOnly),
            ("Duplicate key policy", config.DuplicateKeyPolicy.ToString()),
            ("Max rows per side", config.Limits.MaxRows),
            ("Timeout (seconds)", config.Limits.TimeoutSeconds),
            ("Retries", config.Limits.RetryCount),
            ("Query scope", $"{compiled.Keys.Count} key(s), {compiled.Values.Count} value(s), {config.Filters.Count} filter(s)"),
        };

        ws.Cell(1, 1).Value = "Setting";
        ws.Cell(1, 2).Value = "Value";
        StyleHeader(ws.Range(1, 1, 1, 2));
        for (var i = 0; i < settings.Length; i++)
        {
            ws.Cell(i + 2, 1).Value = settings[i].Item1;
            SetValue(ws.Cell(i + 2, 2), settings[i].Item2);
        }

        ws.Column(1).Width = 32;
        ws.Column(2).Width = 60;
    }

    private static void WriteExecutionLog(IXLWorksheet ws, ComparisonResult result, ExcelExportOptions options)
    {
        var headers = new List<string> { "Side", "Transport", "Model", "Query SHA-256", "Duration (ms)", "Rows", "Attempts", "Truncated", "Error" };
        if (options.IncludeQueryText)
            headers.Add("DAX");
        for (var c = 0; c < headers.Count; c++)
            ws.Cell(1, c + 1).Value = headers[c];
        StyleHeader(ws.Range(1, 1, 1, headers.Count));

        var r = 1;
        foreach (var q in result.Queries)
        {
            r++;
            ws.Cell(r, 1).Value = q.Side.ToString();
            SetValue(ws.Cell(r, 2), q.Transport);
            SetValue(ws.Cell(r, 3), q.ModelIdentity);
            ws.Cell(r, 4).Value = q.QueryHash;
            ws.Cell(r, 5).Value = Math.Round(q.Duration.TotalMilliseconds);
            ws.Cell(r, 6).Value = q.RowCount;
            ws.Cell(r, 7).Value = q.Attempts;
            ws.Cell(r, 8).Value = q.Truncated;
            SetValue(ws.Cell(r, 9), q.Error);
            if (options.IncludeQueryText)
                SetValue(ws.Cell(r, 10), q.Dax);
        }

        r += 2;
        foreach (var (label, value) in new (string, object?)[]
                 {
                     ("Completeness", result.Status.ToString()),
                     ("Run ID", result.RunId.ToString()),
                     ("Application version", result.AppVersion),
                     ("Model A warnings", string.Join(" | ", result.MetadataA?.Warnings ?? [])),
                     ("Model B warnings", string.Join(" | ", result.MetadataB?.Warnings ?? [])),
                 })
        {
            ws.Cell(r, 1).Value = label;
            ws.Cell(r, 1).Style.Font.SetBold();
            SetValue(ws.Cell(r, 2), value);
            r++;
        }

        ws.Columns(1, headers.Count).Width = 20;
        ws.Column(4).Width = 68;
    }

    private static void StyleHeader(IXLRange range) =>
        range.Style.Font.SetBold().Fill.SetBackgroundColor(XLColor.FromHtml("#D9E1F2"));

    public static string StatusText(RowStatus status) => status switch
    {
        RowStatus.Match => "Match",
        RowStatus.Mismatch => "Mismatch",
        RowStatus.OnlyInA => "Only in A",
        RowStatus.OnlyInB => "Only in B",
        RowStatus.InvalidKey => "Invalid key",
        _ => status.ToString(),
    };

    /// <summary>
    /// Writes a value with its native Excel type. Text is always stored as a string literal (never a formula);
    /// text that Excel could interpret as a formula also gets a quote prefix so editing it cannot turn it into one (AT-12).
    /// </summary>
    public static void SetValue(IXLCell cell, object? value)
    {
        switch (value)
        {
            case null or DBNull:
                cell.Value = Blank.Value;
                break;
            case string s:
                cell.Value = s;
                if (s.Length > 0 && s[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
                    cell.Style.IncludeQuotePrefix = true;
                break;
            case bool b:
                cell.Value = b;
                break;
            case DateTime dt:
                cell.Value = dt;
                cell.Style.NumberFormat.Format = dt.TimeOfDay == TimeSpan.Zero ? "yyyy-mm-dd" : DateFormat;
                break;
            case DateTimeOffset dto:
                cell.Value = dto.UtcDateTime;
                cell.Style.NumberFormat.Format = DateFormat;
                break;
            case decimal d:
                cell.Value = (double)d;
                break;
            case double or float or int or long or short or byte or uint or ulong or ushort or sbyte:
                cell.Value = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                break;
            default:
                SetValue(cell, Convert.ToString(value, CultureInfo.InvariantCulture));
                break;
        }
    }
}
