using DaxDiff.Connectors.Adomd;
using DaxDiff.Connectors.Auth;
using DaxDiff.Connectors.Service;
using DaxDiff.Domain;
using Microsoft.Extensions.Logging;

namespace DaxDiff.Connectors;

public sealed class ConnectorFactory : IConnectorFactory
{
    private readonly AuthOptions _options;
    private readonly ILoggerFactory _loggerFactory;

    public ConnectorFactory(AuthOptions options, ILoggerFactory loggerFactory)
    {
        _options = options;
        _loggerFactory = loggerFactory;
    }

    /// <summary>Service connectors use the given source's own sign-in, so each side can be a different tenant or user.</summary>
    public IModelConnector Create(ModelReference reference, SourceSession session) => reference.Kind switch
    {
        SourceKind.DesktopSession => new DesktopSessionConnector(reference, _loggerFactory.CreateLogger<DesktopSessionConnector>()),
        SourceKind.ServiceXmla => new ServiceXmlaConnector(reference, session.Auth, _options, _loggerFactory.CreateLogger<ServiceXmlaConnector>()),
        SourceKind.ServiceRest => new ServiceRestConnector(reference, session.Rest, session.Auth, _loggerFactory.CreateLogger<ServiceRestConnector>()),
        _ => throw new ArgumentOutOfRangeException(nameof(reference)),
    };
}
