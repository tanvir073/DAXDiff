namespace DaxDiff.Presentation.Services;

/// <summary>
/// Command-line arguments. Power BI Desktop External Tools launches the app with
/// <c>"%server%" "%database%"</c>; the explicit <c>--server</c>/<c>--database</c> form is also accepted (FR-004).
/// </summary>
public sealed record LaunchArguments(string? Server, string? Database, string? ProjectPath)
{
    public static LaunchArguments Parse(IReadOnlyList<string> args)
    {
        string? server = null, database = null, project = null;
        var positional = new List<string>();
        for (var i = 0; i < args.Count; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--server" when i + 1 < args.Count:
                    server = args[++i];
                    break;
                case "--database" when i + 1 < args.Count:
                    database = args[++i];
                    break;
                default:
                    positional.Add(args[i]);
                    break;
            }
        }

        foreach (var arg in positional)
        {
            if (arg.EndsWith(Domain.ProjectFile.Extension, StringComparison.OrdinalIgnoreCase))
                project = arg;
            else if (server is null && arg.StartsWith("localhost:", StringComparison.OrdinalIgnoreCase))
                server = arg;
            else if (server is not null && database is null)
                database = arg;
        }

        return new LaunchArguments(server, database, project);
    }
}
