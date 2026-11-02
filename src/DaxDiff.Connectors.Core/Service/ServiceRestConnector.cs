using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using DaxDiff.Connectors.Auth;
using DaxDiff.Domain;
using Microsoft.Extensions.Logging;

namespace DaxDiff.Connectors.Service;

/// <summary>
/// Bounded fallback that runs DAX through the Execute Queries REST API (FR-003, §5.2).
/// Documented limits: 100,000 rows or 1,000,000 values and 15 MB per query, one table per request.
/// A result at or over a limit is always flagged truncated, never reported complete (AT-07).
/// </summary>
public sealed partial class ServiceRestConnector : IModelConnector
{
    public const int MaxRowsPerQuery = 100_000;
    public const int MaxValuesPerQuery = 1_000_000;
    public const long MaxBytesPerQuery = 15L * 1024 * 1024;

    private readonly PowerBiRestClient _client;
    private readonly IAuthService _auth;
    private readonly ILogger<ServiceRestConnector> _logger;

    public ServiceRestConnector(ModelReference reference, PowerBiRestClient client, IAuthService auth, ILogger<ServiceRestConnector> logger)
    {
        if (string.IsNullOrWhiteSpace(reference.WorkspaceId) || string.IsNullOrWhiteSpace(reference.DatasetId))
            throw new DaxDiffException(ErrorCategory.Configuration, "REST-NO-TARGET",
                "The REST transport needs the workspace and semantic model IDs.",
                "Sign in and pick the model from the workspace browser.");
        Reference = reference;
        _client = client;
        _auth = auth;
        _logger = logger;
    }

    public ModelReference Reference { get; }

    public ConnectorCapabilities Capabilities { get; private set; } = new()
    {
        TransportName = "Power BI Service – Execute Queries REST API",
        CanQuery = true,
        Metadata = MetadataSupport.Partial,
        MaxRowsPerQuery = MaxRowsPerQuery,
        MaxValuesPerQuery = MaxValuesPerQuery,
        MaxBytesPerQuery = MaxBytesPerQuery,
        SecurityContext = "Signed-in user",
        Notes =
        [
            "Per query: max 100,000 rows or 1,000,000 values and 15 MB. Larger results are reported as Partial.",
            "Requires Build permission and the tenant setting 'Dataset Execute Queries REST API'.",
            "Metadata uses INFO.VIEW functions and may be incomplete.",
        ],
    };

    public string SecurityContext => $"{_auth.SignedInAccount ?? "Signed-in user"} via REST (as observed by current caller)";

    private string QueryPath =>
        $"groups/{Uri.EscapeDataString(Reference.WorkspaceId!)}/datasets/{Uri.EscapeDataString(Reference.DatasetId!)}/executeQueries";

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        // Capability check: a trivial query proves permission, tenant setting and model availability.
        await ExecuteDaxAsync("EVALUATE ROW(\"Ok\", 1)", new QueryOptions(1, TimeSpan.FromSeconds(60)), cancellationToken);
    }

    public async Task<ModelMetadata> DiscoverMetadataAsync(CancellationToken cancellationToken)
    {
        var options = new QueryOptions(MaxRowsPerQuery, TimeSpan.FromMinutes(2));
        var tables = await RowsetAsync(InfoViewMetadataReader.TablesQuery, options, cancellationToken);
        var columns = await RowsetAsync(InfoViewMetadataReader.ColumnsQuery, options, cancellationToken);
        var measures = await RowsetAsync(InfoViewMetadataReader.MeasuresQuery, options, cancellationToken);
        Rowset? relationships = null;
        try
        {
            relationships = await RowsetAsync(InfoViewMetadataReader.RelationshipsQuery, options, cancellationToken);
        }
        catch (DaxDiffException ex)
        {
            _logger.LogDebug("Relationships unavailable over REST ({Code})", ex.DiagnosticCode);
        }

        var metadata = InfoViewMetadataReader.Build(Reference.DatasetName ?? Reference.DisplayName, tables, columns, measures, relationships);
        return metadata with
        {
            Warnings = [.. metadata.Warnings, "Metadata was retrieved over REST via INFO.VIEW functions and may be incomplete."],
        };
    }

    private async Task<Rowset> RowsetAsync(string dax, QueryOptions options, CancellationToken cancellationToken)
    {
        var result = await ExecuteDaxAsync(dax, options, cancellationToken);
        return new Rowset(result.ColumnNames, result.Rows);
    }

    public async Task<QueryResult> ExecuteDaxAsync(string dax, QueryOptions options, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.Timeout);

        var body = JsonSerializer.Serialize(new
        {
            queries = new[] { new { query = dax } },
            serializerSettings = new { includeNulls = true },
        });

        JsonDocument doc;
        try
        {
            doc = await _client.PostJsonAsync(QueryPath, body, timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new DaxDiffException(ErrorCategory.Timeout, "QRY-TIMEOUT", "The REST query timed out.",
                "Increase the timeout, add filters or reduce the grain.", isTransient: true);
        }

        using (doc)
        {
            return Parse(doc.RootElement, options.MaxRows);
        }
    }

    /// <summary>Parses an executeQueries response. Public for unit testing.</summary>
    public static QueryResult Parse(JsonElement root, int maxRows)
    {
        if (root.TryGetProperty("error", out var topError))
            throw RestQueryError(topError);

        var result = root.GetProperty("results")[0];
        if (result.TryGetProperty("error", out var error))
            throw RestQueryError(error);

        var table = result.GetProperty("tables")[0];
        var names = new List<string>();
        var rows = new List<object?[]>();
        var truncated = false;
        string? reason = null;

        foreach (var row in table.GetProperty("rows").EnumerateArray())
        {
            if (names.Count == 0)
                names.AddRange(row.EnumerateObject().Select(p => p.Name));

            if (rows.Count >= maxRows)
            {
                truncated = true;
                reason = $"Result exceeded the row cap of {maxRows:N0}.";
                break;
            }

            var values = new object?[names.Count];
            foreach (var property in row.EnumerateObject())
            {
                var index = names.IndexOf(property.Name);
                if (index >= 0)
                    values[index] = ToClr(property.Value);
            }

            rows.Add(values);
        }

        // The service cuts results at its limits without failing; treat reaching a limit as truncation.
        if (!truncated && rows.Count >= MaxRowsPerQuery)
        {
            truncated = true;
            reason = "The REST API row limit (100,000) was reached; the result may be incomplete.";
        }
        else if (!truncated && (long)rows.Count * Math.Max(1, names.Count) >= MaxValuesPerQuery)
        {
            truncated = true;
            reason = "The REST API value limit (1,000,000) was reached; the result may be incomplete.";
        }

        return new QueryResult { ColumnNames = names, Rows = rows, IsTruncated = truncated, TruncationReason = reason };
    }

    private static DaxDiffException RestQueryError(JsonElement error)
    {
        var text = error.ToString();
        var lower = text.ToLowerInvariant();
        if (lower.Contains("limit") || lower.Contains("exceed") || lower.Contains("too large"))
            return new DaxDiffException(ErrorCategory.UnsupportedModel, "REST-LIMIT",
                "The query result exceeds the REST API limits (100,000 rows, 1,000,000 values or 15 MB).",
                "Add filters or reduce the grain, or use the XMLA transport.");
        return new DaxDiffException(ErrorCategory.InvalidQuery, "REST-QUERY",
            "The service rejected the query: " + (text.Length > 400 ? text[..400] + "…" : text),
            "Refresh metadata and check the mapped fields.");
    }

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d+)?$")]
    private static partial Regex IsoDateTime();

    /// <summary>
    /// JSON → CLR values aligned with ADOMD types so keys compare across transports:
    /// numbers become decimal (or double), ISO timestamps become <see cref="DateTime"/>.
    /// </summary>
    private static object? ToClr(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Number => value.TryGetInt64(out var l) ? l
            : value.TryGetDecimal(out var d) ? d
            : value.GetDouble(),
        JsonValueKind.String when IsoDateTime().IsMatch(value.GetString()!) =>
            DateTime.Parse(value.GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
        JsonValueKind.String => value.GetString(),
        _ => value.ToString(),
    };

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
