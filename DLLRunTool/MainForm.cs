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
    private bool _closeHandled;
    private bool _stopServicesOnClose;

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
        // Cleanup đã xong từ lần đóng trước → cho phép đóng thật.
        if (_closeHandled)
            return;

        // Tạm chặn đóng để hỏi xác nhận + cleanup có phản hồi (tránh treo không hiện gì).
        e.Cancel = true;

        // 1) Hỏi xác nhận TRƯỚC (nhanh) nếu còn service đang chạy.
        if (!ServiceOrchestrator.IsApplyingUpdate && _bridge != null)
        {
            var running = _bridge.GetRunningServiceCount();
            if (running > 0)
            {
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
                    return; // Ở lại — giữ e.Cancel = true.

                _stopServicesOnClose = result == DialogResult.Yes;
            }
        }

        // 2) Hiện overlay "Đang đóng…" rồi mới cleanup (cho người dùng biết tool đang xử lý).
        _closeHandled = true;
        ShowClosingOverlay();

        try
        {
            await CleanupK8sOnFormClosingAsync().ConfigureAwait(true);
            if (_stopServicesOnClose)
                _bridge?.StopAllServices();
        }
        catch
        {
            // Bỏ qua lỗi cleanup khi thoát.
        }

        // 3) Đóng thật.
        Close();
    }

    private void ShowClosingOverlay()
    {
        try
        {
            var overlay = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(255, 26, 27, 30)
            };
            var label = new Label
            {
                Text = "Đang đóng, vui lòng đợi…",
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 12F, FontStyle.Regular),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter
            };
            overlay.Controls.Add(label);
            Controls.Add(overlay);
            overlay.BringToFront();
            UseWaitCursor = true;
            // Vẽ overlay ngay trước khi cleanup chặn luồng UI.
            Refresh();
            Application.DoEvents();
        }
        catch
        {
            // ignore overlay errors
        }
    }
}
