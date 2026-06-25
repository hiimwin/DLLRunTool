using k8s;
using k8s.Models;

namespace DLLRunTool.Services;

/// <summary>
/// Lens không luôn gọi list cluster-wide — thường list theo từng namespace user được phép.
/// </summary>
internal static class K8sNamespaceDiscovery
{
    public static async Task<IReadOnlyList<string>> DiscoverAsync(
        IKubernetes client,
        string kubeConfigPath,
        string? contextName,
        IReadOnlyList<string>? configuredNamespaces,
        CancellationToken ct = default)
    {
        try
        {
            var all = await client.CoreV1.ListNamespaceAsync(cancellationToken: ct).ConfigureAwait(false);
            return all.Items
                .Select(n => n.Metadata?.Name)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Cast<string>()
                .OrderBy(n => n)
                .ToList();
        }
        catch (k8s.Autorest.HttpOperationException ex) when (ex.Response?.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            // fall through — quyền namespace-scoped
        }

        var hints = CollectNamespaceHints(kubeConfigPath, contextName, configuredNamespaces);
        var accessible = new List<string>();

        foreach (var ns in hints)
        {
            if (await CanListPodsInNamespaceAsync(client, ns, ct).ConfigureAwait(false))
                accessible.Add(ns);
        }

        return accessible.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n).ToList();
    }

    public static List<string> CollectNamespaceHints(
        string kubeConfigPath,
        string? contextName,
        IReadOnlyList<string>? configuredNamespaces)
    {
        var hints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (configuredNamespaces != null)
        {
            foreach (var ns in configuredNamespaces)
            {
                if (!string.IsNullOrWhiteSpace(ns))
                    hints.Add(ns.Trim());
            }
        }

        hints.Add("default");

        if (!File.Exists(kubeConfigPath))
            return hints.ToList();

        try
        {
            using var stream = File.OpenRead(kubeConfigPath);
            var kubeConfig = KubernetesClientConfiguration.LoadKubeConfig(stream);

            foreach (var ctx in kubeConfig.Contexts)
            {
                var ns = ctx.ContextDetails?.Namespace;
                if (!string.IsNullOrWhiteSpace(ns))
                    hints.Add(ns);
            }

            if (!string.IsNullOrWhiteSpace(contextName))
            {
                var current = kubeConfig.Contexts.FirstOrDefault(c =>
                    string.Equals(c.Name, contextName, StringComparison.OrdinalIgnoreCase));
                var currentNs = current?.ContextDetails?.Namespace;
                if (!string.IsNullOrWhiteSpace(currentNs))
                    hints.Add(currentNs);

                foreach (var part in contextName.Split('-', '_', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (part.Length >= 2)
                        hints.Add(part);
                }
            }
        }
        catch
        {
            // ignore parse errors
        }

        return hints.ToList();
    }

    private static async Task<bool> CanListPodsInNamespaceAsync(
        IKubernetes client,
        string ns,
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
                        Verb = "list",
                        Resource = "pods",
                        NamespaceProperty = ns
                    }
                }
            };

            var result = await client.AuthorizationV1
                .CreateSelfSubjectAccessReviewAsync(review, cancellationToken: ct)
                .ConfigureAwait(false);

            if (result.Status?.Allowed == true)
                return true;
        }
        catch
        {
            // fallback: thử list thật
        }

        try
        {
            await client.CoreV1.ListNamespacedPodAsync(ns, limit: 1, cancellationToken: ct).ConfigureAwait(false);
            return true;
        }
        catch (k8s.Autorest.HttpOperationException ex) when (ex.Response?.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            return false;
        }
        catch
        {
            return false;
        }
    }
}
