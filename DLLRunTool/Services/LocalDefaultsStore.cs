using System.Text.Json;
using DLLRunTool.Models;

namespace DLLRunTool.Services;

public static class LocalDefaultsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static string DefaultsFolder =>
        Path.Combine(AppContext.BaseDirectory, "defaults");

    public static string GetPath(string platformId) =>
        Path.Combine(DefaultsFolder, $"local.{platformId}.json");

    public static bool Exists(string platformId) =>
        File.Exists(GetPath(platformId));

    public static async Task SaveAsync(string platformId, ConfigBackupPackage package, CancellationToken ct = default)
    {
        package.Source = "local-scan";
        Directory.CreateDirectory(DefaultsFolder);
        var json = JsonSerializer.Serialize(package, JsonOptions);
        await File.WriteAllTextAsync(GetPath(platformId), json, ct).ConfigureAwait(false);
    }

    public static async Task<ConfigBackupPackage?> LoadAsync(string platformId, CancellationToken ct = default)
    {
        var path = GetPath(platformId);
        if (!File.Exists(path))
            return null;

        return await ConfigBackupManager.LoadBackupAsync(path, ct).ConfigureAwait(false);
    }

    public static (bool Exists, string ScannedAt, int FileCount) GetInfo(string platformId)
    {
        var path = GetPath(platformId);
        if (!File.Exists(path))
            return (false, "", 0);

        try
        {
            var package = JsonSerializer.Deserialize<ConfigBackupPackage>(File.ReadAllText(path), JsonOptions);
            var fileCount = package?.Services.Sum(s => s.ConfigFiles.Count) ?? 0;
            var scannedAt = package?.ExportedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm") ?? "";
            return (true, scannedAt, fileCount);
        }
        catch
        {
            var info = new FileInfo(path);
            return (true, info.LastWriteTime.ToString("dd/MM/yyyy HH:mm"), 0);
        }
    }
}
