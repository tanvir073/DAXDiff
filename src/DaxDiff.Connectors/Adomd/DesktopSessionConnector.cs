using DaxDiff.Domain;
using Microsoft.AnalysisServices.AdomdClient;
using Microsoft.Extensions.Logging;

namespace DaxDiff.Connectors.Adomd;

/// <summary>
/// Queries a PBIX that is open in Power BI Desktop through its local Analysis Services instance (FR-004, §5.1).
/// No offline PBIX access is attempted.
/// </summary>
public sealed class DesktopSessionConnector : AdomdConnectorBase
{
    public DesktopSessionConnector(ModelReference reference, ILogger<DesktopSessionConnector> logger)
        : base(reference, logger)
    {
        if (string.IsNullOrWhiteSpace(reference.DesktopServer))
            throw new DaxDiffException(ErrorCategory.Configuration, "DESK-NO-SESSION",
                $"'{reference.DisplayName}' is not attached to a running Power BI Desktop session.",
                "Open the PBIX in Power BI Desktop, then choose 'Detect sessions' and select it (or launch DAXDiff from External Tools).");
    }

    public override ConnectorCapabilities Capabilities { get; protected set; } = new()
    {
        TransportName = "Power BI Desktop (local Analysis Services)",
        CanQuery = true,
        Metadata = MetadataSupport.Full,
        SecurityContext = "Local Desktop session",
        Notes =
        [
            "The PBIX must stay open in Power BI Desktop for the duration of the run.",
            "Desktop sessions run with model-administrator rights: row-level security roles are not applied.",
        ],
    };

    public override string SecurityContext =>
        $"{Environment.UserDomainName}\\{Environment.UserName} via local Desktop session (model admin; RLS not applied)";

    protected override string TransportName => "Power BI Desktop";

    protected override Task<AdomdConnection> CreateConnectionAsync(CancellationToken cancellationToken)
    {
        var builder = new System.Data.Common.DbConnectionStringBuilder
        {
            ["Data Source"] = Reference.DesktopServer!,
        };
        if (!string.IsNullOrWhiteSpace(Reference.DesktopDatabase))
            builder["Initial Catalog"] = Reference.DesktopDatabase!;
        return Task.FromResult(new AdomdConnection(builder.ConnectionString));
    }

    protected override Task OnConnectedAsync(AdomdConnection connection, CancellationToken cancellationToken)
    {
        // Desktop hosts a single database whose ID changes every session; resolve it if it was not supplied.
        if (string.IsNullOrWhiteSpace(Reference.DesktopDatabase) && !string.IsNullOrEmpty(connection.Database))
            Reference = Reference with { DesktopDatabase = connection.Database };
        return Task.CompletedTask;
    }
}
