using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DLLRunTool.Services;

public sealed class UpdateCheckResult
{
    public string CurrentVersion { get; init; } = "";
    public string LatestVersion { get; init; } = "";
    public string DownloadUrl { get; init; } = "";
    public string ReleaseNotes { get; init; } = "";
    public string ReleasedAt { get; init; } = "";
    public bool IsUpdateAvailable { get; init; }
}

internal sealed class UpdateManifest
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

    [JsonPropertyName("downloadUrl")]
    public string DownloadUrl { get; set; } = "";

    [JsonPropertyName("releaseNotes")]
    public string ReleaseNotes { get; set; } = "";

    [JsonPropertyName("releasedAt")]
    public string ReleasedAt { get; set; } = "";
}

internal sealed class UpdateCheckConfig
{
    [JsonPropertyName("manifestUrl")]
    public string ManifestUrl { get; set; } = "";
}

public static class UpdateChecker
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(12) };

    public static async Task<UpdateCheckResult?> CheckAsync(CancellationToken ct = default)
    {
        var current = AppVersionInfo.Current;
        var manifestUrl = ResolveManifestUrl();
        if (string.IsNullOrWhiteSpace(manifestUrl) || manifestUrl.Contains("REPLACE_", StringComparison.OrdinalIgnoreCase))
            return null;

        try
        {
            var json = await Http.GetStringAsync(manifestUrl, ct).ConfigureAwait(false);
            var manifest = JsonSerializer.Deserialize<UpdateManifest>(json, JsonOptions);
            if (manifest == null || string.IsNullOrWhiteSpace(manifest.Version))
                return null;

            var isNewer = IsRemoteVersionNewer(current, manifest.Version);
            return new UpdateCheckResult
            {
                CurrentVersion = current,
                LatestVersion = manifest.Version.Trim(),
                DownloadUrl = manifest.DownloadUrl?.Trim() ?? "",
                ReleaseNotes = manifest.ReleaseNotes?.Trim() ?? "",
                ReleasedAt = manifest.ReleasedAt?.Trim() ?? "",
                IsUpdateAvailable = isNewer
            };
        }
        catch
        {
            return null;
        }
    }

    private static string? ResolveManifestUrl()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "update-check.config.json");
        if (!File.Exists(path))
            return null;

        try
        {
            var json = File.ReadAllText(path);
            var config = JsonSerializer.Deserialize<UpdateCheckConfig>(json, JsonOptions);
            return string.IsNullOrWhiteSpace(config?.ManifestUrl) ? null : config.ManifestUrl.Trim();
        }
        catch
        {
            return null;
        }
    }

    internal static bool IsRemoteVersionNewer(string current, string remote)
    {
        if (Version.TryParse(NormalizeVersion(current), out var currentVersion) &&
            Version.TryParse(NormalizeVersion(remote), out var remoteVersion))
        {
            return remoteVersion > currentVersion;
        }

        return !string.Equals(current.Trim(), remote.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeVersion(string value)
    {
        var parts = value.Trim().Split('.');
        return parts.Length switch
        {
            1 => $"{parts[0]}.0.0",
            2 => $"{parts[0]}.{parts[1]}.0",
            _ => value.Trim()
        };
    }
}
