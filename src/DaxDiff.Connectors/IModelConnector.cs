using DaxDiff.Domain;

namespace DaxDiff.Connectors;

public sealed record QueryOptions(int MaxRows, TimeSpan Timeout);

/// <summary>Read-only access to one semantic model (§7). Implementations never modify or refresh the model.</summary>
public interface IModelConnector : IAsyncDisposable
{
    ModelReference Reference { get; }

    /// <summary>Current capabilities; refined after <see cref="ConnectAsync"/> and metadata discovery.</summary>
    ConnectorCapabilities Capabilities { get; }

    /// <summary>Who the queries run as, for display and audit (FR-016).</summary>
    string SecurityContext { get; }

    Task ConnectAsync(CancellationToken cancellationToken);

    Task<ModelMetadata> DiscoverMetadataAsync(CancellationToken cancellationToken);

    /// <summary>Executes a DAX query. Results beyond <see cref="QueryOptions.MaxRows"/> are not read and the result is flagged truncated.</summary>
    Task<QueryResult> ExecuteDaxAsync(string dax, QueryOptions options, CancellationToken cancellationToken);
}

public interface IConnectorFactory
{
    IModelConnector Create(ModelReference reference);
}
