using System.Text.Json;
using DLLRunTool.Models;

namespace DLLRunTool.Services;

public static class GlobalConfigManager
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public static string GetFilePath(string platformId, string category) =>
        Path.Combine(AppContext.BaseDirectory, $"global.{platformId}.{category.ToLowerInvariant()}.json");

    public static string GetSecretsFilePath(string platformId, string category) =>
        Path.Combine(AppContext.BaseDirectory, $"global.{platformId}.{category.ToLowerInvariant()}.secrets.json");

    public static async Task<ServiceUiConfig> LoadAsync(string platformId, string category, IEnumerable<ServiceConfig> services, CancellationToken ct = default)
    {
        var path = GetFilePath(platformId, category);
        ServiceUiConfig result;
        if (File.Exists(path))
        {
            var json = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
            result = JsonSerializer.Deserialize<ServiceUiConfig>(json, JsonOptions) ?? new ServiceUiConfig();
        }
        else
        {
            var seed = services.FirstOrDefault(CanApplyGlobalBeConfig);
            if (seed == null)
                return new ServiceUiConfig();

            result = await ConfigFileManager.ReadConfigAsync(seed, ct).ConfigureAwait(false);
        }

        var secretsPath = GetSecretsFilePath(platformId, category);
        if (File.Exists(secretsPath))
        {
            var secretsJson = await File.ReadAllTextAsync(secretsPath, ct).ConfigureAwait(false);
            var node = ConfigSecretsHelper.ParseJsonObject(secretsJson);
            var conn = node?["connectionString"]?.GetValue<string>()
                       ?? node?["ConnectionString"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(conn))
                result.ConnectionString = conn;
        }

        return result;
    }

    public static async Task SaveAndApplyAsync(
        string platformId,
        string category,
        ServiceUiConfig config,
        IReadOnlyList<ServiceConfig> services,
        CancellationToken ct = default)
    {
        var path = GetFilePath(platformId, category);
        var publicConfig = new ServiceUiConfig
        {
            Host = config.Host,
            Scheme = config.Scheme,
            Port = config.Port,
            EnvVars = config.EnvVars,
            ConnectionString = ""
        };
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(publicConfig, JsonOptions), ct).ConfigureAwait(false);

        var secretsPath = GetSecretsFilePath(platformId, category);
        if (!string.IsNullOrWhiteSpace(config.ConnectionString))
        {
            var secretsPayload = JsonSerializer.Serialize(
                new { connectionString = config.ConnectionString },
                JsonOptions);
            await File.WriteAllTextAsync(secretsPath, secretsPayload, ct).ConfigureAwait(false);
        }
        else if (File.Exists(secretsPath))
        {
            File.Delete(secretsPath);
        }

        foreach (var service in services)
        {
            if (!category.Equals("FE", StringComparison.OrdinalIgnoreCase) && !CanApplyGlobalBeConfig(service))
                continue;

            if (category.Equals("FE", StringComparison.OrdinalIgnoreCase))
            {
                if (config.EnvVars != null && config.EnvVars.Count > 0)
                {
                    var merged = await ConfigFileManager.ReadConfigAsync(service, ct).ConfigureAwait(false);
                    merged.EnvVars ??= new Dictionary<string, string>();
                    foreach (var kv in config.EnvVars)
                        merged.EnvVars[kv.Key] = kv.Value;
                    await ConfigFileManager.SaveConfigAsync(service, merged, ct).ConfigureAwait(false);
                }
            }
            else
            {
                var perService = await ConfigFileManager.ReadConfigAsync(service, ct).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(config.ConnectionString))
                    perService.ConnectionString = config.ConnectionString;
                if (!string.IsNullOrWhiteSpace(config.Host))
                    perService.Host = config.Host;
                if (!string.IsNullOrWhiteSpace(config.Scheme))
                    perService.Scheme = config.Scheme;
                await ConfigFileManager.SaveConfigAsync(service, perService, ct).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Redis/exe và gateway không có appsettings SQL — bỏ qua khi áp connection string chung.</summary>
    private static bool CanApplyGlobalBeConfig(ServiceConfig service) =>
        !service.IsExe &&
        !service.IsFrontEnd &&
        !string.IsNullOrEmpty(service.ResolveConfigPath());
}
