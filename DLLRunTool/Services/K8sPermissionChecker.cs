using k8s;
using k8s.Models;

namespace DLLRunTool.Services;

internal static class K8sPermissionChecker
{
    public static async Task<K8sPermissionSummary> EvaluateAsync(
        IKubernetes client,
        string? kubeConfigPath = null,
        string? contextName = null,
        IReadOnlyList<string>? configuredNamespaces = null,
        CancellationToken ct = default)
    {
        var summary = new K8sPermissionSummary();

        summary.CanListNamespaces = await CanIAsync(client, "list", "namespaces", null, ct).ConfigureAwait(false);
        summary.CanListPodsClusterWide = await CanIAsync(client, "list", "pods", null, ct).ConfigureAwait(false);

        if (summary.CanListPodsClusterWide || summary.CanListNamespaces)
            return summary;

        var hints = K8sNamespaceDiscovery.CollectNamespaceHints(
            kubeConfigPath ?? K8sClusterStore.GetDefaultKubeConfigPath(),
            contextName,
            configuredNamespaces);

        var tasks = hints.Select(async ns =>
            await CanIAsync(client, "list", "pods", ns, ct).ConfigureAwait(false) ? ns : null).ToArray();
        var results = await Task.WhenAll(tasks).ConfigureAwait(false);

        foreach (var ns in results.Where(n => n != null).Cast<string>())
            summary.AccessibleNamespaces.Add(ns);

        if (summary.AccessibleNamespaces.Count > 0)
            summary.CanListPodsInNamespaces = true;

        if (summary.HasWorkloadAccess)
        {
            summary.Message =
                "Quyền theo namespace — tool sẽ list pods trong: " +
                string.Join(", ", summary.AccessibleNamespaces);
            return summary;
        }

        summary.Message =
            "Không tìm thấy namespace nào có quyền xem Pods (cluster-wide bị từ chối).\n" +
            "(kubectl: Forbidden khi list pods --all-namespaces)\n\n" +
            "Nếu Lens vẫn xem được: mở Cài đặt cluster → Accessible Namespaces → thêm namespace (vd. msg-qa) rồi Kết nối lại.\n\n" +
            "Hoặc cần admin Azure cấp role trong namespace team:\n" +
            "• Azure Kubernetes Service RBAC Reader\n" +
            "• hoặc Cluster User / Admin\n\n" +
            "Sau khi được cấp quyền: az aks get-credentials … rồi Kết nối lại.";
        return summary;
    }

    private static async Task<bool> CanIAsync(
        IKubernetes client,
        string verb,
        string resource,
        string? ns,
        CancellationToken ct)
    {
        try
        {
            var review = new V1SelfSubjectAccessReview
            {
                Spec = new V1SelfSubjectAccessReviewSpec
                {
                    ResourceAttributes = new V1ResourceAttributes
                    {
                        Verb = verb,
                        Resource = resource,
                        NamespaceProperty = ns
                    }
                }
            };

            var result = await client.AuthorizationV1
                .CreateSelfSubjectAccessReviewAsync(review, cancellationToken: ct)
                .ConfigureAwait(false);

            return result.Status?.Allowed == true;
        }
        catch
        {
            return false;
        }
    }
}

internal sealed class K8sPermissionSummary
{
    public bool CanListNamespaces { get; set; }
    public bool CanListPodsClusterWide { get; set; }
    public bool CanListPodsInNamespaces { get; set; }
    public List<string> AccessibleNamespaces { get; } = [];
    public string? Message { get; set; }
    public bool HasWorkloadAccess =>
        CanListPodsClusterWide || CanListNamespaces || CanListPodsInNamespaces;
    public bool NoWorkloadAccess => !HasWorkloadAccess;
}
