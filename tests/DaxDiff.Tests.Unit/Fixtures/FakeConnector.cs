using DaxDiff.Connectors;
using DaxDiff.Domain;

namespace DaxDiff.Tests.Unit.Fixtures;

/// <summary>Deterministic in-memory connector for orchestration tests (§13.3: connectors behind interfaces).</summary>
internal sealed class FakeConnector : IModelConnector
{
    private readonly Func<string, QueryOptions, CancellationToken, Task<QueryResult>> _execute;

    public FakeConnector(string name, Func<string, QueryOptions, CancellationToken, Task<QueryResult>> execute, SourceKind kind = SourceKind.ServiceXmla)
    {
        _execute = execute;
        Reference = new ModelReference { Kind = kind, DisplayName = name, WorkspaceName = "Test", DatasetName = name };
    }

    public static FakeConnector Returning(string name, QueryResult result) =>
        new(name, (_, options, _) => Task.FromResult(Cap(result, options.MaxRows)));

    private static QueryResult Cap(QueryResult result, int maxRows) => result.Rows.Count <= maxRows
        ? result
        : new QueryResult
        {
            ColumnNames = result.ColumnNames,
            Rows = result.Rows.Take(maxRows).ToList(),
            IsTruncated = true,
            TruncationReason = $"Result exceeded the row cap of {maxRows:N0}.",
        };

    public List<string> ExecutedQueries { get; } = [];
    public ModelReference Reference { get; }
    public ConnectorCapabilities Capabilities { get; } = new() { TransportName = "Fake", CanQuery = true, Metadata = MetadataSupport.Full };
    public string SecurityContext => "test-user";

    public Task ConnectAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<ModelMetadata> DiscoverMetadataAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<QueryResult> ExecuteDaxAsync(string dax, QueryOptions options, CancellationToken cancellationToken)
    {
        ExecutedQueries.Add(dax);
        return _execute(dax, options, cancellationToken);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
