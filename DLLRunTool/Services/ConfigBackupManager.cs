using System.Text.Json;
using DLLRunTool.Models;

namespace DLLRunTool.Services;

public static class ConfigBackupManager
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static string BackupsFolder =>
        Path.Combine(AppContext.BaseDirectory, "backups");

    public static async Task<ConfigBackupPackage> CreateBackupAsync(
        string platformId,
        string platformName,
        IReadOnlyList<ServiceConfig> services,
        string source = "export",
        CancellationToken ct = default)
    {
        var package = new ConfigBackupPackage
        {
            PlatformId = platformId,
            PlatformName = platformName,
            ExportedAt = DateTime.UtcNow,
            Source = source
        };

        foreach (var category in new[] { "be", "fe" })
        {
            var globalPath = GlobalConfigManager.GetFilePath(platformId, category);
            if (!File.Exists(globalPath))
                continue;

            var content = await File.ReadAllTextAsync(globalPath, ct).ConfigureAwait(false);
            var node = ConfigSecretsHelper.ParseJsonObject(content);
            if (node != null)
            {
                ConfigSecretsHelper.RedactServiceUiConfigJson(node);
                package.GlobalConfigs[category] = node.ToJsonString(JsonOptions);
            }
            else
            {
                package.GlobalConfigs[category] = content;
            }

            var globalSecretsPath = GlobalConfigManager.GetSecretsFilePath(platformId, category);
            if (File.Exists(globalSecretsPath))
                package.GlobalConfigs[$"{category}.secrets"] = await File.ReadAllTextAsync(globalSecretsPath, ct).ConfigureAwait(false);
        }

        foreach (var service in services)
        {
            package.Services.Add(await CreateServiceEntryAsync(service, ct).ConfigureAwait(false));
        }

        return package;
    }

    public static async Task<ConfigBackupPackage> ScanLocalFromSourceAsync(
        string platformId,
        string platformName,
        IReadOnlyList<ServiceConfig> services,
        CancellationToken ct = default)
    {
        return await CreateBackupAsync(platformId, platformName, services, "local-scan", ct).ConfigureAwait(false);
    }

    private static async Task<ServiceBackupEntry> CreateServiceEntryAsync(ServiceConfig service, CancellationToken ct)
    {
        var entry = new ServiceBackupEntry
        {
            Id = service.Id,
            Name = service.Name,
            Type = service.Type,
            ProjectPath = service.ResolveSourceProjectPath()
        };

        foreach (var (key, path) in service.GetSourceConfigFiles())
        {
            if (!File.Exists(path))
                continue;

            var content = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);

            if (key.Equals("appsettings.json", StringComparison.OrdinalIgnoreCase))
            {
                var (publicContent, secretsNode) = ConfigSecretsHelper.SplitSensitive(content);
                entry.ConfigFiles[key] = publicContent;
                entry.ConfigFilePaths[key] = path;

                if (secretsNode != null)
                {
                    var secretsKey = "appsettings.secrets.json";
                    var options = new JsonSerializerOptions { WriteIndented = true };
                    entry.ConfigFiles[secretsKey] = secretsNode.ToJsonString(options);
                    entry.ConfigFilePaths[secretsKey] = ConfigFileManager.ResolveSecretsPath(path);
                }

                continue;
            }

            entry.ConfigFiles[key] = content;
            entry.ConfigFilePaths[key] = path;
        }

        if (entry.ConfigFiles.Count > 0)
        {
            var primary = entry.ConfigFiles.ContainsKey("appsettings.Development.json")
                ? "appsettings.Development.json"
                : entry.ConfigFiles.ContainsKey("env.js")
                    ? "env.js"
                    : entry.ConfigFiles.Keys.First();
            entry.ConfigPath = entry.ConfigFilePaths.GetValueOrDefault(primary, "");
            entry.RawContent = entry.ConfigFiles[primary];
        }

        if (!service.IsExe)
        {
            try
            {
                entry.ParsedConfig = await ConfigFileManager.ReadConfigAsync(service, ct).ConfigureAwait(false);
            }
            catch
            {
                // Parsed snapshot is optional — raw config files are still saved.
            }
        }

        return entry;
    }

    public static async Task<string> SaveBackupAsync(ConfigBackupPackage package, string filePath, CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        var json = JsonSerializer.Serialize(package, JsonOptions);
        await File.WriteAllTextAsync(filePath, json, ct).ConfigureAwait(false);
        return filePath;
    }

    public static async Task<ConfigBackupPackage> LoadBackupAsync(string filePath, CancellationToken ct = default)
    {
        var json = await File.ReadAllTextAsync(filePath, ct).ConfigureAwait(false);
        var package = JsonSerializer.Deserialize<ConfigBackupPackage>(json, JsonOptions)
                      ?? throw new InvalidOperationException("File backup không hợp lệ.");

        MigrateLegacyEntries(package);

        if (package.Version < 1)
            throw new InvalidOperationException("Phiên bản backup không được hợp lệ.");

        return package;
    }

    public static ImportPreviewDto BuildPreview(ConfigBackupPackage package, string targetPlatformId, string targetPlatformName, string filePath, bool isLocalDefaults)
    {
        return new ImportPreviewDto
        {
            FilePath = filePath,
            FileName = Path.GetFileName(filePath),
            BackupPlatformId = package.PlatformId,
            BackupPlatformName = package.PlatformName,
            TargetPlatformId = targetPlatformId,
            TargetPlatformName = targetPlatformName,
            ServiceCount = package.Services.Count,
            ConfigFileCount = package.Services.Sum(s => GetEffectiveConfigFiles(s).Count) + package.GlobalConfigs.Count,
            IsLocalDefaults = isLocalDefaults,
            Services = package.Services.Select(s => new ImportServicePreviewDto
            {
                Name = s.Name,
                Type = s.Type,
                Files = GetEffectiveConfigFiles(s).Keys.ToList()
            }).ToList()
        };
    }

    public static async Task<ImportResultDto> ApplyToSourceAsync(
        ConfigBackupPackage package,
        string targetPlatformId,
        IReadOnlyList<ServiceConfig> services,
        CancellationToken ct = default)
    {
        var result = new ImportResultDto();
        var affectedServices = new HashSet<ServiceConfig>();

        foreach (var (category, content) in package.GlobalConfigs)
        {
            if (string.IsNullOrWhiteSpace(content))
                continue;

            var path = GlobalConfigManager.GetFilePath(targetPlatformId, category);
            if (category.EndsWith(".secrets", StringComparison.OrdinalIgnoreCase))
            {
                path = GlobalConfigManager.GetSecretsFilePath(
                    targetPlatformId,
                    category.Replace(".secrets", "", StringComparison.OrdinalIgnoreCase));
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            if (File.Exists(path))
            {
                var existingGlobal = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
                if (ConfigContentComparer.AreEqual(existingGlobal, content, Path.GetFileName(path)))
                {
                    result.UnchangedCount++;
                    result.AppliedCount++;
                    result.Messages.Add($"[Giữ nguyên] Global → {Path.GetFileName(path)}");
                    continue;
                }
            }

            await File.WriteAllTextAsync(path, content, ct).ConfigureAwait(false);
            result.Messages.Add($"[Đã đổi] Global → {Path.GetFileName(path)}");
            result.AppliedCount++;
            result.ChangedCount++;
        }

        foreach (var entry in package.Services)
        {
            var service = services.FirstOrDefault(s => s.Id == entry.Id)
                          ?? services.FirstOrDefault(s =>
                              s.Name.Equals(entry.Name, StringComparison.OrdinalIgnoreCase) &&
                              s.Type.Equals(entry.Type, StringComparison.OrdinalIgnoreCase));

            if (service == null)
            {
                result.SkippedCount++;
                result.Messages.Add($"Bỏ qua '{entry.Name}' — không có service khớp.");
                continue;
            }

            var files = GetEffectiveConfigFiles(entry);
            if (files.Count == 0 && entry.ParsedConfig != null)
            {
                await ConfigFileManager.SaveConfigAsync(service, entry.ParsedConfig, ct).ConfigureAwait(false);
                result.AppliedCount++;
                result.ChangedCount++;
                affectedServices.Add(service);
                result.Messages.Add($"[Đã đổi] {entry.Name} (parsed config → appsettings/launchSettings)");
                continue;
            }

            if (files.Count == 0)
            {
                result.SkippedCount++;
                result.Messages.Add($"Bỏ qua '{entry.Name}' — không có file trong backup.");
                continue;
            }

            foreach (var (key, content) in files)
            {
                var targetPath = ResolveApplyTargetPath(service, key);
                if (string.IsNullOrEmpty(targetPath))
                {
                    result.SkippedCount++;
                    result.Messages.Add($"Bỏ qua '{entry.Name}/{key}' — không tìm thấy path source.");
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);

                if (File.Exists(targetPath))
                {
                    var existing = await File.ReadAllTextAsync(targetPath, ct).ConfigureAwait(false);
                    if (ConfigContentComparer.AreEqual(existing, content, key))
                    {
                        result.UnchangedCount++;
                        result.AppliedCount++;
                        var note = ConfigContentComparer.IsJsonFile(key) &&
                                   !string.Equals(
                                       ConfigContentComparer.NormalizeNewlines(existing),
                                       ConfigContentComparer.NormalizeNewlines(content),
                                       StringComparison.Ordinal)
                            ? " (JSON giống, khác format)"
                            : "";
                        result.Messages.Add($"[Giữ nguyên] {entry.Name} → {key}{note}");
                        continue;
                    }
                }

                if (key.Equals("appsettings.json", StringComparison.OrdinalIgnoreCase))
                {
                    await ConfigFileManager.WriteAppSettingsToSourceAsync(targetPath, content, ct).ConfigureAwait(false);
                }
                else if (key.Equals("appsettings.secrets.json", StringComparison.OrdinalIgnoreCase))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                    await File.WriteAllTextAsync(targetPath, content, ct).ConfigureAwait(false);
                }
                else
                {
                    await File.WriteAllTextAsync(targetPath, content, ct).ConfigureAwait(false);
                }
                result.AppliedCount++;
                result.ChangedCount++;
                affectedServices.Add(service);
                result.Messages.Add($"[Đã đổi] {entry.Name} → {key} ({targetPath})");

                if (key.Equals("ocelot.localhost.json", StringComparison.OrdinalIgnoreCase))
                {
                    result.Messages.Add(
                        $"[Lưu ý] {entry.Name}: runtime gateway chỉ đọc ocelot.json (trong bin khi chạy). File ocelot.localhost.json chỉ để tham chiếu local.");
                }
            }
        }

        foreach (var service in affectedServices.Where(s => !s.IsExe && !s.IsFrontEnd))
        {
            try
            {
                var copied = await ConfigFileManager.SyncSourceConfigToOutputAsync(service, ct).ConfigureAwait(false);
                if (copied > 0)
                {
                    result.Messages.Add(
                        $"[Sync bin] {service.Name}: {copied} file → {service.ResolveRunWorkingDirectory()}");
                }
            }
            catch (Exception ex)
            {
                result.Messages.Add($"[Cảnh báo] Sync bin {service.Name}: {ex.Message}");
            }
        }

        return result;
    }

    private static string? ResolveApplyTargetPath(ServiceConfig service, string key) =>
        service.ResolveExpectedSourceConfigPath(key);

    public static List<BackupFileInfoDto> ListRecentBackups(string? platformId = null, int max = 20)
    {
        var folder = BackupsFolder;
        if (!Directory.Exists(folder))
            return [];

        return Directory.EnumerateFiles(folder, "backup-*.json")
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .Take(max)
            .Select(f => ToBackupInfo(f.FullName))
            .Where(b => platformId == null || b.PlatformId == platformId)
            .ToList();
    }

    private static Dictionary<string, string> GetEffectiveConfigFiles(ServiceBackupEntry entry)
    {
        if (entry.ConfigFiles.Count > 0)
            return entry.ConfigFiles;

        if (!string.IsNullOrWhiteSpace(entry.RawContent) && !string.IsNullOrWhiteSpace(entry.ConfigPath))
        {
            var key = Path.GetFileName(entry.ConfigPath);
            return new Dictionary<string, string> { [key] = entry.RawContent };
        }

        return new Dictionary<string, string>();
    }

    private static void MigrateLegacyEntries(ConfigBackupPackage package)
    {
        foreach (var entry in package.Services)
        {
            if (entry.ConfigFiles.Count > 0)
                continue;

            if (!string.IsNullOrWhiteSpace(entry.RawContent))
            {
                var key = string.IsNullOrWhiteSpace(entry.ConfigPath)
                    ? (entry.Type.Equals("FE", StringComparison.OrdinalIgnoreCase) ? "env.js" : "appsettings.json")
                    : Path.GetFileName(entry.ConfigPath);
                entry.ConfigFiles[key] = entry.RawContent;
                if (!string.IsNullOrWhiteSpace(entry.ConfigPath))
                    entry.ConfigFilePaths[key] = entry.ConfigPath;
            }
        }
    }

    private static BackupFileInfoDto ToBackupInfo(string path)
    {
        var info = new FileInfo(path);
        var dto = new BackupFileInfoDto
        {
            FileName = info.Name,
            FullPath = info.FullName,
            SizeBytes = info.Length
        };

        try
        {
            var json = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("platformId", out var pid))
                dto.PlatformId = pid.GetString() ?? "";
            if (doc.RootElement.TryGetProperty("exportedAt", out var exp))
            {
                if (exp.TryGetDateTime(out var dt))
                    dto.ExportedAt = dt.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
                else
                    dto.ExportedAt = exp.GetString() ?? "";
            }
        }
        catch
        {
            dto.ExportedAt = info.LastWriteTime.ToString("dd/MM/yyyy HH:mm");
        }

        return dto;
    }
}
