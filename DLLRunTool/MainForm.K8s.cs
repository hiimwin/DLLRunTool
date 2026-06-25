using DLLRunTool.Services;
using Microsoft.Web.WebView2.WinForms;

namespace DLLRunTool;

public partial class MainForm
{
    private Panel _panelK8s = null!;
    private WebView2 _webViewK8s = null!;
    private K8sBridgeHost? _k8sBridge;
    private bool _k8sUiInitialized;
    private bool _k8sActivating;

    private void EnsureK8sUi()
    {
        if (_k8sUiInitialized)
            return;

        _k8sUiInitialized = true;

        _panelK8s = new Panel
        {
            Dock = DockStyle.None,
            BackColor = Color.FromArgb(15, 17, 23),
            Visible = false
        };

        _webViewK8s = new WebView2
        {
            Dock = DockStyle.Fill,
            DefaultBackgroundColor = Color.FromArgb(15, 17, 23)
        };

        _panelK8s.Controls.Add(_webViewK8s);
        Controls.Add(_panelK8s);
    }

    public void SyncK8sTheme(string theme)
    {
        if (!_k8sUiInitialized)
            return;

        var light = string.Equals(theme, "light", StringComparison.OrdinalIgnoreCase);
        var bg = light ? Color.FromArgb(238, 241, 246) : Color.FromArgb(19, 20, 22);
        _panelK8s.BackColor = bg;
        _webViewK8s.DefaultBackgroundColor = bg;
        _k8sBridge?.NotifyTheme(theme);
    }
    public void LayoutK8sEmbed(int x, int y, int width, int height)
    {
        if (width <= 0 || height <= 0)
            return;

        EnsureK8sUi();
        _panelK8s.SetBounds(x, y, width, height);
        _panelK8s.Visible = true;
        _panelK8s.BringToFront();
        _ = EnsureK8sDashboardAsync();
    }

    public void HideK8sEmbed()
    {
        if (!_k8sUiInitialized)
            return;

        _panelK8s.Visible = false;
    }

    /// <summary>Ẩn embed khi đổi tab — giữ WebView + kết nối cluster chạy nền.</summary>
    public void SuspendK8sEmbed() => HideK8sEmbed();

    /// <summary>Thu hồi hoàn toàn WebView + bridge (tắt K8s hoặc đóng app).</summary>
    public async void CloseK8sEmbed()
    {
        if (!_k8sUiInitialized)
            return;

        _panelK8s.Visible = false;
        await DisconnectK8sAsync().ConfigureAwait(true);
        TeardownK8sWebView();
    }

    private void TeardownK8sWebView()
    {
        if (!_k8sUiInitialized)
            return;

        try
        {
            _webViewK8s.Dispose();
        }
        catch
        {
            // ignore cleanup errors
        }

        try
        {
            _panelK8s.Controls.Clear();
            Controls.Remove(_panelK8s);
            _panelK8s.Dispose();
        }
        catch
        {
            // ignore cleanup errors
        }

        _k8sUiInitialized = false;
        _panelK8s = null!;
        _webViewK8s = null!;
    }

    private async Task EnsureK8sDashboardAsync()
    {
        if (_k8sBridge != null)
        {
            SyncK8sTheme(UiStateStore.Current.Theme);
            await _k8sBridge.TryAutoConnectIfNeededAsync().ConfigureAwait(true);
            return;
        }

        if (_k8sActivating)
            return;

        _k8sActivating = true;
        try
        {
            _k8sBridge = new K8sBridgeHost(_webViewK8s);
            var ok = await _k8sBridge.InitializeWebViewAsync().ConfigureAwait(true);
            if (!ok)
                await DisconnectK8sAsync().ConfigureAwait(true);
            else
                SyncK8sTheme(UiStateStore.Current.Theme);
        }
        catch
        {
            await DisconnectK8sAsync().ConfigureAwait(true);
        }
        finally
        {
            _k8sActivating = false;
        }
    }

    private async Task DisconnectK8sAsync()
    {
        if (_k8sBridge == null)
            return;

        try
        {
            await _k8sBridge.DisconnectAndReleaseAsync().ConfigureAwait(true);
            await _k8sBridge.DisposeAsync().ConfigureAwait(true);
        }
        catch
        {
            // ignore cleanup errors
        }
        finally
        {
            _k8sBridge = null;
        }
    }

    private async Task CleanupK8sOnFormClosingAsync()
    {
        await DisconnectK8sAsync().ConfigureAwait(false);
        TeardownK8sWebView();
    }
}
