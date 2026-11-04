using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DaxDiff.Connectors;
using DaxDiff.Connectors.Auth;
using DaxDiff.Connectors.Desktop;
using DaxDiff.Connectors.Service;
using DaxDiff.Desktop.Services;
using DaxDiff.Domain;
using Microsoft.Extensions.Logging;
using DaxDiff.Desktop.Resources;

namespace DaxDiff.Desktop.ViewModels;

public enum ConnectionState
{
    NotConnected,
    Working,
    Connected,
    Failed,
}

/// <summary>One source card (A or B): source type, sign-in/attach, model choice, test and metadata (FR-001..FR-005).</summary>
public sealed partial class SourcePanelViewModel : ObservableObject
{
    private readonly IConnectorFactory _factory;
    private readonly DesktopSessionLocator _locator;
    private readonly SourceSession _session;
    private readonly IDialogService _dialogs;
    private readonly ILogger _logger;

    public SourcePanelViewModel(
        Side side,
        IConnectorFactory factory,
        DesktopSessionLocator locator,
        SourceSession session,
        IDialogService dialogs,
        ILogger logger)
    {
        Side = side;
        _factory = factory;
        _locator = locator;
        _session = session;
        _tenant = session.Auth.Tenant;
        _dialogs = dialogs;
        _logger = logger;
        WorkspacesView = ListViews.Create(Workspaces);
        WorkspacesView.Filter = o => string.IsNullOrWhiteSpace(WorkspaceSearch) ||
                                     ((WorkspaceInfo)o).Name.Contains(WorkspaceSearch, StringComparison.OrdinalIgnoreCase);
        DatasetsView = ListViews.Create(Datasets);
        DatasetsView.Filter = o => string.IsNullOrWhiteSpace(DatasetSearch) ||
                                   ((DatasetInfo)o).Name.Contains(DatasetSearch, StringComparison.OrdinalIgnoreCase);
    }

    public Side Side { get; }
    public string Title => Localized.Format(Strings.Source_Title, Side);

    /// <summary>Raised when metadata was (re)loaded or cleared.</summary>
    public event EventHandler? MetadataChanged;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDesktop), nameof(IsXmla), nameof(IsRest), nameof(IsService), nameof(Identity))]
    private SourceKind _kind = SourceKind.DesktopSession;

    public bool IsDesktop { get => Kind == SourceKind.DesktopSession; set { if (value) Kind = SourceKind.DesktopSession; } }
    public bool IsXmla { get => Kind == SourceKind.ServiceXmla; set { if (value) Kind = SourceKind.ServiceXmla; } }
    public bool IsRest { get => Kind == SourceKind.ServiceRest; set { if (value) Kind = SourceKind.ServiceRest; } }
    public bool IsService => Kind != SourceKind.DesktopSession;

    // ---- Desktop -----------------------------------------------------------------------------

    public ObservableCollection<DesktopSession> Sessions { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Identity))]
    private DesktopSession? _selectedSession;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Identity))]
    private string? _pbixPath;

    /// <summary>Database ID handed over by External Tools; used instead of auto-resolution.</summary>
    private string? _externalDatabase;

    [RelayCommand]
    private void DetectSessions()
    {
        var previous = SelectedSession;
        Sessions.Clear();
        foreach (var session in _locator.FindSessions())
            Sessions.Add(session);

        SelectedSession = Sessions.FirstOrDefault(s => previous is not null && s.Port == previous.Port)
                          ?? Sessions.FirstOrDefault(MatchesPbix)
                          ?? (Sessions.Count == 1 ? Sessions[0] : null);
        StatusText = Sessions.Count switch
        {
            0 => Strings.Source_SessionsNone,
            1 => Strings.Source_SessionsOne,
            _ => Localized.Format(Strings.Source_SessionsMany, Sessions.Count),
        };
    }

    private bool MatchesPbix(DesktopSession session)
    {
        if (string.IsNullOrEmpty(PbixPath))
            return false;
        if (session.PbixPath is { } path)
            return string.Equals(Path.GetFullPath(path), Path.GetFullPath(PbixPath), StringComparison.OrdinalIgnoreCase);
        var name = Path.GetFileNameWithoutExtension(PbixPath);
        return session.WindowTitle?.StartsWith(name + " ", StringComparison.OrdinalIgnoreCase) == true ||
               string.Equals(session.WindowTitle, name, StringComparison.OrdinalIgnoreCase);
    }

    [RelayCommand]
    private void BrowsePbix()
    {
        var path = _dialogs.OpenFile(Strings.Source_PbixFilter, Strings.Source_PbixDialogTitle);
        if (path is null)
            return;
        PbixPath = path;
        StatusText = Strings.Source_OpenInDesktopHint;
    }

    [RelayCommand]
    private void OpenPbixInDesktop()
    {
        if (string.IsNullOrEmpty(PbixPath) || !File.Exists(PbixPath))
        {
            _dialogs.ShowInfo(Strings.Source_ChoosePbixFirst);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(PbixPath!) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _dialogs.ShowError(new DaxDiffException(ErrorCategory.Configuration, "DESK-OPEN",
                Strings.Source_OpenInDesktopFailed, Strings.Source_OpenInDesktopFailedRemedy, Side, inner: ex));
        }
    }

    /// <summary>Attach to a session passed by Power BI Desktop External Tools.</summary>
    public void AttachExternal(string server, string? database)
    {
        Kind = SourceKind.DesktopSession;
        DetectSessions();
        var port = int.TryParse(server.Split(':').Last(), out var p) ? p : 0;
        var match = Sessions.FirstOrDefault(s => s.Port == port);
        if (match is null)
        {
            match = new DesktopSession(0, port, "", null, Strings.Source_LaunchedSession, null);
            Sessions.Insert(0, match);
        }

        SelectedSession = match;
        _externalDatabase = database;
        StatusText = Localized.Format(Strings.Source_AttachedExternal, server);
    }

    // ---- Service -----------------------------------------------------------------------------

    public ObservableCollection<WorkspaceInfo> Workspaces { get; } = [];
    public IListView WorkspacesView { get; }
    public ObservableCollection<DatasetInfo> Datasets { get; } = [];
    public IListView DatasetsView { get; }

    private IAuthService Auth => _session.Auth;

    public bool CanBrowseService => Auth.IsConfigured;

    public string ServiceHint => Auth.IsConfigured
        ? Strings.Source_ServiceHintSignIn
        : Strings.Source_ServiceHintNoClientId;

    /// <summary>
    /// Tenant of this source: a domain (contoso.com) or tenant ID, or "organizations" for the signed-in user's own
    /// tenant. Each source has its own (FR-002).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Identity))]
    private string _tenant;

    partial void OnTenantChanged(string value)
    {
        Auth.Tenant = value;
        SignedInAs = Auth.SignedInAccount;
        Workspaces.Clear();
        Datasets.Clear();
    }

    /// <summary>
    /// The account (user@domain) this source signs in as. Without a client ID it is passed to ADOMD, so XMLA does not
    /// silently use the Windows account.
    /// </summary>
    public string? AccountHint
    {
        get => Auth.AccountHint;
        set
        {
            value = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            if (Auth.AccountHint == value)
                return;
            Auth.AccountHint = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Identity));
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSignedIn), nameof(SignInStatusText))]
    private string? _signedInAs;

    public bool IsSignedIn => SignedInAs is not null;

    public string SignInStatusText => SignedInAs is null ? Strings.Source_NotSignedIn : Localized.Format(Strings.Source_SignedInAs, SignedInAs);

    private string SignedInText(AuthToken token) =>
        token.Account + TenantSuffix();

    /// <summary>" (tenant …)" after the account when the source signs in to another tenant than the user's own.</summary>
    private string TenantSuffix() => AuthOptions.IsHomeTenant(Tenant) ? "" : Localized.Format(Strings.Source_TenantSuffix, Tenant);

    [ObservableProperty]
    private string _workspaceSearch = "";

    [ObservableProperty]
    private string _datasetSearch = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Identity))]
    private WorkspaceInfo? _selectedWorkspace;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Identity))]
    private DatasetInfo? _selectedDataset;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Identity))]
    private string? _workspaceName;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Identity))]
    private string? _datasetName;

    private string? _workspaceId;
    private string? _datasetId;

    partial void OnWorkspaceSearchChanged(string value) => WorkspacesView.Refresh();

    partial void OnDatasetSearchChanged(string value) => DatasetsView.Refresh();

    /// <summary>Signs this source in to the Power BI service with an account picker, then lists its workspaces.</summary>
    [RelayCommand]
    private async Task SignInAsync()
    {
        if (!Auth.IsConfigured)
        {
            _dialogs.ShowInfo(ServiceHint + Environment.NewLine + Environment.NewLine + Strings.Source_SetupDocHint, Strings.Source_SignInDialogTitle);
            return;
        }

        await GuardAsync(Strings.Source_SigningIn, async () =>
        {
            Disconnect();
            var token = await Auth.SignInAsync(CancellationToken.None);
            SignedInAs = SignedInText(token);
            await LoadWorkspacesAsync();
            StatusText = Localized.Format(Strings.Source_SignedInWorkspaces, SignedInAs, Workspaces.Count);
        }, setConnected: false);
    }

    [RelayCommand]
    private async Task SignOutAsync()
    {
        Disconnect();
        await Auth.SignOutAsync();
        SignedInAs = null;
        Workspaces.Clear();
        Datasets.Clear();
        StatusText = Strings.Source_SignedOut;
    }

    [RelayCommand]
    private async Task RefreshWorkspacesAsync()
    {
        if (!Auth.IsConfigured)
            return;
        await GuardAsync(Strings.Source_LoadingWorkspaces, async () =>
        {
            await LoadWorkspacesAsync();
            StatusText = Localized.Format(Strings.Source_WorkspacesAvailable, Workspaces.Count);
        }, setConnected: false);
    }

    private async Task LoadWorkspacesAsync()
    {
        var token = await Auth.GetTokenAsync(CancellationToken.None);
        SignedInAs = SignedInText(token);
        Workspaces.Clear();
        foreach (var workspace in await _session.Discovery.GetWorkspacesAsync(CancellationToken.None))
            Workspaces.Add(workspace);
        SelectedWorkspace = Workspaces.FirstOrDefault(w => w.Id == _workspaceId || w.Name == WorkspaceName);
    }

    async partial void OnSelectedWorkspaceChanged(WorkspaceInfo? value)
    {
        Datasets.Clear();
        SelectedDataset = null;
        if (value is null)
            return;
        WorkspaceName = value.Name;
        _workspaceId = value.Id;
        if (!value.IsOnDedicatedCapacity && Kind == SourceKind.ServiceXmla)
            StatusText = Strings.Source_NoCapacity;

        await GuardAsync(Strings.Source_LoadingModels, async () =>
        {
            foreach (var dataset in await _session.Discovery.GetDatasetsAsync(value.Id, CancellationToken.None))
                Datasets.Add(dataset);
            SelectedDataset = Datasets.FirstOrDefault(d => d.Id == _datasetId || d.Name == DatasetName);
            StatusText = Localized.Format(Strings.Source_ModelsInWorkspace, Datasets.Count, value.Name);
        }, setConnected: false);
    }

    partial void OnSelectedDatasetChanged(DatasetInfo? value)
    {
        if (value is null)
            return;
        DatasetName = value.Name;
        _datasetId = value.Id;
        if (value.IsEffectiveIdentityRequired)
            StatusText = Strings.Source_UsesRls;
    }

    // ---- Connection --------------------------------------------------------------------------

    [ObservableProperty]
    private ConnectionState _state;

    [ObservableProperty]
    private string _statusText = Strings.Source_NotConnected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanQueryText))]
    private ConnectorCapabilities? _capabilities;

    /// <summary>"Yes"/"No" in the UI language for the capabilities panel.</summary>
    public string CanQueryText => Capabilities is null ? "" : Capabilities.CanQuery ? Strings.Common_Yes : Strings.Common_No;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MetadataSummary))]
    private ModelMetadata? _metadata;

    [ObservableProperty]
    private string? _securityContext;

    public IModelConnector? Connector { get; private set; }

    public string MetadataSummary => Metadata is null
        ? Strings.Source_NoMetadata
        : Localized.Format(Strings.Source_MetadataSummary, Metadata.Tables.Count, Metadata.AllColumns.Count(), Metadata.Measures.Count) +
          (Metadata.LastRefreshUtc is { } r ? Localized.Format(Strings.Source_LastRefresh, r) : "");

    /// <summary>Always-visible source identity (FR-001).</summary>
    public string Identity => BuildReference()?.Describe() ?? Strings.Source_NotConfigured;

    public ModelReference? BuildReference()
    {
        switch (Kind)
        {
            case SourceKind.DesktopSession when SelectedSession is { } session:
                return new ModelReference
                {
                    Kind = Kind,
                    DisplayName = session.DisplayName,
                    LocalPbixPath = session.PbixPath ?? PbixPath,
                    DesktopServer = session.Server,
                    DesktopDatabase = _externalDatabase,
                };
            case SourceKind.DesktopSession when !string.IsNullOrEmpty(PbixPath):
                return new ModelReference { Kind = Kind, DisplayName = Path.GetFileName(PbixPath), LocalPbixPath = PbixPath };
            case SourceKind.ServiceXmla or SourceKind.ServiceRest when !string.IsNullOrWhiteSpace(DatasetName):
                return new ModelReference
                {
                    Kind = Kind,
                    DisplayName = DatasetName!,
                    WorkspaceId = _workspaceId,
                    WorkspaceName = WorkspaceName,
                    DatasetId = _datasetId,
                    DatasetName = DatasetName,
                    TenantId = AuthOptions.IsHomeTenant(Tenant) ? null : Tenant.Trim(),
                    AccountHint = Auth.SignedInAccount ?? Auth.AccountHint,
                };
            default:
                return null;
        }
    }

    /// <summary>Restores a saved reference (FR-014). Desktop sessions must be re-attached.</summary>
    public void Apply(ModelReference? reference)
    {
        Disconnect();
        if (reference is null)
        {
            Tenant = "organizations";
            return;
        }

        Kind = reference.Kind;
        PbixPath = reference.LocalPbixPath;
        _workspaceId = reference.WorkspaceId;
        _datasetId = reference.DatasetId;
        WorkspaceName = reference.WorkspaceName;
        DatasetName = reference.DatasetName;
        Tenant = string.IsNullOrWhiteSpace(reference.TenantId) ? "organizations" : reference.TenantId;
        AccountHint = reference.AccountHint;
        if (Kind == SourceKind.DesktopSession)
            DetectSessions();
        else
            StatusText = Strings.Source_Restored;
    }

    partial void OnKindChanged(SourceKind value) => Disconnect();

    partial void OnSelectedSessionChanged(DesktopSession? value)
    {
        if (value is not null && Connector?.Reference.DesktopServer != value.Server)
            Disconnect();
    }

    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        var reference = BuildReference();
        if (reference is null || (Kind == SourceKind.DesktopSession && SelectedSession is null))
        {
            _dialogs.ShowInfo(Kind == SourceKind.DesktopSession
                ? Strings.Source_SelectSessionFirst
                : Strings.Source_SelectModelFirst);
            return;
        }

        await GuardAsync(Strings.Source_Connecting, async () =>
        {
            await DisposeConnectorAsync();
            var connector = _factory.Create(reference, _session);
            Connector = connector;
            await connector.ConnectAsync(CancellationToken.None);
            StatusText = Strings.Source_LoadingMetadata;
            var metadata = await connector.DiscoverMetadataAsync(CancellationToken.None);
            if (Kind != SourceKind.DesktopSession && _workspaceId is not null && _datasetId is not null && Auth.IsConfigured)
                metadata = metadata with { LastRefreshUtc = metadata.LastRefreshUtc ?? await _session.Discovery.GetLastRefreshAsync(_workspaceId, _datasetId, CancellationToken.None) };
            if (Auth.SignedInAccount is not null)
                SignedInAs = Auth.SignedInAccount + TenantSuffix();
            Metadata = metadata;
            Capabilities = connector.Capabilities;
            SecurityContext = connector.SecurityContext;
            StatusText = Localized.Format(Strings.Source_ConnectedVia, connector.Capabilities.TransportName);
            _logger.LogInformation("Model {Side} connected via {Transport}", Side, connector.Capabilities.TransportName);
            MetadataChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    [RelayCommand]
    private Task RefreshMetadataAsync() => TestConnectionAsync();

    public void Disconnect()
    {
        _ = DisposeConnectorAsync();
        if (Metadata is not null)
        {
            Metadata = null;
            MetadataChanged?.Invoke(this, EventArgs.Empty);
        }

        Capabilities = null;
        SecurityContext = null;
        State = ConnectionState.NotConnected;
        StatusText = Strings.Source_NotConnected;
        OnPropertyChanged(nameof(Identity));
    }

    private async Task DisposeConnectorAsync()
    {
        var connector = Connector;
        Connector = null;
        if (connector is not null)
            await connector.DisposeAsync();
    }

    private async Task GuardAsync(string busyText, Func<Task> action, bool setConnected = true)
    {
        var previous = State;
        State = ConnectionState.Working;
        StatusText = busyText;
        try
        {
            await action();
            State = setConnected ? ConnectionState.Connected : previous == ConnectionState.Working ? ConnectionState.NotConnected : previous;
        }
        catch (Exception ex)
        {
            State = ConnectionState.Failed;
            var error = ex as DaxDiffException ?? ErrorClassifier.Classify(ex, Title);
            error.WithSide(Side);
            StatusText = $"{error.Message} ({error.DiagnosticCode})";
            _logger.LogWarning("Model {Side} operation failed with {Code}", Side, error.DiagnosticCode);
            _dialogs.ShowError(error);
        }
    }
}
