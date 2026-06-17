using System.Diagnostics;
using System.Management;

namespace DLLRunTool.Services;

/// <summary>
/// Single WMI snapshot shared across all services — avoids N queries per timer tick.
/// </summary>
public static class ProcessStatusCache
{
    private static readonly object Gate = new();
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(8);

    private static DateTime _refreshedAt = DateTime.MinValue;
    private static List<(int Pid, string CommandLine)> _dotnetProcesses = [];
    private static List<(int Pid, string CommandLine)> _nodeProcesses = [];
    private static Dictionary<string, int> _exeProcesses = new(StringComparer.OrdinalIgnoreCase);

    public static void RefreshIfStale(bool force = false)
    {
        lock (Gate)
        {
            if (!force && DateTime.UtcNow - _refreshedAt < CacheTtl)
                return;

            RefreshCore();
        }
    }

    public static void Invalidate()
    {
        lock (Gate) { _refreshedAt = DateTime.MinValue; }
    }

    public static Process? FindForService(Models.ServiceConfig service, bool forceRefresh = false)
    {
        RefreshIfStale(force: forceRefresh);

        if (service.IsExe)
        {
            var cached = TryFindExeInCache(service.DllName);
            return cached ?? FindExeProcessByName(service.DllName);
        }

        lock (Gate)
        {
            if (service.IsFrontEnd)
            {
                foreach (var (pid, cmd) in _nodeProcesses)
                {
                    if (cmd.Contains(service.FolderPath, StringComparison.OrdinalIgnoreCase))
                        return TryGetProcess(pid);
                }
                return null;
            }

            if (string.IsNullOrEmpty(service.DllName))
                return null;

            foreach (var (pid, cmd) in _dotnetProcesses)
            {
                if (cmd.Contains(service.DllName, StringComparison.OrdinalIgnoreCase))
                    return TryGetProcess(pid);
            }
        }

        return null;
    }

    public static Process? FindExeProcess(string dllOrExeName, bool useCache = true)
    {
        if (useCache)
        {
            var cached = TryFindExeInCache(dllOrExeName);
            if (cached != null)
                return cached;
        }

        return FindExeProcessByName(dllOrExeName);
    }

    private static Process? TryFindExeInCache(string dllOrExeName)
    {
        var name = Path.GetFileNameWithoutExtension(dllOrExeName);
        if (string.IsNullOrWhiteSpace(name))
            return null;

        lock (Gate)
        {
            if (!_exeProcesses.TryGetValue(name, out var pid))
                return null;

            return TryGetProcess(pid);
        }
    }

    private static Process? FindExeProcessByName(string dllOrExeName)
    {
        var name = Path.GetFileNameWithoutExtension(dllOrExeName);
        if (string.IsNullOrWhiteSpace(name))
            return null;

        foreach (var proc in Process.GetProcessesByName(name))
        {
            try
            {
                if (!proc.HasExited)
                    return proc;
            }
            catch
            {
                // process exited between enumerate and check
            }
        }

        return null;
    }

    private static void RefreshCore()
    {
        _dotnetProcesses = [];
        _nodeProcesses = [];
        _exeProcesses = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT ProcessId, Name, CommandLine FROM Win32_Process " +
                "WHERE Name = 'dotnet.exe' OR Name = 'node.exe' OR Name = 'redis-server.exe'");

            foreach (var obj in searcher.Get())
            {
                var pid = Convert.ToInt32(obj["ProcessId"]);
                var name = obj["Name"]?.ToString() ?? "";
                var cmd = obj["CommandLine"]?.ToString() ?? "";

                if (name.Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase))
                    _dotnetProcesses.Add((pid, cmd));
                else if (name.Equals("node.exe", StringComparison.OrdinalIgnoreCase))
                    _nodeProcesses.Add((pid, cmd));
                else
                {
                    var exeName = Path.GetFileNameWithoutExtension(name);
                    _exeProcesses[exeName] = pid;
                }
            }
        }
        catch
        {
            // WMI unavailable — keep empty cache
        }

        _refreshedAt = DateTime.UtcNow;
    }

    private static Process? TryGetProcess(int pid)
    {
        try
        {
            var p = Process.GetProcessById(pid);
            return p.HasExited ? null : p;
        }
        catch
        {
            return null;
        }
    }
}
