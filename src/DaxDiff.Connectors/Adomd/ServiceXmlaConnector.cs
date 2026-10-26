using DaxDiff.Connectors.Auth;
using DaxDiff.Domain;
using Microsoft.AnalysisServices;
using Microsoft.AnalysisServices.AdomdClient;
using Microsoft.Extensions.Logging;

namespace DaxDiff.Connectors.Adomd;

/// <summary>
/// Queries a Power BI Service semantic model through the XMLA endpoint (FR-002, FR-003, §5.2).
/// Uses an MSAL token when a client ID is configured, otherwise ADOMD's own interactive sign-in.
/// </summary>
public sealed class ServiceXmlaConnector : AdomdConnectorBase
{
    private readonly IAuthService _auth;
    private readonly AuthOptions _options;
    private string? _account;

    public ServiceXmlaConnector(ModelReference reference, IAuthService auth, AuthOptions options, ILogger<ServiceXmlaConnector> logger)
        : base(reference, logger)
    {
        _auth = auth;
        _options = options;
        if (string.IsNullOrWhiteSpace(reference.WorkspaceName) || string.IsNullOrWhiteSpace(reference.DatasetName))
            throw new DaxDiffException(ErrorCategory.Configuration, "XMLA-NO-TARGET",
                "Select a workspace and semantic model first.", "Choose the workspace and model in the Sources step.");
    }

    public override ConnectorCapabilities Capabilities { get; protected set; } = new()
    {
        TransportName = "Power BI Service – XMLA endpoint",
        CanQuery = true,
        Metadata = MetadataSupport.Full,
        SecurityContext = "Signed-in user",
        Notes =
        [
            "Requires Premium, PPU or Fabric capacity with XMLA read enabled.",
            "Results reflect the signed-in user's permissions; RLS/OLS apply to Viewer-level access, while workspace Admins, Members and Contributors bypass RLS.",
        ],
    };

    public override string SecurityContext =>
        $"{_account ?? _auth.SignedInAccount ?? "Signed-in user"} via XMLA (as observed by current caller)";

    protected override string TransportName => "Power BI XMLA";

    protected override async Task<AdomdConnection> CreateConnectionAsync(CancellationToken cancellationToken)
    {
        var endpoint = (Reference.XmlaEndpointBase ?? _options.XmlaEndpointBase).TrimEnd('/') + "/" + Reference.WorkspaceName;
        var builder = new System.Data.Common.DbConnectionStringBuilder
        {
            ["Data Source"] = endpoint,
            ["Initial Catalog"] = Reference.DatasetName!,
        };
        var connection = new AdomdConnection(builder.ConnectionString);

        if (_auth.IsConfigured)
        {
            var token = await _auth.GetTokenAsync(cancellationToken);
            _account = token.Account;
            connection.AccessToken = new AccessToken(token.AccessToken, token.ExpiresOn, null);
            connection.OnAccessTokenExpired = _ =>
            {
                var refreshed = _auth.GetTokenAsync(CancellationToken.None).GetAwaiter().GetResult();
                return new AccessToken(refreshed.AccessToken, refreshed.ExpiresOn, null);
            };
        }
        else
        {
            Capabilities = Capabilities with
            {
                Notes = [.. Capabilities.Notes, "No client ID configured: ADOMD's built-in interactive sign-in is used."],
            };
        }

        return connection;
    }
}
