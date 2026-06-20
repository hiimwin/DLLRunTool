using System.Reflection;

namespace DLLRunTool.Services;

public static class AppVersionInfo
{
    public static string Current =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
}
