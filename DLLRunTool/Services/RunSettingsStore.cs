using System.Text.Json;

namespace DLLRunTool.Services;

public static class RunSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static bool _loaded;
    private static bool _showConsoleWindow;
    private static bool _showConsoleSelected;
    private static string? _consoleSelectedServiceId;
    private static int _stopGracefulTimeoutMs = 5000;

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
            if (doc.RootElement.TryGetProperty("showConsoleWindow", out var prop))
                _showConsoleWindow = prop.GetBoolean();
            if (doc.RootElement.TryGetProperty("showConsoleSelected", out var sel))
                _showConsoleSelected = sel.GetBoolean();
            if (doc.RootElement.TryGetProperty("consoleSelectedServiceId", out var sid))
                _consoleSelectedServiceId = sid.GetString();
            if (doc.RootElement.TryGetProperty("stopGracefulTimeoutMs", out var timeout) && timeout.TryGetInt32(out var ms))
                _stopGracefulTimeoutMs = Math.Clamp(ms, 0, 30000);
        }
        catch
        {
            _showConsoleWindow = false;
            _showConsoleSelected = false;
            _consoleSelectedServiceId = null;
        }
    }

    private static void Save()
    {
        var json = JsonSerializer.Serialize(new
        {
            showConsoleWindow = _showConsoleWindow,
            showConsoleSelected = _showConsoleSelected,
            consoleSelectedServiceId = _consoleSelectedServiceId,
            stopGracefulTimeoutMs = _stopGracefulTimeoutMs
        }, JsonOptions);
        File.WriteAllText(FilePath, json);
    }
}
