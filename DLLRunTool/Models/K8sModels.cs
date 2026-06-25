namespace DLLRunTool.Models;

public class K8sPodDto
{
    public string Name { get; set; } = "";
    public string Namespace { get; set; } = "";
    public string Status { get; set; } = "";
    public int RestartCount { get; set; }
    public string Age { get; set; } = "";
    public string Container { get; set; } = "";
}

public class K8sNodeDto
{
    public string Name { get; set; } = "";
    public string Status { get; set; } = "";
    public string Roles { get; set; } = "";
    public string Age { get; set; } = "";
}

public class K8sDeploymentDto
{
    public string Name { get; set; } = "";
    public string Namespace { get; set; } = "";
    public string Ready { get; set; } = "";
    public string Age { get; set; } = "";
}

public class K8sListItemDto
{
    public string Name { get; set; } = "";
    public string Namespace { get; set; } = "";
    public string Status { get; set; } = "";
    public string Age { get; set; } = "";
    public string Detail { get; set; } = "";
    public string? Container { get; set; }
    public int RestartCount { get; set; }
    public List<K8sPortDto>? Ports { get; set; }
}

public class K8sPortDto
{
    public string Name { get; set; } = "";
    public int Port { get; set; }
    public string Protocol { get; set; } = "TCP";
}

public class K8sServiceDetailDto
{
    public string Name { get; set; } = "";
    public string Namespace { get; set; } = "";
    public string Created { get; set; } = "";
    public string CreatedAt { get; set; } = "";
    public Dictionary<string, string> Labels { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> Annotations { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> Selector { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string Type { get; set; } = "";
    public string SessionAffinity { get; set; } = "";
    public string ClusterIP { get; set; } = "";
    public List<string> ClusterIPs { get; set; } = [];
    public string IpFamilies { get; set; } = "";
    public string IpFamilyPolicy { get; set; } = "";
    public List<K8sServicePortDetailDto> Ports { get; set; } = [];
    public List<K8sEndpointSliceRowDto> EndpointSlices { get; set; } = [];
    public List<K8sEndpointRowDto> Endpoints { get; set; } = [];
    public List<K8sServiceEventDto> Events { get; set; } = [];
}

public class K8sServicePortDetailDto
{
    public string Name { get; set; } = "";
    public int Port { get; set; }
    public string TargetPort { get; set; } = "";
    public string Protocol { get; set; } = "TCP";
    public int? NodePort { get; set; }
    public bool Forwarding { get; set; }
    public string? PortForwardId { get; set; }
    public string? ConfigId { get; set; }
    public int? LocalPort { get; set; }
    public bool UseHttps { get; set; }
    public bool OpenInBrowser { get; set; }
}

public class K8sEndpointSliceRowDto
{
    public string Name { get; set; } = "";
    public string Endpoints { get; set; } = "";
}

public class K8sEndpointRowDto
{
    public string Name { get; set; } = "";
    public string Endpoints { get; set; } = "";
}

public class K8sServiceEventDto
{
    public string Type { get; set; } = "";
    public string Reason { get; set; } = "";
    public string Message { get; set; } = "";
    public string Age { get; set; } = "";
}

public class K8sPortForwardDto
{
    public string Id { get; set; } = "";
    public string ConfigId { get; set; } = "";
    public string Namespace { get; set; } = "";
    public string ResourceKind { get; set; } = "";
    public string ResourceName { get; set; } = "";
    public int RemotePort { get; set; }
    public int LocalPort { get; set; }
    public bool UseHttps { get; set; }
    public bool OpenInBrowser { get; set; }
    public bool Running { get; set; }
    public string Status { get; set; } = "Disabled";
    public string Protocol { get; set; } = "http";
    public string Url { get; set; } = "";
    public string? Error { get; set; }
}

public class K8sOverviewDto
{
    public string Version { get; set; } = "";
    public int PodCount { get; set; }
    public int NodeCount { get; set; }
    public int DeploymentCount { get; set; }
    public int NamespaceCount { get; set; }
    public string ClusterName { get; set; } = "";
    public string Context { get; set; } = "";
}

public class K8sWebRequest
{
    public string Action { get; set; } = "";
    public string? PodName { get; set; }
    public string? Namespace { get; set; }
    public string? Name { get; set; }
    public string? View { get; set; }
    public string? ClusterId { get; set; }
    public string? KubeConfigPath { get; set; }
    public string? Context { get; set; }
    public string? ClusterName { get; set; }
    public bool CopyToAppFolder { get; set; } = true;
    public List<string>? Namespaces { get; set; }
    public string? Yaml { get; set; }
    public string? Container { get; set; }
    public string? Line { get; set; }
    public string? SessionId { get; set; }
    public string? ResourceKind { get; set; }
    public int? Port { get; set; }
    public int? LocalPort { get; set; }
    public string? PortForwardId { get; set; }
    public string? ConfigId { get; set; }
    public bool UseHttps { get; set; }
    public bool OpenInBrowser { get; set; }
    public bool SaveOnly { get; set; }
}

public class K8sWebResponse
{
    public string Type { get; set; } = "";
    public object? Payload { get; set; }
}
