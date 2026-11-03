using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using DaxDiff.Domain;
using System.Globalization;
using DaxDiff.Application.Resources;

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

    /// <summary>Serialized content without the save time: equal fingerprints mean nothing changed.</summary>
    public static string Fingerprint(ProjectFile project) =>
        JsonSerializer.Serialize(project with
        {
            SchemaVersion = ProjectFile.CurrentSchemaVersion,
            SourceA = Sanitize(project.SourceA),
            SourceB = Sanitize(project.SourceB),
            SavedUtc = default,
        }, JsonOptions);

    public static ProjectFile Deserialize(string json)
    {
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(json);
        }
        catch (JsonException ex)
        {
            throw Invalid(Strings.Prj_NotJson, ex);
        }

        if (node is not JsonObject root)
            throw Invalid(Strings.Prj_Malformed);

        var version = root["schemaVersion"]?.GetValue<int>() ?? 0;
        if (version > ProjectFile.CurrentSchemaVersion)
            throw new DaxDiffException(ErrorCategory.Configuration, "PRJ-NEWER",
                string.Format(CultureInfo.CurrentCulture, Strings.Prj_Newer, version), Strings.Prj_NewerRemedy);

        Migrate(root, version);

        try
        {
            return root.Deserialize<ProjectFile>(JsonOptions) ?? throw Invalid(Strings.Prj_Empty);
        }
        catch (JsonException ex)
        {
            throw Invalid(string.Format(CultureInfo.CurrentCulture, Strings.Prj_Unreadable, ex.Message), ex);
        }
    }

    /// <summary>Synchronous save for shutdown paths that cannot await (e.g. a window closing).</summary>
    public static void Save(ProjectFile project, string path)
    {
        var temp = path + ".tmp";
        File.WriteAllText(temp, Serialize(project));
        File.Move(temp, path, overwrite: true);
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

        if (version < 2)
        {
            MigrateToComparisons(root);
            root["schemaVersion"] = 2;
        }
    }

    /// <summary>
    /// v1 held one "config" with explicit field pairs. v2 holds a list of comparisons whose fields pair by synonym:
    /// each v1 pair becomes a field on each side sharing the pair's label as synonym; dimensions become keys.
    /// </summary>
    private static void MigrateToComparisons(JsonObject root)
    {
        if (root["config"] is not JsonObject config)
            return;
        root.Remove("config");

        var fieldsA = new JsonArray();
        var fieldsB = new JsonArray();
        if (config["fieldPairs"] is JsonArray pairs)
        {
            foreach (var pair in pairs.OfType<JsonObject>())
            {
                var isKey = string.Equals(pair["role"]?.GetValue<string>(), "Dimension", StringComparison.OrdinalIgnoreCase);
                var synonym = pair["alias"]?.GetValue<string>() ?? pair["left"]?["name"]?.GetValue<string>() ?? "";
                fieldsA.Add(Field(pair["left"], isKey, synonym));
                fieldsB.Add(Field(pair["right"], isKey, synonym));
            }
        }

        config.Remove("fieldPairs");
        root["comparisons"] = new JsonArray(new JsonObject
        {
            ["id"] = Guid.NewGuid().ToString(),
            ["name"] = "Comparison 1",
            ["fieldsA"] = fieldsA,
            ["fieldsB"] = fieldsB,
            ["options"] = config,
        });

        // v1 only paired columns as dimensions and measures as values, so no aggregation is needed.
        static JsonObject Field(JsonNode? field, bool isKey, string synonym) => new()
        {
            ["field"] = field?.DeepClone(),
            ["isKey"] = isKey,
            ["aggregation"] = nameof(Aggregation.None),
            ["synonym"] = synonym,
        };
    }

    private static ModelReference? Sanitize(ModelReference? reference) =>
        reference is null ? null : reference with { DesktopServer = null, DesktopDatabase = null };

    private static DaxDiffException Invalid(string message, Exception? inner = null) =>
        new(ErrorCategory.Configuration, "PRJ-INVALID", message, Strings.Prj_InvalidRemedy, inner: inner);
}
