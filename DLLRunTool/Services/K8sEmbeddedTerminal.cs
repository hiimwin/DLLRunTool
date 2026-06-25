using System.Diagnostics;
using System.Text;

namespace DLLRunTool.Services;

/// <summary>PowerShell nhúng — stream stdout/stderr vào WebView K8s.</summary>
public sealed class K8sEmbeddedTerminal : IDisposable
{
    private readonly Action<string> _onOutput;
    private readonly Action? _onExited;
    private readonly object _gate = new();
    private Process? _process;

    public K8sEmbeddedTerminal(Action<string> onOutput, Action? onExited = null)
    {
        _onOutput = onOutput;
        _onExited = onExited;
    }

    public bool IsRunning
    {
        get
        {
            lock (_gate)
                return _process is { HasExited: false };
        }
    }

    public void Start()
    {
        lock (_gate)
        {
            if (IsRunning)
                return;

            StopInternal();

            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            _process = new Process
            {
                EnableRaisingEvents = true,
                StartInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = "-NoLogo -NoExit -ExecutionPolicy Bypass",
                    WorkingDirectory = home,
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                }
            };

            _process.OutputDataReceived += OnDataReceived;
            _process.ErrorDataReceived += OnDataReceived;
            _process.Exited += (_, _) =>
            {
                _onOutput("\n[Terminal đã thoát]\n");
                _onExited?.Invoke();
            };

            _process.Start();
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
        }
    }

    public void WriteLine(string line)
    {
        lock (_gate)
        {
            if (!IsRunning)
                return;

            _process!.StandardInput.WriteLine(line);
            _process.StandardInput.Flush();
        }
    }

    public void Stop()
    {
        lock (_gate)
            StopInternal();
    }

    private void OnDataReceived(object sender, DataReceivedEventArgs e)
    {
        if (e.Data != null)
            _onOutput(e.Data + "\n");
    }

    private void StopInternal()
    {
        if (_process == null)
            return;

        try
        {
            if (!_process.HasExited)
            {
                try
                {
                    _process.StandardInput.WriteLine("exit");
                    _process.StandardInput.Flush();
                }
                catch
                {
                    // ignore
                }

                if (!_process.WaitForExit(800))
                    _process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // ignore
        }
        finally
        {
            _process.Dispose();
            _process = null;
        }
    }

    public void Dispose() => Stop();
}
