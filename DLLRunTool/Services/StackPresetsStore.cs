using System.Text.Json;

namespace DLLRunTool.Services;

public sealed class StackPreset
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public List<string> ServiceIds { get; set; } = [];
}

public static class StackPresetsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static List<StackPreset>? _cache;

    public static IReadOnlyList<StackPreset> Load()
    {
        if (_cache != null)
            return _cache;

        var path = Path.Combine(AppContext.BaseDirectory, "stack-presets.json");
        if (!File.Exists(path))
        {
            _cache = GetDefaultPresets();
            return _cache;
        }

        try
        {
            var json = File.ReadAllText(path);
            _cache = JsonSerializer.Deserialize<List<StackPreset>>(json, JsonOptions) ?? GetDefaultPresets();
        }
        catch
        {
            _cache = GetDefaultPresets();
        }

        return _cache;
    }

    public static StackPreset? Find(string id) =>
        Load().FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

    private static List<StackPreset> GetDefaultPresets() =>
    [
        new StackPreset
        {
            Id = "dev-core",
            Name = "Dev core",
            ServiceIds =
            [
                "loyalty-redis",
                "loyalty-authserver",
                "loyalty-webgateway",
                "loyalty-identity",
                "loyalty-saas",
                "loyalty-administration"
            ]
        },
        new StackPreset
        {
            Id = "dev-full-be",
            Name = "Dev full BE",
            ServiceIds =
            [
                "loyalty-redis",
                "loyalty-authserver",
                "loyalty-webgateway",
                "loyalty-identity",
                "loyalty-saas",
                "loyalty-administration",
                "loyalty-masterdata",
                "loyalty-member",
                "loyalty-customerjourney",
                "loyalty-product",
                "loyalty-segment",
                "loyalty-transaction",
                "loyalty-syncdata"
            ]
        }
    ];
}
