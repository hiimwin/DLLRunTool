using System.Text.Json;
using DLLRunTool.Models;
using k8s;
using k8s.Models;

namespace DLLRunTool.Services;

/// <summary>
/// Kubernetes API thuần C# qua KubernetesClient — không gọi kubectl.exe.
/// </summary>
public sealed class K8sService : IAsyncDisposable
{
    private IKubernetes? _client;
    private bool _disposed;
    private bool _limitedRbac;
    private bool _noWorkloadAccess;
    private string? _kubeConfigPath;
    private string? _activeContext;
    private string? _clusterId;
    private List<string> _configuredNamespaces = [];

    public bool IsConnected => _client != null;
    public bool LimitedRbac => _limitedRbac;
    public bool NoWorkloadAccess => _noWorkloadAccess;
    public string? LastInfo { get; private set; }
    public string? ConnectedClusterName { get; private set; }
    public string? ConnectedContext { get; private set; }
    public string? ConnectedClusterId { get; private set; }
    public string? ConnectedKubeConfigPath => _kubeConfigPath;

    public async Task<(bool Ok, string Message)> ConnectAsync(CancellationToken ct = default) =>
        await ConnectToClusterAsync(clusterId: "local-default", null, null, ct).ConfigureAwait(false);

    public async Task<(bool Ok, string Message)> ConnectToClusterAsync(
        string? clusterId,
        string? kubeConfigPath,
        string? context,
        CancellationToken ct = default)
    {
        try
        {
            await DisconnectAsync().ConfigureAwait(false);

            string resolvedPath;
            string? resolvedContext;
            string displayName;

            if (!string.IsNullOrWhiteSpace(kubeConfigPath))
            {
                resolvedPath = Path.IsPathRooted(kubeConfigPath)
                    ? kubeConfigPath
                    : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, kubeConfigPath));
                resolvedContext = context;
                displayName = Path.GetFileName(resolvedPath);
            }
            else if (!string.IsNullOrWhiteSpace(clusterId))
            {
                var entry = K8sClusterStore.Resolve(clusterId);
                if (entry == null)
                    return (false, $"Không tìm thấy cluster '{clusterId}' trong cấu hình.");

                resolvedPath = string.Equals(entry.Source, "default", StringComparison.OrdinalIgnoreCase)
                    ? K8sClusterStore.GetDefaultKubeConfigPath()
                    : K8sClusterStore.ResolveKubeConfigPath(entry);
                resolvedContext = string.IsNullOrWhiteSpace(context) ? entry.Context : context;
                displayName = entry.Name;
            }
            else
            {
                resolvedPath = K8sClusterStore.GetDefaultKubeConfigPath();
                resolvedContext = context;
                displayName = "kubeconfig mặc định";
            }

            if (!File.Exists(resolvedPath))
                return (false, $"Không tìm thấy kubeconfig:\n{resolvedPath}");

            var config = string.IsNullOrWhiteSpace(resolvedContext)
                ? KubernetesClientConfiguration.BuildConfigFromConfigFile(resolvedPath)
                : KubernetesClientConfiguration.BuildConfigFromConfigFile(resolvedPath, resolvedContext);

            if (config == null)
                return (false, "Không đọc được kubeconfig.");

            _client = new Kubernetes(config);

            var (verified, verifyMsg, limited) = await VerifyClusterAccessAsync(_client, ct).ConfigureAwait(false);
            if (!verified)
            {
                await DisconnectAsync().ConfigureAwait(false);
                return (false, verifyMsg);
            }

            _limitedRbac = limited;
            _kubeConfigPath = resolvedPath;
            _activeContext = resolvedContext ?? config.CurrentContext;
            _clusterId = clusterId;
            _configuredNamespaces = K8sClusterStore
                .ResolveNamespaces(clusterId, _activeContext)
                .ToList();

            var permissions = await K8sPermissionChecker.EvaluateAsync(
                _client,
                resolvedPath,
                _activeContext ?? resolvedContext,
                _configuredNamespaces,
                ct).ConfigureAwait(false);

            if (permissions.NoWorkloadAccess)
            {
                _limitedRbac = true;
                _noWorkloadAccess = true;
                LastInfo = permissions.Message;
            }
            else if (!permissions.CanListPodsClusterWide)
            {
                _limitedRbac = true;
                LastInfo = permissions.Message;
            }

            ConnectedClusterName = displayName;
            ConnectedContext = resolvedContext ?? "";
            ConnectedClusterId = clusterId;
            if (!string.IsNullOrWhiteSpace(clusterId))
                K8sClusterStore.SetLastConnected(clusterId);

            var suffix = limited ? " · quyền hạn chế" : "";
            var note = string.IsNullOrWhiteSpace(verifyMsg) ? "" : $" {verifyMsg}";
            return (true, $"Đã kết nối: {displayName} ({ConnectedContext}){suffix}.{note}");
        }
        catch (k8s.Autorest.HttpOperationException ex) when (ex.Response?.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            await DisconnectAsync().ConfigureAwait(false);
            return (false, "Kubeconfig không hợp lệ hoặc hết hạn (401). Chạy lại az login.");
        }
        catch (k8s.Autorest.HttpOperationException ex) when (ex.Response?.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            await DisconnectAsync().ConfigureAwait(false);
            return (false, FormatK8sError(ex) + "\n\nTài khoản thiếu quyền trên cluster. Liên hệ admin để cấp Role (vd. Azure Kubernetes Cluster User).");
        }
        catch (Exception ex) when (IsConfigMissing(ex))
        {
            await DisconnectAsync().ConfigureAwait(false);
            return (false, "Không có kubeconfig / cluster chưa chạy. Kiểm tra Docker Desktop K8s hoặc file %USERPROFILE%\\.kube\\config.");
        }
        catch (Exception ex)
        {
            await DisconnectAsync().ConfigureAwait(false);
            return (false, $"Kết nối K8s thất bại: {FormatK8sError(ex)}");
        }
    }

    private static async Task<(bool Ok, string Message, bool LimitedRbac)> VerifyClusterAccessAsync(
        IKubernetes client,
        CancellationToken ct)
    {
        try
        {
            _ = await client.Version.GetCodeAsync(cancellationToken: ct).ConfigureAwait(false);
        }
        catch (k8s.Autorest.HttpOperationException ex) when (ex.Response?.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            return (false, "Kubeconfig không hợp lệ hoặc hết hạn (401). Chạy lại az login.", false);
        }
        catch (Exception ex)
        {
            return (false, FormatK8sError(ex), false);
        }

        try
        {
            await client.CoreV1.ListNamespaceAsync(limit: 1, cancellationToken: ct).ConfigureAwait(false);
            return (true, "", false);
        }
        catch (k8s.Autorest.HttpOperationException ex) when (ex.Response?.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            // Lens-style: user đã login nhưng không có cluster-admin — vẫn cho kết nối
            return (true, "", true);
        }
    }

    private static string FormatK8sError(Exception ex)
    {
        if (ex is not k8s.Autorest.HttpOperationException http)
            return ex.Message;

        var status = (int?)http.Response?.StatusCode;
        var body = http.Response?.Content;
        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("message", out var msg))
                    return $"HTTP {status}: {msg.GetString()}";
            }
            catch
            {
                // ignore parse errors
            }

            if (body.Length > 400)
                body = body[..400] + "…";
            return $"HTTP {status}: {body}";
        }

        return $"HTTP {status}: {http.Message}";
    }

    public void UpdateConfiguredNamespaces(IEnumerable<string> namespaces)
    {
        _configuredNamespaces = namespaces
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<IReadOnlyList<K8sPodDto>> GetPodsAsync(CancellationToken ct = default)
    {
        var client = RequireClient();

        if (_noWorkloadAccess)
            return [];

        LastInfo = null;

        try
        {
            var list = await client.CoreV1.ListPodForAllNamespacesAsync(cancellationToken: ct).ConfigureAwait(false);
            return list.Items
                .OrderBy(p => p.Metadata?.NamespaceProperty)
                .ThenBy(p => p.Metadata?.Name)
                .Select(MapPod)
                .ToList();
        }
        catch (k8s.Autorest.HttpOperationException ex) when (ex.Response?.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            _limitedRbac = true;
        }

        var namespaces = await K8sNamespaceDiscovery.DiscoverAsync(
            client,
            _kubeConfigPath ?? K8sClusterStore.GetDefaultKubeConfigPath(),
            _activeContext,
            _configuredNamespaces,
            ct).ConfigureAwait(false);

        if (namespaces.Count == 0)
        {
            LastInfo ??=
                "Không tìm thấy namespace nào có quyền xem Pods.\n" +
                "Nếu cluster AKS (dev-qa-common): cần admin cấp quyền Kubernetes.\n" +
                "Thử cluster demo-k8s (Lens) — có namespace trong kubeconfig.";
            return [];
        }

        var pods = new List<K8sPodDto>();
        foreach (var ns in namespaces)
        {
            try
            {
                var list = await client.CoreV1.ListNamespacedPodAsync(ns, cancellationToken: ct).ConfigureAwait(false);
                pods.AddRange(list.Items.Select(MapPod));
            }
            catch (k8s.Autorest.HttpOperationException ex) when (ex.Response?.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                // skip
            }
        }

        if (pods.Count == 0)
        {
            LastInfo =
                $"Đã thử {namespaces.Count} namespace ({string.Join(", ", namespaces)}) nhưng không có Pod hoặc thiếu quyền.";
        }

        return pods
            .OrderBy(p => p.Namespace)
            .ThenBy(p => p.Name)
            .ToList();
    }

    public async Task<IReadOnlyList<K8sNodeDto>> GetNodesAsync(CancellationToken ct = default)
    {
        var client = RequireClient();
        try
        {
            var list = await client.CoreV1.ListNodeAsync(cancellationToken: ct).ConfigureAwait(false);
            return list.Items
                .OrderBy(n => n.Metadata?.Name)
                .Select(MapNode)
                .ToList();
        }
        catch (k8s.Autorest.HttpOperationException ex) when (ex.Response?.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            LastInfo = "Nodes cần quyền cluster — tài khoản hạn chế thường không xem được (Lens cũng vậy).";
            return [];
        }
    }

    public async Task<IReadOnlyList<K8sDeploymentDto>> GetDeploymentsAsync(CancellationToken ct = default)
    {
        var client = RequireClient();

        try
        {
            var list = await client.AppsV1.ListDeploymentForAllNamespacesAsync(cancellationToken: ct).ConfigureAwait(false);
            return list.Items
                .OrderBy(d => d.Metadata?.NamespaceProperty)
                .ThenBy(d => d.Metadata?.Name)
                .Select(MapDeployment)
                .ToList();
        }
        catch (k8s.Autorest.HttpOperationException ex) when (ex.Response?.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            _limitedRbac = true;
        }

        var namespaces = await K8sNamespaceDiscovery.DiscoverAsync(
            client,
            _kubeConfigPath ?? K8sClusterStore.GetDefaultKubeConfigPath(),
            _activeContext,
            _configuredNamespaces,
            ct).ConfigureAwait(false);

        var deps = new List<K8sDeploymentDto>();
        foreach (var ns in namespaces)
        {
            try
            {
                var list = await client.AppsV1.ListNamespacedDeploymentAsync(ns, cancellationToken: ct).ConfigureAwait(false);
                deps.AddRange(list.Items.Select(MapDeployment));
            }
            catch (k8s.Autorest.HttpOperationException ex) when (ex.Response?.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                // skip
            }
        }

        return deps
            .OrderBy(d => d.Namespace)
            .ThenBy(d => d.Name)
            .ToList();
    }

    public async Task<string> GetPodLogsAsync(string name, string ns, CancellationToken ct = default)
    {
        var client = RequireClient();
        try
        {
            await using var stream = await client.CoreV1.ReadNamespacedPodLogAsync(
                name,
                ns,
                tailLines: 500,
                cancellationToken: ct).ConfigureAwait(false);
            using var reader = new StreamReader(stream);
            return await reader.ReadToEndAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return $"[Lỗi đọc log] {ex.Message}";
        }
    }

    public async Task DeletePodAsync(string name, string ns, CancellationToken ct = default)
    {
        var client = RequireClient();
        await client.CoreV1.DeleteNamespacedPodAsync(
            name,
            ns,
            cancellationToken: ct).ConfigureAwait(false);
    }

    public async Task<string> GetPodYamlAsync(string name, string ns, CancellationToken ct = default)
    {
        var client = RequireClient();
        var pod = await client.CoreV1.ReadNamespacedPodAsync(name, ns, cancellationToken: ct).ConfigureAwait(false);
        return KubernetesYaml.Serialize(pod);
    }

    public async Task ApplyPodYamlAsync(string yaml, CancellationToken ct = default)
    {
        var client = RequireClient();
        var pod = KubernetesYaml.Deserialize<V1Pod>(yaml)
            ?? throw new InvalidOperationException("YAML không hợp lệ.");

        var name = pod.Metadata?.Name;
        var ns = pod.Metadata?.NamespaceProperty;
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(ns))
            throw new InvalidOperationException("Pod YAML cần metadata.name và metadata.namespace.");

        await client.CoreV1.ReplaceNamespacedPodAsync(
            pod,
            name,
            ns,
            cancellationToken: ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<string>> GetNamespacesAsync(CancellationToken ct = default)
    {
        var client = RequireClient();

        try
        {
            var list = await client.CoreV1.ListNamespaceAsync(cancellationToken: ct).ConfigureAwait(false);
            return list.Items
                .Select(n => n.Metadata?.Name)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Cast<string>()
                .OrderBy(n => n)
                .ToList();
        }
        catch (k8s.Autorest.HttpOperationException ex) when (ex.Response?.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            var pods = await GetPodsAsync(ct).ConfigureAwait(false);
            return pods.Select(p => p.Namespace)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n)
                .ToList();
        }
    }

    public async Task<K8sOverviewDto> GetOverviewAsync(CancellationToken ct = default)
    {
        var client = RequireClient();
        var version = "";
        try
        {
            var v = await client.Version.GetCodeAsync(cancellationToken: ct).ConfigureAwait(false);
            version = $"{v.Major}.{v.Minor}";
        }
        catch
        {
            // ignore
        }

        var pods = await GetPodsAsync(ct).ConfigureAwait(false);
        var nodes = await GetNodesAsync(ct).ConfigureAwait(false);
        var deps = await GetDeploymentsAsync(ct).ConfigureAwait(false);
        var ns = await GetNamespacesAsync(ct).ConfigureAwait(false);

        return new K8sOverviewDto
        {
            Version = version,
            PodCount = pods.Count,
            NodeCount = nodes.Count,
            DeploymentCount = deps.Count,
            NamespaceCount = ns.Count,
            ClusterName = ConnectedClusterName ?? "",
            Context = ConnectedContext ?? ""
        };
    }

    public Task<IReadOnlyList<K8sListItemDto>> GetStatefulSetsAsync(CancellationToken ct = default) =>
        ListNamespacedAsync(
            async (c, token) => (await c.AppsV1.ListStatefulSetForAllNamespacesAsync(cancellationToken: token).ConfigureAwait(false)).Items,
            async (c, ns, token) => (await c.AppsV1.ListNamespacedStatefulSetAsync(ns, cancellationToken: token).ConfigureAwait(false)).Items,
            MapStatefulSet,
            ct);

    public Task<IReadOnlyList<K8sListItemDto>> GetDaemonSetsAsync(CancellationToken ct = default) =>
        ListNamespacedAsync(
            async (c, token) => (await c.AppsV1.ListDaemonSetForAllNamespacesAsync(cancellationToken: token).ConfigureAwait(false)).Items,
            async (c, ns, token) => (await c.AppsV1.ListNamespacedDaemonSetAsync(ns, cancellationToken: token).ConfigureAwait(false)).Items,
            MapDaemonSet,
            ct);

    public Task<IReadOnlyList<K8sListItemDto>> GetJobsAsync(CancellationToken ct = default) =>
        ListNamespacedAsync(
            async (c, token) => (await c.BatchV1.ListJobForAllNamespacesAsync(cancellationToken: token).ConfigureAwait(false)).Items,
            async (c, ns, token) => (await c.BatchV1.ListNamespacedJobAsync(ns, cancellationToken: token).ConfigureAwait(false)).Items,
            MapJob,
            ct);

    public Task<IReadOnlyList<K8sListItemDto>> GetCronJobsAsync(CancellationToken ct = default) =>
        ListNamespacedAsync(
            async (c, token) => (await c.BatchV1.ListCronJobForAllNamespacesAsync(cancellationToken: token).ConfigureAwait(false)).Items,
            async (c, ns, token) => (await c.BatchV1.ListNamespacedCronJobAsync(ns, cancellationToken: token).ConfigureAwait(false)).Items,
            MapCronJob,
            ct);

    public Task<IReadOnlyList<K8sListItemDto>> GetConfigMapsAsync(CancellationToken ct = default) =>
        ListNamespacedAsync(
            async (c, token) => (await c.CoreV1.ListConfigMapForAllNamespacesAsync(cancellationToken: token).ConfigureAwait(false)).Items,
            async (c, ns, token) => (await c.CoreV1.ListNamespacedConfigMapAsync(ns, cancellationToken: token).ConfigureAwait(false)).Items,
            MapConfigMap,
            ct);

    public Task<IReadOnlyList<K8sListItemDto>> GetSecretsAsync(CancellationToken ct = default) =>
        ListNamespacedAsync(
            async (c, token) => (await c.CoreV1.ListSecretForAllNamespacesAsync(cancellationToken: token).ConfigureAwait(false)).Items,
            async (c, ns, token) => (await c.CoreV1.ListNamespacedSecretAsync(ns, cancellationToken: token).ConfigureAwait(false)).Items,
            MapSecret,
            ct);

    public Task<IReadOnlyList<K8sListItemDto>> GetServicesAsync(CancellationToken ct = default) =>
        ListNamespacedAsync(
            async (c, token) => (await c.CoreV1.ListServiceForAllNamespacesAsync(cancellationToken: token).ConfigureAwait(false)).Items,
            async (c, ns, token) => (await c.CoreV1.ListNamespacedServiceAsync(ns, cancellationToken: token).ConfigureAwait(false)).Items,
            MapService,
            ct);

    public Task<IReadOnlyList<K8sListItemDto>> GetIngressesAsync(CancellationToken ct = default) =>
        ListNamespacedAsync(
            async (c, token) => (await c.NetworkingV1.ListIngressForAllNamespacesAsync(cancellationToken: token).ConfigureAwait(false)).Items,
            async (c, ns, token) => (await c.NetworkingV1.ListNamespacedIngressAsync(ns, cancellationToken: token).ConfigureAwait(false)).Items,
            MapIngress,
            ct);

    public Task<IReadOnlyList<K8sListItemDto>> GetPersistentVolumeClaimsAsync(CancellationToken ct = default) =>
        ListNamespacedAsync(
            async (c, token) => (await c.CoreV1.ListPersistentVolumeClaimForAllNamespacesAsync(cancellationToken: token).ConfigureAwait(false)).Items,
            async (c, ns, token) => (await c.CoreV1.ListNamespacedPersistentVolumeClaimAsync(ns, cancellationToken: token).ConfigureAwait(false)).Items,
            MapPvc,
            ct);

    public async Task<IReadOnlyList<K8sListItemDto>> GetPersistentVolumesAsync(CancellationToken ct = default)
    {
        var client = RequireClient();
        try
        {
            var list = await client.CoreV1.ListPersistentVolumeAsync(cancellationToken: ct).ConfigureAwait(false);
            return list.Items.Select(MapPv).OrderBy(p => p.Name).ToList();
        }
        catch (k8s.Autorest.HttpOperationException ex) when (ex.Response?.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            LastInfo = "Không có quyền xem Persistent Volumes.";
            return [];
        }
    }

    public async Task<IReadOnlyList<K8sListItemDto>> GetStorageClassesAsync(CancellationToken ct = default)
    {
        var client = RequireClient();
        try
        {
            var list = await client.StorageV1.ListStorageClassAsync(cancellationToken: ct).ConfigureAwait(false);
            return list.Items.Select(MapStorageClass).OrderBy(s => s.Name).ToList();
        }
        catch (k8s.Autorest.HttpOperationException ex) when (ex.Response?.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            LastInfo = "Không có quyền xem Storage Classes.";
            return [];
        }
    }

    public async Task<IReadOnlyList<K8sListItemDto>> GetNamespaceResourcesAsync(CancellationToken ct = default)
    {
        var names = await GetNamespacesAsync(ct).ConfigureAwait(false);
        return names.Select(n => new K8sListItemDto { Name = n, Status = "Active", Age = "-" }).ToList();
    }

    public async Task<IReadOnlyList<K8sListItemDto>> GetEventsAsync(CancellationToken ct = default)
    {
        var client = RequireClient();
        try
        {
            var list = await client.CoreV1.ListEventForAllNamespacesAsync(cancellationToken: ct).ConfigureAwait(false);
            return list.Items
                .OrderByDescending(e => e.LastTimestamp ?? e.EventTime ?? e.Metadata?.CreationTimestamp)
                .Take(200)
                .Select(MapEvent)
                .ToList();
        }
        catch (k8s.Autorest.HttpOperationException ex) when (ex.Response?.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            LastInfo = "Không có quyền xem Events.";
            return [];
        }
    }

    public Task<IReadOnlyList<K8sListItemDto>> GetServiceAccountsAsync(CancellationToken ct = default) =>
        ListNamespacedAsync(
            async (c, token) => (await c.CoreV1.ListServiceAccountForAllNamespacesAsync(cancellationToken: token).ConfigureAwait(false)).Items,
            async (c, ns, token) => (await c.CoreV1.ListNamespacedServiceAccountAsync(ns, cancellationToken: token).ConfigureAwait(false)).Items,
            MapServiceAccount,
            ct);

    public Task<IReadOnlyList<K8sListItemDto>> GetRolesAsync(CancellationToken ct = default) =>
        ListNamespacedAsync(
            async (c, token) => (await c.RbacAuthorizationV1.ListRoleForAllNamespacesAsync(cancellationToken: token).ConfigureAwait(false)).Items,
            async (c, ns, token) => (await c.RbacAuthorizationV1.ListNamespacedRoleAsync(ns, cancellationToken: token).ConfigureAwait(false)).Items,
            MapRole,
            ct);

    public Task<IReadOnlyList<K8sListItemDto>> GetRoleBindingsAsync(CancellationToken ct = default) =>
        ListNamespacedAsync(
            async (c, token) => (await c.RbacAuthorizationV1.ListRoleBindingForAllNamespacesAsync(cancellationToken: token).ConfigureAwait(false)).Items,
            async (c, ns, token) => (await c.RbacAuthorizationV1.ListNamespacedRoleBindingAsync(ns, cancellationToken: token).ConfigureAwait(false)).Items,
            MapRoleBinding,
            ct);

    public async Task<IReadOnlyList<K8sListItemDto>> GetClusterRolesAsync(CancellationToken ct = default)
    {
        var client = RequireClient();
        try
        {
            var list = await client.RbacAuthorizationV1.ListClusterRoleAsync(cancellationToken: ct).ConfigureAwait(false);
            return list.Items.Select(MapClusterRole).OrderBy(r => r.Name).ToList();
        }
        catch (k8s.Autorest.HttpOperationException ex) when (ex.Response?.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            LastInfo = "Không có quyền xem Cluster Roles.";
            return [];
        }
    }

    public async Task<IReadOnlyList<K8sListItemDto>> GetClusterRoleBindingsAsync(CancellationToken ct = default)
    {
        var client = RequireClient();
        try
        {
            var list = await client.RbacAuthorizationV1.ListClusterRoleBindingAsync(cancellationToken: ct).ConfigureAwait(false);
            return list.Items.Select(MapClusterRoleBinding).OrderBy(r => r.Name).ToList();
        }
        catch (k8s.Autorest.HttpOperationException ex) when (ex.Response?.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            LastInfo = "Không có quyền xem Cluster Role Bindings.";
            return [];
        }
    }

    public async Task<IReadOnlyList<K8sListItemDto>> GetCustomResourceDefinitionsAsync(CancellationToken ct = default)
    {
        var client = RequireClient();
        try
        {
            var list = await client.ApiextensionsV1.ListCustomResourceDefinitionAsync(cancellationToken: ct).ConfigureAwait(false);
            return list.Items.Select(MapCrd).OrderBy(c => c.Name).ToList();
        }
        catch (k8s.Autorest.HttpOperationException ex) when (ex.Response?.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            LastInfo = "Không có quyền xem Custom Resources.";
            return [];
        }
    }

    private async Task<IReadOnlyList<K8sListItemDto>> ListNamespacedAsync<TItem>(
        Func<IKubernetes, CancellationToken, Task<IList<TItem>>> listAll,
        Func<IKubernetes, string, CancellationToken, Task<IList<TItem>>> listInNs,
        Func<TItem, K8sListItemDto> map,
        CancellationToken ct)
    {
        var client = RequireClient();

        if (_noWorkloadAccess)
            return [];

        try
        {
            var items = await listAll(client, ct).ConfigureAwait(false);
            return items
                .Select(map)
                .OrderBy(i => i.Namespace)
                .ThenBy(i => i.Name)
                .ToList();
        }
        catch (k8s.Autorest.HttpOperationException ex) when (ex.Response?.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            _limitedRbac = true;
        }

        var namespaces = await K8sNamespaceDiscovery.DiscoverAsync(
            client,
            _kubeConfigPath ?? K8sClusterStore.GetDefaultKubeConfigPath(),
            _activeContext,
            _configuredNamespaces,
            ct).ConfigureAwait(false);

        var result = new List<K8sListItemDto>();
        foreach (var ns in namespaces)
        {
            try
            {
                var items = await listInNs(client, ns, ct).ConfigureAwait(false);
                result.AddRange(items.Select(map));
            }
            catch (k8s.Autorest.HttpOperationException ex) when (ex.Response?.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                // skip namespace
            }
        }

        return result
            .OrderBy(i => i.Namespace)
            .ThenBy(i => i.Name)
            .ToList();
    }

    public string? GetActiveContext() => _activeContext;

    public async Task DisconnectAsync()
    {
        if (_client != null)
        {
            _client.Dispose();
            _client = null;
        }

        ConnectedClusterName = null;
        ConnectedContext = null;
        ConnectedClusterId = null;
        _limitedRbac = false;
        _noWorkloadAccess = false;
        _kubeConfigPath = null;
        _activeContext = null;
        _clusterId = null;
        _configuredNamespaces = [];

        await Task.CompletedTask.ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;
        await DisconnectAsync().ConfigureAwait(false);
    }

    private IKubernetes RequireClient() =>
        _client ?? throw new InvalidOperationException("Chưa kết nối Kubernetes.");

    private static bool IsConfigMissing(Exception ex) =>
        ex.Message.Contains("kubeconfig", StringComparison.OrdinalIgnoreCase) ||
        ex.Message.Contains("Unable to load", StringComparison.OrdinalIgnoreCase) ||
        ex.InnerException?.Message.Contains("kubeconfig", StringComparison.OrdinalIgnoreCase) == true;

    private static K8sPodDto MapPod(V1Pod pod)
    {
        var meta = pod.Metadata;
        var status = pod.Status;
        var restarts = status?.ContainerStatuses?.Sum(c => c.RestartCount) ?? 0;
        var phase = status?.Phase ?? "Unknown";

        if (status?.ContainerStatuses?.Any(c =>
                string.Equals(c.State?.Waiting?.Reason, "CrashLoopBackOff", StringComparison.OrdinalIgnoreCase)) == true)
        {
            phase = "Failed";
        }

        return new K8sPodDto
        {
            Name = meta?.Name ?? "",
            Namespace = meta?.NamespaceProperty ?? "",
            Status = phase,
            RestartCount = restarts,
            Age = FormatAge(meta?.CreationTimestamp),
            Container = pod.Spec?.Containers?.FirstOrDefault()?.Name ?? ""
        };
    }

    private static K8sNodeDto MapNode(V1Node node)
    {
        var ready = node.Status?.Conditions?
            .FirstOrDefault(c => c.Type == "Ready")?.Status == "True";
        var roles = node.Metadata?.Labels?
            .Where(kv => kv.Key.StartsWith("node-role.kubernetes.io/", StringComparison.Ordinal))
            .Select(kv => kv.Key.Replace("node-role.kubernetes.io/", ""))
            .ToList() ?? [];

        return new K8sNodeDto
        {
            Name = node.Metadata?.Name ?? "",
            Status = ready == true ? "Ready" : "NotReady",
            Roles = roles.Count > 0 ? string.Join(", ", roles) : "worker",
            Age = FormatAge(node.Metadata?.CreationTimestamp)
        };
    }

    private static K8sDeploymentDto MapDeployment(V1Deployment dep)
    {
        var ready = dep.Status?.ReadyReplicas ?? 0;
        var desired = dep.Status?.Replicas ?? dep.Spec?.Replicas ?? 0;

        return new K8sDeploymentDto
        {
            Name = dep.Metadata?.Name ?? "",
            Namespace = dep.Metadata?.NamespaceProperty ?? "",
            Ready = $"{ready}/{desired}",
            Age = FormatAge(dep.Metadata?.CreationTimestamp)
        };
    }

    private static K8sListItemDto MapStatefulSet(V1StatefulSet s) => new()
    {
        Name = s.Metadata?.Name ?? "",
        Namespace = s.Metadata?.NamespaceProperty ?? "",
        Status = $"{s.Status?.ReadyReplicas ?? 0}/{s.Status?.Replicas ?? s.Spec?.Replicas ?? 0}",
        Age = FormatAge(s.Metadata?.CreationTimestamp),
        Detail = "StatefulSet"
    };

    private static K8sListItemDto MapDaemonSet(V1DaemonSet d) => new()
    {
        Name = d.Metadata?.Name ?? "",
        Namespace = d.Metadata?.NamespaceProperty ?? "",
        Status = $"{d.Status?.NumberReady ?? 0}/{d.Status?.DesiredNumberScheduled ?? 0}",
        Age = FormatAge(d.Metadata?.CreationTimestamp),
        Detail = "DaemonSet"
    };

    private static K8sListItemDto MapJob(V1Job j) => new()
    {
        Name = j.Metadata?.Name ?? "",
        Namespace = j.Metadata?.NamespaceProperty ?? "",
        Status = j.Status?.Succeeded > 0 ? "Complete" : (j.Status?.Active > 0 ? "Active" : "Pending"),
        Age = FormatAge(j.Metadata?.CreationTimestamp),
        Detail = $"{j.Status?.Succeeded ?? 0}/{j.Spec?.Completions ?? 1}"
    };

    private static K8sListItemDto MapCronJob(V1CronJob c) => new()
    {
        Name = c.Metadata?.Name ?? "",
        Namespace = c.Metadata?.NamespaceProperty ?? "",
        Status = c.Spec?.Suspend == true ? "Suspended" : "Active",
        Age = FormatAge(c.Metadata?.CreationTimestamp),
        Detail = c.Spec?.Schedule ?? ""
    };

    private static K8sListItemDto MapConfigMap(V1ConfigMap cm) => new()
    {
        Name = cm.Metadata?.Name ?? "",
        Namespace = cm.Metadata?.NamespaceProperty ?? "",
        Status = "—",
        Age = FormatAge(cm.Metadata?.CreationTimestamp),
        Detail = $"{cm.Data?.Count ?? 0} keys"
    };

    private static K8sListItemDto MapSecret(V1Secret s) => new()
    {
        Name = s.Metadata?.Name ?? "",
        Namespace = s.Metadata?.NamespaceProperty ?? "",
        Status = s.Type ?? "Opaque",
        Age = FormatAge(s.Metadata?.CreationTimestamp),
        Detail = $"{s.Data?.Count ?? 0} keys"
    };

    private static K8sListItemDto MapService(V1Service s) => new()
    {
        Name = s.Metadata?.Name ?? "",
        Namespace = s.Metadata?.NamespaceProperty ?? "",
        Status = s.Spec?.Type ?? "ClusterIP",
        Age = FormatAge(s.Metadata?.CreationTimestamp),
        Detail = s.Spec?.ClusterIP ?? ""
    };

    private static K8sListItemDto MapIngress(V1Ingress i) => new()
    {
        Name = i.Metadata?.Name ?? "",
        Namespace = i.Metadata?.NamespaceProperty ?? "",
        Status = i.Spec?.IngressClassName ?? "default",
        Age = FormatAge(i.Metadata?.CreationTimestamp),
        Detail = string.Join(", ", i.Spec?.Rules?.Select(r => r.Host).Where(h => !string.IsNullOrWhiteSpace(h)) ?? [])
    };

    private static K8sListItemDto MapPvc(V1PersistentVolumeClaim p) => new()
    {
        Name = p.Metadata?.Name ?? "",
        Namespace = p.Metadata?.NamespaceProperty ?? "",
        Status = p.Status?.Phase ?? "Unknown",
        Age = FormatAge(p.Metadata?.CreationTimestamp),
        Detail = p.Spec?.Resources?.Requests?.TryGetValue("storage", out var q) == true ? q.ToString() : ""
    };

    private static K8sListItemDto MapPv(V1PersistentVolume p) => new()
    {
        Name = p.Metadata?.Name ?? "",
        Status = p.Status?.Phase ?? "Unknown",
        Age = FormatAge(p.Metadata?.CreationTimestamp),
        Detail = p.Spec?.Capacity?.TryGetValue("storage", out var q) == true ? q.ToString() : ""
    };

    private static K8sListItemDto MapStorageClass(V1StorageClass s) => new()
    {
        Name = s.Metadata?.Name ?? "",
        Status = s.Provisioner ?? "",
        Age = FormatAge(s.Metadata?.CreationTimestamp),
        Detail = s.VolumeBindingMode ?? ""
    };

    private static K8sListItemDto MapEvent(Corev1Event e) => new()
    {
        Name = e.InvolvedObject?.Name ?? e.Metadata?.Name ?? "",
        Namespace = e.InvolvedObject?.NamespaceProperty ?? e.Metadata?.NamespaceProperty ?? "",
        Status = e.Type ?? "Normal",
        Age = FormatAge(e.LastTimestamp ?? e.EventTime ?? e.Metadata?.CreationTimestamp),
        Detail = $"{e.Reason}: {(e.Message?.Length > 80 ? e.Message[..80] + "…" : e.Message)}"
    };

    private static K8sListItemDto MapServiceAccount(V1ServiceAccount sa) => new()
    {
        Name = sa.Metadata?.Name ?? "",
        Namespace = sa.Metadata?.NamespaceProperty ?? "",
        Status = "—",
        Age = FormatAge(sa.Metadata?.CreationTimestamp),
        Detail = $"{sa.Secrets?.Count ?? 0} secrets"
    };

    private static K8sListItemDto MapRole(V1Role r) => new()
    {
        Name = r.Metadata?.Name ?? "",
        Namespace = r.Metadata?.NamespaceProperty ?? "",
        Status = "—",
        Age = FormatAge(r.Metadata?.CreationTimestamp),
        Detail = $"{r.Rules?.Count ?? 0} rules"
    };

    private static K8sListItemDto MapRoleBinding(V1RoleBinding rb) => new()
    {
        Name = rb.Metadata?.Name ?? "",
        Namespace = rb.Metadata?.NamespaceProperty ?? "",
        Status = rb.RoleRef?.Kind ?? "",
        Age = FormatAge(rb.Metadata?.CreationTimestamp),
        Detail = rb.RoleRef?.Name ?? ""
    };

    private static K8sListItemDto MapClusterRole(V1ClusterRole r) => new()
    {
        Name = r.Metadata?.Name ?? "",
        Status = "—",
        Age = FormatAge(r.Metadata?.CreationTimestamp),
        Detail = $"{r.Rules?.Count ?? 0} rules"
    };

    private static K8sListItemDto MapClusterRoleBinding(V1ClusterRoleBinding rb) => new()
    {
        Name = rb.Metadata?.Name ?? "",
        Status = rb.RoleRef?.Kind ?? "",
        Age = FormatAge(rb.Metadata?.CreationTimestamp),
        Detail = rb.RoleRef?.Name ?? ""
    };

    private static K8sListItemDto MapCrd(V1CustomResourceDefinition c) => new()
    {
        Name = c.Metadata?.Name ?? "",
        Status = c.Spec?.Scope ?? "",
        Age = FormatAge(c.Metadata?.CreationTimestamp),
        Detail = c.Spec?.Group ?? ""
    };

    private static string FormatAge(DateTime? created)
    {
        if (created == null)
            return "-";

        var span = DateTime.UtcNow - created.Value.ToUniversalTime();
        if (span.TotalDays >= 1)
            return $"{(int)span.TotalDays}d";
        if (span.TotalHours >= 1)
            return $"{(int)span.TotalHours}h";
        if (span.TotalMinutes >= 1)
            return $"{(int)span.TotalMinutes}m";
        return $"{Math.Max(0, (int)span.TotalSeconds)}s";
    }
}
