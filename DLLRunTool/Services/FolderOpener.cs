using System.Diagnostics;
using DLLRunTool.Models;

namespace DLLRunTool.Services;

public static class FolderOpener
{
    public static void OpenPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException("Đường dẫn trống.");

        var target = path;
        if (File.Exists(target))
            target = Path.GetDirectoryName(target) ?? target;

        if (!Directory.Exists(target))
            throw new InvalidOperationException($"Không tìm thấy thư mục: {target}");

        Process.Start(new ProcessStartInfo
        {
            FileName = target,
            UseShellExecute = true
        });
    }

    public static string ResolveFolder(ServiceConfig service, string kind)
    {
        return kind.ToLowerInvariant() switch
        {
            "project" => service.ResolveSourceProjectPath(),
            "bin" => service.ResolveRunWorkingDirectory(),
            "logs" => Path.Combine(AppContext.BaseDirectory, "service-logs"),
            _ => throw new ArgumentException($"Loại folder không hỗ trợ: {kind}")
        };
    }

    public static void OpenCmdAt(string workingDirectory, string? title = null)
    {
        if (string.IsNullOrWhiteSpace(workingDirectory) || !Directory.Exists(workingDirectory))
            throw new InvalidOperationException($"Không tìm thấy thư mục: {workingDirectory}");

        var escaped = workingDirectory.Replace("'", "''");
        var windowTitle = (title ?? "MCP CMD").Replace("\"", "'");
        var ps = $"-NoExit -Command \"Set-Location -LiteralPath '{escaped}'; $Host.UI.RawUI.WindowTitle='{windowTitle}'\"";
        Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = ps,
            WorkingDirectory = workingDirectory,
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Normal
        });
    }
}
