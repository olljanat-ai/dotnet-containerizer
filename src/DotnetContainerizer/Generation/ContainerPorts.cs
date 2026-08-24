using DotnetContainerizer.Model;

namespace DotnetContainerizer.Generation;

/// <summary>
/// Decides which ports an image listens on. Visual Studio keeps the framework defaults, 80 and 443 up
/// to .NET 7 and 8080 and 8081 from .NET 8 on. A hardened image always runs as a non root user, and a
/// non root user cannot bind a port below 1024, so hardened images use 8080 and 8081 for every version.
/// </summary>
internal static class ContainerPorts
{
    public const int HardenedHttp = 8080;
    public const int HardenedHttps = 8081;

    public static int? Http(ProjectInfo project, bool hardened) => project.HttpPort switch
    {
        null => null,
        var port => hardened ? HardenedHttp : port,
    };

    public static int? Https(ProjectInfo project, bool hardened) => project.HttpsPort switch
    {
        null => null,
        var port => hardened ? HardenedHttps : port,
    };

    /// <summary>
    /// The environment variable that tells ASP.NET Core which port to listen on. Up to .NET 7 the
    /// default is port 80, so a hardened image has to move it explicitly.
    /// </summary>
    public static (string Name, string Value)? UrlEnvironmentVariable(ProjectInfo project, bool hardened)
    {
        if (!hardened || project.Kind != ContainerKind.AspNet || Http(project, hardened) is not { } port)
        {
            return null;
        }

        return project.FrameworkMajorVersion >= 8
            ? ("ASPNETCORE_HTTP_PORTS", port.ToString())
            : ("ASPNETCORE_URLS", $"http://+:{port}");
    }
}
