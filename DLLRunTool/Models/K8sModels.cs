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
}

public class K8sWebResponse
{
    public string Type { get; set; } = "";
    public object? Payload { get; set; }
}
