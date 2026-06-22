using System.Net.Http;
using DLLRunTool.Models;

namespace DLLRunTool.Services;

public static class ServiceHealthChecker
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(4)
    };

    static ServiceHealthChecker()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("DLLRunTool/HealthCheck");
    }

    public static bool CanCheck(ServiceConfig service) =>
        !service.IsExe &&
        !string.IsNullOrWhiteSpace(service.Url);

    public static async Task<string> CheckAsync(ServiceConfig service, CancellationToken ct = default)
    {
        if (!CanCheck(service))
            return "unknown";

        if (service.ManagedProcess == null || service.ManagedProcess.HasExited)
            return "crashed";

        var baseUrl = service.Url.TrimEnd('/');
        var paths = new List<string>();
        if (!string.IsNullOrWhiteSpace(service.HealthPath))
            paths.Add(service.HealthPath.TrimStart('/'));
        paths.Add("health");
        paths.Add("health-status");
        paths.Add("");

        foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var url = string.IsNullOrEmpty(path) ? baseUrl : $"{baseUrl}/{path}";
            try
            {
                using var response = await Http.GetAsync(url, ct).ConfigureAwait(false);
                if ((int)response.StatusCode < 500)
                    return response.IsSuccessStatusCode ? "healthy" : "unhealthy";
            }
            catch (HttpRequestException)
            {
                // try next path
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested)
            {
                return "unhealthy";
            }
        }

        return service.IsRunning ? "starting" : "crashed";
    }
}
