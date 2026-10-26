namespace DaxDiff.Domain;

/// <summary>
/// Saved comparison project (<c>.pbicomp</c>, FR-014).
/// Holds source references, mappings and settings only: never passwords, tokens or result data.
/// </summary>
public sealed record ProjectFile
{
    public const int CurrentSchemaVersion = 1;
    public const string Extension = ".pbicomp";

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public string Name { get; init; } = "Untitled comparison";
    public ModelReference? SourceA { get; init; }
    public ModelReference? SourceB { get; init; }
    public ComparisonConfig Config { get; init; } = new();
    public ProjectUiPreferences Ui { get; init; } = new();
    public DateTimeOffset SavedUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record ProjectUiPreferences
{
    public string? ResultsFilter { get; init; }
    public bool ExportAllData { get; init; }
}
