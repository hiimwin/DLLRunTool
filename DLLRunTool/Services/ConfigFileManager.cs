using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using DLLRunTool.Models;

namespace DLLRunTool.Services;

public static class ConfigFileManager
{
    private static readonly JsonDocumentOptions JsonReadOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static readonly Regex EnvJsPattern = new(
        @"window\s*\[\s*""env""\s*\]\s*\[\s*""(?<key>[^""]+)""\s*\]\s*=\s*""(?<value>(?:\\.|[^""])*)""",
        RegexOptions.Compiled);

    public static async Task<ServiceUiConfig> ReadConfigAsync(ServiceConfig service, CancellationToken ct = default)
    {
        var result = new ServiceUiConfig();
        var configPath = service.ResolveConfigPath();

        if (string.IsNullOrEmpty(configPath) || !File.Exists(configPath))
        {
            ApplyUrlFallback(service.Url, result);
            return result;
        }

        if (service.IsFrontEnd)
        {
            var content = await File.ReadAllTextAsync(configPath, ct).ConfigureAwait(false);
            result.EnvVars = ParseEnvJs(content);
            if (result.EnvVars.TryGetValue("base_url", out var baseUrl))
                ParseUrlIntoConfig(baseUrl, result);
            return result;
        }

        var json = await File.ReadAllTextAsync(configPath, ct).ConfigureAwait(false);
        var node = ParseJsonObject(json);
        if (node == null)
        {
            ApplyUrlFallback(service.Url, result);
            return result;
        }

        var selfUrl = node["App"]?["SelfUrl"]?.GetValue<string>()
                      ?? node["Kestrel"]?["Endpoints"]?["Https"]?["Url"]?.GetValue<string>()
                      ?? service.Url;

        var launchPath = service.ResolveSourceConfigPath("launchSettings.json");
        if (string.IsNullOrWhiteSpace(selfUrl) && launchPath != null)
            selfUrl = ReadApplicationUrlFromLaunchSettings(launchPath) ?? service.Url;

        ParseUrlIntoConfig(selfUrl, result);

        if (node["ConnectionStrings"] is JsonObject connStrings)
        {
            var first = connStrings.FirstOrDefault();
            if (first.Key != null && first.Value != null)
                result.ConnectionString = first.Value.GetValue<string>() ?? "";
        }

        return result;
    }

    public static async Task SaveConfigAsync(ServiceConfig service, ServiceUiConfig config, CancellationToken ct = default)
    {
        var configPath = service.ResolveConfigPath();
        if (string.IsNullOrEmpty(configPath))
            throw new InvalidOperationException("Không tìm thấy file cấu hình cho service này.");

        var url = BuildUrl(config.Scheme, config.Host, config.Port);
        service.Url = url;

        if (service.IsFrontEnd)
        {
            await SaveEnvJsAsync(configPath, config.EnvVars ?? new Dictionary<string, string>(), ct).ConfigureAwait(false);
            return;
        }

        await SaveAppSettingsAsync(configPath, url, config.ConnectionString, ct).ConfigureAwait(false);

        foreach (var (key, path) in service.GetSourceConfigFiles())
        {
            if (!key.StartsWith("appsettings", StringComparison.OrdinalIgnoreCase))
                continue;
            if (path.Equals(configPath, StringComparison.OrdinalIgnoreCase))
                continue;
            if (File.Exists(path))
                await SaveAppSettingsAsync(path, url, config.ConnectionString, ct).ConfigureAwait(false);
        }

        var launchPath = service.ResolveSourceConfigPath("launchSettings.json");
        if (!string.IsNullOrEmpty(launchPath) && File.Exists(launchPath))
            await SaveLaunchSettingsUrlAsync(launchPath, url, ct).ConfigureAwait(false);

        await SyncSourceConfigToOutputAsync(service, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Copy appsettings, ocelot*, launchSettings từ source project sang thư mục bin nơi dotnet exec chạy.
    /// </summary>
    public static async Task<int> SyncSourceConfigToOutputAsync(ServiceConfig service, CancellationToken ct = default)
    {
        if (service.IsExe || service.IsFrontEnd)
            return 0;

        service.SyncFolderPathFromDisk();
        var outputDir = service.ResolveRunWorkingDirectory();
        if (string.IsNullOrWhiteSpace(outputDir) || !Directory.Exists(outputDir))
            return 0;

        var copied = 0;
        foreach (var (key, sourcePath) in service.GetSourceConfigFiles())
        {
            if (string.Equals(key, "launchSettings.json", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!File.Exists(sourcePath))
                continue;

            var destPath = Path.Combine(outputDir, key);
            var content = await File.ReadAllTextAsync(sourcePath, ct).ConfigureAwait(false);
            await File.WriteAllTextAsync(destPath, content, ct).ConfigureAwait(false);
            copied++;
        }

        return copied;
    }

    private static async Task SaveLaunchSettingsUrlAsync(string path, string url, CancellationToken ct)
    {
        var json = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        var node = ParseJsonObject(json)
                   ?? throw new InvalidOperationException("launchSettings.json không hợp lệ.");

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return;

        if (node["profiles"] is JsonObject profiles)
        {
            foreach (var profile in profiles)
            {
                if (profile.Value is not JsonObject profileObj)
                    continue;

                if (!string.Equals(profileObj["commandName"]?.GetValue<string>(), "Project", StringComparison.OrdinalIgnoreCase))
                    continue;

                profileObj["applicationUrl"] = url;
            }
        }

        if (node["iisSettings"]?["iisExpress"] is JsonObject iisExpress && uri.Port > 0)
        {
            iisExpress["sslPort"] = uri.Port;
            iisExpress["applicationUrl"] = $"http://localhost:{uri.Port}/";
        }

        var options = new JsonSerializerOptions { WriteIndented = true };
        await File.WriteAllTextAsync(path, node.ToJsonString(options), ct).ConfigureAwait(false);
    }

    public static string? ReadApplicationUrlFromLaunchSettings(string path)
    {
        if (!File.Exists(path))
            return null;

        try
        {
            var json = File.ReadAllText(path);
            var node = ParseJsonObject(json);
            if (node?["profiles"] is not JsonObject profiles)
                return null;

            foreach (var profile in profiles)
            {
                if (profile.Value is not JsonObject obj)
                    continue;

                if (!string.Equals(obj["commandName"]?.GetValue<string>(), "Project", StringComparison.OrdinalIgnoreCase))
                    continue;

                var appUrl = obj["applicationUrl"]?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(appUrl))
                    return appUrl.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
            }
        }
        catch
        {
            // ignore
        }

        return null;
    }

    private static async Task SaveAppSettingsAsync(string path, string url, string connectionString, CancellationToken ct)
    {
        var json = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        var node = ParseJsonObject(json)
                   ?? throw new InvalidOperationException("appsettings.json không hợp lệ.");

        var patched = json;

        if (!string.IsNullOrWhiteSpace(url) && node["App"]?["SelfUrl"] != null)
            patched = ReplaceAppSelfUrl(patched, url);

        if (!string.IsNullOrWhiteSpace(connectionString) && node["ConnectionStrings"] is JsonObject conn)
        {
            foreach (var key in conn.Select(p => p.Key).ToList())
                patched = ReplaceConnectionStringValue(patched, key, connectionString);
        }

        await File.WriteAllTextAsync(path, patched, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Chỉ thay SelfUrl trong section App — không đụng field khác của file.
    /// </summary>
    private static string ReplaceAppSelfUrl(string json, string newUrl)
    {
        const string pattern = @"(?s)(""App""\s*:\s*\{.*?""SelfUrl""\s*:\s*)""((?:\\.|[^""])*)""";
        var replacement = $"$1\"{EscapeJsonStringValue(newUrl)}\"";
        return Regex.IsMatch(json, pattern) ? Regex.Replace(json, pattern, replacement) : json;
    }

    /// <summary>
    /// Chỉ thay value trong section ConnectionStrings — tránh đụng key trùng tên ở chỗ khác (vd. RabbitMQ Default).
    /// </summary>
    private static string ReplaceConnectionStringValue(string json, string key, string newValue)
    {
        var escapedKey = Regex.Escape(key);
        var pattern = $@"(?s)(""ConnectionStrings""\s*:\s*\{{.*?""{escapedKey}""\s*:\s*)""((?:\\.|[^""])*)""";
        var replacement = $"$1\"{EscapeJsonStringValue(newValue)}\"";
        return Regex.IsMatch(json, pattern) ? Regex.Replace(json, pattern, replacement) : json;
    }

    private static string EscapeJsonStringValue(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static async Task SaveEnvJsAsync(string path, Dictionary<string, string> envVars, CancellationToken ct)
    {
        var content = File.Exists(path)
            ? await File.ReadAllTextAsync(path, ct).ConfigureAwait(false)
            : "(function(window) {\n    window.env = window.env || {};\n})(this);\n";

        foreach (var (key, value) in envVars)
        {
            var escaped = value.Replace("\\", "\\\\").Replace("\"", "\\\"");
            var pattern = $@"window\s*\[\s*""env""\s*\]\s*\[\s*""{Regex.Escape(key)}""\s*\]\s*=\s*""(?:\\.|[^""])*""";
            var replacement = $@"window[""env""][""{key}""] = ""{escaped}""";

            if (Regex.IsMatch(content, pattern))
                content = Regex.Replace(content, pattern, replacement);
            else
                content = InsertEnvKey(content, key, escaped);
        }

        await File.WriteAllTextAsync(path, content, ct).ConfigureAwait(false);
    }

    private static string InsertEnvKey(string content, string key, string escapedValue)
    {
        const string marker = "window.env = window.env || {};";
        var insert = $"{marker}\n    window[\"env\"][\"{key}\"] = \"{escapedValue}\";";
        if (content.Contains(marker, StringComparison.Ordinal))
            return content.Replace(marker, insert, StringComparison.Ordinal);

        return content;
    }

    private static Dictionary<string, string> ParseEnvJs(string content)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in EnvJsPattern.Matches(content))
        {
            var key = match.Groups["key"].Value;
            var value = match.Groups["value"].Value.Replace("\\\"", "\"").Replace("\\\\", "\\");
            result[key] = value;
        }
        return result;
    }

    private static void ApplyUrlFallback(string url, ServiceUiConfig config)
    {
        ParseUrlIntoConfig(url, config);
    }

    private static void ParseUrlIntoConfig(string? url, ServiceUiConfig config)
    {
        if (string.IsNullOrWhiteSpace(url))
            return;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return;

        config.Scheme = uri.Scheme;
        config.Host = uri.Host;
        config.Port = uri.Port > 0 ? uri.Port.ToString() : uri.Scheme switch
        {
            "https" => "443",
            "http" => "80",
            _ => ""
        };
    }

    private static string BuildUrl(string scheme, string host, string port)
    {
        var normalizedScheme = string.IsNullOrWhiteSpace(scheme) ? "https" : scheme;
        var normalizedHost = string.IsNullOrWhiteSpace(host) ? "localhost" : host;
        if (string.IsNullOrWhiteSpace(port))
            return $"{normalizedScheme}://{normalizedHost}";

        return $"{normalizedScheme}://{normalizedHost}:{port}";
    }

    private static JsonObject? ParseJsonObject(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        return JsonNode.Parse(json, documentOptions: JsonReadOptions) as JsonObject;
    }
}
