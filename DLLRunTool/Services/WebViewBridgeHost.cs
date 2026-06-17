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
        await _webView.EnsureCoreWebView2Async().ConfigureAwait(true);

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
                _orchestrator.HandleRequest(request);
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
