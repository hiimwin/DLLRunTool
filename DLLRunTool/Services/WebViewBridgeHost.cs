using System.Text.Json;
using DLLRunTool.Models;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace DLLRunTool.Services;

public sealed class WebViewBridgeHost
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly WebView2 _webView;
    private readonly ServiceOrchestrator _orchestrator;
    private bool _isReady;

    public WebViewBridgeHost(WebView2 webView)
    {
        _webView = webView;
        _orchestrator = new ServiceOrchestrator(
            PushToUi,
            payload => PushToUi(new BridgeResponse { Type = "log", Payload = payload }),
            webView.FindForm());
    }

    public async Task InitializeAsync()
    {
        await WebView2EnvironmentHelper.EnsureCoreWebView2Async(_webView).ConfigureAwait(true);

        var wwwroot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        Directory.CreateDirectory(wwwroot);

        _webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
            "app.local",
            wwwroot,
            CoreWebView2HostResourceAccessKind.Allow);

        _webView.CoreWebView2.Settings.AreDevToolsEnabled = true;
        _webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
        _webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
        _webView.CoreWebView2.NavigationCompleted += (_, _) =>
        {
            if (!_isReady)
            {
                _isReady = true;
                _orchestrator.HandleRequest(new BridgeRequest { Action = "init" });
            }
        };

        _webView.Source = new Uri("https://app.local/index.html");
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var json = e.WebMessageAsJson;
            if (string.IsNullOrWhiteSpace(json))
                return;

            var request = JsonSerializer.Deserialize<BridgeRequest>(json, JsonOptions);
            if (request != null)
            {
                if (request.Action is "openK8sEmbed" or "layoutK8sEmbed")
                {
                    if (request.X is int x && request.Y is int y &&
                        request.Width is int w && request.Height is int h)
                    {
                        (_webView.FindForm() as MainForm)?.LayoutK8sEmbed(x, y, w, h);
                    }
                    return;
                }

                if (request.Action == "closeK8sEmbed")
                {
                    (_webView.FindForm() as MainForm)?.CloseK8sEmbed();
                    return;
                }

                if (request.Action is "hideK8sEmbed" or "suspendK8sEmbed")
                {
                    (_webView.FindForm() as MainForm)?.HideK8sEmbed();
                    return;
                }

                if (request.Action == "syncK8sTheme" && !string.IsNullOrWhiteSpace(request.Theme))
                {
                    (_webView.FindForm() as MainForm)?.SyncK8sTheme(request.Theme);
                    return;
                }

                _orchestrator.HandleRequest(request);
            }
        }
        catch (Exception ex)
        {
            PushToUi(new BridgeResponse
            {
                Type = "log",
                Payload = new LogPayload { Level = "error", Message = $"Bridge error: {ex.Message}" }
            });
        }
    }

    private void PushToUi(BridgeResponse response)
    {
        if (_webView.InvokeRequired)
        {
            _webView.BeginInvoke(() => PostMessage(response));
            return;
        }

        PostMessage(response);
    }

    private void PostMessage(BridgeResponse response)
    {
        if (_webView.CoreWebView2 == null)
            return;

        var json = JsonSerializer.Serialize(response, JsonOptions);
        _webView.CoreWebView2.PostWebMessageAsJson(json);
    }

    public int GetRunningServiceCount() => _orchestrator.CountRunningServices();

    public int GetLockedRunningServiceCount() => _orchestrator.CountLockedRunningServices();

    public int StopAllServices() => _orchestrator.StopAllServices();
}
