using System.Diagnostics;
using System.Text.Json;
using DLLRunTool.Models;

namespace DLLRunTool.Services;

public static class RunSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static bool _loaded;
    private static bool _showConsoleWindow;
    private static bool _showConsoleSelected;
    private static string? _consoleSelectedServiceId;
    private static int _stopGracefulTimeoutMs = 5000;
    private static bool _cleanBinBeforeBuild;
    private static Dictionary<string, string> _serviceEnvironmentVariables = CreateDefaultEnvironmentVariables();

    public static int StopGracefulTimeoutMs
    {
        get
        {
            EnsureLoaded();
            return _stopGracefulTimeoutMs;
        }
    }

    public static bool ShowConsoleWindow
    {
        get
        {
            EnsureLoaded();
            return _showConsoleWindow;
        }
    }

    public static bool ShowConsoleSelected
    {
        get
        {
            EnsureLoaded();
            return _showConsoleSelected;
        }
    }

    public static string? ConsoleSelectedServiceId
    {
        get
        {
            EnsureLoaded();
            return _consoleSelectedServiceId;
        }
    }

    public static bool CleanBinBeforeBuild
    {
        get
        {
            EnsureLoaded();
            return _cleanBinBeforeBuild;
        }
    }

    public static IReadOnlyDictionary<string, string> ServiceEnvironmentVariables
    {
        get
        {
            EnsureLoaded();
            return _serviceEnvironmentVariables;
        }
    }

    public static bool ShouldMirrorService(string serviceId) =>
        ShowConsoleWindow ||
        (ShowConsoleSelected &&
         !string.IsNullOrWhiteSpace(ConsoleSelectedServiceId) &&
         string.Equals(serviceId, ConsoleSelectedServiceId, StringComparison.OrdinalIgnoreCase));

    public static void Set(bool showConsoleWindow, bool showConsoleSelected, string? consoleSelectedServiceId)
    {
        EnsureLoaded();
        _showConsoleWindow = showConsoleWindow;
        _showConsoleSelected = showConsoleSelected;
        _consoleSelectedServiceId = string.IsNullOrWhiteSpace(consoleSelectedServiceId)
            ? null
            : consoleSelectedServiceId;
        Save();
    }

    public static void SetShowConsoleWindow(bool value) =>
        Set(value, false, null);

    public static void SetServiceEnvironmentVariables(IReadOnlyDictionary<string, string>? variables)
    {
        EnsureLoaded();
        _serviceEnvironmentVariables = variables == null || variables.Count == 0
            ? CreateDefaultEnvironmentVariables()
            : new Dictionary<string, string>(variables, StringComparer.OrdinalIgnoreCase);
        Save();
    }

    public static void SetCleanBinBeforeBuild(bool value)
    {
        EnsureLoaded();
        _cleanBinBeforeBuild = value;
        Save();
    }

    public static void ApplyToProcess(ProcessStartInfo psi)
    {
        EnsureLoaded();
        foreach (var (key, value) in _serviceEnvironmentVariables)
        {
            if (string.IsNullOrWhiteSpace(key))
                continue;

            psi.Environment[key] = value ?? "";
        }
    }

    public static int CleanProjectOutputFolders(ServiceConfig service)
    {
        if (service.IsFrontEnd)
            return 0;

        var projectDir = service.ResolveSourceProjectPath();
        if (string.IsNullOrWhiteSpace(projectDir) || !Directory.Exists(projectDir))
            throw new InvalidOperationException($"Không tìm thấy thư mục project cho {service.Name}: {projectDir}");

        var removed = 0;
        foreach (var folder in new[] { "bin", "obj" })
        {
            var path = Path.Combine(projectDir, folder);
            if (!Directory.Exists(path))
                continue;

            Directory.Delete(path, recursive: true);
            removed++;
        }

        service.SyncFolderPathFromDisk();
        return removed;
    }

    public static object ToPayload() => new
    {
        showConsoleWindow = ShowConsoleWindow,
        showConsoleSelected = ShowConsoleSelected,
        consoleSelectedServiceId = ConsoleSelectedServiceId,
        stopGracefulTimeoutMs = StopGracefulTimeoutMs,
        cleanBinBeforeBuild = CleanBinBeforeBuild,
        serviceEnvironmentVariables = ServiceEnvironmentVariables
    };

    private static Dictionary<string, string> CreateDefaultEnvironmentVariables() =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Development"
        };

    private static string FilePath =>
        Path.Combine(AppContext.BaseDirectory, "run-settings.json");

    private static void EnsureLoaded()
    {
        if (_loaded)
            return;

        _loaded = true;
        if (!File.Exists(FilePath))
            return;

        try
        {
            var json = File.ReadAllText(FilePath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("showConsoleWindow", out var prop))
                _showConsoleWindow = prop.GetBoolean();
            if (root.TryGetProperty("showConsoleSelected", out var sel))
                _showConsoleSelected = sel.GetBoolean();
            if (root.TryGetProperty("consoleSelectedServiceId", out var sid))
                _consoleSelectedServiceId = sid.GetString();
            if (root.TryGetProperty("stopGracefulTimeoutMs", out var timeout) && timeout.TryGetInt32(out var ms))
                _stopGracefulTimeoutMs = Math.Clamp(ms, 0, 30000);
            if (root.TryGetProperty("cleanBinBeforeBuild", out var cleanBin))
                _cleanBinBeforeBuild = cleanBin.GetBoolean();

            if (root.TryGetProperty("serviceEnvironmentVariables", out var env) &&
                env.ValueKind == JsonValueKind.Object)
            {
                var parsed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var item in env.EnumerateObject())
                {
                    if (!string.IsNullOrWhiteSpace(item.Name))
                        parsed[item.Name] = item.Value.GetString() ?? "";
                }

                if (parsed.Count > 0)
                    _serviceEnvironmentVariables = parsed;
            }
        }
        catch
        {
            _showConsoleWindow = false;
            _showConsoleSelected = false;
            _consoleSelectedServiceId = null;
            _serviceEnvironmentVariables = CreateDefaultEnvironmentVariables();
        }
    }

    private static void Save()
    {
        var json = JsonSerializer.Serialize(new
        {
            showConsoleWindow = _showConsoleWindow,
            showConsoleSelected = _showConsoleSelected,
            consoleSelectedServiceId = _consoleSelectedServiceId,
            stopGracefulTimeoutMs = _stopGracefulTimeoutMs,
            cleanBinBeforeBuild = _cleanBinBeforeBuild,
            serviceEnvironmentVariables = _serviceEnvironmentVariables
        }, JsonOptions);
        File.WriteAllText(FilePath, json);
    }
}
