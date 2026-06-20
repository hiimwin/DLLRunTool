using System.Text.RegularExpressions;

namespace DLLRunTool.Services;

internal static class DotNetOutputResolver
{
    private static readonly Regex TfmFromPathRegex = new(
        $@"[\\/]bin[\\/](?<config>Debug|Release)[\\/](?<tfm>net\d+\.\d+)[\\/]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex TargetFrameworkRegex = new(
        @"<TargetFramework[^>]*>(?<tfm>[^<]+)</TargetFramework>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex TargetFrameworksRegex = new(
        @"<TargetFrameworks[^>]*>(?<tfms>[^<]+)</TargetFrameworks>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static string? ReadTargetFramework(string? csprojPath)
    {
        if (string.IsNullOrWhiteSpace(csprojPath) || !File.Exists(csprojPath))
            return null;

        var text = File.ReadAllText(csprojPath);

        var single = TargetFrameworkRegex.Match(text);
        if (single.Success)
            return single.Groups["tfm"].Value.Trim();

        var multi = TargetFrameworksRegex.Match(text);
        if (!multi.Success)
            return null;

        return multi.Groups["tfms"].Value
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .OrderByDescending(ParseTfmVersion)
            .FirstOrDefault();
    }

    public static string? FindBestDllPath(string projectPath, string dllName, string? preferredTfm)
    {
        var binDir = Path.Combine(projectPath, "bin");
        if (!Directory.Exists(binDir))
            return null;

        var candidates = Directory.EnumerateFiles(binDir, dllName, SearchOption.AllDirectories)
            .Select(path => new FileInfo(path))
            .Where(f => f.Exists)
            .ToList();

        if (candidates.Count == 0)
            return null;

        var best = candidates
            .Select(f => new DllCandidate(f, ParsePathMetadata(f.FullName)))
            .OrderByDescending(c => Score(c, preferredTfm))
            .ThenByDescending(c => c.File.LastWriteTimeUtc)
            .First();

        return best.File.FullName;
    }

    private static int Score(DllCandidate candidate, string? preferredTfm)
    {
        var score = 0;

        if (!string.IsNullOrWhiteSpace(preferredTfm) &&
            string.Equals(candidate.Metadata.Tfm, preferredTfm, StringComparison.OrdinalIgnoreCase))
        {
            score += 10_000;
        }

        score += (int)(ParseTfmVersion(candidate.Metadata.Tfm) * 100);

        if (string.Equals(candidate.Metadata.Configuration, "Debug", StringComparison.OrdinalIgnoreCase))
            score += 50;

        return score;
    }

    private static PathMetadata ParsePathMetadata(string fullPath)
    {
        var match = TfmFromPathRegex.Match(fullPath);
        if (!match.Success)
            return new PathMetadata("", "");

        return new PathMetadata(match.Groups["tfm"].Value, match.Groups["config"].Value);
    }

    private static decimal ParseTfmVersion(string? tfm)
    {
        if (string.IsNullOrWhiteSpace(tfm))
            return 0;

        var match = Regex.Match(tfm, @"net(?<major>\d+)(?:\.(?<minor>\d+))?", RegexOptions.IgnoreCase);
        if (!match.Success)
            return 0;

        var major = int.Parse(match.Groups["major"].Value);
        var minor = match.Groups["minor"].Success ? int.Parse(match.Groups["minor"].Value) : 0;
        return major + minor / 10m;
    }

    private sealed record PathMetadata(string Tfm, string Configuration);

    private sealed record DllCandidate(FileInfo File, PathMetadata Metadata);
}
