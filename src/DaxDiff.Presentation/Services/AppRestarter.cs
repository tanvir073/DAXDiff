using System.ComponentModel;
using System.Diagnostics;

namespace DaxDiff.Presentation.Services;

/// <summary>Starts a new instance of DAXDiff, for example to show a newly chosen language.</summary>
public static class AppRestarter
{
    /// <summary>
    /// Launches the app again with <paramref name="args"/>. Works both for DAXDiff.exe and for the
    /// <c>dotnet DAXDiff.dll</c> form used where application control blocks the unsigned exe.
    /// Returns the error message when it could not start.
    /// </summary>
    public static string? TryStart(IEnumerable<string> args)
    {
        var host = Environment.ProcessPath;
        if (host is null)
            return Resources.Strings.Language_RestartNoPath;

        var start = new ProcessStartInfo(host) { UseShellExecute = false, WorkingDirectory = AppContext.BaseDirectory };
        if (string.Equals(Path.GetFileNameWithoutExtension(host), "dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(System.Reflection.Assembly.GetEntryAssembly()!.Location);
        foreach (var arg in args)
            start.ArgumentList.Add(arg);

        try
        {
            Process.Start(start);
            return null;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return ex.Message;
        }
    }
}
