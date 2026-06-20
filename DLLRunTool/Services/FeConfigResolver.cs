using DLLRunTool.Models;

namespace DLLRunTool.Services;

/// <summary>
/// Gắn biến env.js FE với URL các service BE đã khai báo trong platform (AuthServer, WebGateway, …).
/// </summary>
public static class FeConfigResolver
{
    public static Dictionary<string, string> MergeTemplateAndFile(
        Dictionary<string, string> fromFile,
        Dictionary<string, string> fromTemplate)
    {
        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, _) in fromTemplate)
            merged[key] = fromFile.TryGetValue(key, out var v) ? v : "";

        foreach (var (key, value) in fromFile)
            merged[key] = value;

        return merged;
    }

    public static void ApplyDynamicBindings(
        Dictionary<string, string> envVars,
        IEnumerable<ServiceConfig> allServices,
        bool onlyIfEmpty = true)
    {
        foreach (var (key, url) in ResolveBindings(allServices))
        {
            if (string.IsNullOrWhiteSpace(url))
                continue;

            if (!onlyIfEmpty || !envVars.TryGetValue(key, out var existing) || string.IsNullOrWhiteSpace(existing))
                envVars[key] = url;
        }
    }

    public static IReadOnlyList<FeEnvBinding> DescribeBindings(IEnumerable<ServiceConfig> allServices)
    {
        var services = allServices.ToList();
        var bindings = ResolveBindings(services);
        var result = new List<FeEnvBinding>();

        foreach (var (envKey, url) in bindings)
        {
            if (string.IsNullOrWhiteSpace(url))
                continue;

            result.Add(new FeEnvBinding
            {
                EnvKey = envKey,
                Value = url,
                SourceService = FindSourceServiceName(services, envKey)
            });
        }

        return result;
    }

    private static string? FindSourceServiceName(IReadOnlyList<ServiceConfig> services, string envKey) =>
        envKey switch
        {
            "auth_url" => FindService(services, MatchAuthServer)?.Name,
            "api_url" => FindService(services, MatchWebGateway)?.Name,
            "base_url" => services.FirstOrDefault(s => s.IsFrontEnd)?.Name,
            _ => null
        };

    private static Dictionary<string, string> ResolveBindings(IEnumerable<ServiceConfig> services)
    {
        var list = services.ToList();
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var auth = FindService(list, MatchAuthServer);
        if (auth != null && !string.IsNullOrWhiteSpace(auth.Url))
            result["auth_url"] = NormalizeAuthUrl(auth.Url);

        var gateway = FindService(list, MatchWebGateway);
        if (gateway != null && !string.IsNullOrWhiteSpace(gateway.Url))
            result["api_url"] = TrimTrailingSlash(gateway.Url);

        var fe = list.FirstOrDefault(s => s.IsFrontEnd);
        if (fe != null && !string.IsNullOrWhiteSpace(fe.Url))
            result["base_url"] = TrimTrailingSlash(fe.Url);

        return result;
    }

    private static ServiceConfig? FindService(IEnumerable<ServiceConfig> services, Func<ServiceConfig, bool> predicate) =>
        services.FirstOrDefault(predicate);

    private static bool MatchAuthServer(ServiceConfig service) =>
        service.IsBackEnd &&
        (service.Id.Contains("authserver", StringComparison.OrdinalIgnoreCase) ||
         service.Name.Contains("AuthServer", StringComparison.OrdinalIgnoreCase));

    private static bool MatchWebGateway(ServiceConfig service) =>
        service.IsBackEnd &&
        (service.Id.Contains("webgateway", StringComparison.OrdinalIgnoreCase) ||
         service.Name.Equals("WebGateway", StringComparison.OrdinalIgnoreCase)) &&
        !service.Id.Contains("public", StringComparison.OrdinalIgnoreCase) &&
        !service.Name.Contains("Public", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeAuthUrl(string url) =>
        url.EndsWith('/') ? url : url + "/";

    private static string TrimTrailingSlash(string url) =>
        url.TrimEnd('/');
}
