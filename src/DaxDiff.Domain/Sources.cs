namespace DaxDiff.Domain;

/// <summary>Which side of the comparison a source, query or error belongs to.</summary>
public enum Side
{
    A,
    B,
}

/// <summary>How a semantic model is reached (FR-001, FR-003, FR-004).</summary>
public enum SourceKind
{
    /// <summary>A PBIX opened in Power BI Desktop; queried through its local Analysis Services instance.</summary>
    DesktopSession,

    /// <summary>A Power BI Service semantic model reached through the XMLA endpoint.</summary>
    ServiceXmla,

    /// <summary>A Power BI Service semantic model reached through the Execute Queries REST API (bounded fallback).</summary>
    ServiceRest,
}

/// <summary>Identifies one semantic model. Never holds credentials or tokens (FR-014).</summary>
public sealed record ModelReference
{
    public required SourceKind Kind { get; init; }
    public required string DisplayName { get; init; }

    public string? WorkspaceId { get; init; }
    public string? WorkspaceName { get; init; }
    public string? DatasetId { get; init; }
    public string? DatasetName { get; init; }

    /// <summary>
    /// Microsoft Entra tenant of a Service model: a domain (contoso.com) or tenant ID. Empty or "organizations" means the
    /// signed-in user's home tenant. Each source has its own, so the two models can live in different tenants.
    /// </summary>
    public string? TenantId { get; init; }

    /// <summary>User name last used to sign in to this source; a hint for the next sign-in, never a credential.</summary>
    public string? AccountHint { get; init; }

    /// <summary>XMLA endpoint base, e.g. <c>powerbi://api.powerbi.com/v1.0/myorg</c>.</summary>
    public string? XmlaEndpointBase { get; init; }

    public string? LocalPbixPath { get; init; }

    /// <summary>Local Analysis Services server, e.g. <c>localhost:52341</c>. Session-specific; re-discovered on reload.</summary>
    public string? DesktopServer { get; init; }

    /// <summary>Local database (catalog) ID. Session-specific; re-discovered on reload.</summary>
    public string? DesktopDatabase { get; init; }

    /// <summary>"contoso.com: " for a model in a specific tenant; empty for the user's own tenant.</summary>
    private string TenantPrefix =>
        string.IsNullOrWhiteSpace(TenantId) || TenantId.Trim().ToLowerInvariant() is "organizations" or "common" or "myorg" ? "" : $"{TenantId.Trim()}: ";

    public string Describe() => Kind switch
    {
        SourceKind.DesktopSession => $"Desktop: {DisplayName} ({DesktopServer ?? "not attached"})",
        SourceKind.ServiceXmla => $"Service XMLA: {TenantPrefix}{WorkspaceName}/{DatasetName}",
        SourceKind.ServiceRest => $"Service REST: {TenantPrefix}{WorkspaceName}/{DatasetName}",
        _ => DisplayName,
    };
}

/// <summary>Level of metadata discovery a transport supports (§5.3).</summary>
public enum MetadataSupport
{
    None,
    Partial,
    Full,
}

/// <summary>What a connector can do. Shown to the user; never silently assumed (FR-003).</summary>
public sealed record ConnectorCapabilities
{
    public required string TransportName { get; init; }
    public bool CanQuery { get; init; }
    public MetadataSupport Metadata { get; init; }

    /// <summary>Hard per-query row cap imposed by the transport, if any.</summary>
    public int? MaxRowsPerQuery { get; init; }

    /// <summary>Hard per-query value (cell) cap imposed by the transport, if any.</summary>
    public int? MaxValuesPerQuery { get; init; }

    /// <summary>Hard per-query payload cap in bytes, if any.</summary>
    public long? MaxBytesPerQuery { get; init; }

    public string SecurityContext { get; init; } = "";
    public IReadOnlyList<string> Notes { get; init; } = [];
}
