using DLLRunTool.Services;
using Microsoft.Web.WebView2.WinForms;

namespace DLLRunTool;

public partial class MainForm : Form
{
    private readonly WebView2 _webView = new()
    {
        Dock = DockStyle.Fill,
        DefaultBackgroundColor = Color.FromArgb(255, 26, 27, 30)
    };
    private WebViewBridgeHost? _bridge;
    private bool _webViewInitStarted;

    public MainForm()
    {
        InitializeComponent();
        TrySetWindowIcon();
        Controls.Add(_webView);
        HandleCreated += (_, _) => WindowChrome.ApplyRoundedCorners(this);
        Shown += MainForm_Shown;
        FormClosing += MainForm_FormClosing;
    }

    private void TrySetWindowIcon()
    {
        try
        {
            var extracted = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            if (extracted != null)
                Icon = extracted;
        }
        catch
        {
            // ignore
        }
    }

    private async void MainForm_Shown(object? sender, EventArgs e)
    {
        if (_webViewInitStarted)
            return;

        _webViewInitStarted = true;

        try
        {
            _bridge = new WebViewBridgeHost(_webView);
            await _bridge.InitializeAsync();
        }
        catch (Exception ex)
        {
            StyledMessageBox.Show(
                this,
                $"Không thể khởi tạo WebView2.\n\n{WebView2EnvironmentHelper.FormatInitError(ex)}",
                ServiceOrchestrator.AppTitle,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    public WebViewBridgeHost? BridgeHost => _bridge;

    private async void MainForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
        await CleanupK8sOnFormClosingAsync().ConfigureAwait(false);

        if (ServiceOrchestrator.IsApplyingUpdate)
            return;

        if (_bridge == null)
            return;

        var running = _bridge.GetRunningServiceCount();
        if (running <= 0)
            return;

        var lockedRunning = _bridge.GetLockedRunningServiceCount();
        var stoppable = running - lockedRunning;
        var lockedNote = lockedRunning > 0
            ? $"\n\n{lockedRunning} service đã khóa sẽ tiếp tục chạy."
            : "";

        var result = StyledMessageBox.Show(
            this,
            $"Có {running} service đang chạy.{lockedNote}\n\n" +
            (stoppable > 0
                ? $"• Có = Dừng {stoppable} service (trừ khóa) rồi thoát\n"
                : "• Có = Thoát (không có service nào bị dừng — tất cả đều khóa)\n") +
            "• Không = Thoát tool, để service chạy nền\n" +
            "• Hủy = Ở lại",
            ServiceOrchestrator.AppTitle,
            MessageBoxButtons.YesNoCancel,
            MessageBoxIcon.Question);

        if (result == DialogResult.Cancel)
            e.Cancel = true;
        else if (result == DialogResult.Yes)
            _bridge.StopAllServices();
    }
}
