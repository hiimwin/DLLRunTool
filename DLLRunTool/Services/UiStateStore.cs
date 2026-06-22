using System.Text.Json;

namespace DLLRunTool.Services;

public sealed class UiState
{
    public string View { get; set; } = "dashboard";
    public string LastServiceView { get; set; } = "dashboard";
    public string Category { get; set; } = "BE";
    public string PlatformId { get; set; } = "loyalty";
    public string LogFilterServiceId { get; set; } = "";
}

public static class UiStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static bool _loaded;
    private static UiState _state = new();

    public static UiState Current
    {
        get
        {
            EnsureLoaded();
            return _state;
        }
    }

    public static void Save(UiState state)
    {
        EnsureLoaded();
        _state = state;
        File.WriteAllText(FilePath, JsonSerializer.Serialize(_state, JsonOptions));
    }

    public static void Patch(Action<UiState> mutate)
    {
        EnsureLoaded();
        mutate(_state);
        Save(_state);
    }

    private static string FilePath =>
        Path.Combine(AppContext.BaseDirectory, "ui-state.json");

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
            _state = JsonSerializer.Deserialize<UiState>(json, JsonOptions) ?? new UiState();
        }
        catch
        {
            _state = new UiState();
        }
    }
}
