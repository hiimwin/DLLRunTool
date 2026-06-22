using System.Text.RegularExpressions;
using DLLRunTool.Models;

namespace DLLRunTool.Services;

public sealed class ConfigSecretFinding
{
    public string ServiceName { get; set; } = "";
    public string FilePath { get; set; } = "";
    public string Reason { get; set; } = "";
}

public static class ConfigSecretScanner
{
    private static readonly Regex PasswordInJson = new(
        @"(Password|Pwd|Secret|ConnectionString|AbpLicenseCode)\s*[=:]\s*[^""\s]{4,}",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static List<ConfigSecretFinding> ScanServices(IEnumerable<ServiceConfig> services)
    {
        var findings = new List<ConfigSecretFinding>();
        foreach (var service in services)
        {
            foreach (var (_, path) in service.GetSourceConfigFiles())
            {
                if (!File.Exists(path))
                    continue;

                var name = Path.GetFileName(path);
                if (!name.StartsWith("appsettings", StringComparison.OrdinalIgnoreCase) &&
                    !name.Equals("env.js", StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    var content = File.ReadAllText(path);
                    if (LooksSensitive(content, name))
                    {
                        findings.Add(new ConfigSecretFinding
                        {
                            ServiceName = service.Name,
                            FilePath = path,
                            Reason = name.Contains("secrets", StringComparison.OrdinalIgnoreCase)
                                ? "File secrets trong source"
                                : "Có password/connection string trong file config"
                        });
                    }
                }
                catch
                {
                    // ignore unreadable
                }
            }
        }

        return findings;
    }

    private static bool LooksSensitive(string content, string fileName)
    {
        if (fileName.Contains("secrets", StringComparison.OrdinalIgnoreCase))
            return true;

        if (content.Contains("ConnectionStrings", StringComparison.OrdinalIgnoreCase) &&
            PasswordInJson.IsMatch(content))
            return true;

        if (content.Contains("Password=", StringComparison.OrdinalIgnoreCase) ||
            content.Contains("Pwd=", StringComparison.OrdinalIgnoreCase))
            return true;

        return content.Contains("AbpLicenseCode", StringComparison.OrdinalIgnoreCase);
    }
}
