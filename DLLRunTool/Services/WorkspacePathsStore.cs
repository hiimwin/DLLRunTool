using System.Text.Json;
using System.Text.RegularExpressions;

namespace DLLRunTool.Services;

public sealed record WorkspacePathDefinition(
    string Key,
    string Label,
    string Hint,
    string Scope);

public sealed record WorkspacePathIssue(string Key, string Label, string Message);

public static class WorkspacePathsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private static readonly Regex PlaceholderRegex = new(@"\{\{(\w+)\}\}", RegexOptions.Compiled);

    private static Dictionary<string, string> _paths = new(StringComparer.OrdinalIgnoreCase);

    public static string LocalFilePath => Path.Combine(AppContext.BaseDirectory, "paths.local.json");
    public static string ExampleFilePath => Path.Combine(AppContext.BaseDirectory, "paths.local.example.json");

    public static IReadOnlyList<WorkspacePathDefinition> Definitions { get; } =
    [
        new("loyaltyRoot", "LoyaltyPlatform", "Thư mục chứa loyalty-platform và loyalty-admin-portal", "loyalty"),
        new("fptcxRoot", "FPTCXSuite", "Thư mục chứa thư mục fptcxsuite", "fptcx"),
        new("redisPath", "Redis", "Thư mục chứa redis-server.exe", "all")
    ];

    public static void EnsureLoaded()
    {
        if (!File.Exists(LocalFilePath))
        {
            if (File.Exists(ExampleFilePath))
                File.Copy(ExampleFilePath, LocalFilePath);
            else
                Save(CreateDefaults());
        }

        Load();
    }

    public static void Load()
    {
        _paths = CreateDefaults();
        if (!File.Exists(LocalFilePath))
            return;

        var fromFile = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(LocalFilePath), JsonOptions);
        if (fromFile == null)
            return;

        foreach (var (key, value) in fromFile)
            _paths[key] = value ?? "";
    }

    public static Dictionary<string, string> GetAll() => new(_paths, StringComparer.OrdinalIgnoreCase);

    public static void Save(Dictionary<string, string> paths)
    {
        _paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var def in Definitions)
            _paths[def.Key] = paths.TryGetValue(def.Key, out var v) ? v?.Trim() ?? "" : "";

        Directory.CreateDirectory(Path.GetDirectoryName(LocalFilePath)!);
        File.WriteAllText(LocalFilePath, JsonSerializer.Serialize(_paths, JsonOptions));
    }

    public static string Resolve(string? pathTemplate)
    {
        if (string.IsNullOrWhiteSpace(pathTemplate))
            return pathTemplate ?? "";

        var result = pathTemplate;
        if (!result.Contains("{{", StringComparison.Ordinal))
            return NormalizePath(result);

        foreach (Match match in PlaceholderRegex.Matches(result))
        {
            var key = match.Groups[1].Value;
            if (!_paths.TryGetValue(key, out var root) || string.IsNullOrWhiteSpace(root))
                continue;

            result = result.Replace(match.Value, root.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
        }

        return NormalizePath(result);
    }

    public static bool HasUnresolved(string? path) =>
        !string.IsNullOrEmpty(path) && path.Contains("{{", StringComparison.Ordinal);

    public static List<WorkspacePathIssue> ValidateRoots()
    {
        var issues = new List<WorkspacePathIssue>();
        foreach (var def in Definitions)
        {
            _paths.TryGetValue(def.Key, out var value);
            if (string.IsNullOrWhiteSpace(value))
            {
                issues.Add(new WorkspacePathIssue(def.Key, def.Label, "Chưa cấu hình — mở tab Workspace Paths"));
                continue;
            }

            if (!Directory.Exists(value))
                issues.Add(new WorkspacePathIssue(def.Key, def.Label, $"Không tồn tại: {value}"));
        }

        return issues;
    }

    public static List<string> ValidateServicePaths(IEnumerable<Models.ServiceConfig> services)
    {
        var missing = new List<string>();
        foreach (var svc in services)
        {
            if (string.IsNullOrWhiteSpace(svc.FolderPath))
                continue;

            if (HasUnresolved(svc.FolderPath))
            {
                missing.Add($"{svc.Name}: chưa resolve path (thiếu workspace root)");
                continue;
            }

            if (!Directory.Exists(svc.FolderPath))
                missing.Add($"{svc.Name}: {svc.FolderPath}");
        }

        return missing;
    }

    private static Dictionary<string, string> CreateDefaults() =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["loyaltyRoot"] = @"D:\Codes\LoyaltyPlatform",
            ["fptcxRoot"] = @"D:\Codes\FPTCXSuite",
            ["redisPath"] = @"D:\Codes\Redis"
        };

    private static string NormalizePath(string path)
    {
        try
        {
            return Path.IsPathRooted(path) ? Path.GetFullPath(path) : path;
        }
        catch
        {
            return path;
        }
    }
}
