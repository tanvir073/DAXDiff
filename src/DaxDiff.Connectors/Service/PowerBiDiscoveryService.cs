using System.Text.Json;

namespace DaxDiff.Connectors.Service;

public sealed record WorkspaceInfo(string Id, string Name, bool IsOnDedicatedCapacity, string? Type);

public sealed record DatasetInfo(
    string Id,
    string Name,
    string WorkspaceId,
    string? ConfiguredBy,
    bool IsEffectiveIdentityRequired,
    string? TargetStorageMode);

/// <summary>Browses workspaces and semantic models the signed-in user can access (FR-002).</summary>
public sealed class PowerBiDiscoveryService
{
    private readonly PowerBiRestClient _client;

    public PowerBiDiscoveryService(PowerBiRestClient client) => _client = client;

    public async Task<IReadOnlyList<WorkspaceInfo>> GetWorkspacesAsync(CancellationToken cancellationToken)
    {
        using var doc = await _client.GetAsync("groups?$top=5000", cancellationToken);
        return doc.RootElement.GetProperty("value").EnumerateArray()
            .Select(w => new WorkspaceInfo(
                w.GetProperty("id").GetString()!,
                w.GetProperty("name").GetString()!,
                w.TryGetProperty("isOnDedicatedCapacity", out var d) && d.ValueKind == JsonValueKind.True,
                w.TryGetProperty("type", out var t) ? t.GetString() : null))
            .OrderBy(w => w.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<IReadOnlyList<DatasetInfo>> GetDatasetsAsync(string workspaceId, CancellationToken cancellationToken)
    {
        using var doc = await _client.GetAsync($"groups/{Uri.EscapeDataString(workspaceId)}/datasets", cancellationToken);
        return doc.RootElement.GetProperty("value").EnumerateArray()
            .Select(d => new DatasetInfo(
                d.GetProperty("id").GetString()!,
                d.GetProperty("name").GetString()!,
                workspaceId,
                d.TryGetProperty("configuredBy", out var c) ? c.GetString() : null,
                d.TryGetProperty("isEffectiveIdentityRequired", out var e) && e.ValueKind == JsonValueKind.True,
                d.TryGetProperty("targetStorageMode", out var s) ? s.GetString() : null))
            .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>End time of the latest completed refresh, used to warn about snapshot inconsistency (§14).</summary>
    public async Task<DateTimeOffset?> GetLastRefreshAsync(string workspaceId, string datasetId, CancellationToken cancellationToken)
    {
        try
        {
            using var doc = await _client.GetAsync(
                $"groups/{Uri.EscapeDataString(workspaceId)}/datasets/{Uri.EscapeDataString(datasetId)}/refreshes?$top=5", cancellationToken);
            foreach (var refresh in doc.RootElement.GetProperty("value").EnumerateArray())
            {
                if (refresh.TryGetProperty("status", out var status) && status.GetString() == "Completed" &&
                    refresh.TryGetProperty("endTime", out var end) && end.TryGetDateTimeOffset(out var endTime))
                    return endTime;
            }
        }
        catch (Domain.DaxDiffException)
        {
            // Refresh history needs more than Build permission on some models; it is informational only.
        }

        return null;
    }
}
