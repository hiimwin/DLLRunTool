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

        foreach (var ns in hints)
        {
            if (await CanIAsync(client, "list", "pods", ns, ct).ConfigureAwait(false))
            {
                summary.CanListPodsInNamespaces = true;
                summary.AccessibleNamespaces.Add(ns);
            }
        }

        if (summary.HasWorkloadAccess)
        {
            summary.Message =
                "Quyền theo namespace — tool sẽ list pods trong: " +
                string.Join(", ", summary.AccessibleNamespaces);
            return summary;
        }

        summary.Message =
            "Tài khoản Azure đã login nhưng không có quyền xem Pods trên cluster này.\n" +
            "(kubectl cũng báo Forbidden: cannot list resource «pods»)\n\n" +
            "Đây là giới hạn AKS RBAC — không phải lỗi tool. Lens cũng chỉ hiện chấm xanh «đã kết nối».\n\n" +
            "Cần admin Azure cấp role, ví dụ:\n" +
            "• Azure Kubernetes Service RBAC Reader (trong namespace team)\n" +
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
