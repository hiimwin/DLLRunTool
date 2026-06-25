using System.Diagnostics;

namespace DLLRunTool.Services;

public static class K8sTerminalLauncher
{
    public static void OpenPodShell(string ns, string pod, string? context = null, string? container = null)
    {
        var ctx = BuildContextArg(context);
        var containerArg = string.IsNullOrWhiteSpace(container) ? "" : $"-c \"{EscapePs(container)}\" ";
        var cmd = $"kubectl {ctx}-n \"{EscapePs(ns)}\" exec -it \"{EscapePs(pod)}\" {containerArg}-- sh";
        OpenWindowsTerminal(cmd, title: $"shell:{ns}/{pod}");
    }

    private static string BuildContextArg(string? context) =>
        string.IsNullOrWhiteSpace(context) ? "" : $"--context \"{EscapePs(context)}\" ";

    private static string EscapePs(string value) => value.Replace("\"", "`\"");

    private static void OpenWindowsTerminal(string command, string? title = null)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        try
        {
            if (OperatingSystem.IsWindows())
            {
                var wt = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Microsoft", "WindowsApps", "wt.exe");

                var titleArg = string.IsNullOrWhiteSpace(title) ? "" : $"--title \"{EscapePs(title)}\" ";
                if (File.Exists(wt))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = wt,
                        Arguments = $"{titleArg}-d \"{home}\" powershell -NoExit -Command \"{command}\"",
                        UseShellExecute = true
                    });
                    return;
                }
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoExit -Command \"{command}\"",
                WorkingDirectory = home,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Không mở được terminal: {ex.Message}", ex);
        }
    }
}
