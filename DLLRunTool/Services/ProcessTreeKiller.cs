using System.Diagnostics;
using System.Management;

namespace DLLRunTool.Services;

public static class ProcessTreeKiller
{
    public static Process? FindRunningProcess(Models.ServiceConfig service, bool forceRefresh = false)
    {
        if (service.ManagedProcess != null && !service.ManagedProcess.HasExited)
            return service.ManagedProcess;

        return ProcessStatusCache.FindForService(service, forceRefresh);
    }

    public static void KillProcessTree(Process? process, int gracefulTimeoutMs = 0)
    {
        if (process == null)
            return;

        try
        {
            if (!process.HasExited)
            {
                if (gracefulTimeoutMs > 0)
                {
                    try { process.CloseMainWindow(); } catch { /* console apps */ }
                    if (process.WaitForExit(gracefulTimeoutMs))
                        return;
                }

                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            TryTaskKill(process.Id);
        }

        KillChildren(process.Id);
    }

    public static void KillByService(Models.ServiceConfig service, int gracefulTimeoutMs = 0)
    {
        var tracked = service.ManagedProcess;
        if (tracked != null && !tracked.HasExited)
            KillProcessTree(tracked, gracefulTimeoutMs);

        if (service.IsExe)
        {
            var found = FindRunningProcess(service);
            if (found != null)
                KillProcessTree(found, gracefulTimeoutMs);
        }
        else if (service.IsFrontEnd)
        {
            foreach (var name in new[] { "node.exe", "npm.cmd", "npm.exe" })
            {
                foreach (var proc in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(name)))
                {
                    var cmd = GetCommandLine(proc.Id);
                    if (cmd != null && (
                        cmd.Contains(service.FolderPath, StringComparison.OrdinalIgnoreCase) ||
                        (!string.IsNullOrEmpty(service.DllName) && cmd.Contains(service.DllName, StringComparison.OrdinalIgnoreCase))))
                    {
                        KillProcessTree(proc, gracefulTimeoutMs);
                    }
                }
            }
        }
        else
        {
            var dotnet = FindDotnetProcess(service.DllName);
            if (dotnet != null)
                KillProcessTree(dotnet, gracefulTimeoutMs);
        }

        service.ManagedProcess = null;
        ProcessStatusCache.Invalidate();
    }

    private static Process? FindDotnetProcess(string dllName)
    {
        return FindProcessByCommandLine(["dotnet.exe"], dllName);
    }

    private static Process? FindProcessByCommandLine(string[] processNames, params string[] markers)
    {
        foreach (var processName in processNames)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    $"SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name = '{processName}'");

                foreach (var obj in searcher.Get())
                {
                    var commandLine = obj["CommandLine"]?.ToString() ?? "";
                    var required = markers.Where(m => !string.IsNullOrEmpty(m)).ToArray();
                    if (required.Length == 0 || required.All(m => commandLine.Contains(m, StringComparison.OrdinalIgnoreCase)))
                    {
                        var pid = Convert.ToInt32(obj["ProcessId"]);
                        return Process.GetProcessById(pid);
                    }
                }
            }
            catch
            {
                // WMI may be unavailable
            }
        }

        return null;
    }

    private static void KillChildren(int parentPid)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                $"SELECT ProcessId FROM Win32_Process WHERE ParentProcessId = {parentPid}");

            foreach (var obj in searcher.Get())
            {
                var childPid = Convert.ToInt32(obj["ProcessId"]);
                try
                {
                    var child = Process.GetProcessById(childPid);
                    KillProcessTree(child);
                }
                catch
                {
                    TryTaskKill(childPid);
                }
            }
        }
        catch
        {
            // ignore
        }
    }

    private static void TryTaskKill(int pid)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "taskkill",
                Arguments = $"/PID {pid} /T /F",
                CreateNoWindow = true,
                UseShellExecute = false
            };
            Process.Start(psi)?.WaitForExit(3000);
        }
        catch
        {
            // ignore
        }
    }

    private static string? GetCommandLine(int pid)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                $"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {pid}");

            foreach (var obj in searcher.Get())
                return obj["CommandLine"]?.ToString();
        }
        catch
        {
            // ignore
        }

        return null;
    }
}
