using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DaxDiff.Connectors;
using DaxDiff.Connectors.Auth;
using DaxDiff.Connectors.Desktop;
using DaxDiff.Connectors.Service;
using DaxDiff.Desktop.Services;
using DaxDiff.Domain;
using Microsoft.Extensions.Logging;

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
        WorkspacesView = CollectionViewSource.GetDefaultView(Workspaces);
        WorkspacesView.Filter = o => string.IsNullOrWhiteSpace(WorkspaceSearch) ||
                                     ((WorkspaceInfo)o).Name.Contains(WorkspaceSearch, StringComparison.OrdinalIgnoreCase);
        DatasetsView = CollectionViewSource.GetDefaultView(Datasets);
        DatasetsView.Filter = o => string.IsNullOrWhiteSpace(DatasetSearch) ||
                                   ((DatasetInfo)o).Name.Contains(DatasetSearch, StringComparison.OrdinalIgnoreCase);
    }

    public Side Side { get; }
    public string Title => $"Model {Side}";

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
            0 => "No running Power BI Desktop models found. Open the PBIX in Power BI Desktop, then detect again.",
            1 => "One running model found. Verify it is the right file, then Test.",
            _ => $"{Sessions.Count} running models found. Pick the right one by file name and port.",
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
        var path = _dialogs.OpenFile("Power BI files (*.pbix;*.pbip)|*.pbix;*.pbip|All files (*.*)|*.*", "Choose the PBIX to compare");
        if (path is null)
            return;
        PbixPath = path;
        StatusText = "DAXDiff queries the model while it is open in Power BI Desktop. Choose 'Open in Power BI Desktop', wait for it to load, then 'Detect sessions'.";
    }

    [RelayCommand]
    private void OpenPbixInDesktop()
    {
        if (string.IsNullOrEmpty(PbixPath) || !File.Exists(PbixPath))
        {
            _dialogs.ShowInfo("Choose a PBIX file first.");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(PbixPath!) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _dialogs.ShowError(new DaxDiffException(ErrorCategory.Configuration, "DESK-OPEN",
                "Could not open the file with Power BI Desktop.", "Make sure Power BI Desktop is installed and associated with .pbix files.", Side, inner: ex));
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
            match = new DesktopSession(0, port, "", null, "Launched from Power BI Desktop", null);
            Sessions.Insert(0, match);
        }

        SelectedSession = match;
        _externalDatabase = database;
        StatusText = $"Attached from Power BI Desktop External Tools ({server}). Test the connection to load metadata.";
    }

    // ---- Service -----------------------------------------------------------------------------

    public ObservableCollection<WorkspaceInfo> Workspaces { get; } = [];
    public ICollectionView WorkspacesView { get; }
    public ObservableCollection<DatasetInfo> Datasets { get; } = [];
    public ICollectionView DatasetsView { get; }

    private IAuthService Auth => _session.Auth;

    public bool CanBrowseService => Auth.IsConfigured;

    public string ServiceHint => Auth.IsConfigured
        ? "Each model signs in separately, so A and B can be in the same tenant or in different tenants, as the same or different users."
        : "No Entra client ID is configured (Settings), so DAXDiff cannot sign in to browse workspaces or use REST. " +
          "XMLA works without it: enter your account, the workspace (or its connection link) and the model, then Test connection. " +
          "Microsoft's sign-in uses that account, and asks for it when it is not signed in yet.";

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

    public string SignInStatusText => SignedInAs is null ? "Not signed in" : $"Signed in as {SignedInAs}";

    private string SignedInText(AuthToken token) =>
        $"{token.Account}{(AuthOptions.IsHomeTenant(Tenant) ? "" : $" (tenant {Tenant})")}";

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
            _dialogs.ShowInfo(ServiceHint + Environment.NewLine + Environment.NewLine + "See docs/setup.md for the app registration steps.", "Sign in to Power BI");
            return;
        }

        await GuardAsync("Signing in…", async () =>
        {
            Disconnect();
            var token = await Auth.SignInAsync(CancellationToken.None);
            SignedInAs = SignedInText(token);
            await LoadWorkspacesAsync();
            StatusText = $"Signed in as {SignedInAs}. {Workspaces.Count} workspace(s) available.";
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
        StatusText = "Signed out.";
    }

    [RelayCommand]
    private async Task RefreshWorkspacesAsync()
    {
        if (!Auth.IsConfigured)
            return;
        await GuardAsync("Loading workspaces…", async () =>
        {
            await LoadWorkspacesAsync();
            StatusText = $"{Workspaces.Count} workspace(s) available.";
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
            StatusText = "This workspace is not on Premium/PPU/Fabric capacity, so XMLA is likely unavailable. Consider the REST transport.";

        await GuardAsync("Loading semantic models…", async () =>
        {
            foreach (var dataset in await _session.Discovery.GetDatasetsAsync(value.Id, CancellationToken.None))
                Datasets.Add(dataset);
            SelectedDataset = Datasets.FirstOrDefault(d => d.Id == _datasetId || d.Name == DatasetName);
            StatusText = $"{Datasets.Count} semantic model(s) in {value.Name}.";
        }, setConnected: false);
    }

    partial void OnSelectedDatasetChanged(DatasetInfo? value)
    {
        if (value is null)
            return;
        DatasetName = value.Name;
        _datasetId = value.Id;
        if (value.IsEffectiveIdentityRequired)
            StatusText = "This model uses row-level security: results will reflect your role membership.";
    }

    // ---- Connection --------------------------------------------------------------------------

    [ObservableProperty]
    private ConnectionState _state;

    [ObservableProperty]
    private string _statusText = "Not connected.";

    [ObservableProperty]
    private ConnectorCapabilities? _capabilities;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MetadataSummary))]
    private ModelMetadata? _metadata;

    [ObservableProperty]
    private string? _securityContext;

    public IModelConnector? Connector { get; private set; }

    public string MetadataSummary => Metadata is null
        ? "No metadata loaded."
        : $"{Metadata.Tables.Count} tables, {Metadata.AllColumns.Count()} columns, {Metadata.Measures.Count} measures" +
          (Metadata.LastRefreshUtc is { } r ? $"; last refresh {r:yyyy-MM-dd HH:mm} UTC" : "");

    /// <summary>Always-visible source identity (FR-001).</summary>
    public string Identity => BuildReference()?.Describe() ?? "Not configured";

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
            StatusText = "Saved source restored. Sign in and Test to reload metadata.";
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
                ? "Select a running Power BI Desktop session first (Detect sessions)."
                : "Enter or select a workspace and semantic model first.");
            return;
        }

        await GuardAsync("Connecting…", async () =>
        {
            await DisposeConnectorAsync();
            var connector = _factory.Create(reference, _session);
            Connector = connector;
            await connector.ConnectAsync(CancellationToken.None);
            StatusText = "Connected. Loading metadata…";
            var metadata = await connector.DiscoverMetadataAsync(CancellationToken.None);
            if (Kind != SourceKind.DesktopSession && _workspaceId is not null && _datasetId is not null && Auth.IsConfigured)
                metadata = metadata with { LastRefreshUtc = metadata.LastRefreshUtc ?? await _session.Discovery.GetLastRefreshAsync(_workspaceId, _datasetId, CancellationToken.None) };
            if (Auth.SignedInAccount is not null)
                SignedInAs = Auth.SignedInAccount + (AuthOptions.IsHomeTenant(Tenant) ? "" : $" (tenant {Tenant})");
            Metadata = metadata;
            Capabilities = connector.Capabilities;
            SecurityContext = connector.SecurityContext;
            StatusText = $"Connected via {connector.Capabilities.TransportName}.";
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
        StatusText = "Not connected.";
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
