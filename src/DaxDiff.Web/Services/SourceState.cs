using DaxDiff.Connectors;
using DaxDiff.Connectors.Auth;
using DaxDiff.Connectors.Service;
using DaxDiff.Domain;

namespace DaxDiff.Web.Services;

/// <summary>
/// One source (model A or B) in the browser: its own sign-in, the workspace and model picked, and the open connection.
/// Only Power BI Service models over the Execute Queries REST API can be reached from a browser.
/// </summary>
public sealed class SourceState
{
    private readonly SourceSession _session;
    private readonly ILoggerFactory _loggerFactory;

    public SourceState(Side side, SourceSession session, ILoggerFactory loggerFactory)
    {
        Side = side;
        _session = session;
        _loggerFactory = loggerFactory;
    }

    public event Action? Changed;

    public Side Side { get; }
    public IAuthService Auth => _session.Auth;

    /// <summary>The model this source points at, as saved in the project.</summary>
    public ModelReference? Reference { get; private set; }

    public string Tenant { get; set; } = "";
    public IReadOnlyList<WorkspaceInfo> Workspaces { get; private set; } = [];
    public IReadOnlyList<DatasetInfo> Datasets { get; private set; } = [];
    public string? WorkspaceId { get; private set; }

    public IModelConnector? Connector { get; private set; }
    public ModelMetadata? Metadata { get; private set; }
    public bool IsConnected => Connector is not null && Metadata is not null;

    public string? Busy { get; private set; }
    public DaxDiffException? Error { get; private set; }

    /// <summary>Why this source can't be reached from a browser, if it can't.</summary>
    public string? Unsupported => Reference?.Kind == SourceKind.DesktopSession
        ? "This project points at a model open in Power BI Desktop, which a browser cannot reach. Pick a published model instead."
        : null;

    public async Task LoadAsync(ModelReference? reference)
    {
        await DisconnectAsync();
        Reference = reference;
        Tenant = reference?.TenantId ?? "";
        Auth.Tenant = Tenant;
        Auth.AccountHint = reference?.AccountHint;
        Workspaces = [];
        Datasets = [];
        WorkspaceId = reference?.WorkspaceId;
        Error = null;
        Notify();
    }

    public Task SignInAsync() => GuardAsync("Signing in", async () =>
    {
        Auth.Tenant = Tenant;
        await Auth.SignInAsync(CancellationToken.None);
        await LoadWorkspacesCoreAsync();
    });

    public async Task SignOutAsync()
    {
        await DisconnectAsync();
        await Auth.SignOutAsync();
        Workspaces = [];
        Datasets = [];
        Notify();
    }

    public Task LoadWorkspacesAsync() => GuardAsync("Loading workspaces", LoadWorkspacesCoreAsync);

    private async Task LoadWorkspacesCoreAsync()
    {
        Workspaces = await _session.Discovery.GetWorkspacesAsync(CancellationToken.None);
        var match = Workspaces.FirstOrDefault(w => w.Id == WorkspaceId)
                    ?? Workspaces.FirstOrDefault(w => string.Equals(w.Name, Reference?.WorkspaceName, StringComparison.OrdinalIgnoreCase));
        if (match is not null)
        {
            WorkspaceId = match.Id;
            Datasets = await _session.Discovery.GetDatasetsAsync(match.Id, CancellationToken.None);
        }
    }

    public Task SelectWorkspaceAsync(string? workspaceId) => GuardAsync("Loading semantic models", async () =>
    {
        WorkspaceId = workspaceId;
        Datasets = string.IsNullOrEmpty(workspaceId) ? [] : await _session.Discovery.GetDatasetsAsync(workspaceId, CancellationToken.None);
    });

    public async Task SelectDatasetAsync(string? datasetId)
    {
        await DisconnectAsync();
        var workspace = Workspaces.FirstOrDefault(w => w.Id == WorkspaceId);
        var dataset = Datasets.FirstOrDefault(d => d.Id == datasetId);
        if (workspace is not null && dataset is not null)
        {
            Reference = new ModelReference
            {
                Kind = SourceKind.ServiceRest,
                DisplayName = dataset.Name,
                WorkspaceId = workspace.Id,
                WorkspaceName = workspace.Name,
                DatasetId = dataset.Id,
                DatasetName = dataset.Name,
                TenantId = AuthOptions.IsHomeTenant(Tenant) ? null : Tenant.Trim(),
                AccountHint = Auth.SignedInAccount ?? Auth.AccountHint,
            };
        }

        Notify();
    }

    /// <summary>The dataset in the list that the current reference points at (by ID, else by name).</summary>
    public string? SelectedDatasetId =>
        (Datasets.FirstOrDefault(d => d.Id == Reference?.DatasetId)
         ?? Datasets.FirstOrDefault(d => string.Equals(d.Name, Reference?.DatasetName, StringComparison.OrdinalIgnoreCase)))?.Id;

    /// <summary>Opens the model over REST and reads its metadata. XMLA references from the desktop app are reached over REST too.</summary>
    public Task ConnectAsync() => GuardAsync("Connecting", async () =>
    {
        await DisconnectAsync();
        if (Reference is null || Unsupported is not null)
            throw new DaxDiffException(ErrorCategory.Configuration, "WEB-NO-MODEL", Unsupported ?? "No semantic model is selected.",
                "Sign in and pick a workspace and semantic model.");

        var reference = await ResolveAsync(Reference);
        var connector = new ServiceRestConnector(reference, _session.Rest, Auth, _loggerFactory.CreateLogger<ServiceRestConnector>());
        await connector.ConnectAsync(CancellationToken.None);
        var metadata = await connector.DiscoverMetadataAsync(CancellationToken.None);
        metadata = metadata with
        {
            LastRefreshUtc = metadata.LastRefreshUtc
                             ?? await _session.Discovery.GetLastRefreshAsync(reference.WorkspaceId!, reference.DatasetId!, CancellationToken.None),
        };
        Reference = reference;
        Connector = connector;
        Metadata = metadata;
    });

    /// <summary>Fills in workspace and model IDs from their names (desktop XMLA projects store names only).</summary>
    private async Task<ModelReference> ResolveAsync(ModelReference reference)
    {
        var resolved = reference with { Kind = SourceKind.ServiceRest, AccountHint = Auth.SignedInAccount ?? reference.AccountHint };
        if (!string.IsNullOrEmpty(resolved.WorkspaceId) && !string.IsNullOrEmpty(resolved.DatasetId))
            return resolved;

        if (Workspaces.Count == 0)
            await LoadWorkspacesCoreAsync();
        var workspace = Workspaces.FirstOrDefault(w => w.Id == resolved.WorkspaceId)
                        ?? Workspaces.FirstOrDefault(w => string.Equals(w.Name, resolved.WorkspaceName, StringComparison.OrdinalIgnoreCase))
                        ?? throw new DaxDiffException(ErrorCategory.Configuration, "WEB-WORKSPACE",
                            $"The workspace '{resolved.WorkspaceName}' was not found for {Auth.SignedInAccount ?? "the signed-in account"}.",
                            "Check the tenant and account, or pick the workspace from the list.");
        var datasets = await _session.Discovery.GetDatasetsAsync(workspace.Id, CancellationToken.None);
        var dataset = datasets.FirstOrDefault(d => d.Id == resolved.DatasetId)
                      ?? datasets.FirstOrDefault(d => string.Equals(d.Name, resolved.DatasetName, StringComparison.OrdinalIgnoreCase))
                      ?? throw new DaxDiffException(ErrorCategory.Configuration, "WEB-DATASET",
                          $"The semantic model '{resolved.DatasetName}' was not found in '{workspace.Name}'.",
                          "Pick the semantic model from the list.");
        WorkspaceId = workspace.Id;
        Datasets = datasets;
        return resolved with { WorkspaceId = workspace.Id, WorkspaceName = workspace.Name, DatasetId = dataset.Id, DatasetName = dataset.Name };
    }

    /// <summary>Uses an in-memory model instead of the Power BI service (the sample project).</summary>
    public async Task UseSampleAsync(SampleConnector connector)
    {
        await DisconnectAsync();
        Reference = connector.Reference;
        Connector = connector;
        Metadata = connector.Metadata;
        Error = null;
        Notify();
    }

    public async Task DisconnectAsync()
    {
        if (Connector is not null)
            await Connector.DisposeAsync();
        Connector = null;
        Metadata = null;
    }

    private async Task GuardAsync(string busy, Func<Task> action)
    {
        Busy = busy;
        Error = null;
        Notify();
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            Error = ErrorClassifier.Classify(ex, "Power BI").WithSide(Side);
        }
        finally
        {
            Busy = null;
            Notify();
        }
    }

    private void Notify() => Changed?.Invoke();
}
