using DLLRunTool.Services;
using Microsoft.Web.WebView2.WinForms;

namespace DLLRunTool;

public partial class MainForm : Form
{
    private readonly WebView2 _webView = new() { Dock = DockStyle.Fill };
    private WebViewBridgeHost? _bridge;

    public MainForm()
    {
        InitializeComponent();
        Controls.Add(_webView);
        Load += MainForm_Load;
        FormClosing += MainForm_FormClosing;
    }

    private async void MainForm_Load(object? sender, EventArgs e)
    {
        try
        {
            _bridge = new WebViewBridgeHost(_webView);
            await _bridge.InitializeAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Không thể khởi tạo WebView2.\n\n{ex.Message}\n\nHãy cài đặt WebView2 Runtime.",
                ServiceOrchestrator.AppTitle,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    public WebViewBridgeHost? BridgeHost => _bridge;

    private void MainForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
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

        var result = MessageBox.Show(
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
