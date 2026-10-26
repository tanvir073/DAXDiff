using System.Diagnostics;
using System.Management;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace DaxDiff.Connectors.Desktop;

/// <summary>A running Power BI Desktop model the user can attach to.</summary>
public sealed record DesktopSession(
    int EngineProcessId,
    int Port,
    string WorkspaceDataPath,
    int? DesktopProcessId,
    string? WindowTitle,
    string? PbixPath)
{
    public string Server => $"localhost:{Port}";

    /// <summary>Label used to disambiguate sessions in the UI.</summary>
    public string DisplayName
    {
        get
        {
            var title = WindowTitle?.Replace(" - Power BI Desktop", "", StringComparison.OrdinalIgnoreCase).Trim();
            if (!string.IsNullOrEmpty(PbixPath))
                return $"{Path.GetFileNameWithoutExtension(PbixPath)} (port {Port})";
            return string.IsNullOrEmpty(title) ? $"Power BI Desktop session (port {Port})" : $"{title} (port {Port})";
        }
    }
}

/// <summary>
/// Finds running Power BI Desktop models: each open PBIX hosts an msmdsrv.exe instance whose port is
/// written to <c>msmdsrv.port.txt</c> in its workspace data folder (§5.1).
/// </summary>
public sealed partial class DesktopSessionLocator
{
    private readonly ILogger<DesktopSessionLocator> _logger;

    public DesktopSessionLocator(ILogger<DesktopSessionLocator> logger) => _logger = logger;

    [GeneratedRegex("-s\\s+\"(?<path>[^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex DataDirArgument();

    [GeneratedRegex("\"(?<path>[^\"]+\\.pbi[xt])\"", RegexOptions.IgnoreCase)]
    private static partial Regex PbixArgument();

    public IReadOnlyList<DesktopSession> FindSessions()
    {
        try
        {
            return FindViaWmi();
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            _logger.LogWarning(ex, "WMI session discovery failed; scanning workspace folders instead");
            return FindViaWorkspaceFolders();
        }
    }

    private List<DesktopSession> FindViaWmi()
    {
        var processes = new Dictionary<int, (int Parent, string? CommandLine, string Name)>();
        using (var searcher = new ManagementObjectSearcher(
                   "SELECT ProcessId, ParentProcessId, CommandLine, Name FROM Win32_Process WHERE Name = 'msmdsrv.exe' OR Name = 'PBIDesktop.exe'"))
        {
            foreach (var item in searcher.Get().Cast<ManagementObject>())
            {
                using (item)
                {
                    processes[Convert.ToInt32(item["ProcessId"])] = (
                        Convert.ToInt32(item["ParentProcessId"]),
                        item["CommandLine"] as string,
                        item["Name"] as string ?? "");
                }
            }
        }

        var sessions = new List<DesktopSession>();
        foreach (var (pid, info) in processes.Where(p => p.Value.Name.Equals("msmdsrv.exe", StringComparison.OrdinalIgnoreCase)))
        {
            var dataDir = info.CommandLine is null ? null : DataDirArgument().Match(info.CommandLine) is { Success: true } m ? m.Groups["path"].Value : null;
            if (dataDir is null || ReadPort(dataDir) is not { } port)
                continue;

            int? desktopPid = processes.TryGetValue(info.Parent, out var parent) &&
                              parent.Name.Equals("PBIDesktop.exe", StringComparison.OrdinalIgnoreCase)
                ? info.Parent
                : null;
            string? pbix = null;
            if (desktopPid is { } dp && processes[dp].CommandLine is { } cmd && PbixArgument().Match(cmd) is { Success: true } pm)
                pbix = pm.Groups["path"].Value;

            sessions.Add(new DesktopSession(pid, port, dataDir, desktopPid, WindowTitle(desktopPid), pbix));
        }

        return sessions.OrderBy(s => s.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private List<DesktopSession> FindViaWorkspaceFolders()
    {
        var roots = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Power BI Desktop", "AnalysisServicesWorkspaces"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Microsoft", "Power BI Desktop Store App", "AnalysisServicesWorkspaces"),
        };

        var sessions = new List<DesktopSession>();
        foreach (var root in roots.Where(Directory.Exists))
        {
            foreach (var workspace in Directory.EnumerateDirectories(root))
            {
                var dataDir = Path.Combine(workspace, "Data");
                if (ReadPort(dataDir) is { } port)
                    sessions.Add(new DesktopSession(0, port, dataDir, null, null, null));
            }
        }

        return sessions;
    }

    /// <summary>The port file is UTF-16 encoded by msmdsrv.</summary>
    private static int? ReadPort(string dataDir)
    {
        var file = Path.Combine(dataDir, "msmdsrv.port.txt");
        if (!File.Exists(file))
            return null;
        try
        {
            var bytes = File.ReadAllBytes(file);
            var text = bytes.Length > 1 && bytes[1] == 0 ? Encoding.Unicode.GetString(bytes) : Encoding.UTF8.GetString(bytes);
            return int.TryParse(text.Trim('﻿', '\0', ' ', '\r', '\n'), out var port) ? port : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static string? WindowTitle(int? pid)
    {
        if (pid is null)
            return null;
        try
        {
            using var process = Process.GetProcessById(pid.Value);
            return string.IsNullOrWhiteSpace(process.MainWindowTitle) ? null : process.MainWindowTitle;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
