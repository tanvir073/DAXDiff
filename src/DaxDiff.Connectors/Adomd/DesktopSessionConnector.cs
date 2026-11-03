using DaxDiff.Domain;
using Microsoft.AnalysisServices.AdomdClient;
using Microsoft.Extensions.Logging;
using System.Globalization;
using DaxDiff.Connectors.Resources;

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
                string.Format(CultureInfo.CurrentCulture, Strings.Desk_NoSession, reference.DisplayName),
                Strings.Remedy_DeskNoSession);
    }

    public override ConnectorCapabilities Capabilities { get; protected set; } = new()
    {
        TransportName = Strings.Desk_TransportName,
        CanQuery = true,
        Metadata = MetadataSupport.Full,
        SecurityContext = Strings.Desk_SecurityContextShort,
        Notes =
        [
            Strings.Desk_NoteKeepOpen,
            Strings.Desk_NoteNoRls,
        ],
    };

    public override string SecurityContext =>
        string.Format(CultureInfo.CurrentCulture, Strings.Desk_SecurityContext, $"{Environment.UserDomainName}\\{Environment.UserName}");

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
