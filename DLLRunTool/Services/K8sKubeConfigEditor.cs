using System.Text;
using YamlDotNet.RepresentationModel;

namespace DLLRunTool.Services;

/// <summary>Xóa context khỏi kubeconfig (giống Lens "Remove from kubeconfig").</summary>
public static class K8sKubeConfigEditor
{
    public static (bool Ok, string Message) TryRemoveContext(string kubeConfigPath, string contextName)
    {
        if (string.IsNullOrWhiteSpace(kubeConfigPath) || !File.Exists(kubeConfigPath))
            return (false, "Không tìm thấy file kubeconfig.");

        if (string.IsNullOrWhiteSpace(contextName))
            return (false, "Thiếu tên context.");

        try
        {
            using var reader = new StreamReader(kubeConfigPath);
            var yaml = new YamlStream();
            yaml.Load(reader);

            if (yaml.Documents.Count == 0 || yaml.Documents[0].RootNode is not YamlMappingNode root)
                return (false, "Kubeconfig không hợp lệ.");

            string? clusterRef = null;
            string? userRef = null;

            if (root.Children.TryGetValue(new YamlScalarNode("contexts"), out var contextsNode)
                && contextsNode is YamlSequenceNode contexts)
            {
                for (var i = contexts.Children.Count - 1; i >= 0; i--)
                {
                    if (contexts.Children[i] is not YamlMappingNode ctxEntry)
                        continue;

                    if (!ctxEntry.Children.TryGetValue(new YamlScalarNode("name"), out var nameNode)
                        || nameNode is not YamlScalarNode nameScalar)
                        continue;

                    if (!string.Equals(nameScalar.Value, contextName, StringComparison.Ordinal))
                        continue;

                    if (ctxEntry.Children.TryGetValue(new YamlScalarNode("context"), out var inner)
                        && inner is YamlMappingNode ctxMap)
                    {
                        if (ctxMap.Children.TryGetValue(new YamlScalarNode("cluster"), out var cNode)
                            && cNode is YamlScalarNode cScalar)
                            clusterRef = cScalar.Value;
                        if (ctxMap.Children.TryGetValue(new YamlScalarNode("user"), out var uNode)
                            && uNode is YamlScalarNode uScalar)
                            userRef = uScalar.Value;
                    }

                    contexts.Children.RemoveAt(i);
                }
            }

            if (root.Children.TryGetValue(new YamlScalarNode("current-context"), out var curNode)
                && curNode is YamlScalarNode curScalar
                && string.Equals(curScalar.Value, contextName, StringComparison.Ordinal))
            {
                root.Children.Remove(new YamlScalarNode("current-context"));
            }

            RemoveNamedEntry(root, "clusters", clusterRef);
            RemoveNamedEntry(root, "users", userRef);
            RemoveNamedEntry(root, "authinfos", userRef);

            var backup = kubeConfigPath + ".bak";
            File.Copy(kubeConfigPath, backup, overwrite: true);

            using var writer = new StreamWriter(kubeConfigPath, false, new UTF8Encoding(false));
            yaml.Save(writer, assignAnchors: false);

            return (true, $"Đã xóa context '{contextName}' khỏi kubeconfig (backup: {Path.GetFileName(backup)}).");
        }
        catch (Exception ex)
        {
            return (false, $"Không sửa được kubeconfig: {ex.Message}");
        }
    }

    private static void RemoveNamedEntry(YamlMappingNode root, string section, string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return;

        if (!root.Children.TryGetValue(new YamlScalarNode(section), out var node) || node is not YamlSequenceNode list)
            return;

        for (var i = list.Children.Count - 1; i >= 0; i--)
        {
            if (list.Children[i] is not YamlMappingNode entry)
                continue;

            if (entry.Children.TryGetValue(new YamlScalarNode("name"), out var nameNode)
                && nameNode is YamlScalarNode scalar
                && string.Equals(scalar.Value, name, StringComparison.Ordinal))
            {
                list.Children.RemoveAt(i);
            }
        }
    }
}
