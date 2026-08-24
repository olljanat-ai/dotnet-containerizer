namespace DotnetContainerizer.Model;

/// <summary>Kind of container image that fits a project, if any.</summary>
internal enum ContainerKind
{
    /// <summary>The project is not containerizable (library, test project, unsupported framework).</summary>
    None,

    /// <summary>ASP.NET Core application, runs on the aspnet runtime image and listens on a port.</summary>
    AspNet,

    /// <summary>Console or worker application, runs on the plain runtime image.</summary>
    Console,
}

/// <summary>Everything the generators need to know about a single project.</summary>
internal sealed class ProjectInfo
{
    public required string FullPath { get; init; }

    /// <summary>Project file name without extension, e.g. <c>Contoso.Api</c>.</summary>
    public required string Name { get; init; }

    /// <summary>Directory holding the project file.</summary>
    public required string Directory { get; init; }

    /// <summary>Project path relative to the build context root, using forward slashes.</summary>
    public required string RelativePath { get; init; }

    /// <summary>Name of the produced assembly, without extension.</summary>
    public required string AssemblyName { get; init; }

    /// <summary>Value of the <c>Sdk</c> attribute, e.g. <c>Microsoft.NET.Sdk.Web</c>.</summary>
    public string Sdk { get; init; } = "Microsoft.NET.Sdk";

    public string? OutputType { get; init; }

    /// <summary>The target framework moniker the container is built for, e.g. <c>net8.0</c>.</summary>
    public string? TargetFramework { get; init; }

    /// <summary>The framework version used as image tag, e.g. <c>8.0</c>.</summary>
    public string? FrameworkVersion { get; init; }

    public int FrameworkMajorVersion { get; init; }

    public bool IsTestProject { get; init; }

    /// <summary>Full paths of the projects referenced by this project.</summary>
    public IReadOnlyList<string> ProjectReferences { get; init; } = [];

    public ContainerKind Kind { get; init; }

    /// <summary>Why the project is skipped, when <see cref="Kind"/> is <see cref="ContainerKind.None"/>.</summary>
    public string? SkipReason { get; init; }

    /// <summary>Container port the application listens on, for ASP.NET Core projects.</summary>
    public int? HttpPort { get; init; }

    /// <summary>Container port used for HTTPS, when the project has an HTTPS launch profile.</summary>
    public int? HttpsPort { get; init; }

    public bool IsContainerizable => Kind != ContainerKind.None;

    /// <summary>Lower case, dash separated name used for image repositories and Kubernetes objects.</summary>
    public string ComponentName => Naming.ToKebabCase(Name);

    public override string ToString() => $"{Name} ({Kind})";
}
