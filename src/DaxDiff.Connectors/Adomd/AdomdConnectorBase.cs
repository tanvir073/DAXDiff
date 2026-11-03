using DaxDiff.Domain;
using Microsoft.AnalysisServices.AdomdClient;
using Microsoft.Extensions.Logging;
using System.Globalization;
using DaxDiff.Connectors.Resources;

namespace DaxDiff.Connectors.Adomd;

/// <summary>
/// Shared ADOMD.NET plumbing for Desktop sessions and Service XMLA: serialized command execution,
/// cancellation, row capping and DMV-based metadata with fallbacks.
/// </summary>
public abstract class AdomdConnectorBase : IModelConnector
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private AdomdConnection? _connection;

    protected AdomdConnectorBase(ModelReference reference, ILogger logger)
    {
        Reference = reference;
        Logger = logger;
    }

    public ModelReference Reference { get; protected set; }
    public abstract ConnectorCapabilities Capabilities { get; protected set; }
    public abstract string SecurityContext { get; }
    protected ILogger Logger { get; }
    protected abstract string TransportName { get; }

    protected abstract Task<AdomdConnection> CreateConnectionAsync(CancellationToken cancellationToken);

    /// <summary>Hook to resolve session details (such as the Desktop database ID) after opening.</summary>
    protected virtual Task OnConnectedAsync(AdomdConnection connection, CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_connection?.State == System.Data.ConnectionState.Open)
                return;
            _connection?.Dispose();
            var connection = await CreateConnectionAsync(cancellationToken);
            await Task.Run(connection.Open, cancellationToken);
            _connection = connection;
            await OnConnectedAsync(connection, cancellationToken);
            Logger.LogInformation("Connected via {Transport}", TransportName);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw ErrorClassifier.Classify(ex, TransportName);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<QueryResult> ExecuteDaxAsync(string dax, QueryOptions options, CancellationToken cancellationToken)
    {
        await ConnectAsync(cancellationToken);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await Task.Run(() => Execute(_connection!, dax, options, cancellationToken), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested)
                throw new OperationCanceledException(cancellationToken);
            throw ErrorClassifier.Classify(ex, TransportName);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static QueryResult Execute(AdomdConnection connection, string commandText, QueryOptions options, CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.CommandText = commandText;
        command.CommandTimeout = (int)Math.Ceiling(options.Timeout.TotalSeconds);
        using var registration = cancellationToken.Register(() =>
        {
            try
            {
                command.Cancel();
            }
            catch
            {
                // Cancellation is best effort; the token is still observed below.
            }
        });

        using var reader = command.ExecuteReader();
        var names = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToList();
        var rows = new List<object?[]>();
        var truncated = false;
        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (rows.Count >= options.MaxRows)
            {
                truncated = true;
                break;
            }

            var values = new object?[reader.FieldCount];
            reader.GetValues(values!);
            for (var i = 0; i < values.Length; i++)
                if (values[i] is DBNull)
                    values[i] = null;
            rows.Add(values);
        }

        if (truncated)
        {
            try
            {
                command.Cancel();
            }
            catch
            {
                // The server may already have finished streaming.
            }
        }

        return new QueryResult
        {
            ColumnNames = names,
            Rows = rows,
            IsTruncated = truncated,
            TruncationReason = truncated ? string.Format(CultureInfo.CurrentCulture, Strings.Truncated_RowCap, options.MaxRows) : null,
        };
    }

    public async Task<ModelMetadata> DiscoverMetadataAsync(CancellationToken cancellationToken)
    {
        await ConnectAsync(cancellationToken);
        var options = new QueryOptions(int.MaxValue, TimeSpan.FromMinutes(5));
        var modelName = Reference.DatasetName ?? Reference.DisplayName;

        try
        {
            var tables = await RowsetAsync("SELECT * FROM $SYSTEM.TMSCHEMA_TABLES", options, cancellationToken);
            var columns = await RowsetAsync("SELECT * FROM $SYSTEM.TMSCHEMA_COLUMNS", options, cancellationToken);
            var measures = await RowsetAsync("SELECT * FROM $SYSTEM.TMSCHEMA_MEASURES", options, cancellationToken);
            var relationships = await OptionalRowsetAsync("SELECT * FROM $SYSTEM.TMSCHEMA_RELATIONSHIPS", options, cancellationToken);
            var calcGroups = await OptionalRowsetAsync("SELECT * FROM $SYSTEM.TMSCHEMA_CALCULATION_GROUPS", options, cancellationToken);
            var partitions = await OptionalRowsetAsync("SELECT * FROM $SYSTEM.TMSCHEMA_PARTITIONS", options, cancellationToken);

            DateTimeOffset? lastRefresh = partitions?.Rows
                .Select(r => partitions.Get(r, "RefreshedTime"))
                .OfType<DateTime>()
                .Where(d => d.Year > 1900)
                .Select(d => (DateTimeOffset?)new DateTimeOffset(DateTime.SpecifyKind(d, DateTimeKind.Utc)))
                .DefaultIfEmpty(null)
                .Max();

            Capabilities = Capabilities with { Metadata = MetadataSupport.Full };
            return TmschemaMetadataReader.Build(modelName, tables, columns, measures, relationships, calcGroups, lastRefresh);
        }
        catch (DaxDiffException ex) when (ex.Category is ErrorCategory.Authorization or ErrorCategory.InvalidQuery or ErrorCategory.Unknown)
        {
            Logger.LogInformation("TMSCHEMA metadata unavailable ({Code}); trying INFO.VIEW", ex.DiagnosticCode);
        }

        try
        {
            var tables = await RowsetAsync(InfoViewMetadataReader.TablesQuery, options, cancellationToken);
            var columns = await RowsetAsync(InfoViewMetadataReader.ColumnsQuery, options, cancellationToken);
            var measures = await RowsetAsync(InfoViewMetadataReader.MeasuresQuery, options, cancellationToken);
            var relationships = await OptionalRowsetAsync(InfoViewMetadataReader.RelationshipsQuery, options, cancellationToken);
            Capabilities = Capabilities with { Metadata = MetadataSupport.Full };
            return InfoViewMetadataReader.Build(modelName, tables, columns, measures, relationships);
        }
        catch (DaxDiffException ex) when (ex.Category is ErrorCategory.Authorization or ErrorCategory.InvalidQuery or ErrorCategory.Unknown)
        {
            Logger.LogInformation("INFO.VIEW metadata unavailable ({Code}); falling back to MDSCHEMA", ex.DiagnosticCode);
        }

        var mdColumns = await RowsetAsync(MdschemaMetadataReader.ColumnsQuery, options, cancellationToken);
        var mdMeasures = await RowsetAsync(MdschemaMetadataReader.MeasuresQuery, options, cancellationToken);
        Capabilities = Capabilities with { Metadata = MetadataSupport.Partial };
        return MdschemaMetadataReader.Build(modelName, mdColumns, mdMeasures);
    }

    private async Task<Rowset> RowsetAsync(string query, QueryOptions options, CancellationToken cancellationToken)
    {
        var result = await ExecuteDaxAsync(query, options, cancellationToken);
        return new Rowset(result.ColumnNames, result.Rows);
    }

    private async Task<Rowset?> OptionalRowsetAsync(string query, QueryOptions options, CancellationToken cancellationToken)
    {
        try
        {
            return await RowsetAsync(query, options, cancellationToken);
        }
        catch (DaxDiffException ex)
        {
            Logger.LogDebug("Optional metadata query failed ({Code})", ex.DiagnosticCode);
            return null;
        }
    }

    public ValueTask DisposeAsync()
    {
        _connection?.Dispose();
        _connection = null;
        _gate.Dispose();
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }
}
