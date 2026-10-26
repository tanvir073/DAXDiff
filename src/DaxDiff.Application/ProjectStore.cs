using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using DaxDiff.Domain;

namespace DaxDiff.Application;

/// <summary>
/// Saves and loads <c>.pbicomp</c> projects (FR-014, NFR-08): settings only, schema-versioned, with migration on load.
/// Session-specific Desktop ports and database IDs are not persisted, so a reload can never attach to the wrong model.
/// </summary>
public static class ProjectStore
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Serialize(ProjectFile project)
    {
        var sanitized = project with
        {
            SchemaVersion = ProjectFile.CurrentSchemaVersion,
            SourceA = Sanitize(project.SourceA),
            SourceB = Sanitize(project.SourceB),
            SavedUtc = DateTimeOffset.UtcNow,
        };
        return JsonSerializer.Serialize(sanitized, JsonOptions);
    }

    public static ProjectFile Deserialize(string json)
    {
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(json);
        }
        catch (JsonException ex)
        {
            throw Invalid("The project file is not valid JSON.", ex);
        }

        if (node is not JsonObject root)
            throw Invalid("The project file is empty or malformed.");

        var version = root["schemaVersion"]?.GetValue<int>() ?? 0;
        if (version > ProjectFile.CurrentSchemaVersion)
            throw new DaxDiffException(ErrorCategory.Configuration, "PRJ-NEWER",
                $"This project was saved by a newer DAXDiff (schema {version}).", "Update DAXDiff to open it.");

        Migrate(root, version);

        try
        {
            return root.Deserialize<ProjectFile>(JsonOptions) ?? throw Invalid("The project file is empty.");
        }
        catch (JsonException ex)
        {
            throw Invalid("The project file could not be read: " + ex.Message, ex);
        }
    }

    public static async Task SaveAsync(ProjectFile project, string path, CancellationToken cancellationToken = default)
    {
        var temp = path + ".tmp";
        await File.WriteAllTextAsync(temp, Serialize(project), cancellationToken);
        File.Move(temp, path, overwrite: true);
    }

    public static async Task<ProjectFile> LoadAsync(string path, CancellationToken cancellationToken = default) =>
        Deserialize(await File.ReadAllTextAsync(path, cancellationToken));

    /// <summary>Upgrades older schemas in place. Version 0 is a pre-release format without a version field.</summary>
    private static void Migrate(JsonObject root, int version)
    {
        if (version < 1)
        {
            // v0 stored "limits.maxRowCount"; v1 renamed it to "maxRows".
            if (root["config"]?["limits"] is JsonObject limits && limits["maxRowCount"] is { } legacy)
            {
                limits.Remove("maxRowCount");
                limits["maxRows"] = legacy.DeepClone();
            }

            root["schemaVersion"] = 1;
        }
    }

    private static ModelReference? Sanitize(ModelReference? reference) =>
        reference is null ? null : reference with { DesktopServer = null, DesktopDatabase = null };

    private static DaxDiffException Invalid(string message, Exception? inner = null) =>
        new(ErrorCategory.Configuration, "PRJ-INVALID", message, "Choose a valid .pbicomp file.", inner: inner);
}
