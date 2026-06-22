using System.Diagnostics;
using System.Text;

namespace DLLRunTool.Services;

/// <summary>
/// Ghi log ra file và mở CMD mirror — luôn stream vào tool, CMD riêng hiện ngay khi bật.
/// </summary>
public static class ServiceLogMirror
{
    private const int MaxLogFileBytes = 5 * 1024 * 1024;
    private static readonly object Gate = new();
    private static readonly Dictionary<string, MirrorSession> Sessions = new(StringComparer.OrdinalIgnoreCase);

    public static string EnsureLogFile(string serviceId, string serviceName)
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "service-logs");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"{SanitizeFileName(serviceId)}.log");
        try
        {
            File.WriteAllText(path, $"=== {serviceName} {DateTime.Now:yyyy-MM-dd HH:mm:ss} ==={Environment.NewLine}", Encoding.UTF8);
        }
        catch
        {
            // ignore
        }

        return path;
    }

    public static void AppendLine(string serviceId, string line)
    {
        MirrorSession? session;
        lock (Gate)
        {
            Sessions.TryGetValue(serviceId, out session);
        }

        if (session == null)
            return;

        try
        {
            RotateIfNeeded(session.LogPath);
            File.AppendAllText(session.LogPath, line + Environment.NewLine, Encoding.UTF8);
        }
        catch
        {
            // ignore
        }
    }

    public static void Register(string serviceId, string logPath)
    {
        lock (Gate)
        {
            if (!Sessions.TryGetValue(serviceId, out var session))
            {
                Sessions[serviceId] = new MirrorSession { LogPath = logPath };
                return;
            }

            session.LogPath = logPath;
        }
    }

    public static void Unregister(string serviceId)
    {
        CloseMirror(serviceId);
        lock (Gate)
        {
            Sessions.Remove(serviceId);
        }
    }

    public static void OpenMirror(string serviceId, string serviceName)
    {
        lock (Gate)
        {
            if (!Sessions.TryGetValue(serviceId, out var session))
                return;

            if (session.MirrorProcess != null && !session.MirrorProcess.HasExited)
                return;

            var escapedPath = session.LogPath.Replace("'", "''");
            var title = serviceName.Replace("\"", "'");
            var ps = $"-NoProfile -NoExit -Command \"$Host.UI.RawUI.WindowTitle='{title}'; Get-Content -LiteralPath '{escapedPath}' -Wait -Tail 30\"";
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = ps,
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Normal
            };

            try
            {
                session.MirrorProcess = Process.Start(psi);
            }
            catch
            {
                // ignore
            }
        }
    }

    public static void CloseMirror(string serviceId)
    {
        Process? proc;
        lock (Gate)
        {
            if (!Sessions.TryGetValue(serviceId, out var session))
                return;
            proc = session.MirrorProcess;
            session.MirrorProcess = null;
        }

        if (proc == null || proc.HasExited)
            return;

        try { ProcessTreeKiller.KillProcessTree(proc); } catch { /* ignore */ }
    }

    public static void OpenMirrorsForAllRegistered()
    {
        List<(string Id, string Name)> list;
        lock (Gate)
        {
            list = Sessions.Keys.Select(id => (id, id)).ToList();
        }

        foreach (var id in list.Select(x => x.Id))
            OpenMirror(id, id);
    }

    public static void OpenMirrors(IEnumerable<(string Id, string Name)> services)
    {
        foreach (var (id, name) in services)
            OpenMirror(id, name);
    }

    public static void CloseAllMirrors()
    {
        List<string> ids;
        lock (Gate)
        {
            ids = Sessions.Keys.ToList();
        }

        foreach (var id in ids)
            CloseMirror(id);
    }

    private static void RotateIfNeeded(string logPath)
    {
        try
        {
            if (!File.Exists(logPath))
                return;

            var info = new FileInfo(logPath);
            if (info.Length < MaxLogFileBytes)
                return;

            var archive = logPath + $".{DateTime.Now:yyyyMMdd-HHmmss}.old";
            File.Move(logPath, archive, overwrite: true);
            File.WriteAllText(logPath, $"=== rotated {DateTime.Now:yyyy-MM-dd HH:mm:ss} ==={Environment.NewLine}", Encoding.UTF8);

            var dir = Path.GetDirectoryName(logPath);
            if (string.IsNullOrEmpty(dir))
                return;

            foreach (var old in Directory.EnumerateFiles(dir, $"{Path.GetFileNameWithoutExtension(logPath)}.*.old")
                         .Select(f => new FileInfo(f))
                         .OrderByDescending(f => f.LastWriteTimeUtc)
                         .Skip(3))
            {
                try { old.Delete(); } catch { /* ignore */ }
            }
        }
        catch
        {
            // ignore rotation errors
        }
    }

    private static string SanitizeFileName(string value)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            value = value.Replace(c, '_');
        return value;
    }

    private sealed class MirrorSession
    {
        public string LogPath { get; set; } = "";
        public Process? MirrorProcess { get; set; }
    }
}
