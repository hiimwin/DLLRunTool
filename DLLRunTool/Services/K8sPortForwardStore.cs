using DLLRunTool.Models;

namespace DLLRunTool.Services;

public static class K8sPortForwardStore
{
    public static string BuildConfigId(string ns, string kind, string name, int remotePort) =>
        $"{ns}/{K8sPortForwardManager.NormalizeKindPublic(kind)}/{name}:{remotePort}";

    public static IReadOnlyList<K8sPortForwardConfig> GetForContext(string? context)
    {
        if (string.IsNullOrWhiteSpace(context))
            return [];

        K8sClusterStore.EnsureLoaded();
        var local = K8sClusterStore.LoadLocalFile();
        if (!local.PortForwardsByContext.TryGetValue(context, out var list) || list == null)
            return [];

        return list.OrderBy(c => c.Namespace).ThenBy(c => c.ResourceName).ToList();
    }

    public static K8sPortForwardConfig? Find(string? context, string configId)
    {
        if (string.IsNullOrWhiteSpace(context))
            return null;

        return GetForContext(context).FirstOrDefault(c =>
            string.Equals(c.Id, configId, StringComparison.OrdinalIgnoreCase));
    }

    public static K8sPortForwardConfig Save(
        string context,
        string ns,
        string resourceKind,
        string resourceName,
        int remotePort,
        int localPort,
        bool useHttps,
        bool openInBrowser)
    {
        K8sClusterStore.EnsureLoaded();
        var local = K8sClusterStore.LoadLocalFile();
        local.PortForwardsByContext ??= new Dictionary<string, List<K8sPortForwardConfig>>(StringComparer.OrdinalIgnoreCase);

        if (!local.PortForwardsByContext.TryGetValue(context, out var list) || list == null)
        {
            list = [];
            local.PortForwardsByContext[context] = list;
        }

        var id = BuildConfigId(ns, resourceKind, resourceName, remotePort);
        var existing = list.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            existing.LocalPort = localPort;
            existing.UseHttps = useHttps;
            existing.OpenInBrowser = openInBrowser;
            K8sClusterStore.SaveLocalFile(local);
            return existing;
        }

        var cfg = new K8sPortForwardConfig
        {
            Id = id,
            Namespace = ns,
            ResourceKind = K8sPortForwardManager.NormalizeKindPublic(resourceKind),
            ResourceName = resourceName,
            RemotePort = remotePort,
            LocalPort = localPort,
            UseHttps = useHttps,
            OpenInBrowser = openInBrowser
        };
        list.Add(cfg);
        K8sClusterStore.SaveLocalFile(local);
        return cfg;
    }

    public static void Remove(string? context, string configId)
    {
        if (string.IsNullOrWhiteSpace(context) || string.IsNullOrWhiteSpace(configId))
            return;

        K8sClusterStore.EnsureLoaded();
        var local = K8sClusterStore.LoadLocalFile();
        if (local.PortForwardsByContext == null
            || !local.PortForwardsByContext.TryGetValue(context, out var list)
            || list == null)
            return;

        list.RemoveAll(c => string.Equals(c.Id, configId, StringComparison.OrdinalIgnoreCase));
        K8sClusterStore.SaveLocalFile(local);
    }
}
