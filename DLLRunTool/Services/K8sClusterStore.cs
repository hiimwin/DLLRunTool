using System.Text.Json;
using DLLRunTool.Models;
using k8s;

namespace DLLRunTool.Services;

/// <summary>
/// Cấu hình cluster K8s dùng chung (k8s.clusters.json) + override cá nhân (k8s.local.json).
/// </summary>
public static class K8sClusterStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static string SharedFilePath => Path.Combine(AppContext.BaseDirectory, "k8s.clusters.json");
    public static string LocalFilePath => Path.Combine(AppContext.BaseDirectory, "k8s.local.json");
    public static string ExampleFilePath => Path.Combine(AppContext.BaseDirectory, "k8s.clusters.example.json");
    public static string KubeConfigsFolder => Path.Combine(AppContext.BaseDirectory, "kubeconfigs");

    public static void EnsureLoaded()
    {
        Directory.CreateDirectory(KubeConfigsFolder);

        if (!File.Exists(LocalFilePath))
            SaveLocal(new K8sClustersFile());
    }

    public static IReadOnlyList<string> GetAllKubeConfigPaths()
    {
        var paths = new List<string>();
        var fromEnv = Environment.GetEnvironmentVariable("KUBECONFIG");
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            foreach (var part in fromEnv.Split(';', Path.PathSeparator))
            {
                var p = part.Trim().Trim('"');
                if (File.Exists(p) && !paths.Contains(p, StringComparer.OrdinalIgnoreCase))
                    paths.Add(Path.GetFullPath(p));
            }
        }

        if (paths.Count == 0)
        {
            var def = GetDefaultKubeConfigPath();
            if (File.Exists(def))
                paths.Add(def);
        }

        foreach (var lensPath in GetLensKubeConfigPaths())
        {
            if (!paths.Contains(lensPath, StringComparer.OrdinalIgnoreCase))
                paths.Add(lensPath);
        }

        return paths;
    }

    public static IEnumerable<string> GetLensKubeConfigPaths()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Lens", "kubeconfigs");
        if (!Directory.Exists(dir))
            yield break;

        foreach (var file in Directory.EnumerateFiles(dir))
            yield return file;
    }

    public static IReadOnlyList<K8sClusterUiDto> GetAllForUi()
    {
        EnsureLoaded();
        var shared = LoadFile(SharedFilePath);
        var local = LoadFile(LocalFilePath);
        var lastId = local.LastConnectedId ?? shared.LastConnectedId;
        var hotbar = new HashSet<string>(local.HotbarIds ?? [], StringComparer.OrdinalIgnoreCase);
        var hidden = new HashSet<string>(local.HiddenClusterIds ?? [], StringComparer.OrdinalIgnoreCase);

        var merged = new Dictionary<string, K8sClusterEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in shared.Clusters)
        {
            if (!string.IsNullOrWhiteSpace(c.Id))
                merged[c.Id] = c;
        }

        foreach (var c in local.Clusters)
        {
            if (!string.IsNullOrWhiteSpace(c.Id))
                merged[c.Id] = c;
        }

        var list = merged.Values
            .Select(e => ToUiDto(e, shared.Clusters.Any(s => s.Id == e.Id), local, hotbar))
            .Where(d => !hidden.Contains(d.Id))
            .ToList();

        foreach (var discovered in DiscoverMachineContexts(local))
        {
            if (hidden.Contains(discovered.Id))
                continue;
            if (list.All(x => !string.Equals(x.Id, discovered.Id, StringComparison.OrdinalIgnoreCase)))
                list.Add(ApplyUiMeta(discovered, local, hotbar));
        }

        if (!string.IsNullOrWhiteSpace(lastId))
        {
            foreach (var item in list.Where(x => x.Id == lastId))
                item.Name = "★ " + item.Name.TrimStart('★', ' ');
        }

        return list
            .OrderByDescending(c => hotbar.Contains(c.Id))
            .ThenByDescending(c => c.Id.StartsWith("machine:", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(c => c.IsShared)
            .ThenBy(c => c.Name.TrimStart('★', ' '))
            .ToList();
    }

    public static object GetClusterSettings(string? clusterId, string? context)
    {
        EnsureLoaded();
        var local = LoadFile(LocalFilePath);
        var entry = string.IsNullOrWhiteSpace(clusterId) ? null : Resolve(clusterId);
        var ctx = context ?? entry?.Context ?? "";
        var namespaces = ResolveNamespaces(clusterId, ctx).ToList();
        var color = ResolveClusterColor(local, clusterId, ctx);
        var onHotbar = !string.IsNullOrWhiteSpace(clusterId)
            && (local.HotbarIds ?? []).Contains(clusterId, StringComparer.OrdinalIgnoreCase);

        return new
        {
            clusterId,
            clusterName = entry?.Name ?? ctx,
            context = ctx,
            kubeConfigPath = entry != null ? ResolveKubeConfigPath(entry) : GetDefaultKubeConfigPath(),
            namespaces,
            color,
            onHotbar,
            canRemoveFromKubeconfig = !string.IsNullOrWhiteSpace(clusterId)
                && clusterId.StartsWith("machine:", StringComparison.OrdinalIgnoreCase)
        };
    }

    public static void AddToHotbar(string clusterId)
    {
        if (string.IsNullOrWhiteSpace(clusterId))
            return;

        EnsureLoaded();
        var local = LoadFile(LocalFilePath);
        local.HotbarIds ??= [];
        if (!local.HotbarIds.Contains(clusterId, StringComparer.OrdinalIgnoreCase))
            local.HotbarIds.Insert(0, clusterId);
        SaveLocal(local);
    }

    public static void ToggleHotbar(string clusterId)
    {
        if (string.IsNullOrWhiteSpace(clusterId))
            return;

        EnsureLoaded();
        var local = LoadFile(LocalFilePath);
        local.HotbarIds ??= [];
        if (local.HotbarIds.Contains(clusterId, StringComparer.OrdinalIgnoreCase))
            local.HotbarIds.RemoveAll(id => string.Equals(id, clusterId, StringComparison.OrdinalIgnoreCase));
        else
            local.HotbarIds.Insert(0, clusterId);
        SaveLocal(local);
    }

    public static void SetClusterColor(string? clusterId, string? context, string color)
    {
        EnsureLoaded();
        var local = LoadFile(LocalFilePath);
        local.ClusterColors ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var key = !string.IsNullOrWhiteSpace(clusterId) ? clusterId : context ?? "";
        if (string.IsNullOrWhiteSpace(key))
            return;

        local.ClusterColors[key] = NormalizeColor(color);
        SaveLocal(local);
    }

    public static (bool Ok, string Message) HideOrRemoveCluster(string clusterId)
    {
        if (string.IsNullOrWhiteSpace(clusterId))
            return (false, "Thiếu clusterId.");

        EnsureLoaded();
        var local = LoadFile(LocalFilePath);
        local.HiddenClusterIds ??= [];
        if (!local.HiddenClusterIds.Contains(clusterId, StringComparer.OrdinalIgnoreCase))
            local.HiddenClusterIds.Add(clusterId);

        local.HotbarIds?.RemoveAll(id => string.Equals(id, clusterId, StringComparison.OrdinalIgnoreCase));
        if (string.Equals(local.LastConnectedId, clusterId, StringComparison.OrdinalIgnoreCase))
            local.LastConnectedId = null;

        SaveLocal(local);

        if (!clusterId.StartsWith("machine:", StringComparison.OrdinalIgnoreCase))
            return (true, "Đã ẩn cluster khỏi danh sách.");

        var parts = clusterId.Split(':', 3);
        if (parts.Length != 3)
            return (true, "Đã ẩn cluster khỏi danh sách.");

        var fileName = parts[1];
        var ctx = parts[2];
        var path = GetAllKubeConfigPaths()
            .FirstOrDefault(p => string.Equals(Path.GetFileName(p), fileName, StringComparison.OrdinalIgnoreCase));
        if (path == null)
            return (true, "Đã ẩn cluster khỏi danh sách.");

        var (ok, msg) = K8sKubeConfigEditor.TryRemoveContext(path, ctx);
        return ok ? (true, msg) : (true, $"Đã ẩn khỏi tool. {msg}");
    }

    public static string GetClusterColor(string? clusterId, string? context)
    {
        EnsureLoaded();
        var local = LoadFile(LocalFilePath);
        return ResolveClusterColor(local, clusterId, context);
    }

    private static string NormalizeColor(string? color) => color?.ToLowerInvariant() switch
    {
        "green" => "green",
        "red" => "red",
        "blue" => "blue",
        "grey" or "gray" => "grey",
        _ => "yellow"
    };

    private static string ResolveClusterColor(K8sClustersFile local, string? clusterId, string? context)
    {
        if (!string.IsNullOrWhiteSpace(clusterId)
            && local.ClusterColors.TryGetValue(clusterId, out var byId))
            return NormalizeColor(byId);

        if (!string.IsNullOrWhiteSpace(context)
            && local.ClusterColors.TryGetValue(context, out var byCtx))
            return NormalizeColor(byCtx);

        return "yellow";
    }

    public static K8sClusterEntry? Resolve(string clusterId)
    {
        EnsureLoaded();
        var shared = LoadFile(SharedFilePath);
        var local = LoadFile(LocalFilePath);

        var entry = local.Clusters.FirstOrDefault(c => c.Id == clusterId)
            ?? shared.Clusters.FirstOrDefault(c => c.Id == clusterId)
            ?? DiscoverMachineEntries().FirstOrDefault(c => c.Id == clusterId);

        if (entry != null)
            return Clone(entry);

        // machine id: machine:<file>:<context>
        if (clusterId.StartsWith("machine:", StringComparison.OrdinalIgnoreCase))
        {
            var parts = clusterId.Split(':', 3);
            if (parts.Length == 3)
            {
                var fileName = parts[1];
                var ctx = parts[2];
                var path = GetAllKubeConfigPaths()
                    .FirstOrDefault(p => string.Equals(Path.GetFileName(p), fileName, StringComparison.OrdinalIgnoreCase));
                if (path != null)
                {
                    return new K8sClusterEntry
                    {
                        Id = clusterId,
                        Name = ctx,
                        Source = "file",
                        KubeConfigPath = path,
                        Context = ctx
                    };
                }
            }
        }

        return null;
    }

    public static void SetLastConnected(string clusterId)
    {
        EnsureLoaded();
        var local = LoadFile(LocalFilePath);
        local.LastConnectedId = clusterId;
        SaveLocal(local);
    }

    public static IReadOnlyList<string> ResolveNamespaces(string? clusterId, string? context)
    {
        EnsureLoaded();
        var merged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(clusterId))
        {
            var entry = Resolve(clusterId);
            if (entry?.Namespaces != null)
            {
                foreach (var ns in entry.Namespaces)
                {
                    if (!string.IsNullOrWhiteSpace(ns))
                        merged.Add(ns.Trim());
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(context))
        {
            var local = LoadFile(LocalFilePath);
            if (local.NamespaceByContext.TryGetValue(context, out var fromContext))
            {
                foreach (var ns in fromContext)
                {
                    if (!string.IsNullOrWhiteSpace(ns))
                        merged.Add(ns.Trim());
                }
            }
        }

        return merged.OrderBy(n => n).ToList();
    }

    public static void SaveNamespacesForContext(string context, IEnumerable<string> namespaces)
    {
        if (string.IsNullOrWhiteSpace(context))
            return;

        EnsureLoaded();
        var local = LoadFile(LocalFilePath);
        local.NamespaceByContext[context] = namespaces
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n)
            .ToList();
        SaveLocal(local);
    }

    public static K8sClusterEntry AddLocalCluster(string name, string relativeKubeConfigPath, string? context)
    {
        EnsureLoaded();
        var local = LoadFile(LocalFilePath);
        var id = $"local-{Guid.NewGuid():N}"[..12];
        var entry = new K8sClusterEntry
        {
            Id = id,
            Name = name,
            Source = "file",
            KubeConfigPath = relativeKubeConfigPath.Replace('\\', '/'),
            Context = context ?? ""
        };
        local.Clusters.Add(entry);
        SaveLocal(local);
        return entry;
    }

    public static string ImportKubeConfigToAppFolder(string sourcePath)
    {
        Directory.CreateDirectory(KubeConfigsFolder);
        var fileName = Path.GetFileName(sourcePath);
        if (string.IsNullOrWhiteSpace(fileName))
            fileName = "kubeconfig.yaml";

        var dest = Path.Combine(KubeConfigsFolder, fileName);
        File.Copy(sourcePath, dest, overwrite: true);
        return Path.GetRelativePath(AppContext.BaseDirectory, dest).Replace('\\', '/');
    }

    public static string ResolveKubeConfigPath(K8sClusterEntry entry)
    {
        if (string.Equals(entry.Source, "default", StringComparison.OrdinalIgnoreCase))
            return GetDefaultKubeConfigPath();

        var path = entry.KubeConfigPath ?? "";
        if (Path.IsPathRooted(path))
            return path;

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, path));
    }

    public static string GetDefaultKubeConfigPath()
    {
        var fromEnv = Environment.GetEnvironmentVariable("KUBECONFIG");
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            var first = fromEnv.Split(';', ':')[0].Trim();
            if (File.Exists(first))
                return first;
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".kube",
            "config");
    }

    public static IReadOnlyList<string> ListContexts(string kubeConfigPath)
    {
        if (!File.Exists(kubeConfigPath))
            return [];

        try
        {
            using var stream = File.OpenRead(kubeConfigPath);
            var kubeConfig = KubernetesClientConfiguration.LoadKubeConfig(stream);
            return kubeConfig.Contexts
                .Select(c => c.Name)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    private static IEnumerable<K8sClusterEntry> DiscoverMachineEntries()
    {
        foreach (var path in GetAllKubeConfigPaths())
        {
            foreach (var ctx in ListContexts(path))
            {
                yield return new K8sClusterEntry
                {
                    Id = $"machine:{Path.GetFileName(path)}:{ctx}",
                    Name = ctx,
                    Source = "file",
                    KubeConfigPath = path,
                    Context = ctx
                };
            }
        }
    }

    private static IEnumerable<K8sClusterUiDto> DiscoverMachineContexts(K8sClustersFile local)
    {
        var hotbar = new HashSet<string>(local.HotbarIds ?? [], StringComparer.OrdinalIgnoreCase);
        foreach (var path in GetAllKubeConfigPaths())
        {
            var contexts = ListContexts(path);
            var fromLens = path.Contains("Lens", StringComparison.OrdinalIgnoreCase);
            foreach (var ctx in contexts)
            {
                var dto = new K8sClusterUiDto
                {
                    Id = $"machine:{Path.GetFileName(path)}:{ctx}",
                    Name = fromLens ? $"{ctx} (Lens)" : ctx,
                    Source = "file",
                    KubeConfigPath = path,
                    Context = ctx,
                    Contexts = contexts.ToList(),
                    IsShared = false,
                    IsLocal = false,
                    KubeConfigExists = true
                };
                yield return ApplyUiMeta(dto, local, hotbar);
            }
        }
    }

    private static K8sClusterUiDto ApplyUiMeta(K8sClusterUiDto dto, K8sClustersFile local, HashSet<string> hotbar)
    {
        dto.OnHotbar = hotbar.Contains(dto.Id);
        dto.Color = ResolveClusterColor(local, dto.Id, dto.Context);
        return dto;
    }

    private static K8sClusterUiDto ToUiDto(K8sClusterEntry entry, bool isShared, K8sClustersFile local, HashSet<string> hotbar)
    {
        var kubePath = string.Equals(entry.Source, "default", StringComparison.OrdinalIgnoreCase)
            ? GetDefaultKubeConfigPath()
            : ResolveKubeConfigPath(entry);

        var contexts = ListContexts(kubePath);
        var context = string.IsNullOrWhiteSpace(entry.Context)
            ? contexts.FirstOrDefault() ?? ""
            : entry.Context;

        return ApplyUiMeta(new K8sClusterUiDto
        {
            Id = entry.Id,
            Name = entry.Name,
            Source = entry.Source,
            KubeConfigPath = kubePath,
            Context = context,
            Server = entry.Server,
            Contexts = contexts.ToList(),
            IsShared = isShared,
            IsLocal = !isShared,
            KubeConfigExists = string.Equals(entry.Source, "default", StringComparison.OrdinalIgnoreCase)
                ? File.Exists(kubePath)
                : File.Exists(kubePath)
        }, local, hotbar);
    }

    private static K8sClustersFile LoadFile(string path)
    {
        if (!File.Exists(path))
            return new K8sClustersFile();

        try
        {
            return JsonSerializer.Deserialize<K8sClustersFile>(File.ReadAllText(path), JsonOptions)
                   ?? new K8sClustersFile();
        }
        catch
        {
            return new K8sClustersFile();
        }
    }

    private static void SaveLocal(K8sClustersFile file)
    {
        File.WriteAllText(LocalFilePath, JsonSerializer.Serialize(file, JsonOptions));
    }

    private static K8sClusterEntry Clone(K8sClusterEntry e) => new()
    {
        Id = e.Id,
        Name = e.Name,
        Source = e.Source,
        KubeConfigPath = e.KubeConfigPath,
        Context = e.Context,
        Server = e.Server,
        Namespaces = e.Namespaces?.ToList()
    };
}
