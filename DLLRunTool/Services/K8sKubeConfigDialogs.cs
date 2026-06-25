using System.Windows.Forms;

namespace DLLRunTool.Services;

public static class K8sKubeConfigDialogs
{
    public static string? PickKubeConfigFile(IWin32Window? owner)
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Chọn file kubeconfig",
            Filter = "Kubeconfig (*.yaml;*.yml;*.json;*.*)|*.yaml;*.yml;*.json;*.*",
            InitialDirectory = K8sClusterStore.KubeConfigsFolder
        };

        if (!Directory.Exists(dlg.InitialDirectory))
            dlg.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        return dlg.ShowDialog(owner) == DialogResult.OK ? dlg.FileName : null;
    }
}
