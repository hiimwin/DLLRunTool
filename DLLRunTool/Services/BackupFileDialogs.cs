using System.Windows.Forms;

namespace DLLRunTool.Services;

public static class BackupFileDialogs
{
    public static string? PickSavePath(IWin32Window? owner, string defaultFileName)
    {
        using var dlg = new SaveFileDialog
        {
            Title = "Export cấu hình",
            Filter = "Backup JSON (*.json)|*.json",
            DefaultExt = "json",
            FileName = defaultFileName,
            InitialDirectory = ConfigBackupManager.BackupsFolder
        };

        Directory.CreateDirectory(ConfigBackupManager.BackupsFolder);
        return dlg.ShowDialog(owner) == DialogResult.OK ? dlg.FileName : null;
    }

    public static string? PickOpenPath(IWin32Window? owner)
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Import cấu hình",
            Filter = "Backup JSON (*.json)|*.json",
            DefaultExt = "json",
            InitialDirectory = ConfigBackupManager.BackupsFolder
        };

        if (!Directory.Exists(ConfigBackupManager.BackupsFolder))
            dlg.InitialDirectory = AppContext.BaseDirectory;

        return dlg.ShowDialog(owner) == DialogResult.OK ? dlg.FileName : null;
    }

    public static bool ConfirmApplyToSource(IWin32Window? owner, string platformName, string backupPlatform, int fileCount)
    {
        var msg = $"Apply cấu hình vào SOURCE CODE của '{platformName}'?\n\n" +
                  $"Nguồn backup: {backupPlatform}\n" +
                  $"Sẽ ghi đè {fileCount} file (appsettings.json, launchSettings.json, ocelot*.json, env.js...)\n" +
                  "trực tiếp trong thư mục project source.\n\n" +
                  "Hành động này không thể hoàn tác tự động.";

        return StyledMessageBox.Show(owner, msg, "Apply vào Source", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) ==
               DialogResult.Yes;
    }

    public static bool ConfirmImport(IWin32Window? owner, string platformName, string backupPlatform, int serviceCount) =>
        ConfirmApplyToSource(owner, platformName, backupPlatform, serviceCount);

    public static string? PickFolder(IWin32Window? owner, string? description = null, string? initialPath = null)
    {
        using var dlg = new FolderBrowserDialog
        {
            Description = description ?? "Chọn thư mục",
            UseDescriptionForTitle = true
        };

        if (!string.IsNullOrWhiteSpace(initialPath) && Directory.Exists(initialPath))
            dlg.InitialDirectory = initialPath;

        return dlg.ShowDialog(owner) == DialogResult.OK ? dlg.SelectedPath : null;
    }
}
