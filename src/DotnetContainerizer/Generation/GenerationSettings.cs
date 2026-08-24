namespace DotnetContainerizer.Generation;

/// <summary>Values that the pipeline and chart templates are filled with.</summary>
internal sealed class GenerationSettings
{
    /// <summary>Login server of the Azure Container Registry, e.g. <c>contoso.azurecr.io</c>.</summary>
    public required string Registry { get; init; }

    /// <summary>Name of the Azure DevOps Docker registry service connection.</summary>
    public required string ServiceConnection { get; init; }

    /// <summary>Repository prefix for the generated images, e.g. <c>contoso</c> in <c>contoso/contoso-api</c>.</summary>
    public required string ImagePrefix { get; init; }

    /// <summary>Name of the generated Helm chart, also used as Helm release name.</summary>
    public required string ChartName { get; init; }

    /// <summary>Name of the Azure DevOps Kubernetes service connection used by the deploy stage.</summary>
    public string KubernetesServiceConnection { get; init; } = "aks-service-connection";

    /// <summary>Kubernetes namespace the chart is deployed into.</summary>
    public string Namespace { get; init; } = "default";

    /// <summary>Whether the pipeline should contain a Helm deploy stage.</summary>
    public bool IncludeHelm { get; init; } = true;

    public ContainerOs Os { get; init; } = ContainerOs.Linux;

    /// <summary>Build agent image used by the generated pipeline jobs.</summary>
    public string VmImage => Os == ContainerOs.Windows ? "windows-latest" : "ubuntu-latest";
}
