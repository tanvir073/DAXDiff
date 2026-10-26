using DaxDiff.Connectors.Adomd;
using DaxDiff.Connectors.Auth;
using DaxDiff.Connectors.Service;
using DaxDiff.Domain;
using Microsoft.Extensions.Logging;

namespace DaxDiff.Connectors;

public sealed class ConnectorFactory : IConnectorFactory
{
    private readonly IAuthService _auth;
    private readonly AuthOptions _options;
    private readonly PowerBiRestClient _restClient;
    private readonly ILoggerFactory _loggerFactory;

    public ConnectorFactory(IAuthService auth, AuthOptions options, PowerBiRestClient restClient, ILoggerFactory loggerFactory)
    {
        _auth = auth;
        _options = options;
        _restClient = restClient;
        _loggerFactory = loggerFactory;
    }

    public IModelConnector Create(ModelReference reference) => reference.Kind switch
    {
        SourceKind.DesktopSession => new DesktopSessionConnector(reference, _loggerFactory.CreateLogger<DesktopSessionConnector>()),
        SourceKind.ServiceXmla => new ServiceXmlaConnector(reference, _auth, _options, _loggerFactory.CreateLogger<ServiceXmlaConnector>()),
        SourceKind.ServiceRest => new ServiceRestConnector(reference, _restClient, _auth, _loggerFactory.CreateLogger<ServiceRestConnector>()),
        _ => throw new ArgumentOutOfRangeException(nameof(reference)),
    };
}
