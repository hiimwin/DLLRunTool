namespace DLLRunTool.Models;

public sealed class K8sClusterEntry
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>default = ~/.kube/config · file = kubeConfigPath cạnh exe hoặc absolute</summary>
    public string Source { get; set; } = "file";
    public string? KubeConfigPath { get; set; }
    public string? Context { get; set; }
    public string? Server { get; set; }
    /// <summary>Namespace gợi ý khi user không có quyền list cluster-wide (giống Lens chọn namespace).</summary>
    public List<string>? Namespaces { get; set; }
}

public sealed class K8sClustersFile
{
    public List<K8sClusterEntry> Clusters { get; set; } = [];
    public string? LastConnectedId { get; set; }
    /// <summary>Namespace theo context kubeconfig (lưu cá nhân trong k8s.local.json).</summary>
    public Dictionary<string, List<string>> NamespaceByContext { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Cluster ghim nhanh (hotbar) — hiện đầu danh sách.</summary>
    public List<string> HotbarIds { get; set; } = [];
    /// <summary>Ẩn cluster khỏi catalog (Remove from kubeconfig trong UI).</summary>
    public List<string> HiddenClusterIds { get; set; } = [];
    /// <summary>Màu trạng thái cluster: green | yellow | red | blue | grey.</summary>
    public Dictionary<string, string> ClusterColors { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

public sealed class K8sClusterUiDto
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Source { get; set; } = "";
    public string? KubeConfigPath { get; set; }
    public string? Context { get; set; }
    public string? Server { get; set; }
    public List<string> Contexts { get; set; } = [];
    public bool IsShared { get; set; }
    public bool IsLocal { get; set; }
    public bool KubeConfigExists { get; set; }
    public bool OnHotbar { get; set; }
    public string? Color { get; set; }
}
