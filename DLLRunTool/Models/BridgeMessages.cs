using System.Text.Json.Serialization;

namespace DLLRunTool.Models;

public class BridgeRequest
{
    [JsonPropertyName("action")]
    public string Action { get; set; } = "";

    [JsonPropertyName("platformId")]
    public string? PlatformId { get; set; }

    [JsonPropertyName("serviceId")]
    public string? ServiceId { get; set; }

    [JsonPropertyName("category")]
    public string? Category { get; set; }

    [JsonPropertyName("theme")]
    public string? Theme { get; set; }

    [JsonPropertyName("config")]
    public ServiceUiConfig? Config { get; set; }

    [JsonPropertyName("filePath")]
    public string? FilePath { get; set; }

    [JsonPropertyName("exportAllPlatforms")]
    public bool ExportAllPlatforms { get; set; }

    [JsonPropertyName("pathKey")]
    public string? PathKey { get; set; }

    [JsonPropertyName("paths")]
    public Dictionary<string, string>? Paths { get; set; }

    [JsonPropertyName("showConsoleWindow")]
    public bool? ShowConsoleWindow { get; set; }

    [JsonPropertyName("showConsoleSelected")]
    public bool? ShowConsoleSelected { get; set; }

    [JsonPropertyName("consoleSelectedServiceId")]
    public string? ConsoleSelectedServiceId { get; set; }

    [JsonPropertyName("locked")]
    public bool? Locked { get; set; }

    [JsonPropertyName("confirmed")]
    public bool? Confirmed { get; set; }

    [JsonPropertyName("folderKind")]
    public string? FolderKind { get; set; }

    [JsonPropertyName("presetId")]
    public string? PresetId { get; set; }

    [JsonPropertyName("view")]
    public string? View { get; set; }

    [JsonPropertyName("logFilterServiceId")]
    public string? LogFilterServiceId { get; set; }
}

public class ServiceUiConfig
{
    [JsonPropertyName("host")]
    public string Host { get; set; } = "localhost";

    [JsonPropertyName("port")]
    public string Port { get; set; } = "";

    [JsonPropertyName("scheme")]
    public string Scheme { get; set; } = "https";

    [JsonPropertyName("connectionString")]
    public string ConnectionString { get; set; } = "";

    [JsonPropertyName("envVars")]
    public Dictionary<string, string>? EnvVars { get; set; }
}

public class BridgeResponse
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("payload")]
    public object? Payload { get; set; }
}

public class ServiceStateDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "BE";

    [JsonPropertyName("isRunning")]
    public bool IsRunning { get; set; }

    [JsonPropertyName("url")]
    public string Url { get; set; } = "";

    [JsonPropertyName("processName")]
    public string ProcessName { get; set; } = "";

    [JsonPropertyName("dllName")]
    public string DllName { get; set; } = "";

    [JsonPropertyName("isExe")]
    public bool IsExe { get; set; }

    [JsonPropertyName("isLocked")]
    public bool IsLocked { get; set; }

    [JsonPropertyName("isRunProtected")]
    public bool IsRunProtected { get; set; }

    [JsonPropertyName("isRunBlocked")]
    public bool IsRunBlocked { get; set; }

    [JsonPropertyName("isStarting")]
    public bool IsStarting { get; set; }

    [JsonPropertyName("isBuilding")]
    public bool IsBuilding { get; set; }

    [JsonPropertyName("healthStatus")]
    public string HealthStatus { get; set; } = "unknown";

    [JsonPropertyName("healthCheckAttempt")]
    public int HealthCheckAttempt { get; set; }

    [JsonPropertyName("healthCheckMaxAttempts")]
    public int HealthCheckMaxAttempts { get; set; }

    [JsonPropertyName("enableHealthCheck")]
    public bool EnableHealthCheck { get; set; } = true;

    [JsonPropertyName("notes")]
    public string Notes { get; set; } = "";
}

public class ServiceDetailDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "BE";

    [JsonPropertyName("isRunning")]
    public bool IsRunning { get; set; }

    [JsonPropertyName("url")]
    public string Url { get; set; } = "";

    [JsonPropertyName("host")]
    public string Host { get; set; } = "";

    [JsonPropertyName("port")]
    public string Port { get; set; } = "";

    [JsonPropertyName("scheme")]
    public string Scheme { get; set; } = "https";

    [JsonPropertyName("connectionString")]
    public string ConnectionString { get; set; } = "";

    [JsonPropertyName("envVars")]
    public Dictionary<string, string> EnvVars { get; set; } = new();

    [JsonPropertyName("configPath")]
    public string ConfigPath { get; set; } = "";

    [JsonPropertyName("projectPath")]
    public string ProjectPath { get; set; } = "";

    [JsonPropertyName("feBindings")]
    public List<FeEnvBinding>? FeBindings { get; set; }
}

public sealed class FeEnvBinding
{
    [JsonPropertyName("envKey")]
    public string EnvKey { get; set; } = "";

    [JsonPropertyName("value")]
    public string Value { get; set; } = "";

    [JsonPropertyName("sourceService")]
    public string? SourceService { get; set; }
}

public class LogPayload
{
    [JsonPropertyName("serviceId")]
    public string? ServiceId { get; set; }

    [JsonPropertyName("level")]
    public string Level { get; set; } = "info";

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";

    [JsonPropertyName("timestamp")]
    public string Timestamp { get; set; } = DateTime.Now.ToString("HH:mm:ss.fff");

    [JsonPropertyName("serviceName")]
    public string? ServiceName { get; set; }
}

public class BuildProgressPayload
{
    [JsonPropertyName("serviceId")]
    public string ServiceId { get; set; } = "";

    [JsonPropertyName("percent")]
    public int Percent { get; set; }

    [JsonPropertyName("label")]
    public string Label { get; set; } = "";

    [JsonPropertyName("active")]
    public bool Active { get; set; }
}
