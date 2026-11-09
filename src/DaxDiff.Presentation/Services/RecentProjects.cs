using System.Text.Json;

namespace DaxDiff.Presentation.Services;

/// <summary>Most-recently-used project paths (paths only, no project content).</summary>
public sealed class RecentProjects
{
    private const int Max = 10;

    public IReadOnlyList<string> Load()
    {
        try
        {
            return File.Exists(AppPaths.RecentProjects)
                ? JsonSerializer.Deserialize<List<string>>(File.ReadAllText(AppPaths.RecentProjects))?.Where(File.Exists).ToList() ?? []
                : [];
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    public IReadOnlyList<string> Add(string path)
    {
        var list = Load().Where(p => !string.Equals(p, path, StringComparison.OrdinalIgnoreCase)).Prepend(path).Take(Max).ToList();
        try
        {
            Directory.CreateDirectory(AppPaths.Root);
            File.WriteAllText(AppPaths.RecentProjects, JsonSerializer.Serialize(list));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The recent list is a convenience only.
        }

        return list;
    }
}
