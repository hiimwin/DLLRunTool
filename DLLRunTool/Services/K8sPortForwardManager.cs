using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using DLLRunTool.Models;

namespace DLLRunTool.Services;

public sealed class K8sPortForwardSession
{
    public string Id { get; init; } = "";
    public string ConfigId { get; init; } = "";
    public string Namespace { get; init; } = "";
    public string ResourceKind { get; init; } = "";
    public string ResourceName { get; init; } = "";
    public int RemotePort { get; init; }
    public int LocalPort { get; init; }
    public string Context { get; init; } = "";
    public bool UseHttps { get; init; }
    public bool OpenInBrowser { get; init; }
    public bool Running { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.Now;
    public string Url => $"{(UseHttps ? "https" : "http")}://localhost:{LocalPort}";
}

/// <summary>Quản lý kubectl port-forward — tương thích kubelogin/Azure CLI.</summary>
public static class K8sPortForwardManager
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, (K8sPortForwardSession Session, Process Process)> Sessions = new(StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<K8sPortForwardSession> GetActiveSessions()
    {
        lock (Gate)
        {
            PruneExited();
            return Sessions.Values.Select(v => v.Session).OrderByDescending(s => s.StartedAt).ToList();
        }
    }

    public static K8sPortForwardSession Start(
        string ns,
        string resourceKind,
        string resourceName,
        int remotePort,
        int? localPort,
        string? context,
        string? kubeConfigPath,
        bool useHttps = false,
        bool openInBrowser = false)
    {
        if (string.IsNullOrWhiteSpace(ns) || string.IsNullOrWhiteSpace(resourceName))
            throw new ArgumentException("Thiếu namespace hoặc tên resource.");

        if (remotePort <= 0)
            throw new ArgumentException("Port không hợp lệ.");

        var kind = NormalizeKind(resourceKind);
        var local = localPort ?? remotePort;
        var configId = K8sPortForwardStore.BuildConfigId(ns, kind, resourceName, remotePort);

        lock (Gate)
        {
            PruneExited();
            StopByConfigId(configId);

            var session = new K8sPortForwardSession
            {
                Id = $"{configId}@{local}",
                ConfigId = configId,
                Namespace = ns,
                ResourceKind = kind,
                ResourceName = resourceName,
                RemotePort = remotePort,
                LocalPort = local,
                Context = context ?? "",
                UseHttps = useHttps,
                OpenInBrowser = openInBrowser,
                Running = true
            };

            var psi = BuildStartInfo(ns, kind, resourceName, local, remotePort, context, kubeConfigPath);
            var process = new Process
            {
                StartInfo = psi,
                EnableRaisingEvents = true
            };

            var err = new StringBuilder();
            process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                    err.AppendLine(e.Data);
            };

            if (!process.Start())
                throw new InvalidOperationException("Không khởi chạy được kubectl port-forward.");

            process.BeginErrorReadLine();
            Sessions[session.Id] = (session, process);

            process.Exited += (_, _) =>
            {
                lock (Gate)
                {
                    session.Running = false;
                    if (err.Length > 0)
                        session.Error = err.ToString().Trim();
                }
            };

            return session;
        }
    }

    public static void StopByConfigId(string configId)
    {
        lock (Gate)
        {
            foreach (var key in Sessions.Keys.Where(k => k.StartsWith(configId, StringComparison.OrdinalIgnoreCase)).ToList())
            {
                if (Sessions.TryGetValue(key, out var entry))
                {
                    TryKill(entry.Process);
                    entry.Session.Running = false;
                    Sessions.Remove(key);
                }
            }
        }
    }

    public static IReadOnlyList<K8sPortForwardDto> GetMergedList(string? context)
    {
        var configs = K8sPortForwardStore.GetForContext(context);
        var active = GetActiveSessions().Where(s => string.IsNullOrWhiteSpace(context)
            || string.Equals(s.Context, context, StringComparison.OrdinalIgnoreCase)).ToList();

        var map = new Dictionary<string, K8sPortForwardDto>(StringComparer.OrdinalIgnoreCase);

        foreach (var cfg in configs)
        {
            map[cfg.Id] = new K8sPortForwardDto
            {
                Id = cfg.Id,
                ConfigId = cfg.Id,
                Namespace = cfg.Namespace,
                ResourceKind = cfg.ResourceKind,
                ResourceName = cfg.ResourceName,
                RemotePort = cfg.RemotePort,
                LocalPort = cfg.LocalPort,
                UseHttps = cfg.UseHttps,
                OpenInBrowser = cfg.OpenInBrowser,
                Running = false,
                Status = "Disabled",
                Url = $"{(cfg.UseHttps ? "https" : "http")}://localhost:{cfg.LocalPort}",
                Protocol = cfg.UseHttps ? "https" : "http"
            };
        }

        foreach (var s in active)
        {
            var dto = new K8sPortForwardDto
            {
                Id = s.Id,
                ConfigId = s.ConfigId,
                Namespace = s.Namespace,
                ResourceKind = s.ResourceKind,
                ResourceName = s.ResourceName,
                RemotePort = s.RemotePort,
                LocalPort = s.LocalPort,
                UseHttps = s.UseHttps,
                OpenInBrowser = s.OpenInBrowser,
                Running = s.Running,
                Status = s.Running ? "Active" : "Disabled",
                Url = s.Url,
                Protocol = s.UseHttps ? "https" : "http",
                Error = s.Error
            };
            map[s.ConfigId] = dto;
        }

        return map.Values
            .OrderByDescending(d => d.Running)
            .ThenBy(d => d.Namespace)
            .ThenBy(d => d.ResourceName)
            .ToList();
    }

    public static bool Stop(string? sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return false;

        lock (Gate)
        {
            if (!Sessions.TryGetValue(sessionId, out var entry))
                return false;

            TryKill(entry.Process);
            entry.Session.Running = false;
            Sessions.Remove(sessionId);
            return true;
        }
    }

    public static void StopAll()
    {
        lock (Gate)
        {
            foreach (var entry in Sessions.Values)
            {
                TryKill(entry.Process);
                entry.Session.Running = false;
            }
            Sessions.Clear();
        }
    }

    private static void PruneExited()
    {
        foreach (var key in Sessions.Where(p => p.Value.Process.HasExited).Select(p => p.Key).ToList())
            Sessions.Remove(key);
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            // ignore
        }
    }

    public static string NormalizeKindPublic(string? kind) => NormalizeKind(kind);

    private static string NormalizeKind(string? kind) => kind?.ToLowerInvariant() switch
    {
        "pod" or "pods" => "pod",
        "service" or "services" or "svc" => "svc",
        _ => string.IsNullOrWhiteSpace(kind) ? "svc" : kind.Trim().ToLowerInvariant()
    };

    private static ProcessStartInfo BuildStartInfo(
        string ns,
        string kind,
        string name,
        int localPort,
        int remotePort,
        string? context,
        string? kubeConfigPath)
    {
        var kubectl = ResolveKubectlPath();
        var args = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(context))
            args.Append($"--context \"{EscapeArg(context)}\" ");
        if (!string.IsNullOrWhiteSpace(kubeConfigPath))
            args.Append($"--kubeconfig \"{EscapeArg(kubeConfigPath)}\" ");
        args.Append($"port-forward -n \"{EscapeArg(ns)}\" {kind}/{EscapeArg(name)} {localPort}:{remotePort}");

        var psi = new ProcessStartInfo
        {
            FileName = kubectl,
            Arguments = args.ToString(),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        };

        if (!string.IsNullOrWhiteSpace(kubeConfigPath))
            psi.Environment["KUBECONFIG"] = kubeConfigPath;

        return psi;
    }

    private static string ResolveKubectlPath()
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(';', Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(dir))
                continue;
            var candidate = Path.Combine(dir.Trim(), OperatingSystem.IsWindows() ? "kubectl.exe" : "kubectl");
            if (File.Exists(candidate))
                return candidate;
        }

        return "kubectl";
    }

    private static int FindFreeLocalPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    private static string EscapeArg(string value) => value.Replace("\"", "\\\"");
}
