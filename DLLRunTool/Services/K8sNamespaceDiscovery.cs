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
        var tasks = hints.Select(async ns =>
            await CanAccessNamespaceAsync(client, ns, ct).ConfigureAwait(false) ? ns : null).ToArray();
        var results = await Task.WhenAll(tasks).ConfigureAwait(false);

        foreach (var ns in results.Where(n => n != null).Cast<string>())
            accessible.Add(ns);

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

        foreach (var ns in K8sClusterStore.GetLensNamespaceHints())
            hints.Add(ns);

        if (!string.IsNullOrWhiteSpace(contextName))
        {
            foreach (var ns in K8sClusterStore.GetNamespacesForContextFromCatalog(contextName))
                hints.Add(ns);
            foreach (var ns in CollectAksNamespaceHeuristics(contextName))
                hints.Add(ns);
        }

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

    /// <summary>Gợi ý namespace AKS/Loyalty — không cần Lens trên máy mới.</summary>
    private static IEnumerable<string> CollectAksNamespaceHeuristics(string contextName)
    {
        var lower = contextName.ToLowerInvariant();
        if (lower.Contains("qa", StringComparison.Ordinal))
            yield return "msg-qa";
        if (lower.Contains("uat", StringComparison.Ordinal))
            yield return "vbb-uat";
        if (lower.Contains("dev", StringComparison.Ordinal))
            yield return "msg-dev";
        if (lower.Contains("prod", StringComparison.Ordinal))
            yield return "msg-prod";
    }

    private static async Task<bool> CanAccessNamespaceAsync(
        IKubernetes client,
        string ns,
        CancellationToken ct)
    {
        foreach (var resource in new[] { "pods", "services", "deployments" })
        {
            if (await CanIListInNamespaceAsync(client, ns, resource, ct).ConfigureAwait(false))
                return true;
        }

        return false;
    }

    private static async Task<bool> CanIListInNamespaceAsync(
        IKubernetes client,
        string ns,
        string resource,
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
                        Resource = resource,
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
            // fallback below
        }

        try
        {
            switch (resource)
            {
                case "pods":
                    await client.CoreV1.ListNamespacedPodAsync(ns, limit: 1, cancellationToken: ct).ConfigureAwait(false);
                    return true;
                case "services":
                    await client.CoreV1.ListNamespacedServiceAsync(ns, limit: 1, cancellationToken: ct).ConfigureAwait(false);
                    return true;
                case "deployments":
                    await client.AppsV1.ListNamespacedDeploymentAsync(ns, limit: 1, cancellationToken: ct).ConfigureAwait(false);
                    return true;
            }
        }
        catch (k8s.Autorest.HttpOperationException ex) when (ex.Response?.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            return false;
        }
        catch
        {
            return false;
        }

        return false;
    }
}
