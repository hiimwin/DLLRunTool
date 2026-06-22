using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using DLLRunTool.Models;

namespace DLLRunTool.Services;

public sealed class AsyncCommandRunner
{
    private static readonly Regex PercentRegex = new(@"(\d+)%", RegexOptions.Compiled);
    private static readonly Regex ProjectBuiltRegex = new(@"\s+->\s+.+", RegexOptions.Compiled);

    private readonly Action<LogPayload> _emitLog;
    private readonly Action<BuildProgressPayload>? _emitBuildProgress;
    private readonly Action<string>? _onServiceProcessExited;
    private readonly object _gate = new();
    private readonly Dictionary<string, Process> _buildProcesses = new();

    public bool ShowConsoleWindow { get; set; }

    public AsyncCommandRunner(
        Action<LogPayload> emitLog,
        Action<BuildProgressPayload>? emitBuildProgress = null,
        Action<string>? onServiceProcessExited = null)
    {
        _emitLog = emitLog;
        _emitBuildProgress = emitBuildProgress;
        _onServiceProcessExited = onServiceProcessExited;
    }

    public async Task<int> RunBuildAsync(ServiceConfig service, CancellationToken ct = default)
    {
        var (fileName, arguments, workingDir) = ResolveBuildCommand(service);
        return await RunStreamingProcessAsync(service.Id, "build", fileName, arguments, workingDir, ct, trackBuildProgress: true).ConfigureAwait(false);
    }

    public Task<Process?> RunServiceAsync(ServiceConfig service, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            ProcessStatusCache.RefreshIfStale(force: service.IsExe);
            var existing = ProcessTreeKiller.FindRunningProcess(service, forceRefresh: service.IsExe);
            if (existing != null && !existing.HasExited)
            {
                service.ManagedProcess = existing;
                _emitLog(new LogPayload
                {
                    ServiceId = service.Id,
                    ServiceName = service.Name,
                    Level = "info",
                    Message = $"{service.Name} đã đang chạy (PID {existing.Id})."
                });
                return existing;
            }

            var cmd = ResolveRunCommand(service);

            try
            {
                _emitLog(new LogPayload
                {
                    ServiceId = service.Id,
                    ServiceName = service.Name,
                    Level = "info",
                    Message = $"▶ RUN: cd \"{cmd.WorkingDirectory}\" && {cmd.FileName} {cmd.Arguments}".Trim()
                });

                var process = service.IsExe
                    ? StartExeService(service, cmd.FileName, cmd.Arguments, cmd.WorkingDirectory)
                    : StartConsoleService(service, cmd);

                return process;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Không thể khởi chạy {service.Name}: {ex.Message}", ex);
            }
        }, ct);
    }

    private Process? StartExeService(ServiceConfig service, string fileName, string arguments, string workingDir)
    {
        EnsureWorkingDirectory(workingDir, service.Name);

        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDir,
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Minimized
        };

        Process? started = null;
        try
        {
            started = Process.Start(psi);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Process.Start thất bại: {ex.Message}", ex);
        }

        ProcessStatusCache.Invalidate();
        var startedPid = started?.Id;

        for (var i = 0; i < 40; i++)
        {
            Thread.Sleep(250);

            if (startedPid is > 0)
            {
                var direct = TryGetLiveProcess(startedPid.Value);
                if (direct != null)
                    return AttachExeProcess(service, direct);
            }

            ProcessStatusCache.RefreshIfStale(force: true);
            var found = ProcessTreeKiller.FindRunningProcess(service, forceRefresh: true)
                        ?? ProcessStatusCache.FindExeProcess(service.DllName, useCache: false);
            if (found != null && !found.HasExited)
                return AttachExeProcess(service, found);
        }

        var fallback = ProcessStatusCache.FindExeProcess(service.DllName, useCache: false);
        if (fallback != null && !fallback.HasExited)
            return AttachExeProcess(service, fallback);

        if (startedPid is > 0 && IsProcessNameRunning(service.DllName))
        {
            var late = ProcessStatusCache.FindExeProcess(service.DllName, useCache: false);
            if (late != null && !late.HasExited)
                return AttachExeProcess(service, late);

            _emitLog(new LogPayload
            {
                ServiceId = service.Id,
                Level = "success",
                Message = $"{service.Name} đã khởi động (PID {startedPid}) — process đang chạy."
            });
            return started;
        }

        throw new InvalidOperationException(
            $"{service.Name} không phát hiện được sau khi start. Thử chạy thủ công: {fileName}");
    }

    private Process AttachExeProcess(ServiceConfig service, Process found)
    {
        service.ManagedProcess = found;
        found.EnableRaisingEvents = true;
        found.Exited += (_, _) =>
        {
            if (service.ManagedProcess?.Id == found.Id)
                service.ManagedProcess = null;
        };
        _emitLog(new LogPayload
        {
            ServiceId = service.Id,
            Level = "success",
            Message = $"{service.Name} đã khởi động (PID {found.Id})."
        });
        return found;
    }

    private static Process? TryGetLiveProcess(int pid)
    {
        try
        {
            var p = Process.GetProcessById(pid);
            return p.HasExited ? null : p;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsProcessNameRunning(string dllOrExeName)
    {
        var name = Path.GetFileNameWithoutExtension(dllOrExeName);
        if (string.IsNullOrWhiteSpace(name))
            return false;

        return Process.GetProcessesByName(name).Any(p =>
        {
            try { return !p.HasExited; }
            catch { return false; }
        });
    }

    private Process? StartConsoleService(ServiceConfig service, ResolvedRunCommand cmd)
    {
        EnsureWorkingDirectory(cmd.WorkingDirectory, service.Name);

        var logPath = ServiceLogMirror.EnsureLogFile(service.Id, service.Name);
        ServiceLogMirror.Register(service.Id, logPath);

        var psi = new ProcessStartInfo
        {
            FileName = cmd.FileName,
            Arguments = cmd.Arguments,
            WorkingDirectory = cmd.WorkingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        var process = Process.Start(psi);
        if (process == null)
            throw new InvalidOperationException("Process.Start trả về null.");

        service.ManagedProcess = process;
        process.EnableRaisingEvents = true;
        AttachLogStreaming(service, process, "run");
        AttachProcessExitHandler(service, process);

        if (RunSettingsStore.ShouldMirrorService(service.Id))
        {
            ServiceLogMirror.OpenMirror(service.Id, service.Name);
            var modeLabel = RunSettingsStore.ShowConsoleWindow ? "CMD tất cả" : "CMD đang chọn";
            _emitLog(new LogPayload
            {
                ServiceId = service.Id,
                ServiceName = service.Name,
                Level = "info",
                Message = $"{service.Name}: log trong tool + {modeLabel} (Alt+Tab)."
            });
        }

        return process;
    }

    private void AttachProcessExitHandler(ServiceConfig service, Process process)
    {
        process.Exited += (_, _) =>
        {
            if (service.ManagedProcess?.Id == process.Id)
                service.ManagedProcess = null;

            ServiceLogMirror.CloseMirror(service.Id);
            ServiceLogMirror.Unregister(service.Id);

            int exitCode;
            try { exitCode = process.ExitCode; }
            catch { exitCode = -1; }

            _emitLog(new LogPayload
            {
                ServiceId = service.Id,
                ServiceName = service.Name,
                Level = exitCode == 0 ? "info" : "error",
                Message = $"{service.Name} đã thoát (exit code {exitCode})."
            });
            _onServiceProcessExited?.Invoke(service.Id);
        };
    }

    public void CancelBuild(string serviceId)
    {
        lock (_gate)
        {
            if (_buildProcesses.TryGetValue(serviceId, out var proc))
            {
                ProcessTreeKiller.KillProcessTree(proc);
                _buildProcesses.Remove(serviceId);
            }
        }
    }

    private (string FileName, string Arguments, string WorkingDirectory) ResolveBuildCommand(ServiceConfig service)
    {
        if (service.IsExe)
            throw new InvalidOperationException($"{service.Name} là executable — không hỗ trợ build.");

        if (!string.IsNullOrWhiteSpace(service.BuildCommand))
            return ParseCommand(service.BuildCommand, service.ResolveProjectPath());

        if (service.IsFrontEnd)
            return ("cmd.exe", "/c npm run build", service.ResolveProjectPath());

        var csproj = service.TryResolveCsprojPath()
                     ?? throw new InvalidOperationException($"Không tìm thấy .csproj cho {service.Name}. Kiểm tra Workspace Paths.");

        var projectDir = Path.GetDirectoryName(csproj)!
                           ?? throw new InvalidOperationException($"Thư mục project không hợp lệ: {csproj}");

        return ("dotnet", $"build \"{csproj}\" -c Debug -v:m", projectDir);
    }

    private sealed record ResolvedRunCommand(string FileName, string Arguments, string WorkingDirectory);

    private static ResolvedRunCommand ResolveRunCommand(ServiceConfig service)
    {
        if (!string.IsNullOrWhiteSpace(service.RunCommand))
        {
            var (fileName, arguments, workingDir) = ParseCommand(service.RunCommand, service.ResolveRunWorkingDirectory());
            return new ResolvedRunCommand(fileName, arguments, workingDir);
        }

        if (service.IsExe)
        {
            var exePath = Path.Combine(service.FolderPath, service.DllName);
            return new ResolvedRunCommand(exePath, "", service.FolderPath);
        }

        if (service.IsFrontEnd)
            return new ResolvedRunCommand("cmd.exe", "/c npm run start", service.ResolveProjectPath());

        var url = string.IsNullOrWhiteSpace(service.Url) ? "" : $" --urls \"{service.Url}\"";
        var dllPath = service.FindDllOutputPath();
        var workDir = service.ResolveRunWorkingDirectory();

        if (dllPath != null && File.Exists(dllPath))
        {
            // Chạy tay: cd bin\Debug\netX.0 && dotnet Service.dll --urls "..."
            // Không dùng dotnet run / dotnet exec — tránh ABP license.
            var dllFileName = Path.GetFileName(dllPath);
            return new ResolvedRunCommand("dotnet", $"{dllFileName}{url}", workDir);
        }

        throw new InvalidOperationException(
            $"Chưa có {service.DllName} trong bin. Hãy Build trước.\n" +
            $"Chạy đúng: cd \"{workDir}\" && dotnet {service.DllName}{url}");
    }

    private static void EnsureWorkingDirectory(string workingDir, string serviceName)
    {
        if (!Directory.Exists(workingDir))
            throw new InvalidOperationException(
                $"Thư mục làm việc không tồn tại cho {serviceName}: {workingDir}");
    }

    private static (string FileName, string Arguments, string WorkingDirectory) ParseCommand(string command, string workingDir)
    {
        command = command.Trim();
        if (command.StartsWith("npm ", StringComparison.OrdinalIgnoreCase) ||
            command.StartsWith("yarn ", StringComparison.OrdinalIgnoreCase))
        {
            return ("cmd.exe", $"/c {command}", workingDir);
        }

        var parts = SplitCommand(command);
        if (parts.Count == 0)
            return ("cmd.exe", "/c " + command, workingDir);

        if (parts.Count == 1)
            return (parts[0], "", workingDir);

        return (parts[0], string.Join(" ", parts.Skip(1)), workingDir);
    }

    private static List<string> SplitCommand(string command)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        foreach (var ch in command)
        {
            if (ch == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (char.IsWhiteSpace(ch) && !inQuotes)
            {
                if (current.Length > 0)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
                continue;
            }

            current.Append(ch);
        }

        if (current.Length > 0)
            result.Add(current.ToString());

        return result;
    }

    private async Task<int> RunStreamingProcessAsync(
        string serviceId,
        string phase,
        string fileName,
        string arguments,
        string workingDir,
        CancellationToken ct,
        bool trackBuildProgress = false)
    {
        EnsureWorkingDirectory(workingDir, serviceId);

        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        lock (_gate)
            _buildProcesses[serviceId] = process;

        var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var buildProgress = 5;
        var projectsBuilt = 0;

        void HandleLine(string? line, string level)
        {
            if (string.IsNullOrEmpty(line))
                return;

            _emitLog(new LogPayload { ServiceId = serviceId, Level = level, Message = $"[{phase}] {line}" });

            if (!trackBuildProgress)
                return;

            var percentMatch = PercentRegex.Match(line);
            if (percentMatch.Success && int.TryParse(percentMatch.Groups[1].Value, out var pct))
            {
                buildProgress = Math.Clamp(pct, buildProgress, 99);
                EmitBuildProgress(serviceId, buildProgress, line.Trim());
                return;
            }

            if (line.Contains("Determining projects to restore", StringComparison.OrdinalIgnoreCase))
            {
                EmitBuildProgress(serviceId, 10, "Đang restore packages...");
                return;
            }

            if (line.Contains("Restored ", StringComparison.OrdinalIgnoreCase))
            {
                buildProgress = Math.Max(buildProgress, 20);
                EmitBuildProgress(serviceId, buildProgress, "Restore xong");
                return;
            }

            if (ProjectBuiltRegex.IsMatch(line))
            {
                projectsBuilt++;
                buildProgress = Math.Min(20 + projectsBuilt * 12, 95);
                EmitBuildProgress(serviceId, buildProgress, "Đang biên dịch...");
            }
        }

        process.OutputDataReceived += (_, e) => HandleLine(e.Data, "info");
        process.ErrorDataReceived += (_, e) => HandleLine(e.Data, "error");
        process.Exited += (_, _) => tcs.TrySetResult(process.ExitCode);

        _emitLog(new LogPayload
        {
            ServiceId = serviceId,
            Level = "info",
            Message = $"▶ {phase.ToUpperInvariant()}: {fileName} {arguments}"
        });

        if (trackBuildProgress)
            EmitBuildProgress(serviceId, 5, "Bắt đầu build...");

        if (!process.Start())
            throw new InvalidOperationException("Không thể khởi chạy tiến trình build.");

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await using var reg = ct.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                    ProcessTreeKiller.KillProcessTree(process);
            }
            catch
            {
                // ignore
            }
        });

        try
        {
            var exitCode = await tcs.Task.ConfigureAwait(false);
            if (trackBuildProgress)
            {
                EmitBuildProgress(serviceId, exitCode == 0 ? 100 : buildProgress,
                    exitCode == 0 ? "Build hoàn tất" : "Build thất bại", active: false);
            }

            return exitCode;
        }
        finally
        {
            lock (_gate)
                _buildProcesses.Remove(serviceId);
        }
    }

    private void EmitBuildProgress(string serviceId, int percent, string label, bool active = true)
    {
        _emitBuildProgress?.Invoke(new BuildProgressPayload
        {
            ServiceId = serviceId,
            Percent = Math.Clamp(percent, 0, 100),
            Label = label.Length > 80 ? label[..80] : label,
            Active = active
        });
    }

    private void AttachLogStreaming(ServiceConfig service, Process process, string phase)
    {
        process.OutputDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                _emitLog(new LogPayload
                {
                    ServiceId = service.Id,
                    ServiceName = service.Name,
                    Level = "info",
                    Message = $"[{phase}] {e.Data}"
                });
                ServiceLogMirror.AppendLine(service.Id, e.Data);
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                _emitLog(new LogPayload
                {
                    ServiceId = service.Id,
                    ServiceName = service.Name,
                    Level = "error",
                    Message = $"[{phase}] {e.Data}"
                });
                ServiceLogMirror.AppendLine(service.Id, e.Data);
            }
        };

        try
        {
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }
        catch
        {
            // Some GUI processes don't support redirect
        }
    }
}
