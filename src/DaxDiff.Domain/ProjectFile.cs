namespace DaxDiff.Domain;

/// <summary>
/// A DAXDiff project (<c>.pbicomp</c>, FR-014): the two sources and every comparison defined against them.
/// Holds references and settings only: never passwords, tokens or result data.
/// </summary>
public sealed record ProjectFile
{
    public const int CurrentSchemaVersion = 2;
    public const string Extension = ".pbicomp";

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public string Name { get; init; } = "Untitled project";
    public ModelReference? SourceA { get; init; }
    public ModelReference? SourceB { get; init; }
    public IReadOnlyList<ComparisonDefinition> Comparisons { get; init; } = [];
    public ProjectUiPreferences Ui { get; init; } = new();
    public DateTimeOffset SavedUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record ProjectUiPreferences
{
    public string? ResultsFilter { get; init; }
    public bool ExportAllData { get; init; }
}
