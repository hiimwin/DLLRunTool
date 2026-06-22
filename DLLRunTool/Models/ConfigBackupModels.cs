namespace DLLRunTool.Models;

public class ConfigBackupPackage
{
    public int Version { get; set; } = 2;
    public DateTime ExportedAt { get; set; } = DateTime.UtcNow;
    public string PlatformId { get; set; } = "";
    public string PlatformName { get; set; } = "";
    public string Source { get; set; } = "export";
    public Dictionary<string, string> GlobalConfigs { get; set; } = new();
    public List<ServiceBackupEntry> Services { get; set; } = [];
}

public class ServiceBackupEntry
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Type { get; set; } = "BE";
    public string ProjectPath { get; set; } = "";
    public string ConfigPath { get; set; } = "";
    public string? RawContent { get; set; }
    public ServiceUiConfig? ParsedConfig { get; set; }
    public Dictionary<string, string> ConfigFiles { get; set; } = new();
    public Dictionary<string, string> ConfigFilePaths { get; set; } = new();
}

public class BackupPreviewDto
{
    public string PlatformId { get; set; } = "";
    public string PlatformName { get; set; } = "";
    public int BackEndCount { get; set; }
    public int FrontEndCount { get; set; }
    public int ConfigFileCount { get; set; }
    public string BackupsFolder { get; set; } = "";
    public List<BackupFileInfoDto> RecentBackups { get; set; } = [];
    public bool HasLocalDefaults { get; set; }
    public string LocalDefaultsPath { get; set; } = "";
    public string LocalDefaultsScannedAt { get; set; } = "";
    public int LocalDefaultsFileCount { get; set; }
}

public class BackupFileInfoDto
{
    public string FileName { get; set; } = "";
    public string FullPath { get; set; } = "";
    public string PlatformId { get; set; } = "";
    public string ExportedAt { get; set; } = "";
    public long SizeBytes { get; set; }
}

public class ImportPreviewDto
{
    public string FilePath { get; set; } = "";
    public string FileName { get; set; } = "";
    public string BackupPlatformId { get; set; } = "";
    public string BackupPlatformName { get; set; } = "";
    public string TargetPlatformId { get; set; } = "";
    public string TargetPlatformName { get; set; } = "";
    public int ServiceCount { get; set; }
    public int ConfigFileCount { get; set; }
    public List<ImportServicePreviewDto> Services { get; set; } = [];
    public bool IsLocalDefaults { get; set; }
    public int DryRunChangedCount { get; set; }
    public int DryRunUnchangedCount { get; set; }
    public int DryRunSkippedCount { get; set; }
    public List<string> DryRunMessages { get; set; } = [];
}

public class ImportServicePreviewDto
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public List<string> Files { get; set; } = [];
}

public class ImportResultDto
{
    public int AppliedCount { get; set; }
    public int ChangedCount { get; set; }
    public int UnchangedCount { get; set; }
    public int SkippedCount { get; set; }
    public List<string> Messages { get; set; } = [];
}
