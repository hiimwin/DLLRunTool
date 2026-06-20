namespace DLLRunTool.Services;

/// <summary>
/// Reload service list when services.json / services.loyalty.json changes on disk.
/// </summary>
public sealed class ServicesConfigWatcher : IDisposable
{
    private readonly FileSystemWatcher[] _watchers;
    private readonly Action _onChanged;
    private DateTime _lastReloadUtc = DateTime.MinValue;
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(400);

    public ServicesConfigWatcher(Action onChanged, params string[] fileNames)
    {
        _onChanged = onChanged;
        var baseDir = AppContext.BaseDirectory;
        _watchers = fileNames.Select(name => CreateWatcher(baseDir, name)).ToArray();
    }

    private FileSystemWatcher CreateWatcher(string directory, string fileName)
    {
        var watcher = new FileSystemWatcher(directory, fileName)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
            EnableRaisingEvents = true
        };

        watcher.Changed += OnFileEvent;
        watcher.Created += OnFileEvent;
        watcher.Renamed += OnFileEvent;
        return watcher;
    }

    private void OnFileEvent(object sender, FileSystemEventArgs e)
    {
        var now = DateTime.UtcNow;
        if (now - _lastReloadUtc < Debounce)
            return;

        _lastReloadUtc = now;

        // File may still be locked briefly while saving.
        Thread.Sleep(120);
        _onChanged();
    }

    public void Dispose()
    {
        foreach (var watcher in _watchers)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }
    }
}
