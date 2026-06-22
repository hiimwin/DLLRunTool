using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using DLLRunTool.Models;

namespace DLLRunTool.Services;

public static class ServiceHealthChecker
{
    /// <summary>Đợi trước mỗi lần GET health: 30s → 60s → 120s (check lúc ~30s, ~90s, ~210s).</summary>
    public static readonly int[] PollDelaysBeforeCheckMs = [30_000, 60_000, 120_000];

    public static int MaxPollAttempts => PollDelaysBeforeCheckMs.Length;

    private static readonly HttpClient Http = CreateHttpClient();

    static ServiceHealthChecker()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("DLLRunTool/HealthCheck");
    }

    public static bool CanCheck(ServiceConfig service) =>
        service.EnableHealthCheck &&
        !service.IsExe &&
        !string.IsNullOrWhiteSpace(service.Url);

    /// <summary>
    /// healthy | unhealthy | no-health (không có endpoint) | pending (chưa kết nối được / 5xx — thử lại).
    /// </summary>
    public static async Task<string> CheckAsync(ServiceConfig service, CancellationToken ct = default)
    {
        if (!CanCheck(service))
            return "unknown";

        if (service.ManagedProcess == null || service.ManagedProcess.HasExited)
            return "crashed";

        var baseUrl = service.Url.TrimEnd('/');
        var paths = BuildProbePaths(service);

        var sawResponse = false;
        var saw404Only = true;
        var sawUnhealthy = false;

        foreach (var path in paths)
        {
            var url = string.IsNullOrEmpty(path) ? baseUrl : $"{baseUrl}/{path}";
            try
            {
                using var response = await Http.GetAsync(url, ct).ConfigureAwait(false);
                sawResponse = true;
                var code = (int)response.StatusCode;

                if (code is >= 200 and < 300)
                    return "healthy";

                if (code == 404)
                    continue;

                saw404Only = false;

                if (code is >= 500)
                    continue;

                sawUnhealthy = true;
            }
            catch (HttpRequestException)
            {
                saw404Only = false;
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested)
            {
                saw404Only = false;
            }
        }

        if (sawUnhealthy)
            return "unhealthy";

        if (sawResponse && saw404Only)
            return "no-health";

        return "pending";
    }

    private static List<string> BuildProbePaths(ServiceConfig service)
    {
        var paths = new List<string>();

        if (!string.IsNullOrWhiteSpace(service.HealthPath))
            paths.Add(service.HealthPath.TrimStart('/'));

        if (service.IsFrontEnd)
        {
            // Angular/npm dev server: không có /health — chỉ cần URL gốc trả HTML.
            if (!paths.Contains("", StringComparer.OrdinalIgnoreCase))
                paths.Add("");
            return paths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        paths.Add("health");
        paths.Add("health-status");
        paths.Add("");
        return paths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static HttpClient CreateHttpClient()
    {
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = AcceptLocalDevCertificate
        };
        return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
    }

    /// <summary>localhost dev thường dùng cert self-signed (NET::ERR_CERT_AUTHORITY_INVALID trên browser).</summary>
    private static bool AcceptLocalDevCertificate(
        HttpRequestMessage message,
        X509Certificate2? certificate,
        X509Chain? chain,
        SslPolicyErrors errors)
    {
        if (errors == SslPolicyErrors.None)
            return true;

        return IsLocalDevHost(message.RequestUri?.Host);
    }

    private static bool IsLocalDevHost(string? host) =>
        !string.IsNullOrEmpty(host) &&
        (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
         host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
         host.Equals("::1", StringComparison.OrdinalIgnoreCase));
}
