using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Text;

namespace DLLRunTool.Services;

public static class UpdateApplier
{
    private static readonly HttpClient Http = CreateHttpClient();

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true })
        {
            Timeout = TimeSpan.FromMinutes(15)
        };
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "DLLRunTool-Updater");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/octet-stream, application/zip, */*");
        return client;
    }

    private static readonly string[] PreserveFileNames =
    [
        "paths.local.json",
        "k8s.local.json",
        "service-locks.json",
        "run-settings.json"
    ];

    public static async Task ApplyAndRestartAsync(
        string downloadUrl,
        int processId,
        string installDir,
        Action<string, string> log,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(downloadUrl))
            throw new InvalidOperationException("Chưa có downloadUrl trong manifest.");

        installDir = Path.GetFullPath(installDir);
        var workDir = Path.Combine(Path.GetTempPath(), "DLLRunTool-update", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDir);

        var zipPath = Path.Combine(workDir, "update.zip");
        var extractDir = Path.Combine(workDir, "extract");
        Directory.CreateDirectory(extractDir);

        try
        {
            log("info", "Đang tải bản cập nhật...");
            await DownloadAsync(downloadUrl, zipPath, (pct) =>
                log("info", $"Tải update: {pct}%"), ct).ConfigureAwait(false);

            log("info", "Đang giải nén...");
            ZipFile.ExtractToDirectory(zipPath, extractDir, overwriteFiles: true);

            var stagingDir = ResolveStagingRoot(extractDir);
            if (!File.Exists(Path.Combine(stagingDir, "DLLRunTool.exe")))
                throw new InvalidOperationException("Gói update không hợp lệ (thiếu DLLRunTool.exe).");

            var preserveDir = Path.Combine(workDir, "preserve");
            Directory.CreateDirectory(preserveDir);
            CopyPreserveData(installDir, preserveDir);

            var scriptPath = Path.Combine(workDir, "apply-update.ps1");
            await File.WriteAllTextAsync(scriptPath, BuildApplyScript(
                installDir, stagingDir, preserveDir, processId), ct).ConfigureAwait(false);

            log("success", "Đang áp dụng bản mới và khởi động lại tool...");
            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{scriptPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            });
        }
        catch
        {
            try { Directory.Delete(workDir, true); } catch { /* ignore */ }
            throw;
        }
    }

    private static async Task DownloadAsync(string url, string destPath, Action<int> onProgress, CancellationToken ct)
    {
        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? -1;
        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var file = File.Create(destPath);

        var buffer = new byte[1024 * 128];
        long read = 0;
        int lastPct = -1;

        while (true)
        {
            var n = await stream.ReadAsync(buffer, ct).ConfigureAwait(false);
            if (n == 0) break;
            await file.WriteAsync(buffer.AsMemory(0, n), ct).ConfigureAwait(false);
            read += n;

            if (total > 0)
            {
                var pct = (int)(read * 100 / total);
                if (pct != lastPct && pct % 5 == 0)
                {
                    lastPct = pct;
                    onProgress(pct);
                }
            }
        }

        onProgress(100);
    }

    private static string ResolveStagingRoot(string extractDir)
    {
        var exe = Directory.EnumerateFiles(extractDir, "DLLRunTool.exe", SearchOption.AllDirectories).FirstOrDefault();
        return string.IsNullOrEmpty(exe) ? extractDir : Path.GetDirectoryName(exe)!;
    }

    private static void CopyPreserveData(string installDir, string preserveDir)
    {
        foreach (var name in PreserveFileNames)
        {
            var src = Path.Combine(installDir, name);
            if (File.Exists(src))
                File.Copy(src, Path.Combine(preserveDir, name), true);
        }

        foreach (var pattern in new[] { "global.*.json", "global.*.secrets.json" })
        {
            foreach (var src in Directory.EnumerateFiles(installDir, pattern))
            {
                var dest = Path.Combine(preserveDir, Path.GetFileName(src));
                File.Copy(src, dest, true);
            }
        }

        CopyDirectoryIfExists(Path.Combine(installDir, "backups"), Path.Combine(preserveDir, "backups"));
        CopyDirectoryIfExists(Path.Combine(installDir, "defaults"), Path.Combine(preserveDir, "defaults"));
    }

    private static void CopyDirectoryIfExists(string source, string dest)
    {
        if (!Directory.Exists(source))
            return;

        Directory.CreateDirectory(dest);
        foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(dir.Replace(source, dest, StringComparison.OrdinalIgnoreCase));

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = file.Replace(source, dest, StringComparison.OrdinalIgnoreCase);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, true);
        }
    }

    private static string BuildApplyScript(string installDir, string stagingDir, string preserveDir, int processId)
    {
        var sb = new StringBuilder();
        sb.AppendLine("$ErrorActionPreference = 'Stop'");
        sb.AppendLine($"$pidToWait = {processId}");
        sb.AppendLine($"$installDir = '{EscapePs(installDir)}'");
        sb.AppendLine($"$stagingDir = '{EscapePs(stagingDir)}'");
        sb.AppendLine($"$preserveDir = '{EscapePs(preserveDir)}'");
        sb.AppendLine("try { Wait-Process -Id $pidToWait -Timeout 120 -ErrorAction SilentlyContinue } catch {}");
        sb.AppendLine("Start-Sleep -Seconds 2");
        sb.AppendLine("robocopy $stagingDir $installDir /E /XO /R:2 /W:1 /NFL /NDL /NJH /NJS /nc /ns /np | Out-Null");
        sb.AppendLine("if ($LASTEXITCODE -ge 8) { exit 1 }");
        sb.AppendLine("foreach ($name in @('paths.local.json','service-locks.json','run-settings.json')) {");
        sb.AppendLine("  $src = Join-Path $preserveDir $name");
        sb.AppendLine("  if (Test-Path $src) { Copy-Item $src (Join-Path $installDir $name) -Force }");
        sb.AppendLine("}");
        sb.AppendLine("Get-ChildItem $preserveDir -Filter 'global.*.json' -ErrorAction SilentlyContinue | ForEach-Object {");
        sb.AppendLine("  Copy-Item $_.FullName (Join-Path $installDir $_.Name) -Force");
        sb.AppendLine("}");
        sb.AppendLine("Get-ChildItem $preserveDir -Filter 'global.*.secrets.json' -ErrorAction SilentlyContinue | ForEach-Object {");
        sb.AppendLine("  Copy-Item $_.FullName (Join-Path $installDir $_.Name) -Force");
        sb.AppendLine("}");
        sb.AppendLine("$backups = Join-Path $preserveDir 'backups'");
        sb.AppendLine("if (Test-Path $backups) {");
        sb.AppendLine("  $dest = Join-Path $installDir 'backups'");
        sb.AppendLine("  New-Item -ItemType Directory -Path $dest -Force | Out-Null");
        sb.AppendLine("  robocopy $backups $dest /E /XO /R:1 /W:1 /NFL /NDL /NJH /NJS | Out-Null");
        sb.AppendLine("}");
        sb.AppendLine("$defaults = Join-Path $preserveDir 'defaults'");
        sb.AppendLine("if (Test-Path $defaults) {");
        sb.AppendLine("  $dest = Join-Path $installDir 'defaults'");
        sb.AppendLine("  New-Item -ItemType Directory -Path $dest -Force | Out-Null");
        sb.AppendLine("  robocopy $defaults $dest /E /XO /R:1 /W:1 /NFL /NDL /NJH /NJS | Out-Null");
        sb.AppendLine("}");
        sb.AppendLine("Start-Process -FilePath (Join-Path $installDir 'DLLRunTool.exe') -WorkingDirectory $installDir");
        sb.AppendLine("Start-Sleep -Seconds 5");
        sb.AppendLine("Remove-Item (Split-Path $preserveDir -Parent) -Recurse -Force -ErrorAction SilentlyContinue");
        return sb.ToString();
    }

    private static string EscapePs(string value) => value.Replace("'", "''");
}
