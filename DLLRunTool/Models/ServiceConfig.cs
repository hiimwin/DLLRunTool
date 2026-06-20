using System.Diagnostics;
using System.Text.Json.Serialization;
using DLLRunTool.Services;

namespace DLLRunTool.Models;

public class ServiceConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "";
    public string FolderPath { get; set; } = "";
    public string ProjectPath { get; set; } = "";
    public string ConfigPath { get; set; } = "";
    public string DllName { get; set; } = "";
    public string Url { get; set; } = "";
    public bool IsExe { get; set; }
    public string Type { get; set; } = "BE";
    public string RunCommand { get; set; } = "";
    public string BuildCommand { get; set; } = "";

    [JsonIgnore]
    public Process? ManagedProcess { get; set; }

    [JsonIgnore]
    public bool IsRunning => ManagedProcess != null && !ManagedProcess.HasExited;

    public bool IsBackEnd => string.Equals(Type, "BE", StringComparison.OrdinalIgnoreCase);
    public bool IsFrontEnd => string.Equals(Type, "FE", StringComparison.OrdinalIgnoreCase);

    public string ResolveProjectPath() => ResolveSourceProjectPath();

    /// <summary>
    /// Thư mục project source (có .csproj), không bao giờ trả về bin/Debug.
    /// </summary>
    public string ResolveSourceProjectPath()
    {
        if (!string.IsNullOrWhiteSpace(ProjectPath) &&
            Directory.Exists(ProjectPath) &&
            !IsUnderBin(ProjectPath))
        {
            return ProjectPath;
        }

        if (!string.IsNullOrWhiteSpace(FolderPath))
        {
            var dir = new DirectoryInfo(FolderPath);
            while (dir != null)
            {
                if (dir.Exists)
                {
                    if (dir.GetFiles("*.csproj").FirstOrDefault() != null)
                        return dir.FullName;

                    if (File.Exists(Path.Combine(dir.FullName, "package.json")))
                        return dir.FullName;
                }

                dir = dir.Parent;
            }

            var stripped = StripBinSegment(FolderPath);
            if (!string.Equals(stripped, FolderPath, StringComparison.OrdinalIgnoreCase) &&
                Directory.Exists(stripped))
            {
                return stripped;
            }
        }

        return StripBinSegment(FolderPath);
    }

    private static bool IsUnderBin(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);

    private static string StripBinSegment(string path)
    {
        var normalized = path.Replace('/', Path.DirectorySeparatorChar);
        var binToken = $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}";
        var idx = normalized.IndexOf(binToken, StringComparison.OrdinalIgnoreCase);
        return idx > 0 ? normalized[..idx] : normalized;
    }

    public string? TryResolveCsprojPath()
    {
        var project = ResolveProjectPath();
        if (Directory.Exists(project))
        {
            var csproj = Directory.EnumerateFiles(project, "*.csproj", SearchOption.TopDirectoryOnly).FirstOrDefault();
            if (csproj != null)
                return csproj;
        }

        if (string.IsNullOrWhiteSpace(FolderPath))
            return null;

        var dir = new DirectoryInfo(FolderPath);
        while (dir != null)
        {
            if (dir.Exists)
            {
                var csproj = dir.GetFiles("*.csproj").FirstOrDefault();
                if (csproj != null)
                    return csproj.FullName;
            }

            dir = dir.Parent;
        }

        return null;
    }

    public string? FindDllOutputPath()
    {
        if (string.IsNullOrWhiteSpace(DllName) || IsExe || IsFrontEnd)
            return null;

        var project = ResolveSourceProjectPath();
        var preferredTfm = DotNetOutputResolver.ReadTargetFramework(TryResolveCsprojPath());
        var resolved = DotNetOutputResolver.FindBestDllPath(project, DllName, preferredTfm);
        if (resolved != null)
            return resolved;

        if (!string.IsNullOrWhiteSpace(FolderPath))
        {
            var direct = Path.Combine(FolderPath, DllName);
            if (File.Exists(direct))
                return Path.GetFullPath(direct);
        }

        return null;
    }

    public string ResolveDllFullPath() =>
        FindDllOutputPath() ?? Path.Combine(FolderPath, DllName);

    public string ResolveRunWorkingDirectory()
    {
        var dll = FindDllOutputPath();
        if (dll != null)
            return Path.GetDirectoryName(dll)!;

        if (Directory.Exists(FolderPath))
            return FolderPath;

        var csproj = TryResolveCsprojPath();
        if (!string.IsNullOrWhiteSpace(csproj))
            return Path.GetDirectoryName(csproj)!;

        return ResolveProjectPath();
    }

    public bool SyncFolderPathFromDisk()
    {
        var dll = FindDllOutputPath();
        if (dll == null)
            return false;

        FolderPath = Path.GetDirectoryName(dll)!;
        return true;
    }

    public string ResolveConfigPath()
    {
        if (!string.IsNullOrWhiteSpace(ConfigPath) && File.Exists(ConfigPath))
            return ConfigPath;

        var projectPath = ResolveSourceProjectPath();
        if (IsFrontEnd)
        {
            var envJs = Path.Combine(projectPath, "src", "assets", "env.js");
            if (File.Exists(envJs))
                return envJs;

            var dotEnv = Path.Combine(projectPath, ".env");
            if (File.Exists(dotEnv))
                return dotEnv;
        }
        else
        {
            var searchDir = new DirectoryInfo(projectPath);
            while (searchDir != null)
            {
                var dev = Path.Combine(searchDir.FullName, "appsettings.Development.json");
                if (File.Exists(dev))
                    return dev;

                var prod = Path.Combine(searchDir.FullName, "appsettings.json");
                if (File.Exists(prod))
                    return prod;

                searchDir = searchDir.Parent;
            }
        }

        return "";
    }

    public IReadOnlyList<(string Key, string FullPath)> GetSourceConfigFiles()
    {
        var files = new List<(string, string)>();
        var project = ResolveProjectPath();

        if (IsFrontEnd)
        {
            AddIfExists(files, "env.js", Path.Combine(project, "src", "assets", "env.js"));
            AddIfExists(files, ".env", Path.Combine(project, ".env"));
            return files;
        }

        if (IsExe)
            return files;

        AddIfExists(files, "appsettings.json", Path.Combine(project, "appsettings.json"));
        AddIfExists(files, "appsettings.Development.json", Path.Combine(project, "appsettings.Development.json"));
        AddIfExists(files, "appsettings.secrets.json", Path.Combine(project, "appsettings.secrets.json"));
        AddIfExists(files, "launchSettings.json", Path.Combine(project, "Properties", "launchSettings.json"));
        AddOcelotConfigFiles(files, project);
        return files;
    }

    private static void AddOcelotConfigFiles(List<(string Key, string FullPath)> files, string projectPath)
    {
        if (!Directory.Exists(projectPath))
            return;

        var knownNames = new[]
        {
            "ocelot.localhost.json",
            "ocelot.json",
            "ocelot-local.localhost.json",
            "ocelot-local.json"
        };

        var added = new HashSet<string>(files.Select(f => f.FullPath), StringComparer.OrdinalIgnoreCase);

        foreach (var name in knownNames)
        {
            var path = Path.Combine(projectPath, name);
            if (added.Add(path) && File.Exists(path))
                files.Add((name, path));
        }

        foreach (var path in Directory.EnumerateFiles(projectPath, "ocelot*.json", SearchOption.TopDirectoryOnly))
        {
            if (added.Add(path))
                files.Add((Path.GetFileName(path), path));
        }
    }

    public string? ResolveSourceConfigPath(string key)
    {
        var existing = GetSourceConfigFiles()
            .FirstOrDefault(f => f.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
            .FullPath;

        if (!string.IsNullOrEmpty(existing))
            return existing;

        return ResolveExpectedSourceConfigPath(key);
    }

    /// <summary>
    /// Path source chuẩn cho file config (kể cả chưa tồn tại) — dùng khi apply backup.
    /// </summary>
    public string? ResolveExpectedSourceConfigPath(string key)
    {
        var project = ResolveSourceProjectPath();
        if (string.IsNullOrWhiteSpace(project))
            return null;

        if (IsFrontEnd)
        {
            return key.ToLowerInvariant() switch
            {
                "env.js" => Path.Combine(project, "src", "assets", "env.js"),
                ".env" => Path.Combine(project, ".env"),
                _ => Path.Combine(project, key)
            };
        }

        if (IsExe)
            return null;

        return key.ToLowerInvariant() switch
        {
            "appsettings.json" => Path.Combine(project, "appsettings.json"),
            "appsettings.development.json" => Path.Combine(project, "appsettings.Development.json"),
            "appsettings.secrets.json" => Path.Combine(project, "appsettings.secrets.json"),
            "launchsettings.json" => Path.Combine(project, "Properties", "launchSettings.json"),
            _ => Path.Combine(project, key)
        };
    }

    private static void AddIfExists(List<(string Key, string FullPath)> list, string key, string path)
    {
        if (File.Exists(path))
            list.Add((key, path));
    }
}

public class PlatformDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string ConfigFile { get; set; } = "";
}
