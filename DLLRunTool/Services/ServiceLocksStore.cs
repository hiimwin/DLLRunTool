using System.Text.Json;

namespace DLLRunTool.Services;

public static class ServiceLocksStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly HashSet<string> LockedIds = new(StringComparer.OrdinalIgnoreCase);
    private static bool _loaded;

    public static bool IsLocked(string serviceId)
    {
        EnsureLoaded();
        return !string.IsNullOrWhiteSpace(serviceId) && LockedIds.Contains(serviceId);
    }

    public static bool Toggle(string serviceId)
    {
        var next = !IsLocked(serviceId);
        SetLocked(serviceId, next);
        return next;
    }

    public static void SetLocked(string serviceId, bool locked)
    {
        EnsureLoaded();
        if (string.IsNullOrWhiteSpace(serviceId))
            return;

        if (locked)
            LockedIds.Add(serviceId);
        else
            LockedIds.Remove(serviceId);

        Save();
    }

    private static string FilePath =>
        Path.Combine(AppContext.BaseDirectory, "service-locks.json");

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
            var ids = JsonSerializer.Deserialize<List<string>>(json);
            if (ids == null)
                return;

            foreach (var id in ids)
            {
                if (!string.IsNullOrWhiteSpace(id))
                    LockedIds.Add(id);
            }
        }
        catch
        {
            LockedIds.Clear();
        }
    }

    private static void Save()
    {
        var json = JsonSerializer.Serialize(LockedIds.OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToList(), JsonOptions);
        File.WriteAllText(FilePath, json);
    }
}
