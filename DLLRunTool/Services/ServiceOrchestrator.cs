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
    private readonly Dictionary<string, string> _healthStatus = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (int Attempt, int Max)> _healthPollProgress = new(StringComparer.OrdinalIgnoreCase);

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

        ServiceLocksStore.EnsureProtectedDefaults(_platformServices.Values.SelectMany(s => s));

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
                case "cleanBin":
                    await CleanBinServiceAsync(request.ServiceId!, request.PlatformId).ConfigureAwait(false);
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
                    SaveRunSettings(request);
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
                case "saveUiState":
                    SaveUiState(request);
                    break;
                case "openFolder":
                    OpenServiceFolder(request);
                    break;
                case "openCmdAtProject":
                    OpenCmdAtProject(request);
                    break;
                case "runStackPreset":
                    await RunStackPresetAsync(request.PresetId).ConfigureAwait(false);
                    break;
                case "stopStackPreset":
                    StopStackPreset(request.PresetId);
                    break;
                case "scanConfigSecrets":
                    SendConfigSecretScan();
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
                theme = UiStateStore.Current.Theme,
                appAuthor = AppAuthor,
                appTitle = AppTitle,
                appVersion = AppVersionInfo.Current,
                workspace = BuildWorkspacePayload(),
                runSettings = RunSettingsStore.ToPayload(),
                uiState = UiStateStore.Current,
                stackPresets = StackPresetsStore.Load().Select(p => new { p.Id, p.Name, count = p.ServiceIds.Count }),
                configSecretFindings = ConfigSecretScanner.ScanServices(GetActiveServices())
                    .Select(f => new { f.ServiceName, f.FilePath, f.Reason })
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
            var answer = RunOnUi(() => StyledMessageBox.Show(
                _uiOwner?.FindForm(),
                $"Có {running} service đang chạy.\n\nCập nhật sẽ thoát tool và khởi động lại — service nền vẫn chạy.\nTiếp tục?",
                AppTitle,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question));
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

        ServiceLocksStore.EnsureProtectedDefaults(_platformServices.Values.SelectMany(s => s));
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
        var allServices = GetActiveServices();
        var services = GetServicesByCategory(category);
        var config = await GlobalConfigManager.LoadAsync(_activePlatformId, category, services, allServices).ConfigureAwait(false);

        _pushToUi(new BridgeResponse
        {
            Type = "globalConfig",
            Payload = new
            {
                platformId = _activePlatformId,
                category,
                config,
                feBindings = category.Equals("FE", StringComparison.OrdinalIgnoreCase)
                    ? FeConfigResolver.DescribeBindings(allServices)
                    : null
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
        var service = FindService(serviceId, platformId);
        if (service != null && IsRunBlocked(service))
        {
            PushLog(new LogPayload
            {
                ServiceId = service.Id,
                ServiceName = service.Name,
                Level = "warning",
                Message = $"{service.Name} đang khóa — mở khóa trước khi restart."
            });
            return;
        }

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
            var target = FindService(serviceId, platformId);
            if (target != null)
                await RunServiceCoreAsync(new BridgeRequest { ServiceId = serviceId, PlatformId = platformId }, target).ConfigureAwait(false);
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

        if (service.IsFrontEnd)
        {
            var allServices = GetActiveServices();
            config.EnvVars ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var template = await ConfigFileManager.ReadEnvJsTemplateAsync(service).ConfigureAwait(false);
            config.EnvVars = FeConfigResolver.MergeTemplateAndFile(config.EnvVars, template);
            FeConfigResolver.ApplyDynamicBindings(config.EnvVars, allServices);
        }

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
                ProjectPath = service.ResolveProjectPath(),
                FeBindings = service.IsFrontEnd
                    ? FeConfigResolver.DescribeBindings(GetActiveServices()).ToList()
                    : null
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

        if (IsRunBlocked(service) && request.Confirmed != true)
        {
            PushLog(new LogPayload
            {
                ServiceId = service.Id,
                ServiceName = service.Name,
                Level = "warning",
                Message = $"{service.Name} đang khóa — mở khóa và xác nhận trước khi chạy."
            });
            return;
        }

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

        ProcessTreeKiller.KillByService(service, RunSettingsStore.StopGracefulTimeoutMs);
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
            var mode = RunSettingsStore.ShowConsoleWindow ? "log tool + CMD mirror" : "log trong tool";
            PushLog(new LogPayload
            {
                ServiceId = service.Id,
                ServiceName = service.Name,
                Level = "success",
                Message = $"{service.Name} đang chạy ({mode})..."
            });
            if (ServiceHealthChecker.CanCheck(service))
            {
                _healthStatus[service.Id] = "starting";
                _healthPollProgress[service.Id] = (0, ServiceHealthChecker.MaxPollAttempts);
                _ = PollHealthAsync(service);
            }
            else
            {
                _healthStatus.Remove(service.Id);
                _healthPollProgress.Remove(service.Id);
            }
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

        ProcessTreeKiller.KillByService(service, RunSettingsStore.StopGracefulTimeoutMs);
        _healthStatus[service.Id] = "unknown";
        _healthPollProgress.Remove(service.Id);
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

        if (IsRunBlocked(service))
        {
            PushLog(new LogPayload
            {
                ServiceId = service.Id,
                ServiceName = service.Name,
                Level = "warning",
                Message = $"{service.Name} đang khóa — mở khóa trước khi build."
            });
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
            if (RunSettingsStore.CleanBinBeforeBuild && !service.IsFrontEnd)
            {
                try
                {
                    var removed = RunSettingsStore.CleanProjectOutputFolders(service);
                    PushLog(new LogPayload
                    {
                        ServiceId = service.Id,
                        ServiceName = service.Name,
                        Level = "info",
                        Message = removed > 0
                            ? $"{service.Name}: đã xóa bin/obj trước build."
                            : $"{service.Name}: không có bin/obj để xóa trước build."
                    });
                }
                catch (Exception ex)
                {
                    PushLog(new LogPayload
                    {
                        ServiceId = service.Id,
                        ServiceName = service.Name,
                        Level = "error",
                        Message = $"{service.Name}: xóa bin/obj thất bại — {ex.Message}"
                    });
                    return;
                }
            }

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

    private async Task CleanBinServiceAsync(string serviceId, string? platformId = null)
    {
        var service = FindService(serviceId, platformId);
        if (service == null)
            return;

        if (service.IsExe || service.IsFrontEnd)
        {
            PushLog(new LogPayload
            {
                ServiceId = service.Id,
                ServiceName = service.Name,
                Level = "warning",
                Message = $"{service.Name} không hỗ trợ xóa bin/obj."
            });
            return;
        }

        if (service.IsRunning)
        {
            PushLog(new LogPayload
            {
                ServiceId = service.Id,
                ServiceName = service.Name,
                Level = "warning",
                Message = $"{service.Name} đang chạy — dừng service trước khi xóa bin/obj."
            });
            return;
        }

        if (!TryAcquireServiceOperation(service.Id, "clean"))
        {
            PushLog(new LogPayload
            {
                ServiceId = service.Id,
                ServiceName = service.Name,
                Level = "warning",
                Message = $"{service.Name} đang bận — vui lòng đợi."
            });
            return;
        }

        try
        {
            await Task.Run(() =>
            {
                var removed = RunSettingsStore.CleanProjectOutputFolders(service);
                PushLog(new LogPayload
                {
                    ServiceId = service.Id,
                    ServiceName = service.Name,
                    Level = removed > 0 ? "success" : "info",
                    Message = removed > 0
                        ? $"{service.Name}: đã xóa bin/obj ({service.ResolveSourceProjectPath()})."
                        : $"{service.Name}: không có thư mục bin/obj để xóa."
                });
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            PushLog(new LogPayload
            {
                ServiceId = service.Id,
                ServiceName = service.Name,
                Level = "error",
                Message = $"{service.Name}: xóa bin/obj thất bại — {ex.Message}"
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

                ProcessTreeKiller.KillByService(service, RunSettingsStore.StopGracefulTimeoutMs);
                _healthStatus[service.Id] = "unknown";
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
            Payload = RunSettingsStore.ToPayload()
        });
    }

    private void SaveRunSettings(BridgeRequest request)
    {
        var showAll = request.ShowConsoleWindow ?? RunSettingsStore.ShowConsoleWindow;
        var showSelected = request.ShowConsoleSelected ?? RunSettingsStore.ShowConsoleSelected;
        var selectedId = request.ConsoleSelectedServiceId ?? RunSettingsStore.ConsoleSelectedServiceId;

        if (request.ShowConsoleWindow == true)
            showSelected = false;
        if (request.ShowConsoleSelected == true)
            showAll = false;

        if (!showSelected)
            selectedId = null;

        RunSettingsStore.Set(showAll, showSelected, selectedId);

        if (request.ServiceEnvironmentVariables != null)
            RunSettingsStore.SetServiceEnvironmentVariables(request.ServiceEnvironmentVariables);

        if (request.CleanBinBeforeBuild.HasValue)
            RunSettingsStore.SetCleanBinBeforeBuild(request.CleanBinBeforeBuild.Value);

        _commandRunner.ShowConsoleWindow = showAll;

        ApplyConsoleMirrors(showAll, showSelected, selectedId);
        SendRunSettings();
    }

    private void ApplyConsoleMirrors(bool showAll, bool showSelected, string? selectedId)
    {
        var running = GetActiveServices()
            .Where(s => s.IsRunning && s.ManagedProcess != null && !s.ManagedProcess.HasExited)
            .ToList();

        if (showAll)
        {
            foreach (var service in running)
            {
                EnsureServiceLogRegistered(service);
                ServiceLogMirror.OpenMirror(service.Id, service.Name);
            }

            PushLog(new LogPayload
            {
                Level = "info",
                Message = running.Count > 0
                    ? $"CMD tất cả: đã mở {running.Count} cửa sổ mirror — log vẫn hiện trong Console Log."
                    : "CMD tất cả: cửa sổ mirror mở khi RUN service."
            });
            return;
        }

        ServiceLogMirror.CloseAllMirrors();

        if (showSelected && !string.IsNullOrWhiteSpace(selectedId))
        {
            var service = FindService(selectedId, _activePlatformId)
                          ?? _platformServices.Values.SelectMany(l => l).FirstOrDefault(s => s.Id == selectedId);

            if (service != null)
            {
                EnsureServiceLogRegistered(service);
                ServiceLogMirror.OpenMirror(service.Id, service.Name);
                PushLog(new LogPayload
                {
                    ServiceId = service.Id,
                    ServiceName = service.Name,
                    Level = "info",
                    Message = $"CMD đang chọn: mirror cho {service.Name} — log vẫn hiện trong Console Log."
                });
                return;
            }

            PushLog(new LogPayload
            {
                Level = "warning",
                Message = "CMD đang chọn: không tìm thấy service trong dropdown."
            });
            return;
        }

        PushLog(new LogPayload
        {
            Level = "info",
            Message = "Log chỉ hiện trong Console Log bên dưới."
        });
    }

    private static void EnsureServiceLogRegistered(ServiceConfig service)
    {
        var logDir = Path.Combine(AppContext.BaseDirectory, "service-logs");
        var logPath = Path.Combine(logDir, $"{service.Id}.log");
        if (!File.Exists(logPath))
            logPath = ServiceLogMirror.EnsureLogFile(service.Id, service.Name);
        ServiceLogMirror.Register(service.Id, logPath);
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
        var preview = ConfigBackupManager.BuildPreview(package, platform.Id, platform.Name, path, isLocal);
        var dryRun = await ConfigBackupManager.ApplyToSourceAsync(package, platform.Id, GetActiveServices(), dryRun: true).ConfigureAwait(false);
        preview.DryRunChangedCount = dryRun.ChangedCount;
        preview.DryRunUnchangedCount = dryRun.UnchangedCount;
        preview.DryRunSkippedCount = dryRun.SkippedCount;
        preview.DryRunMessages = dryRun.Messages;

        _pushToUi(new BridgeResponse
        {
            Type = "importPreview",
            Payload = preview
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
        var locked = ServiceLocksStore.IsLocked(s.Id);
        _healthPollProgress.TryGetValue(s.Id, out var hp);
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
            IsLocked = locked,
            IsRunProtected = s.RunProtected,
            IsRunBlocked = IsRunBlocked(s),
            IsStarting = op == "run",
            IsBuilding = op == "build",
            HealthStatus = ServiceHealthChecker.CanCheck(s)
                ? _healthStatus.GetValueOrDefault(s.Id, s.IsRunning ? "starting" : "unknown")
                : "",
            HealthCheckAttempt = hp.Attempt,
            HealthCheckMaxAttempts = hp.Max > 0 ? hp.Max : ServiceHealthChecker.MaxPollAttempts,
            EnableHealthCheck = s.EnableHealthCheck && !s.IsExe && !string.IsNullOrWhiteSpace(s.Url),
            Notes = s.Notes ?? ""
        };
    }

    private void SaveUiState(BridgeRequest request)
    {
        UiStateStore.Patch(state =>
        {
            if (!string.IsNullOrWhiteSpace(request.View))
                state.View = request.View!;
            if (!string.IsNullOrWhiteSpace(request.RailSection))
                state.RailSection = request.RailSection!;
            if (!string.IsNullOrWhiteSpace(request.HandbookTab))
                state.HandbookTab = request.HandbookTab!;
            if (!string.IsNullOrWhiteSpace(request.LastServiceView))
                state.LastServiceView = request.LastServiceView!;
            if (!string.IsNullOrWhiteSpace(request.Category))
                state.Category = request.Category!;
            if (!string.IsNullOrWhiteSpace(request.PlatformId))
                state.PlatformId = request.PlatformId!;
            if (request.LogFilterServiceId != null)
                state.LogFilterServiceId = request.LogFilterServiceId;
            if (!string.IsNullOrWhiteSpace(request.Theme))
                state.Theme = request.Theme!;
        });
    }

    private void OpenServiceFolder(BridgeRequest request)
    {
        var service = FindService(request.ServiceId!, request.PlatformId);
        if (service == null)
            return;

        var path = FolderOpener.ResolveFolder(service, request.FolderKind ?? "project");
        FolderOpener.OpenPath(path);
        PushLog(new LogPayload
        {
            ServiceId = service.Id,
            Level = "info",
            Message = $"Đã mở thư mục {request.FolderKind}: {path}"
        });
    }

    private void OpenCmdAtProject(BridgeRequest request)
    {
        var service = FindService(request.ServiceId!, request.PlatformId);
        if (service == null)
            return;

        var dir = service.ResolveSourceProjectPath();
        FolderOpener.OpenCmdAt(dir, service.Name);
        PushLog(new LogPayload
        {
            ServiceId = service.Id,
            Level = "info",
            Message = $"CMD tại project: {dir}"
        });
    }

    private async Task RunStackPresetAsync(string? presetId)
    {
        var preset = string.IsNullOrWhiteSpace(presetId) ? null : StackPresetsStore.Find(presetId);
        if (preset == null)
        {
            PushLog(new LogPayload { Level = "error", Message = "Không tìm thấy dev stack preset." });
            return;
        }

        PushLog(new LogPayload { Level = "info", Message = $"Bắt đầu stack «{preset.Name}» ({preset.ServiceIds.Count} service)..." });
        foreach (var id in preset.ServiceIds)
        {
            var service = FindService(id);
            if (service == null)
            {
                PushLog(new LogPayload { Level = "warning", Message = $"Bỏ qua {id} — không có trong danh sách." });
                continue;
            }

            if (IsRunBlocked(service))
            {
                PushLog(new LogPayload { ServiceId = service.Id, Level = "warning", Message = $"{service.Name} bị khóa — bỏ qua." });
                continue;
            }

            if (ProcessTreeKiller.FindRunningProcess(service) != null)
                continue;

            await RunServiceAsync(new BridgeRequest { ServiceId = service.Id, PlatformId = _activePlatformId }).ConfigureAwait(false);
            await Task.Delay(600).ConfigureAwait(false);
        }

        PushLog(new LogPayload { Level = "success", Message = $"Hoàn tất chạy stack «{preset.Name}»." });
    }

    private void StopStackPreset(string? presetId)
    {
        var preset = string.IsNullOrWhiteSpace(presetId) ? null : StackPresetsStore.Find(presetId);
        if (preset == null)
            return;

        var stopped = 0;
        foreach (var id in preset.ServiceIds.AsEnumerable().Reverse())
        {
            var service = FindService(id);
            if (service == null || ServiceLocksStore.IsLocked(service.Id))
                continue;

            if (ProcessTreeKiller.FindRunningProcess(service, forceRefresh: true) == null)
                continue;

            ProcessTreeKiller.KillByService(service, RunSettingsStore.StopGracefulTimeoutMs);
            _healthStatus[service.Id] = "unknown";
            stopped++;
        }

        PushLog(new LogPayload
        {
            Level = stopped > 0 ? "warning" : "info",
            Message = stopped > 0 ? $"Đã dừng {stopped} service trong stack «{preset.Name}»." : $"Stack «{preset.Name}» — không có service nào đang chạy."
        });
        RefreshAllStatuses();
    }

    private void SendConfigSecretScan()
    {
        var findings = ConfigSecretScanner.ScanServices(GetActiveServices());
        _pushToUi(new BridgeResponse
        {
            Type = "configSecretScan",
            Payload = findings.Select(f => new { f.ServiceName, f.FilePath, f.Reason })
        });
    }

    private async Task PollHealthAsync(ServiceConfig service)
    {
        if (!ServiceHealthChecker.CanCheck(service))
            return;

        var delays = ServiceHealthChecker.PollDelaysBeforeCheckMs;
        var max = delays.Length;

        for (var i = 0; i < max; i++)
        {
            await Task.Delay(delays[i]).ConfigureAwait(false);

            if (!service.IsRunning)
            {
                _healthStatus[service.Id] = "crashed";
                _healthPollProgress.Remove(service.Id);
                PushServicesList();
                return;
            }

            var attempt = i + 1;
            _healthStatus[service.Id] = "checking";
            _healthPollProgress[service.Id] = (attempt, max);
            PushServicesList();

            var status = await ServiceHealthChecker.CheckAsync(service).ConfigureAwait(false);

            if (status == "healthy")
            {
                _healthStatus[service.Id] = "healthy";
                _healthPollProgress.Remove(service.Id);
                PushServicesList();
                return;
            }

            if (status == "no-health")
            {
                _healthStatus[service.Id] = "no-health";
                _healthPollProgress.Remove(service.Id);
                PushLog(new LogPayload
                {
                    ServiceId = service.Id,
                    ServiceName = service.Name,
                    Level = "info",
                    Message = $"{service.Name}: không có endpoint health — chỉ theo process đang chạy."
                });
                PushServicesList();
                return;
            }

            var isLast = i == max - 1;
            if (!isLast)
            {
                _healthStatus[service.Id] = "retrying";
                _healthPollProgress[service.Id] = (attempt, max);
                var waitSec = delays[i + 1] / 1000;
                PushLog(new LogPayload
                {
                    ServiceId = service.Id,
                    ServiceName = service.Name,
                    Level = "warning",
                    Message = $"{service.Name}: health chưa OK — thử lại sau ~{waitSec}s ({attempt}/{max})."
                });
                PushServicesList();
                continue;
            }

            _healthStatus[service.Id] = status == "pending" ? "unhealthy" : status;
            _healthPollProgress.Remove(service.Id);
            PushServicesList();

            if (_healthStatus[service.Id] == "unhealthy")
            {
                PushLog(new LogPayload
                {
                    ServiceId = service.Id,
                    ServiceName = service.Name,
                    Level = "warning",
                    Message = $"{service.Name}: health lỗi sau {max} lần thử ({service.Url})."
                });
            }
        }
    }

    private static bool IsRunBlocked(ServiceConfig service) =>
        service.RunProtected && ServiceLocksStore.IsLocked(service.Id);

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
