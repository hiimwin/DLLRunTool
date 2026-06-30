using System.Text.Json;
using System.Text.Json.Serialization;
using DLLRunTool.Models;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace DLLRunTool.Services;

public sealed class K8sBridgeHost : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly WebView2 _webView;
    private readonly K8sService _k8s = new();
    private K8sTerminalSessions? _terminalSessions;
    private bool _pageReady;
    private bool _disposed;
    private int _connectGeneration;

    public K8sBridgeHost(WebView2 webView) => _webView = webView;

    public bool IsActive => _k8s.IsConnected;

    /// <summary>Chỉ khởi tạo WebView + HTML — chưa kết nối API K8s (tiết kiệm RAM).</summary>
    public async Task<bool> InitializeWebViewAsync(CancellationToken ct = default)
    {
        await WebView2EnvironmentHelper.EnsureCoreWebView2Async(_webView, ct).ConfigureAwait(true);

        _webView.CoreWebView2.Settings.AreDevToolsEnabled = true;
        _webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
        _webView.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;
        _webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
        _webView.CoreWebView2.NavigationCompleted -= OnNavigationCompleted;
        _webView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;

        _pageReady = false;
        var wwwroot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        var htmlPath = Path.Combine(wwwroot, "k8s_dashboard.html");
        if (!File.Exists(htmlPath))
        {
            PostMessage("error", new { message = "Không tìm thấy k8s_dashboard.html trong wwwroot." });
            return false;
        }

        _webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
            "k8s.local",
            wwwroot,
            CoreWebView2HostResourceAccessKind.Allow);

        _webView.CoreWebView2.Navigate("https://k8s.local/k8s_dashboard.html");
        return true;
    }

    public async Task DisconnectAndReleaseAsync()
    {
        _pageReady = false;

        if (_webView.CoreWebView2 != null)
        {
            _webView.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;
            _webView.CoreWebView2.NavigationCompleted -= OnNavigationCompleted;
            try
            {
                _webView.CoreWebView2.Navigate("about:blank");
            }
            catch
            {
                // ignore
            }
        }

        await _k8s.DisconnectAsync().ConfigureAwait(false);
    }

    private async void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (!e.IsSuccess || _webView.CoreWebView2 == null)
            return;

        var src = _webView.CoreWebView2.Source ?? "";
        if (!src.Contains("k8s_dashboard", StringComparison.OrdinalIgnoreCase))
            return;

        _pageReady = true;
        await SendClusterCatalogAsync().ConfigureAwait(false);
        await TryAutoConnectLastSessionAsync().ConfigureAwait(false);
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        _ = HandleWebMessageAsync(e.WebMessageAsJson);
    }

    private async Task HandleWebMessageAsync(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return;

        try
        {
            var request = JsonSerializer.Deserialize<K8sWebRequest>(json, JsonOptions);
            if (request == null || string.IsNullOrWhiteSpace(request.Action))
                return;

            switch (request.Action.ToLowerInvariant())
            {
                case "ready":
                    _pageReady = true;
                    await SendClusterCatalogAsync().ConfigureAwait(false);
                    PostMessage("theme", new { theme = UiStateStore.Current.Theme });
                    if (_k8s.IsConnected)
                        await NotifyConnectedStateAsync().ConfigureAwait(false);
                    else
                        await TryAutoConnectLastSessionAsync().ConfigureAwait(false);
                    break;
                case "listclusters":
                    await SendClusterCatalogAsync().ConfigureAwait(false);
                    if (!_k8s.IsConnected)
                        await TryAutoConnectLastSessionAsync().ConfigureAwait(false);
                    break;
                case "openterminal":
                case "createterminal":
                    CreateEmbeddedTerminal();
                    break;
                case "terminalinput":
                    SendTerminalInput(request.SessionId, request.Line);
                    break;
                case "closeterminal":
                    CloseEmbeddedTerminal(request.SessionId);
                    break;
                case "browsekubeconfig":
                    await BrowseKubeConfigAsync().ConfigureAwait(false);
                    break;
                case "importkubeconfig":
                    await ImportKubeConfigAsync(request).ConfigureAwait(false);
                    break;
                case "connectcluster":
                    await ConnectClusterAsync(request).ConfigureAwait(false);
                    break;
                case "disconnectcluster":
                    await DisconnectClusterAsync(keepSession: true).ConfigureAwait(false);
                    break;
                case "logoutcluster":
                    await LogoutClusterAsync().ConfigureAwait(false);
                    break;
                case "savenamespaces":
                    await SaveNamespacesAsync(request).ConfigureAwait(false);
                    break;
                case "refresh":
                    await RefreshAsync(request.View).ConfigureAwait(false);
                    break;
                case "getlogs":
                    await PushLogsAsync(request.PodName ?? request.Name, request.Namespace).ConfigureAwait(false);
                    break;
                case "getyaml":
                    await PushYamlAsync(request.PodName ?? request.Name, request.Namespace).ConfigureAwait(false);
                    break;
                case "deletepod":
                    await DeletePodAndRefreshAsync(request.PodName ?? request.Name, request.Namespace).ConfigureAwait(false);
                    break;
                case "applyyaml":
                    await ApplyYamlAsync(request).ConfigureAwait(false);
                    break;
                case "podshell":
                    OpenPodShell(request);
                    break;
                case "getclustersettings":
                    PostMessage("clusterSettings", K8sClusterStore.GetClusterSettings(
                        request.ClusterId ?? _k8s.ConnectedClusterId,
                        request.Context ?? _k8s.ConnectedContext));
                    break;
                case "addtohotbar":
                    if (!string.IsNullOrWhiteSpace(request.ClusterId))
                    {
                        K8sClusterStore.ToggleHotbar(request.ClusterId);
                        PostMessage("toast", new { message = "Đã cập nhật hotbar." });
                        await SendClusterCatalogAsync().ConfigureAwait(false);
                        PostMessage("clusterSettings", K8sClusterStore.GetClusterSettings(
                            request.ClusterId, request.Context ?? _k8s.ConnectedContext));
                    }
                    break;
                case "setclusterappearance":
                    K8sClusterStore.SetClusterColor(
                        request.ClusterId ?? _k8s.ConnectedClusterId,
                        request.Context ?? _k8s.ConnectedContext,
                        request.Name ?? "yellow");
                    PostMessage("toast", new { message = "Đã đổi màu cluster." });
                    PostMessage("clusterSettings", K8sClusterStore.GetClusterSettings(
                        request.ClusterId ?? _k8s.ConnectedClusterId,
                        request.Context ?? _k8s.ConnectedContext));
                    break;
                case "removefromkubeconfig":
                    await RemoveFromKubeconfigAsync(request).ConfigureAwait(false);
                    break;
                case "startportforward":
                    StartPortForward(request);
                    break;
                case "stopportforward":
                    StopPortForward(request);
                    break;
                case "removeportforward":
                    RemovePortForward(request);
                    break;
                case "getportforwarddefaults":
                    GetPortForwardDefaults(request);
                    break;
                case "getservicedetail":
                    await PushServiceDetailAsync(request.Name ?? request.PodName, request.Namespace).ConfigureAwait(false);
                    break;
                case "getserviceyaml":
                    await PushServiceYamlAsync(request.Name ?? request.PodName, request.Namespace).ConfigureAwait(false);
                    break;
                case "deleteservice":
                    await DeleteServiceAndRefreshAsync(request.Name ?? request.PodName, request.Namespace).ConfigureAwait(false);
                    break;
            }
        }
        catch (Exception ex)
        {
            PostMessage("error", new { message = ex.Message });
        }
    }

    public async Task TryAutoConnectIfNeededAsync()
    {
        if (!_pageReady || _k8s.IsConnected)
            return;

        await TryAutoConnectLastSessionAsync().ConfigureAwait(false);
    }

    private async Task TryAutoConnectLastSessionAsync()
    {
        if (_k8s.IsConnected)
            return;

        var session = K8sClusterStore.GetLastSession();
        if (session == null || !session.Remember)
            return;

        var clusterId = K8sClusterStore.ResolveClusterIdForSession(session);
        if (string.IsNullOrWhiteSpace(clusterId))
            return;

        var (ok, message) = await _k8s.ConnectToClusterAsync(
            clusterId,
            session.KubeConfigPath,
            session.Context).ConfigureAwait(false);

        if (!ok)
        {
            PostMessage("info", new
            {
                message = $"Chưa kết nối được cluster đã lưu ({session.Context ?? clusterId}).\n{message}\n\nBấm «Đăng nhập cloud» nếu cần az login, rồi Quét lại."
            });
            return;
        }

        K8sClusterStore.SaveLastSession(clusterId, _k8s.ConnectedContext, _k8s.ConnectedKubeConfigPath);
        var gen = ++_connectGeneration;
        PostConnectedFast(clusterId, autoReconnect: true);
        _ = BackgroundLoadClusterDataAsync(clusterId, gen);
    }

    private void PostConnectedFast(string? clusterId, bool autoReconnect = false)
    {
        PostMessage("connected", new
        {
            loading = true,
            message = $"Đã kết nối: {_k8s.ConnectedClusterName}",
            autoReconnect,
            clusterName = _k8s.ConnectedClusterName,
            clusterId,
            context = _k8s.ConnectedContext,
            kubeConfigPath = _k8s.ConnectedKubeConfigPath,
            namespaces = K8sClusterStore.ResolveNamespaces(clusterId, _k8s.ConnectedContext),
            theme = UiStateStore.Current.Theme,
            color = K8sClusterStore.GetClusterColor(clusterId, _k8s.ConnectedContext)
        });
    }

    private async Task BackgroundLoadClusterDataAsync(string? clusterId, int generation)
    {
        try
        {
            await _k8s.ApplyPermissionsAsync().ConfigureAwait(false);
            if (generation != _connectGeneration)
                return;

            var namespaceList = await _k8s.GetNamespacesAsync().ConfigureAwait(false);
            if (generation != _connectGeneration)
                return;

            PostMessage("clusterData", new
            {
                clusterId,
                limitedRbac = _k8s.LimitedRbac,
                noWorkloadAccess = _k8s.NoWorkloadAccess,
                namespaces = K8sClusterStore.ResolveNamespaces(clusterId, _k8s.ConnectedContext),
                permissionInfo = _k8s.LastInfo,
                namespaceList
            });

            await PushPodsAsync().ConfigureAwait(false);
            if (generation != _connectGeneration)
                return;

            StartEnabledForwards(generation);
            await PushPortForwardsAsync().ConfigureAwait(false);
            PostMessage("loadingDone", new { clusterId });
            PostInfoIfAny();
        }
        catch (Exception ex)
        {
            if (generation == _connectGeneration)
                PostMessage("error", new { message = $"Tải dữ liệu cluster: {ex.Message}" });
        }
    }

    private Task SendClusterCatalogAsync()
    {
        K8sClusterStore.EnsureLoaded();
        var clusters = K8sClusterStore.GetAllForUi();
        var kubePaths = K8sClusterStore.GetAllKubeConfigPaths();
        PostMessage("clusters", new
        {
            clusters,
            kubeConfigPaths = kubePaths,
            connected = _k8s.IsConnected,
            connectedClusterId = _k8s.ConnectedClusterId,
            connectedClusterName = _k8s.ConnectedClusterName,
            connectedContext = _k8s.ConnectedContext,
            theme = UiStateStore.Current.Theme
        });
        return Task.CompletedTask;
    }

    private async Task NotifyConnectedStateAsync()
    {
        if (!_k8s.IsConnected)
            return;

        var clusterId = _k8s.ConnectedClusterId;
        var gen = ++_connectGeneration;
        PostConnectedFast(clusterId);
        await BackgroundLoadClusterDataAsync(clusterId, gen).ConfigureAwait(false);
    }

    private K8sTerminalSessions TerminalSessions =>
        _terminalSessions ??= new K8sTerminalSessions(
            (sessionId, text) => PostMessage("terminalOutput", new { sessionId, text }),
            sessionId => PostMessage("terminalExited", new { sessionId }));

    private void CreateEmbeddedTerminal()
    {
        try
        {
            var sessionId = TerminalSessions.CreateSession();
            var hint = _k8s.IsConnected
                ? $"PowerShell — cluster {_k8s.ConnectedClusterName} · context {_k8s.ConnectedContext}"
                : K8sClusterStore.GetLastSession()?.Remember == true
                    ? "PowerShell — phiên cluster đã lưu; tool sẽ tự kết nối. Chỉ cần az login khi token hết hạn."
                    : "PowerShell — az login / az aks get-credentials … (lần đầu hoặc sau Đăng xuất).";

            var cwd = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            PostMessage("terminalStarted", new { sessionId, message = hint, cwd });
        }
        catch (Exception ex)
        {
            PostMessage("error", new { message = ex.Message });
        }
    }

    private void SendTerminalInput(string? sessionId, string? line)
    {
        if (string.IsNullOrWhiteSpace(line) || string.IsNullOrWhiteSpace(sessionId))
            return;

        try
        {
            TerminalSessions.WriteLine(sessionId, line);
        }
        catch (Exception ex)
        {
            PostMessage("error", new { message = ex.Message });
        }
    }

    private void CloseEmbeddedTerminal(string? sessionId)
    {
        try
        {
            TerminalSessions.CloseSession(sessionId);
            PostMessage("terminalClosed", new { sessionId });
        }
        catch (Exception ex)
        {
            PostMessage("error", new { message = ex.Message });
        }
    }

    private async Task BrowseKubeConfigAsync()
    {
        string? path = null;
        if (_webView.InvokeRequired)
        {
            path = (string?)_webView.Invoke(() =>
                K8sKubeConfigDialogs.PickKubeConfigFile(_webView.FindForm()));
        }
        else
        {
            path = K8sKubeConfigDialogs.PickKubeConfigFile(_webView.FindForm());
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            PostMessage("toast", new { message = "Đã hủy chọn file." });
            return;
        }

        var contexts = K8sClusterStore.ListContexts(path);
        PostMessage("kubeConfigBrowsed", new
        {
            path,
            fileName = Path.GetFileName(path),
            contexts
        });
    }

    private async Task ImportKubeConfigAsync(K8sWebRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.KubeConfigPath))
        {
            PostMessage("error", new { message = "Thiếu đường dẫn kubeconfig." });
            return;
        }

        var sourcePath = request.KubeConfigPath;
        var relative = request.CopyToAppFolder
            ? K8sClusterStore.ImportKubeConfigToAppFolder(sourcePath)
            : sourcePath;

        var name = string.IsNullOrWhiteSpace(request.ClusterName)
            ? Path.GetFileNameWithoutExtension(sourcePath)
            : request.ClusterName.Trim();

        var entry = K8sClusterStore.AddLocalCluster(name, relative, request.Context);
        PostMessage("toast", new { message = $"Đã thêm cluster: {name}" });
        await SendClusterCatalogAsync().ConfigureAwait(false);
        PostMessage("clusterImported", new { clusterId = entry.Id });
    }

    private async Task ConnectClusterAsync(K8sWebRequest request)
    {
        try
        {
            if (_k8s.IsConnected)
                await _k8s.DisconnectAsync().ConfigureAwait(false);

            var (ok, message) = await _k8s.ConnectToClusterAsync(
                request.ClusterId,
                request.KubeConfigPath,
                request.Context).ConfigureAwait(false);

            if (!ok)
            {
                PostMessage("connectFailed", new { message });
                return;
            }

            K8sClusterStore.SaveLastSession(
                request.ClusterId ?? _k8s.ConnectedClusterId ?? "",
                _k8s.ConnectedContext,
                _k8s.ConnectedKubeConfigPath);

            var clusterId = request.ClusterId ?? _k8s.ConnectedClusterId;
            var gen = ++_connectGeneration;
            PostConnectedFast(clusterId);
            _ = BackgroundLoadClusterDataAsync(clusterId, gen);
        }
        catch (Exception ex)
        {
            await _k8s.DisconnectAsync().ConfigureAwait(false);
            PostMessage("connectFailed", new { message = $"Kết nối cluster thất bại: {ex.Message}" });
        }
    }

    private async Task SaveNamespacesAsync(K8sWebRequest request)
    {
        var context = request.Context ?? _k8s.ConnectedContext;
        if (string.IsNullOrWhiteSpace(context))
        {
            PostMessage("error", new { message = "Chưa có context cluster." });
            return;
        }

        var namespaces = (request.Namespaces ?? [])
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n.Trim())
            .ToList();

        if (namespaces.Count == 0 && !string.IsNullOrWhiteSpace(request.Name))
        {
            namespaces = request.Name
                .Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
        }

        if (namespaces.Count == 0)
        {
            K8sClusterStore.SaveNamespacesForContext(context, []);
            _k8s.UpdateConfiguredNamespaces([]);
            PostMessage("toast", new { message = "Đã xóa danh sách namespace tùy chỉnh." });
            PostMessage("clusterSettings", K8sClusterStore.GetClusterSettings(
                _k8s.ConnectedClusterId, context));
            return;
        }

        K8sClusterStore.SaveNamespacesForContext(context, namespaces);
        _k8s.UpdateConfiguredNamespaces(namespaces);
        PostMessage("toast", new { message = $"Đã lưu {namespaces.Count} namespace." });
        PostMessage("clusterSettings", K8sClusterStore.GetClusterSettings(
            _k8s.ConnectedClusterId, context));
        await PushPodsAsync().ConfigureAwait(false);
    }

    private async Task RemoveFromKubeconfigAsync(K8sWebRequest request)
    {
        var clusterId = request.ClusterId ?? _k8s.ConnectedClusterId;
        if (string.IsNullOrWhiteSpace(clusterId))
        {
            PostMessage("error", new { message = "Không xác định được cluster." });
            return;
        }

        var wasConnected = _k8s.IsConnected
            && string.Equals(_k8s.ConnectedClusterId, clusterId, StringComparison.OrdinalIgnoreCase);

        var (ok, message) = K8sClusterStore.HideOrRemoveCluster(clusterId);
        if (wasConnected)
            await DisconnectClusterAsync().ConfigureAwait(false);
        else
            await SendClusterCatalogAsync().ConfigureAwait(false);

        PostMessage("toast", new { message = ok ? message : "Đã xử lý yêu cầu xóa cluster." });
    }

    private async Task DisconnectClusterAsync(bool keepSession = false)
    {
        await _k8s.DisconnectAsync().ConfigureAwait(false);
        PostMessage("disconnected", new { message = "Đã ngắt kết nối cluster.", keepSession });
        await SendClusterCatalogAsync().ConfigureAwait(false);
    }

    private async Task LogoutClusterAsync()
    {
        K8sClusterStore.ClearLastSession();
        await _k8s.DisconnectAsync().ConfigureAwait(false);
        PostMessage("disconnected", new { message = "Đã đăng xuất cluster. Chạy az logout / az login trong terminal khi cần đổi tài khoản." });
        await SendClusterCatalogAsync().ConfigureAwait(false);
    }

    private async Task RefreshAsync(string? view)
    {
        if (!_k8s.IsConnected)
        {
            PostMessage("error", new { message = "Chưa kết nối cluster. Chọn cluster để đăng nhập." });
            return;
        }

        switch ((view ?? "pods").ToLowerInvariant())
        {
            case "overview":
                await PushOverviewAsync().ConfigureAwait(false);
                break;
            case "nodes":
                await PushNodesAsync().ConfigureAwait(false);
                break;
            case "deployments":
                await PushDeploymentsAsync().ConfigureAwait(false);
                break;
            case "statefulsets":
                await PushListAsync("statefulsets", () => _k8s.GetStatefulSetsAsync()).ConfigureAwait(false);
                break;
            case "daemonsets":
                await PushListAsync("daemonsets", () => _k8s.GetDaemonSetsAsync()).ConfigureAwait(false);
                break;
            case "jobs":
                await PushListAsync("jobs", () => _k8s.GetJobsAsync()).ConfigureAwait(false);
                break;
            case "cronjobs":
                await PushListAsync("cronjobs", () => _k8s.GetCronJobsAsync()).ConfigureAwait(false);
                break;
            case "configmaps":
                await PushListAsync("configmaps", () => _k8s.GetConfigMapsAsync()).ConfigureAwait(false);
                break;
            case "secrets":
                await PushListAsync("secrets", () => _k8s.GetSecretsAsync()).ConfigureAwait(false);
                break;
            case "services":
                await PushListAsync("services", () => _k8s.GetServicesAsync()).ConfigureAwait(false);
                break;
            case "ingresses":
                await PushListAsync("ingresses", () => _k8s.GetIngressesAsync()).ConfigureAwait(false);
                break;
            case "persistentvolumeclaims":
            case "pvcs":
                await PushListAsync("persistentvolumeclaims", () => _k8s.GetPersistentVolumeClaimsAsync()).ConfigureAwait(false);
                break;
            case "persistentvolumes":
            case "pvs":
                await PushListAsync("persistentvolumes", () => _k8s.GetPersistentVolumesAsync()).ConfigureAwait(false);
                break;
            case "storageclasses":
                await PushListAsync("storageclasses", () => _k8s.GetStorageClassesAsync()).ConfigureAwait(false);
                break;
            case "namespaces":
                await PushListAsync("namespaces", () => _k8s.GetNamespaceResourcesAsync()).ConfigureAwait(false);
                break;
            case "events":
                await PushListAsync("events", () => _k8s.GetEventsAsync()).ConfigureAwait(false);
                break;
            case "serviceaccounts":
                await PushListAsync("serviceaccounts", () => _k8s.GetServiceAccountsAsync()).ConfigureAwait(false);
                break;
            case "roles":
                await PushListAsync("roles", () => _k8s.GetRolesAsync()).ConfigureAwait(false);
                break;
            case "rolebindings":
                await PushListAsync("rolebindings", () => _k8s.GetRoleBindingsAsync()).ConfigureAwait(false);
                break;
            case "clusterroles":
                await PushListAsync("clusterroles", () => _k8s.GetClusterRolesAsync()).ConfigureAwait(false);
                break;
            case "clusterrolebindings":
                await PushListAsync("clusterrolebindings", () => _k8s.GetClusterRoleBindingsAsync()).ConfigureAwait(false);
                break;
            case "customresources":
            case "crds":
                await PushListAsync("customresources", () => _k8s.GetCustomResourceDefinitionsAsync()).ConfigureAwait(false);
                break;
            case "applications":
            case "helm":
                PostMessage("info", new { message = "Tính năng đang phát triển — sẽ bổ sung Helm / Applications sau." });
                PostMessage(view ?? "applications", Array.Empty<K8sListItemDto>());
                break;
            case "portforwarding":
                await PushPortForwardsAsync().ConfigureAwait(false);
                break;
            default:
                await PushPodsAsync().ConfigureAwait(false);
                break;
        }
    }

    private async Task PushOverviewAsync()
    {
        if (!_pageReady || !_k8s.IsConnected)
            return;

        try
        {
            var overview = await _k8s.GetOverviewAsync().ConfigureAwait(false);
            PostMessage("overview", overview);
            PostInfoIfAny();
        }
        catch (Exception ex)
        {
            PostMessage("info", new { message = ex.Message });
        }
    }

    private async Task PushListAsync(string view, Func<Task<IReadOnlyList<K8sListItemDto>>> fetch)
    {
        if (!_pageReady || !_k8s.IsConnected)
            return;

        try
        {
            var items = await fetch().ConfigureAwait(false);
            PostMessage(view, items);
            PostInfoIfAny();
        }
        catch (Exception ex)
        {
            PostMessage("info", new { message = ex.Message, showNamespaceBar = _k8s.LimitedRbac });
        }
    }

    private async Task PushPodsAsync()
    {
        if (!_pageReady || !_k8s.IsConnected)
            return;

        try
        {
            var pods = await _k8s.GetPodsAsync().ConfigureAwait(false);
            var items = pods.Select(p => new K8sListItemDto
            {
                Name = p.Name,
                Namespace = p.Namespace,
                Status = p.Status,
                Age = p.Age,
                RestartCount = p.RestartCount,
                Container = p.Container
            }).ToList();
            PostMessage("pods", items);
            PostInfoIfAny();
        }
        catch (Exception ex)
        {
            PostMessage("info", new { message = ex.Message, showNamespaceBar = true });
        }
    }

    private async Task PushNodesAsync()
    {
        if (!_pageReady || !_k8s.IsConnected)
            return;

        try
        {
            var nodes = await _k8s.GetNodesAsync().ConfigureAwait(false);
            var items = nodes.Select(n => new K8sListItemDto
            {
                Name = n.Name,
                Status = n.Status,
                Detail = n.Roles,
                Age = n.Age
            }).ToList();
            PostMessage("nodes", items);
            PostInfoIfAny();
        }
        catch (Exception ex)
        {
            PostMessage("info", new { message = ex.Message });
        }
    }

    private async Task PushDeploymentsAsync()
    {
        if (!_pageReady || !_k8s.IsConnected)
            return;

        try
        {
            var deps = await _k8s.GetDeploymentsAsync().ConfigureAwait(false);
            var items = deps.Select(d => new K8sListItemDto
            {
                Name = d.Name,
                Namespace = d.Namespace,
                Status = d.Ready,
                Age = d.Age
            }).ToList();
            PostMessage("deployments", items);
            PostInfoIfAny();
        }
        catch (Exception ex)
        {
            PostMessage("info", new { message = ex.Message, showNamespaceBar = true });
        }
    }

    private void PostInfoIfAny()
    {
        if (string.IsNullOrWhiteSpace(_k8s.LastInfo))
            return;

        PostMessage("info", new
        {
            message = _k8s.LastInfo,
            showNamespaceBar = _k8s.LimitedRbac
        });
    }

    private void GetPortForwardDefaults(K8sWebRequest request)
    {
        var name = request.Name ?? request.PodName;
        var ns = request.Namespace;
        var port = request.Port ?? 0;
        var kind = request.ResourceKind ?? "svc";
        var context = _k8s.ConnectedContext;

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(ns) || port <= 0)
        {
            PostMessage("portForwardDefaults", new { localPort = port, useHttps = false, openInBrowser = false });
            return;
        }

        var configId = K8sPortForwardStore.BuildConfigId(ns, kind, name, port);
        var saved = K8sPortForwardStore.Find(context, configId);
        PostMessage("portForwardDefaults", new
        {
            localPort = saved?.LocalPort ?? port,
            useHttps = saved?.UseHttps ?? false,
            openInBrowser = saved?.OpenInBrowser ?? false,
            configId
        });
    }

    private void StartPortForward(K8sWebRequest request)
    {
        if (!_k8s.IsConnected)
        {
            PostMessage("error", new { message = "Chưa kết nối cluster." });
            return;
        }

        var name = request.Name ?? request.PodName;
        var ns = request.Namespace;
        var port = request.Port ?? 0;
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(ns) || port <= 0)
        {
            PostMessage("error", new { message = "Thiếu tên resource, namespace hoặc port." });
            return;
        }

        var localPort = request.LocalPort ?? port;
        var useHttps = request.UseHttps;
        var openInBrowser = request.OpenInBrowser;
        var kind = request.ResourceKind ?? "svc";
        var context = _k8s.ConnectedContext ?? "";

        try
        {
            K8sPortForwardStore.Save(context, ns, kind, name, port, localPort, useHttps, openInBrowser);

            if (request.SaveOnly)
            {
                PostMessage("toast", new { message = "Đã lưu cấu hình port-forward." });
                _ = PushPortForwardsAsync();
                return;
            }

            var session = K8sPortForwardManager.Start(
                ns,
                kind,
                name,
                port,
                localPort,
                context,
                _k8s.ConnectedKubeConfigPath,
                useHttps,
                openInBrowser);

            K8sPortForwardStore.SetEnabled(context, session.ConfigId, true);

            PostMessage("portForwardStarted", new
            {
                message = $"Port-forward: {session.Url} → {ns}/{name}:{session.RemotePort}",
                url = session.Url,
                openInBrowser = session.OpenInBrowser
            });
            _ = PushPortForwardsAsync();
            if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(ns))
                _ = RefreshServiceDetailIfOpenAsync(name, ns);
        }
        catch (Exception ex)
        {
            PostMessage("error", new { message = ex.Message });
        }
    }

    private void StopPortForward(K8sWebRequest request)
    {
        var id = request.PortForwardId ?? request.ConfigId;
        if (string.IsNullOrWhiteSpace(id))
        {
            PostMessage("error", new { message = "Thiếu id port-forward." });
            return;
        }

        // id có thể là sessionId ("configId@localPort") hoặc configId thuần.
        var configId = id.Contains('@') ? id[..id.IndexOf('@')] : id;

        if (id.Contains('@'))
            K8sPortForwardManager.Stop(id);
        else
            K8sPortForwardManager.StopByConfigId(id);

        // User chủ động dừng → không tự khởi động lại lần kết nối sau.
        K8sPortForwardStore.SetEnabled(_k8s.ConnectedContext, configId, false);

        PostMessage("toast", new { message = "Đã dừng port-forward." });
        _ = PushPortForwardsAsync();
    }

    private async Task PushServiceDetailAsync(string? name, string? ns)
    {
        if (!_k8s.IsConnected)
        {
            PostMessage("error", new { message = "Chưa kết nối cluster." });
            return;
        }

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(ns))
        {
            PostMessage("error", new { message = "Thiếu tên service hoặc namespace." });
            return;
        }

        try
        {
            var detail = await _k8s.GetServiceDetailAsync(name, ns).ConfigureAwait(false);
            ApplyPortForwardStatusToServiceDetail(detail);
            PostMessage("serviceDetail", detail);
        }
        catch (Exception ex)
        {
            PostMessage("error", new { message = $"Không tải được service: {ex.Message}" });
        }
    }

    private Task RefreshServiceDetailIfOpenAsync(string name, string ns) =>
        PushServiceDetailAsync(name, ns);

    private void ApplyPortForwardStatusToServiceDetail(K8sServiceDetailDto detail)
    {
        var forwards = K8sPortForwardManager.GetMergedList(_k8s.ConnectedContext)
            .Where(f => string.Equals(f.Namespace, detail.Namespace, StringComparison.OrdinalIgnoreCase)
                && string.Equals(f.ResourceName, detail.Name, StringComparison.OrdinalIgnoreCase)
                && string.Equals(f.ResourceKind, "svc", StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var port in detail.Ports)
        {
            var match = forwards.FirstOrDefault(f => f.RemotePort == port.Port);
            if (match == null)
                continue;

            port.Forwarding = match.Running;
            port.PortForwardId = match.Id;
            port.ConfigId = match.ConfigId;
            port.LocalPort = match.LocalPort;
            port.UseHttps = match.UseHttps;
            port.OpenInBrowser = match.OpenInBrowser;
        }
    }

    private async Task PushServiceYamlAsync(string? name, string? ns)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(ns))
        {
            PostMessage("error", new { message = "Thiếu tên service hoặc namespace." });
            return;
        }

        try
        {
            var yaml = await _k8s.GetServiceYamlAsync(name, ns).ConfigureAwait(false);
            PostMessage("serviceYaml", new { name, ns, text = yaml });
        }
        catch (Exception ex)
        {
            PostMessage("error", new { message = $"YAML: {ex.Message}" });
        }
    }

    private async Task DeleteServiceAndRefreshAsync(string? name, string? ns)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(ns))
        {
            PostMessage("error", new { message = "Thiếu tên service hoặc namespace." });
            return;
        }

        try
        {
            await _k8s.DeleteServiceAsync(name, ns).ConfigureAwait(false);
            PostMessage("toast", new { message = $"Đã xóa service {ns}/{name}" });
            PostMessage("serviceDetailClosed", new { });
            await PushListAsync("services", () => _k8s.GetServicesAsync()).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            PostMessage("error", new { message = ex.Message });
        }
    }

    private void RemovePortForward(K8sWebRequest request)
    {
        var configId = request.ConfigId ?? request.PortForwardId;
        if (string.IsNullOrWhiteSpace(configId))
        {
            PostMessage("error", new { message = "Thiếu id port-forward." });
            return;
        }

        if (configId.Contains('@'))
        {
            K8sPortForwardManager.Stop(configId);
            var at = configId.IndexOf('@');
            configId = configId[..at];
        }
        else
            K8sPortForwardManager.StopByConfigId(configId);

        K8sPortForwardStore.Remove(_k8s.ConnectedContext, configId);
        PostMessage("toast", new { message = "Đã xóa port-forward." });
        _ = PushPortForwardsAsync();
    }

    /// <summary>
    /// Tự khởi động lại các port-forward đã được bật (AutoStart=true) cho context vừa kết nối —
    /// để mở lại tool / kết nối lại cluster thì những cái đang chạy trước đó tiếp tục chạy,
    /// không phải Start tay lại từng cái.
    /// </summary>
    private void StartEnabledForwards(int generation)
    {
        var context = _k8s.ConnectedContext;
        if (string.IsNullOrWhiteSpace(context))
            return;

        var enabled = K8sPortForwardStore.GetForContext(context).Where(c => c.AutoStart).ToList();
        if (enabled.Count == 0)
            return;

        var running = new HashSet<string>(
            K8sPortForwardManager.GetActiveSessions().Select(s => s.ConfigId),
            StringComparer.OrdinalIgnoreCase);

        var started = 0;
        foreach (var cfg in enabled)
        {
            if (generation != _connectGeneration)
                return;
            if (running.Contains(cfg.Id))
                continue;

            try
            {
                K8sPortForwardManager.Start(
                    cfg.Namespace,
                    cfg.ResourceKind,
                    cfg.ResourceName,
                    cfg.RemotePort,
                    cfg.LocalPort,
                    context,
                    _k8s.ConnectedKubeConfigPath,
                    cfg.UseHttps,
                    cfg.OpenInBrowser);
                started++;
            }
            catch
            {
                // Lỗi 1 cái (vd. port bận) → bỏ qua, vẫn hiện Disabled để user Start tay.
            }
        }

        if (started > 0)
            PostMessage("toast", new { message = $"Đã tự khởi động lại {started} port-forward đang bật." });
    }

    private Task PushPortForwardsAsync()
    {
        var items = K8sPortForwardManager.GetMergedList(_k8s.ConnectedContext);
        PostMessage("portforwarding", items);
        return Task.CompletedTask;
    }

    private async Task PushLogsAsync(string? name, string? ns)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(ns))
        {
            PostMessage("error", new { message = "Thiếu podName hoặc namespace." });
            return;
        }

        var logs = await _k8s.GetPodLogsAsync(name, ns).ConfigureAwait(false);
        PostMessage("logs", new { podName = name, ns, text = logs });
    }

    private async Task PushYamlAsync(string? name, string? ns)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(ns))
        {
            PostMessage("error", new { message = "Thiếu podName hoặc namespace." });
            return;
        }

        try
        {
            var yaml = await _k8s.GetPodYamlAsync(name, ns).ConfigureAwait(false);
            PostMessage("yaml", new { podName = name, ns, text = yaml });
        }
        catch (Exception ex)
        {
            PostMessage("error", new { message = $"YAML: {ex.Message}" });
        }
    }

    private void OpenPodShell(K8sWebRequest request)
    {
        var name = request.PodName ?? request.Name;
        var ns = request.Namespace;
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(ns))
        {
            PostMessage("error", new { message = "Thiếu pod hoặc namespace." });
            return;
        }

        try
        {
            RunOnUiThread(() => K8sTerminalLauncher.OpenPodShell(
                ns,
                name,
                _k8s.GetActiveContext(),
                request.Container));

            PostMessage("terminalOpened", new
            {
                message = $"Shell: {ns}/{name}",
                podName = name,
                ns
            });
        }
        catch (Exception ex)
        {
            PostMessage("error", new { message = ex.Message });
        }
    }

    private void RunOnUiThread(Action action)
    {
        if (_webView.InvokeRequired)
            _webView.Invoke(action);
        else
            action();
    }

    private async Task ApplyYamlAsync(K8sWebRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Yaml))
        {
            PostMessage("error", new { message = "YAML trống." });
            return;
        }

        try
        {
            await _k8s.ApplyPodYamlAsync(request.Yaml).ConfigureAwait(false);
            PostMessage("toast", new { message = "Đã áp dụng YAML pod." });
            await PushPodsAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            PostMessage("error", new { message = $"Apply YAML: {ex.Message}" });
        }
    }

    private async Task DeletePodAndRefreshAsync(string? name, string? ns)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(ns))
        {
            PostMessage("error", new { message = "Thiếu podName hoặc namespace." });
            return;
        }

        await _k8s.DeletePodAsync(name, ns).ConfigureAwait(false);
        PostMessage("toast", new { message = $"Đã xóa pod {ns}/{name}" });
        await Task.Delay(400).ConfigureAwait(false);
        await PushPodsAsync().ConfigureAwait(false);
    }

    public void NotifyTheme(string theme)
    {
        if (string.IsNullOrWhiteSpace(theme))
            return;

        UiStateStore.Patch(s => s.Theme = theme);
        PostMessage("theme", new { theme });
    }

    private void PostMessage(string type, object? payload)
    {
        if (_webView.InvokeRequired)
        {
            _webView.BeginInvoke(() => PostMessage(type, payload));
            return;
        }

        if (_webView.CoreWebView2 == null)
            return;

        var json = JsonSerializer.Serialize(new K8sWebResponse { Type = type, Payload = payload }, JsonOptions);
        _webView.CoreWebView2.PostWebMessageAsJson(json);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;
        _terminalSessions?.Dispose();
        _terminalSessions = null;
        await DisconnectAndReleaseAsync().ConfigureAwait(false);
        await _k8s.DisposeAsync().ConfigureAwait(false);
    }
}
