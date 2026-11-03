using DaxDiff.Connectors.Auth;
using DaxDiff.Domain;
using Microsoft.AnalysisServices;
using Microsoft.AnalysisServices.AdomdClient;
using Microsoft.Extensions.Logging;
using System.Globalization;
using DaxDiff.Connectors.Resources;

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
                Strings.Xmla_NoTarget, Strings.Remedy_XmlaNoTarget);
    }

    public override ConnectorCapabilities Capabilities { get; protected set; } = new()
    {
        TransportName = Strings.Xmla_TransportName,
        CanQuery = true,
        Metadata = MetadataSupport.Full,
        SecurityContext = Strings.SignedInUser,
        Notes =
        [
            Strings.Xmla_NoteCapacity,
            Strings.Xmla_NoteRls,
        ],
    };

    public override string SecurityContext =>
        string.Format(CultureInfo.CurrentCulture, Strings.Xmla_SecurityContext, AuthOptions.IsHomeTenant(Reference.TenantId)
            ? _account ?? _auth.SignedInAccount ?? Strings.SignedInUser
            : string.Format(CultureInfo.CurrentCulture, Strings.Xmla_InTenant, _account ?? _auth.SignedInAccount ?? Strings.SignedInUser, Reference.TenantId));

    protected override string TransportName => "Power BI XMLA";

    /// <summary>
    /// The XMLA data source for a workspace. For another tenant the "myorg" segment is replaced by that tenant's domain
    /// or ID (powerbi://api.powerbi.com/v1.0/contoso.com/Sales), which is how the service addresses external tenants.
    /// A full connection link copied from the workspace settings is used as it is, except that its "myorg" segment
    /// also gets the tenant.
    /// </summary>
    public static string DataSource(string endpointBase, string? tenant, string workspace)
    {
        workspace = workspace.Trim();
        if (workspace.StartsWith("powerbi://", StringComparison.OrdinalIgnoreCase))
        {
            var at = workspace.IndexOf("/myorg/", StringComparison.OrdinalIgnoreCase);
            return at < 0 || AuthOptions.IsHomeTenant(tenant)
                ? workspace
                : workspace[..(at + 1)] + tenant!.Trim() + workspace[(at + "/myorg".Length)..];
        }
        var baseUrl = endpointBase.TrimEnd('/');
        if (!AuthOptions.IsHomeTenant(tenant) && baseUrl.EndsWith("/myorg", StringComparison.OrdinalIgnoreCase))
            baseUrl = baseUrl[..^"myorg".Length] + tenant!.Trim();
        return baseUrl + "/" + workspace;
    }

    protected override async Task<AdomdConnection> CreateConnectionAsync(CancellationToken cancellationToken)
    {
        var endpoint = DataSource(Reference.XmlaEndpointBase ?? _options.XmlaEndpointBase, Reference.TenantId ?? _auth.Tenant, Reference.WorkspaceName!);
        var builder = new System.Data.Common.DbConnectionStringBuilder
        {
            ["Data Source"] = endpoint,
            ["Initial Catalog"] = Reference.DatasetName!,
        };
        // Without MSAL, ADOMD signs in on its own and silently picks the Windows account. "User ID" (no password) makes it
        // sign in as the account the user named instead, prompting when that account has no cached sign-in.
        if (!_auth.IsConfigured && !string.IsNullOrWhiteSpace(Reference.AccountHint))
        {
            builder["User ID"] = Reference.AccountHint.Trim();
            _account = Reference.AccountHint.Trim();
        }
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
                Notes = [.. Capabilities.Notes, Strings.Xmla_NoteNoClientId],
            };
        }

        return connection;
    }
}
