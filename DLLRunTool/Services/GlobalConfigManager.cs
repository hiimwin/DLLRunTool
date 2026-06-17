using System.Text.Json;
using DLLRunTool.Models;

namespace DLLRunTool.Services;

public static class GlobalConfigManager
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public static string GetFilePath(string platformId, string category) =>
        Path.Combine(AppContext.BaseDirectory, $"global.{platformId}.{category.ToLowerInvariant()}.json");

    public static async Task<ServiceUiConfig> LoadAsync(string platformId, string category, IEnumerable<ServiceConfig> services, CancellationToken ct = default)
    {
        var path = GetFilePath(platformId, category);
        if (File.Exists(path))
        {
            var json = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
            return JsonSerializer.Deserialize<ServiceUiConfig>(json, JsonOptions) ?? new ServiceUiConfig();
        }

        var seed = services.FirstOrDefault(CanApplyGlobalBeConfig);
        if (seed == null)
            return new ServiceUiConfig();

        return await ConfigFileManager.ReadConfigAsync(seed, ct).ConfigureAwait(false);
    }

    public static async Task SaveAndApplyAsync(
        string platformId,
        string category,
        ServiceUiConfig config,
        IReadOnlyList<ServiceConfig> services,
        CancellationToken ct = default)
    {
        var path = GetFilePath(platformId, category);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(config, JsonOptions), ct).ConfigureAwait(false);

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
