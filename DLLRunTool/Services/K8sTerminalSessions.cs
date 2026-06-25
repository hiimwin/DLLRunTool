namespace DLLRunTool.Services;

/// <summary>Quản lý nhiều PowerShell nhúng — mỗi tab một session.</summary>
public sealed class K8sTerminalSessions : IDisposable
{
    private readonly Action<string, string> _onOutput;
    private readonly Action<string> _onExited;
    private readonly object _gate = new();
    private readonly Dictionary<string, K8sEmbeddedTerminal> _sessions = new();
    private int _counter;

    public K8sTerminalSessions(Action<string, string> onOutput, Action<string> onExited)
    {
        _onOutput = onOutput;
        _onExited = onExited;
    }

    public string CreateSession()
    {
        lock (_gate)
        {
            var id = $"t{Interlocked.Increment(ref _counter)}";
            var term = new K8sEmbeddedTerminal(
                text => _onOutput(id, text),
                () => OnSessionExited(id));
            term.Start();
            _sessions[id] = term;
            return id;
        }
    }

    public void WriteLine(string sessionId, string line)
    {
        lock (_gate)
        {
            if (!_sessions.TryGetValue(sessionId, out var term))
                throw new InvalidOperationException("Terminal session không tồn tại.");

            if (!term.IsRunning)
                term.Start();

            term.WriteLine(line);
        }
    }

    public void CloseSession(string? sessionId)
    {
        lock (_gate)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
                return;

            if (_sessions.Remove(sessionId, out var term))
                term.Dispose();
        }
    }

    private void OnSessionExited(string sessionId)
    {
        lock (_gate)
            _sessions.Remove(sessionId);

        _onExited(sessionId);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            foreach (var term in _sessions.Values)
                term.Dispose();
            _sessions.Clear();
        }
    }
}
