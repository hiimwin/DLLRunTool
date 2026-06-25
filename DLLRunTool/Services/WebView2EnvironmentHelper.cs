using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace DLLRunTool.Services;

/// <summary>
/// Shared WebView2 environment — user data in %LOCALAPPDATA% (writable even when exe is in Program Files).
/// </summary>
internal static class WebView2EnvironmentHelper
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static CoreWebView2Environment? _environment;

    public static string UserDataFolder =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Win_Trung",
            "WebView2");

    public static async Task<CoreWebView2Environment> GetOrCreateEnvironmentAsync(CancellationToken ct = default)
    {
        if (_environment != null)
            return _environment;

        await Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_environment != null)
                return _environment;

            Directory.CreateDirectory(UserDataFolder);

            _environment = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: UserDataFolder).ConfigureAwait(false);

            return _environment;
        }
        finally
        {
            Gate.Release();
        }
    }

    public static async Task EnsureCoreWebView2Async(WebView2 webView, CancellationToken ct = default)
    {
        if (webView.CoreWebView2 != null)
            return;

        if (!webView.IsHandleCreated)
            webView.CreateControl();

        var env = await GetOrCreateEnvironmentAsync(ct).ConfigureAwait(false);
        await webView.EnsureCoreWebView2Async(env).ConfigureAwait(false);
    }

    public static string FormatInitError(Exception ex)
    {
        var lines = new List<string> { ex.Message };
        for (var inner = ex.InnerException; inner != null; inner = inner.InnerException)
            lines.Add(inner.Message);

        lines.Add("");
        lines.Add($"User data: {UserDataFolder}");
        lines.Add("Nếu vẫn lỗi: xóa thư mục trên rồi mở lại app.");
        lines.Add("Hoặc cài WebView2 Runtime: https://go.microsoft.com/fwlink/p/?LinkId=2124703");

        return string.Join(Environment.NewLine, lines);
    }
}
