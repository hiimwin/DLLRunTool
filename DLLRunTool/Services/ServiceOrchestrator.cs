using System.Diagnostics;
using System.Text.Json;
using DLLRunTool.Models;

namespace DLLRunTool.Services;

public sealed class ServiceOrchestrator
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly Dictionary<string, List<ServiceConfig>> _platformServices = new();
    private readonly List<PlatformDefinition> _platforms;
    private readonly AsyncCommandRunner _commandRunner;
    private readonly Action<BridgeResponse> _pushToUi;
    private readonly System.Windows.Forms.Timer _statusTimer;
    private readonly ServicesConfigWatcher _servicesConfigWatcher;
    private readonly Control? _uiOwner;
    private string _activePlatformId = "loyalty";
    private string _lastStatusSnapshot = "";
    private int _statusRefreshRunning;
    private readonly object _serviceOpsGate = new();
    private readonly Dictionary<string, string> _serviceOps = new(StringComparer.OrdinalIgnoreCase);

    public const string AppAuthor = "Win_Trung";
    public const string AppTitle = "Win_Trung - Microservices Control Panel";

    public static bool IsApplyingUpdate { get; private set; }

    public ServiceOrchestrator(Action<BridgeResponse> pushToUi, Action<LogPayload> emitLog, Control? uiOwner = null)
    {
        _pushToUi = pushToUi;
        _commandRunner = new AsyncCommandRunner(
            payload => PushLog(payload),
            payload => _pushToUi(new BridgeResponse { Type = "buildProgress", Payload = payload }),
            _ => RefreshAllStatuses());
        _commandRunner.ShowConsoleWindow = RunSettingsStore.ShowConsoleWindow;
        _uiOwner = uiOwner;
        _platforms =
        [
            new PlatformDefinition { Id = "loyalty", Name = "LoyaltyPlatform", ConfigFile = "services.loyalty.json" },
            new PlatformDefinition { Id = "fptcx", Name = "FPTCXSuite", ConfigFile = "services.json" }
        ];

        WorkspacePathsStore.EnsureLoaded();

        foreach (var platform in _platforms)
            _platformServices[platform.Id] = LoadServices(platform.ConfigFile);

        _statusTimer = new System.Windows.Forms.Timer { Interval = 8000 };
        _statusTimer.Tick += (_, _) => _ = RefreshStatusesAsync();
        _statusTimer.Start();

        _ = Task.Run(async () =>
        {
            await Task.Delay(8000).ConfigureAwait(false);
            await EnsureLocalDefaultsAsync().ConfigureAwait(false);
        });

        _servicesConfigWatcher = new ServicesConfigWatcher(
            () => RunOnUi(ReloadServicesFromDisk),
            "services.loyalty.json",
            "services.json");
    }

    private async Task EnsureLocalDefaultsAsync()
    {
        try
        {
            foreach (var platform in _platforms)
            {
                if (LocalDefaultsStore.Exists(platform.Id))
                    continue;

                try
                {
                    await ScanAndSaveLocalDefaultsAsync(platform.Id).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    PushLog(new LogPayload
                    {
                        Level = "warning",
                        Message = $"Quét local defaults ({platform.Name}): {ex.Message}"
                    });
                }
            }
        }
        catch (Exception ex)
        {
            PushLog(new LogPayload { Level = "warning", Message = $"Quét local defaults: {ex.Message}" });
        }
    }

    private async Task ScanAndSaveLocalDefaultsAsync(string platformId)
    {
        var platform = _platforms.First(p => p.Id == platformId);
        var services = _platformServices.TryGetValue(platformId, out var list) ? list : [];
        var package = await ConfigBackupManager.ScanLocalFromSourceAsync(platform.Id, platform.Name, services).ConfigureAwait(false);
        await LocalDefaultsStore.SaveAsync(platform.Id, package).ConfigureAwait(false);

        PushLog(new LogPayload
        {
            Level = "success",
            Message = $"Đã quét cấu hình local mặc định cho {platform.Name} ({package.Services.Sum(s => s.ConfigFiles.Count)} files)."
        });

        if (platformId == _activePlatformId)
            SendBackupPreview();
    }

    public void HandleRequest(BridgeRequest request)
    {
        _ = HandleRequestAsync(request);
    }

    private async Task HandleRequestAsync(BridgeRequest request)
    {
        try
        {
            switch (request.Action)
            {
                case "init":
                    SendInit();
                    _ = CheckForUpdatesAsync();
                    break;
                case "selectPlatform":
                    _activePlatformId = request.PlatformId ?? _activePlatformId;
                    PushPlatformChanged();
                    PushServicesList();
                    break;
                case "reloadServices":
                    ReloadServicesFromDisk();
                    break;
                case "loadGlobalConfig":
                    await SendGlobalConfigAsync(request.Category ?? "BE").ConfigureAwait(false);
                    break;
                case "saveGlobalConfig":
                    await SaveGlobalConfigAsync(request).ConfigureAwait(false);
                    break;
                case "restart":
                    await RestartServiceAsync(request.ServiceId!, request.PlatformId).ConfigureAwait(false);
                    break;
                case "selectService":
                    await SendServiceDetailAsync(request.ServiceId!, request.PlatformId).ConfigureAwait(false);
                    break;
                case "saveConfig":
                    await SaveConfigAsync(request).ConfigureAwait(false);
                    break;
                case "run":
                    await RunServiceAsync(request).ConfigureAwait(false);
                    break;
                case "stop":
                    StopService(request.ServiceId!, request.PlatformId);
                    break;
                case "build":
                    await BuildServiceAsync(request.ServiceId!, request.PlatformId).ConfigureAwait(false);
                    break;
                case "refreshStatus":
                    RefreshAllStatuses();
                    break;
                case "getBackupPreview":
                    SendBackupPreview();
                    break;
                case "exportConfig":
                    await ExportConfigAsync().ConfigureAwait(false);
                    break;
                case "importConfig":
                    await PreviewImportAsync(request.FilePath).ConfigureAwait(false);
                    break;
                case "previewImport":
                    await PreviewImportAsync(request.FilePath).ConfigureAwait(false);
                    break;
                case "applyImport":
                    await ApplyImportAsync(request.FilePath).ConfigureAwait(false);
                    break;
                case "scanLocalDefaults":
                    await ScanAndSaveLocalDefaultsAsync(_activePlatformId).ConfigureAwait(false);
                    break;
                case "applyLocalDefaults":
                    await ApplyLocalDefaultsAsync().ConfigureAwait(false);
                    break;
                case "loadWorkspacePaths":
                    SendWorkspacePaths();
                    break;
                case "saveWorkspacePaths":
                    SaveWorkspacePaths(request.Paths);
                    break;
                case "browseWorkspaceFolder":
                    BrowseWorkspaceFolder(request.PathKey);
                    break;
                case "loadRunSettings":
                    SendRunSettings();
                    break;
                case "saveRunSettings":
                    SaveRunSettings(request.ShowConsoleWindow ?? false);
                    break;
                case "stopAll":
                    StopAllServices();
                    break;
                case "toggleServiceLock":
                    ToggleServiceLock(request);
                    break;
                case "checkUpdate":
                    await CheckForUpdatesAsync(forceNotify: true).ConfigureAwait(false);
                    break;
                case "applyUpdate":
                case "openUpdateUrl":
                    await ApplyUpdateAsync(request.FilePath).ConfigureAwait(false);
                    break;
            }
        }
        catch (Exception ex)
        {
            PushLog(new LogPayload { ServiceId = request.ServiceId, Level = "error", Message = ex.Message });
        }
    }

    private void SendInit()
    {
        _pushToUi(new BridgeResponse
        {
            Type = "init",
            Payload = new
            {
                platforms = _platforms.Select(p => new { p.Id, p.Name }),
                activePlatformId = _activePlatformId,
                theme = "dark",
                appAuthor = AppAuthor,
                appTitle = AppTitle,
                appVersion = AppVersionInfo.Current,
                workspace = BuildWorkspacePayload(),
                runSettings = new { showConsoleWindow = RunSettingsStore.ShowConsoleWindow }
            }
        });
        PushServicesList();
    }

    private async Task CheckForUpdatesAsync(bool forceNotify = false)
    {
        var result = await UpdateChecker.CheckAsync().ConfigureAwait(false);
        if (result == null)
        {
            if (forceNotify)
            {
                PushLog(new LogPayload
                {
                    Level = "info",
                    Message = "Không kiểm tra được bản mới (chưa cấu manifest URL hoặc không có mạng)."
                });
            }
            return;
        }

        if (!result.IsUpdateAvailable)
        {
            if (forceNotify)
            {
                PushLog(new LogPayload
                {
                    Level = "success",
                    Message = $"Bạn đang dùng bản mới nhất (v{result.CurrentVersion})."
                });
            }
            return;
        }

        _pushToUi(new BridgeResponse { Type = "updateAvailable", Payload = result });
        PushLog(new LogPayload
        {
            Level = "warning",
            Message = $"Có bản mới v{result.LatestVersion} (đang dùng v{result.CurrentVersion})."
        });
    }

    private async Task ApplyUpdateAsync(string? downloadUrl)
    {
        if (string.IsNullOrWhiteSpace(downloadUrl))
        {
            PushLog(new LogPayload { Level = "warning", Message = "Chưa có link tải trong update-manifest.json." });
            return;
        }

        var running = CountRunningServices();
        if (running > 0)
        {
            var answer = RunOnUi(() => System.Windows.Forms.MessageBox.Show(
                _uiOwner?.FindForm(),
                $"Có {running} service đang chạy.\n\nCập nhật sẽ thoát tool và khởi động lại — service nền vẫn chạy.\nTiếp tục?",
                AppTitle,
                System.Windows.Forms.MessageBoxButtons.YesNo,
                System.Windows.Forms.MessageBoxIcon.Question));
            if (answer != System.Windows.Forms.DialogResult.Yes)
                return;
        }

        try
        {
            IsApplyingUpdate = true;
            _statusTimer.Stop();

            var installDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var pid = Environment.ProcessId;

            await UpdateApplier.ApplyAndRestartAsync(
                downloadUrl.Trim(),
                pid,
                installDir,
                (level, message) => PushLog(new LogPayload { Level = level, Message = message }))
                .ConfigureAwait(false);

            RunOnUi(() => _uiOwner?.FindForm()?.Close());
        }
        catch (Exception ex)
        {
            IsApplyingUpdate = false;
            _statusTimer.Start();
            PushLog(new LogPayload { Level = "error", Message = $"Cập nhật thất bại: {ex.Message}" });
        }
    }

    private static object BuildWorkspacePayload()
    {
        var issues = WorkspacePathsStore.ValidateRoots();
        return new
        {
            paths = WorkspacePathsStore.GetAll(),
            definitions = WorkspacePathsStore.Definitions.Select(d => new
            {
                d.Key,
                d.Label,
                d.Hint,
                d.Scope
            }),
            pathsFile = WorkspacePathsStore.LocalFilePath,
            rootIssues = issues.Select(i => new { i.Key, i.Label, i.Message }),
            isConfigured = issues.Count == 0
        };
    }

    private void SendWorkspacePaths()
    {
        var allServices = _platformServices.Values.SelectMany(s => s);
        _pushToUi(new BridgeResponse
        {
            Type = "workspacePaths",
            Payload = new
            {
                paths = WorkspacePathsStore.GetAll(),
                definitions = WorkspacePathsStore.Definitions.Select(d => new
                {
                    d.Key,
                    d.Label,
                    d.Hint,
                    d.Scope
                }),
                pathsFile = WorkspacePathsStore.LocalFilePath,
                rootIssues = WorkspacePathsStore.ValidateRoots().Select(i => new { i.Key, i.Label, i.Message }),
                missingServices = WorkspacePathsStore.ValidateServicePaths(allServices),
                isConfigured = WorkspacePathsStore.ValidateRoots().Count == 0
            }
        });
    }

    private void SaveWorkspacePaths(Dictionary<string, string>? paths)
    {
        if (paths == null || paths.Count == 0)
        {
            PushLog(new LogPayload { Level = "warning", Message = "Không có đường dẫn workspace để lưu." });
            return;
        }

        WorkspacePathsStore.Save(paths);
        ReloadAllServices();
        PushLog(new LogPayload { Level = "success", Message = $"Đã lưu workspace paths → {WorkspacePathsStore.LocalFilePath}" });
        SendWorkspacePaths();
        PushServicesList();
    }

    private void BrowseWorkspaceFolder(string? pathKey)
    {
        if (string.IsNullOrWhiteSpace(pathKey))
            return;

        var def = WorkspacePathsStore.Definitions.FirstOrDefault(d =>
            d.Key.Equals(pathKey, StringComparison.OrdinalIgnoreCase));
        if (def == null)
            return;

        var current = WorkspacePathsStore.GetAll().TryGetValue(pathKey, out var p) ? p : null;
        var selected = RunOnUi(() => BackupFileDialogs.PickFolder(_uiOwner, def.Hint, current));
        if (string.IsNullOrWhiteSpace(selected))
            return;

        var paths = WorkspacePathsStore.GetAll();
        paths[pathKey] = selected;
        WorkspacePathsStore.Save(paths);
        ReloadAllServices();

        _pushToUi(new BridgeResponse
        {
            Type = "workspaceFolderPicked",
            Payload = new { pathKey, path = selected, workspace = BuildWorkspacePayload() }
        });
        PushServicesList();
    }

    private void ReloadAllServices()
    {
        foreach (var platform in _platforms)
            _platformServices[platform.Id] = LoadServices(platform.ConfigFile);

        ProcessStatusCache.Invalidate();
        _lastStatusSnapshot = "";
    }

    private void ReloadServicesFromDisk()
    {
        ReloadAllServices();
        var count = GetActiveServices().Count;
        PushServicesList();
        PushLog(new LogPayload
        {
            Level = "success",
            Message = $"Đã tải lại danh sách service ({count} mục). Sửa services*.json → lưu file → tool tự cập nhật."
        });
    }

    private void PushPlatformChanged()
    {
        _pushToUi(new BridgeResponse
        {
            Type = "platformChanged",
            Payload = new { platformId = _activePlatformId }
        });
    }

    private async Task SendGlobalConfigAsync(string category)
    {
        var services = GetServicesByCategory(category);
        var config = await GlobalConfigManager.LoadAsync(_activePlatformId, category, services).ConfigureAwait(false);

        _pushToUi(new BridgeResponse
        {
            Type = "globalConfig",
            Payload = new
            {
                platformId = _activePlatformId,
                category,
                config
            }
        });
    }

    private async Task SaveGlobalConfigAsync(BridgeRequest request)
    {
        if (request.Config == null)
            return;

        var category = request.Category ?? "BE";
        var services = GetServicesByCategory(category);
        var applicable = category.Equals("FE", StringComparison.OrdinalIgnoreCase)
            ? services
            : services.Where(s => !s.IsExe && !string.IsNullOrEmpty(s.ResolveConfigPath())).ToList();

        await GlobalConfigManager.SaveAndApplyAsync(_activePlatformId, category, request.Config, services).ConfigureAwait(false);

        var skipped = services.Count - applicable.Count;
        var skippedNote = skipped > 0 ? $" (bỏ qua {skipped} không có appsettings, ví dụ Redis)" : "";
        PushLog(new LogPayload
        {
            Level = "success",
            Message = $"Đã lưu cấu hình chung ({category}) cho {applicable.Count} service(s){skippedNote}."
        });
    }

    private async Task RestartServiceAsync(string serviceId, string? platformId = null)
    {
        if (!TryAcquireServiceOperation(serviceId, "run"))
        {
            var busy = FindService(serviceId, platformId);
            if (busy != null)
            {
                PushLog(new LogPayload
                {
                    ServiceId = busy.Id,
                    ServiceName = busy.Name,
                    Level = "warning",
                    Message = $"{busy.Name} đang khởi động — vui lòng đợi."
                });
            }
            return;
        }

        PushRunProgress(serviceId, true, "Đang restart...");
        PushServicesList();

        try
        {
            StopService(serviceId, platformId);
            await Task.Delay(500).ConfigureAwait(false);
            var service = FindService(serviceId, platformId);
            if (service != null)
                await RunServiceCoreAsync(new BridgeRequest { ServiceId = serviceId, PlatformId = platformId }, service).ConfigureAwait(false);
        }
        finally
        {
            PushRunProgress(serviceId, false, "");
            ReleaseServiceOperation(serviceId);
            RefreshAllStatuses();
        }
    }

    private void PushServicesList()
    {
        var services = GetActiveServices();
        RefreshStatuses(services);

        var be = services.Where(s => s.IsBackEnd).Select(ToStateDto).ToList();
        var fe = services.Where(s => s.IsFrontEnd).Select(ToStateDto).ToList();

        _pushToUi(new BridgeResponse
        {
            Type = "services",
            Payload = new { backEnd = be, frontEnd = fe, platformId = _activePlatformId }
        });
    }

    private async Task SendServiceDetailAsync(string serviceId, string? platformId = null)
    {
        var service = FindService(serviceId, platformId);
        if (service == null)
            return;

        ProcessTreeKiller.FindRunningProcess(service);
        var config = await ConfigFileManager.ReadConfigAsync(service).ConfigureAwait(false);

        _pushToUi(new BridgeResponse
        {
            Type = "serviceDetail",
            Payload = new ServiceDetailDto
            {
                Id = service.Id,
                Name = service.Name,
                Type = service.Type,
                IsRunning = service.IsRunning || ProcessTreeKiller.FindRunningProcess(service) != null,
                Url = service.Url,
                Host = config.Host,
                Port = config.Port,
                Scheme = config.Scheme,
                ConnectionString = config.ConnectionString,
                EnvVars = config.EnvVars ?? new Dictionary<string, string>(),
                ConfigPath = service.ResolveConfigPath(),
                ProjectPath = service.ResolveProjectPath()
            }
        });
    }

    private async Task SaveConfigAsync(BridgeRequest request)
    {
        var service = FindService(request.ServiceId!, request.PlatformId);
        if (service == null || request.Config == null)
            return;

        await ConfigFileManager.SaveConfigAsync(service, request.Config).ConfigureAwait(false);
        PushLog(new LogPayload { ServiceId = service.Id, Level = "success", Message = $"Đã lưu & sync config → source + bin output" });
        await SendServiceDetailAsync(service.Id, request.PlatformId).ConfigureAwait(false);
    }

    private async Task RunServiceAsync(BridgeRequest request)
    {
        var service = FindService(request.ServiceId!, request.PlatformId);
        if (service == null)
            return;

        if (!TryAcquireServiceOperation(service.Id, "run"))
        {
            PushLog(new LogPayload
            {
                ServiceId = service.Id,
                ServiceName = service.Name,
                Level = "warning",
                Message = $"{service.Name} đang khởi động — vui lòng đợi."
            });
            return;
        }

        PushRunProgress(service.Id, true, "Đang khởi động...");
        PushServicesList();

        try
        {
            await RunServiceCoreAsync(request, service).ConfigureAwait(false);
        }
        finally
        {
            PushRunProgress(service.Id, false, "");
            ReleaseServiceOperation(service.Id);
            RefreshAllStatuses();
        }
    }

    private async Task RunServiceCoreAsync(BridgeRequest request, ServiceConfig service)
    {
        if (request.Config != null)
            await ConfigFileManager.SaveConfigAsync(service, request.Config).ConfigureAwait(false);

        var existing = ProcessTreeKiller.FindRunningProcess(service);
        if (existing != null && !existing.HasExited)
        {
            service.ManagedProcess = existing;
            PushLog(new LogPayload
            {
                ServiceId = service.Id,
                ServiceName = service.Name,
                Level = "warning",
                Message = $"{service.Name} đã chạy nền (PID {existing.Id}) — không có log. STOP rồi RUN lại từ tool để xem log."
            });
            return;
        }

        ProcessTreeKiller.KillByService(service);
        try
        {
            _commandRunner.ShowConsoleWindow = RunSettingsStore.ShowConsoleWindow;
            if (!service.IsExe && !service.IsFrontEnd)
            {
                var synced = await ConfigFileManager.SyncSourceConfigToOutputAsync(service).ConfigureAwait(false);
                if (synced > 0)
                {
                    PushLog(new LogPayload
                    {
                        ServiceId = service.Id,
                        Level = "info",
                        Message = $"Đã sync {synced} file config (appsettings, ocelot...) → bin output."
                    });
                }
            }

            await _commandRunner.RunServiceAsync(service).ConfigureAwait(false);
            service.SyncFolderPathFromDisk();
            var mode = RunSettingsStore.ShowConsoleWindow ? "console riêng" : "log trong tool";
            PushLog(new LogPayload
            {
                ServiceId = service.Id,
                ServiceName = service.Name,
                Level = "success",
                Message = $"{service.Name} đang chạy ({mode})..."
            });
        }
        catch (Exception ex)
        {
            PushLog(new LogPayload { ServiceId = service.Id, Level = "error", Message = ex.Message });
        }
    }

    private void StopService(string serviceId, string? platformId = null)
    {
        var service = FindService(serviceId, platformId);
        if (service == null)
            return;

        ProcessTreeKiller.KillByService(service);
        PushLog(new LogPayload { ServiceId = service.Id, Level = "warning", Message = $"Service {service.Name} đã dừng." });
        RefreshAllStatuses();
    }

    private async Task BuildServiceAsync(string serviceId, string? platformId = null)
    {
        var service = FindService(serviceId, platformId);
        if (service == null)
            return;

        if (service.IsExe)
        {
            PushLog(new LogPayload { ServiceId = service.Id, Level = "warning", Message = $"{service.Name} là executable — không có bước build." });
            return;
        }

        if (!TryAcquireServiceOperation(service.Id, "build"))
        {
            PushLog(new LogPayload
            {
                ServiceId = service.Id,
                ServiceName = service.Name,
                Level = "warning",
                Message = $"{service.Name} đang bận (build/khởi động) — vui lòng đợi."
            });
            return;
        }

        PushLog(new LogPayload { ServiceId = service.Id, Level = "info", Message = $"Bắt đầu build {service.Name}..." });
        _pushToUi(new BridgeResponse
        {
            Type = "buildProgress",
            Payload = new BuildProgressPayload
            {
                ServiceId = service.Id,
                Percent = 0,
                Label = "Chuẩn bị build...",
                Active = true
            }
        });

        try
        {
            var exitCode = await _commandRunner.RunBuildAsync(service).ConfigureAwait(false);

            if (exitCode == 0)
            {
                service.SyncFolderPathFromDisk();
                await ConfigFileManager.SyncSourceConfigToOutputAsync(service).ConfigureAwait(false);
            }

            PushLog(new LogPayload
            {
                ServiceId = service.Id,
                Level = exitCode == 0 ? "success" : "error",
                Message = exitCode == 0 ? $"Build {service.Name} thành công." : $"Build {service.Name} thất bại (exit {exitCode})."
            });
        }
        finally
        {
            ReleaseServiceOperation(service.Id);
            PushServicesList();
        }
    }

    private void RefreshAllStatuses(bool forceUiPush = true)
    {
        ProcessStatusCache.RefreshIfStale(force: forceUiPush);
        foreach (var list in _platformServices.Values)
            RefreshStatuses(list);

        if (!forceUiPush && !HasActiveStatusChanged())
            return;

        _lastStatusSnapshot = BuildActiveStatusSnapshot();
        PushServicesList();
    }

    private async Task RefreshStatusesAsync()
    {
        if (Interlocked.CompareExchange(ref _statusRefreshRunning, 1, 0) != 0)
            return;

        try
        {
            await Task.Run(() =>
            {
                ProcessStatusCache.RefreshIfStale();
                foreach (var list in _platformServices.Values)
                    RefreshStatuses(list);
            }).ConfigureAwait(false);

            if (!HasActiveStatusChanged())
                return;

            _lastStatusSnapshot = BuildActiveStatusSnapshot();
            PushServicesList();
        }
        finally
        {
            Interlocked.Exchange(ref _statusRefreshRunning, 0);
        }
    }

    private string BuildActiveStatusSnapshot() =>
        string.Join("|", GetActiveServices().Select(s => $"{s.Id}:{s.IsRunning}"));

    private bool HasActiveStatusChanged() =>
        BuildActiveStatusSnapshot() != _lastStatusSnapshot;

    private static void RefreshStatuses(List<ServiceConfig> services)
    {
        foreach (var svc in services)
        {
            if (svc.ManagedProcess != null && svc.ManagedProcess.HasExited)
                svc.ManagedProcess = null;

            if (!svc.IsRunning)
            {
                var found = ProcessStatusCache.FindForService(svc);
                if (found != null && !found.HasExited)
                    svc.ManagedProcess = found;
            }
        }
    }

    private List<ServiceConfig> GetActiveServices() =>
        _platformServices.TryGetValue(_activePlatformId, out var list) ? list : [];

    private ServiceConfig? FindService(string serviceId, string? platformId = null)
    {
        var pid = platformId ?? _activePlatformId;
        return _platformServices.TryGetValue(pid, out var list)
            ? list.FirstOrDefault(s => s.Id == serviceId)
            : null;
    }

    private List<ServiceConfig> GetServicesByCategory(string category) =>
        GetActiveServices().Where(s =>
            category.Equals("FE", StringComparison.OrdinalIgnoreCase) ? s.IsFrontEnd : s.IsBackEnd).ToList();

    public int CountRunningServices() =>
        GetRunningServices().Count;

    public int CountLockedRunningServices() =>
        GetRunningServices().Count(s => ServiceLocksStore.IsLocked(s.Id));

    private List<ServiceConfig> GetRunningServices()
    {
        ProcessStatusCache.RefreshIfStale(force: true);
        return _platformServices.Values
            .SelectMany(list => list)
            .Where(s => ProcessTreeKiller.FindRunningProcess(s, forceRefresh: true) != null)
            .ToList();
    }

    public int StopAllServices()
    {
        var stopped = 0;
        var skippedLocked = 0;
        foreach (var list in _platformServices.Values)
        {
            foreach (var service in list)
            {
                if (ProcessTreeKiller.FindRunningProcess(service, forceRefresh: true) == null)
                    continue;

                if (ServiceLocksStore.IsLocked(service.Id))
                {
                    skippedLocked++;
                    continue;
                }

                ProcessTreeKiller.KillByService(service);
                stopped++;
            }
        }

        if (stopped > 0 || skippedLocked > 0)
        {
            var skippedNote = skippedLocked > 0 ? $" ({skippedLocked} khóa — giữ chạy)" : "";
            PushLog(new LogPayload
            {
                Level = stopped > 0 ? "warning" : "info",
                Message = stopped > 0
                    ? $"Đã dừng {stopped} service{skippedNote}."
                    : $"Không dừng service nào{skippedNote}."
            });
        }

        RefreshAllStatuses();
        return stopped;
    }

    private void ToggleServiceLock(BridgeRequest request)
    {
        var service = FindService(request.ServiceId!, request.PlatformId);
        if (service == null)
            return;

        var locked = request.Locked ?? !ServiceLocksStore.IsLocked(service.Id);
        ServiceLocksStore.SetLocked(service.Id, locked);

        PushLog(new LogPayload
        {
            ServiceId = service.Id,
            ServiceName = service.Name,
            Level = "info",
            Message = locked
                ? $"{service.Name}: đã khóa — không bị Stop All / thoát tool dừng."
                : $"{service.Name}: đã mở khóa."
        });

        PushServicesList();
    }

    private void SendRunSettings()
    {
        _pushToUi(new BridgeResponse
        {
            Type = "runSettings",
            Payload = new { showConsoleWindow = RunSettingsStore.ShowConsoleWindow }
        });
    }

    private void SaveRunSettings(bool showConsoleWindow)
    {
        RunSettingsStore.SetShowConsoleWindow(showConsoleWindow);
        _commandRunner.ShowConsoleWindow = showConsoleWindow;
        PushLog(new LogPayload
        {
            Level = "info",
            Message = showConsoleWindow
                ? "Chế độ CMD riêng: log hiện trong cửa sổ CMD bên ngoài (Alt+Tab) — KHÔNG hiện trong Console Log. Đóng CMD = tắt service."
                : "Chế độ mặc định: log service hiện trong Console Log bên dưới (kéo mép trên khung log để phóng to)."
        });
        SendRunSettings();
    }

    private void PushLog(LogPayload payload)
    {
        if (string.IsNullOrEmpty(payload.ServiceName) && !string.IsNullOrEmpty(payload.ServiceId))
        {
            var svc = FindService(payload.ServiceId, _activePlatformId)
                      ?? _platformServices.Values.SelectMany(l => l).FirstOrDefault(s => s.Id == payload.ServiceId);
            if (svc != null)
                payload.ServiceName = svc.Name;
        }

        _pushToUi(new BridgeResponse { Type = "log", Payload = payload });
    }

    private T RunOnUi<T>(Func<T> action)
    {
        if (_uiOwner == null || !_uiOwner.InvokeRequired)
            return action();

        return (T)_uiOwner.Invoke(action)!;
    }

    private void RunOnUi(Action action)
    {
        if (_uiOwner == null || !_uiOwner.InvokeRequired)
            action();
        else
            _uiOwner.Invoke(action);
    }

    private void SendBackupPreview()
    {
        var platform = _platforms.First(p => p.Id == _activePlatformId);
        var services = GetActiveServices();
        var configCount = services.Count(s => !s.IsExe && s.GetSourceConfigFiles().Count > 0);
        var localInfo = LocalDefaultsStore.GetInfo(platform.Id);

        _pushToUi(new BridgeResponse
        {
            Type = "backupPreview",
            Payload = new BackupPreviewDto
            {
                PlatformId = platform.Id,
                PlatformName = platform.Name,
                BackEndCount = services.Count(s => s.IsBackEnd),
                FrontEndCount = services.Count(s => s.IsFrontEnd),
                ConfigFileCount = configCount,
                BackupsFolder = ConfigBackupManager.BackupsFolder,
                RecentBackups = ConfigBackupManager.ListRecentBackups(platform.Id),
                HasLocalDefaults = localInfo.Exists,
                LocalDefaultsPath = LocalDefaultsStore.GetPath(platform.Id),
                LocalDefaultsScannedAt = localInfo.ScannedAt,
                LocalDefaultsFileCount = localInfo.FileCount
            }
        });
    }

    private async Task ExportConfigAsync()
    {
        var platform = _platforms.First(p => p.Id == _activePlatformId);
        var services = GetActiveServices();
        var defaultName = $"backup-{platform.Id}-{DateTime.Now:yyyyMMdd-HHmmss}.json";

        var path = RunOnUi(() => BackupFileDialogs.PickSavePath(_uiOwner, defaultName));
        if (string.IsNullOrEmpty(path))
        {
            PushLog(new LogPayload { Level = "info", Message = "Export đã hủy." });
            return;
        }

        var package = await ConfigBackupManager.CreateBackupAsync(platform.Id, platform.Name, services).ConfigureAwait(false);
        await ConfigBackupManager.SaveBackupAsync(package, path).ConfigureAwait(false);

        var autoCopy = Path.Combine(ConfigBackupManager.BackupsFolder, Path.GetFileName(path));
        if (!path.Equals(autoCopy, StringComparison.OrdinalIgnoreCase))
            await ConfigBackupManager.SaveBackupAsync(package, autoCopy).ConfigureAwait(false);

        PushLog(new LogPayload
        {
            Level = "success",
            Message = $"Export thành công: {path} ({package.Services.Count} services, {package.GlobalConfigs.Count} global configs)"
        });

        SendBackupPreview();
    }

    private async Task PreviewImportAsync(string? filePath)
    {
        var path = filePath;
        if (string.IsNullOrWhiteSpace(path))
            path = RunOnUi(() => BackupFileDialogs.PickOpenPath(_uiOwner));

        if (string.IsNullOrWhiteSpace(path))
        {
            PushLog(new LogPayload { Level = "info", Message = "Chọn file đã hủy." });
            return;
        }

        if (!File.Exists(path))
        {
            PushLog(new LogPayload { Level = "error", Message = $"File không tồn tại: {path}" });
            return;
        }

        var package = await ConfigBackupManager.LoadBackupAsync(path).ConfigureAwait(false);
        var platform = _platforms.First(p => p.Id == _activePlatformId);
        var isLocal = path.Equals(LocalDefaultsStore.GetPath(platform.Id), StringComparison.OrdinalIgnoreCase);

        _pushToUi(new BridgeResponse
        {
            Type = "importPreview",
            Payload = ConfigBackupManager.BuildPreview(package, platform.Id, platform.Name, path, isLocal)
        });
    }

    private async Task ApplyImportAsync(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            PushLog(new LogPayload { Level = "error", Message = "Chưa chọn file backup để apply." });
            return;
        }

        var package = await ConfigBackupManager.LoadBackupAsync(filePath).ConfigureAwait(false);
        var platform = _platforms.First(p => p.Id == _activePlatformId);
        var services = GetActiveServices();
        var fileCount = package.Services.Sum(s => s.ConfigFiles.Count > 0 ? s.ConfigFiles.Count : (s.RawContent != null ? 1 : 0));

        var confirmed = RunOnUi(() =>
            BackupFileDialogs.ConfirmApplyToSource(_uiOwner, platform.Name, package.PlatformName, fileCount));

        if (!confirmed)
        {
            PushLog(new LogPayload { Level = "info", Message = "Apply đã hủy." });
            return;
        }

        var result = await ConfigBackupManager.ApplyToSourceAsync(package, platform.Id, services).ConfigureAwait(false);

        foreach (var msg in result.Messages)
        {
            var level = msg.StartsWith("Bỏ qua", StringComparison.OrdinalIgnoreCase) ? "warning"
                : msg.StartsWith("[Cảnh báo]", StringComparison.OrdinalIgnoreCase) ? "warning"
                : msg.StartsWith("[Lưu ý]", StringComparison.OrdinalIgnoreCase) ? "info"
                : msg.StartsWith("[Giữ nguyên]", StringComparison.OrdinalIgnoreCase) ? "info"
                : msg.StartsWith("[Sync bin]", StringComparison.OrdinalIgnoreCase) ? "info"
                : "success";
            PushLog(new LogPayload { Level = level, Message = msg });
        }

        PushLog(new LogPayload
        {
            Level = "success",
            Message = $"Apply vào source: {result.ChangedCount} file đổi, {result.UnchangedCount} giữ nguyên (đã giống source), {result.SkippedCount} bỏ qua. Đã sync sang bin output."
        });

        _pushToUi(new BridgeResponse { Type = "importResult", Payload = result });
        SendBackupPreview();
    }

    private async Task ApplyLocalDefaultsAsync()
    {
        var path = LocalDefaultsStore.GetPath(_activePlatformId);
        if (!File.Exists(path))
        {
            PushLog(new LogPayload { Level = "warning", Message = "Chưa có local defaults. Bấm 'Quét từ Source' trước." });
            return;
        }

        await ApplyImportAsync(path).ConfigureAwait(false);
    }

    private bool TryAcquireServiceOperation(string serviceId, string operation)
    {
        lock (_serviceOpsGate)
        {
            if (_serviceOps.ContainsKey(serviceId))
                return false;

            _serviceOps[serviceId] = operation;
            return true;
        }
    }

    private void ReleaseServiceOperation(string serviceId)
    {
        lock (_serviceOpsGate)
        {
            _serviceOps.Remove(serviceId);
        }
    }

    private string? GetServiceOperation(string serviceId)
    {
        lock (_serviceOpsGate)
        {
            return _serviceOps.TryGetValue(serviceId, out var op) ? op : null;
        }
    }

    private void PushRunProgress(string serviceId, bool active, string label)
    {
        _pushToUi(new BridgeResponse
        {
            Type = "runProgress",
            Payload = new BuildProgressPayload
            {
                ServiceId = serviceId,
                Active = active,
                Percent = active ? 0 : 100,
                Label = label
            }
        });
    }

    private ServiceStateDto ToStateDto(ServiceConfig s)
    {
        var op = GetServiceOperation(s.Id);
        return new ServiceStateDto
        {
            Id = s.Id,
            Name = s.Name,
            Type = s.Type,
            IsRunning = s.IsRunning,
            Url = s.Url,
            ProcessName = s.IsRunning
                ? (s.IsExe ? s.ManagedProcess?.ProcessName ?? "" : Path.GetFileNameWithoutExtension(s.DllName))
                : "",
            DllName = s.DllName,
            IsExe = s.IsExe,
            IsLocked = ServiceLocksStore.IsLocked(s.Id),
            IsStarting = op == "run",
            IsBuilding = op == "build"
        };
    }

    private static List<ServiceConfig> LoadServices(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, fileName);
        if (!File.Exists(path))
            return [];

        var json = File.ReadAllText(path);
        var services = JsonSerializer.Deserialize<List<ServiceConfig>>(json, JsonOptions) ?? [];

        foreach (var svc in services)
        {
            if (string.IsNullOrWhiteSpace(svc.Id))
                svc.Id = $"{svc.Name}-{Guid.NewGuid():N}"[..Math.Min(24, svc.Name.Length + 9)];

            if (string.IsNullOrWhiteSpace(svc.Type))
                svc.Type = svc.IsFrontEndByConfig() ? "FE" : "BE";

            svc.FolderPath = WorkspacePathsStore.Resolve(svc.FolderPath);
            svc.ProjectPath = WorkspacePathsStore.Resolve(svc.ProjectPath);
            svc.ConfigPath = WorkspacePathsStore.Resolve(svc.ConfigPath);
            svc.SyncFolderPathFromDisk();
        }

        return services;
    }
}

file static class ServiceConfigExtensions
{
    public static bool IsFrontEndByConfig(this ServiceConfig svc) =>
        string.Equals(svc.Type, "FE", StringComparison.OrdinalIgnoreCase)
        || (string.IsNullOrEmpty(svc.DllName) && !svc.IsExe && File.Exists(Path.Combine(svc.FolderPath, "package.json")));
}
