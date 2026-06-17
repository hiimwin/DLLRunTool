using System.Text.Json;

namespace DLLRunTool.Services;

public static class RunSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static bool _loaded;
    private static bool _showConsoleWindow;

    public static bool ShowConsoleWindow
    {
        get
        {
            EnsureLoaded();
            return _showConsoleWindow;
        }
    }

    public static void SetShowConsoleWindow(bool value)
    {
        EnsureLoaded();
        _showConsoleWindow = value;
        Save();
    }

    private static string FilePath =>
        Path.Combine(AppContext.BaseDirectory, "run-settings.json");

    private static void EnsureLoaded()
    {
        if (_loaded)
            return;

        _loaded = true;
        if (!File.Exists(FilePath))
        {
            _showConsoleWindow = false;
            return;
        }

        try
        {
            var json = File.ReadAllText(FilePath);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("showConsoleWindow", out var prop))
                _showConsoleWindow = prop.GetBoolean();
        }
        catch
        {
            _showConsoleWindow = false;
        }
    }

    private static void Save()
    {
        var json = JsonSerializer.Serialize(new { showConsoleWindow = _showConsoleWindow }, JsonOptions);
        File.WriteAllText(FilePath, json);
    }
}
