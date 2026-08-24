using DotnetContainerizer.Generation;
using DotnetContainerizer.Scanning;

namespace DotnetContainerizer.Tests;

public class DockerfileGeneratorTests
{
    [Fact]
    public void Web_project_gets_an_aspnet_dockerfile()
    {
        using var solution = new TestSolution()
            .AddSolution("Contoso", "src/Contoso.Api/Contoso.Api.csproj")
            .AddProject("src/Contoso.Api/Contoso.Api.csproj", TestSolution.WebProject())
            .AddFile("src/Contoso.Api/Properties/launchSettings.json", TestSolution.HttpsLaunchSettings);

        var scan = SolutionScanner.Scan(solution.Root, includeTestProjects: false);
        var dockerfile = DockerfileGenerator.Generate(scan.Containerizable[0], scan, ContainerOs.Linux);

        Assert.Equal(Path.Combine(solution.Root, "src", "Contoso.Api", "Dockerfile"), dockerfile.Path);
        Assert.Contains("FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base", dockerfile.Content);
        Assert.Contains("FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build", dockerfile.Content);
        Assert.Contains("USER $APP_UID", dockerfile.Content);
        Assert.Contains("EXPOSE 8080", dockerfile.Content);
        Assert.Contains("EXPOSE 8081", dockerfile.Content);
        Assert.Contains("COPY [\"src/Contoso.Api/Contoso.Api.csproj\", \"src/Contoso.Api/\"]", dockerfile.Content);
        Assert.Contains("RUN dotnet restore \"src/Contoso.Api/Contoso.Api.csproj\"", dockerfile.Content);
        Assert.Contains("WORKDIR \"/src/src/Contoso.Api\"", dockerfile.Content);
        Assert.Contains("ENTRYPOINT [\"dotnet\", \"Contoso.Api.dll\"]", dockerfile.Content);
    }

    [Fact]
    public void Worker_project_gets_a_runtime_dockerfile_without_ports()
    {
        using var solution = new TestSolution()
            .AddProject("src/Contoso.Worker/Contoso.Worker.csproj", TestSolution.WorkerProject());

        var scan = SolutionScanner.Scan(solution.Root, includeTestProjects: false);
        var dockerfile = DockerfileGenerator.Generate(scan.Containerizable[0], scan, ContainerOs.Linux);

        Assert.Contains("FROM mcr.microsoft.com/dotnet/runtime:8.0 AS base", dockerfile.Content);
        Assert.DoesNotContain("EXPOSE", dockerfile.Content);
    }

    [Fact]
    public void Referenced_projects_are_copied_before_restore()
    {
        using var solution = new TestSolution()
            .AddProject("src/Api/Api.csproj", """
                <Project Sdk="Microsoft.NET.Sdk.Web">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                  </PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="..\Core\Core.csproj" />
                  </ItemGroup>
                </Project>
                """)
            .AddProject("src/Core/Core.csproj", TestSolution.LibraryProject());

        var scan = SolutionScanner.Scan(solution.Root, includeTestProjects: false);
        var content = DockerfileGenerator.Generate(scan.Containerizable[0], scan, ContainerOs.Linux).Content;

        Assert.Contains("COPY [\"src/Core/Core.csproj\", \"src/Core/\"]", content);
        Assert.True(
            content.IndexOf("COPY [\"src/Core/Core.csproj\"", StringComparison.Ordinal)
                < content.IndexOf("RUN dotnet restore", StringComparison.Ordinal),
            "referenced projects must be copied before the restore step");
    }

    [Fact]
    public void Windows_containers_use_nanoserver_images_and_no_app_uid()
    {
        using var solution = new TestSolution()
            .AddProject("src/Api/Api.csproj", TestSolution.WebProject());

        var scan = SolutionScanner.Scan(solution.Root, includeTestProjects: false);
        var content = DockerfileGenerator.Generate(scan.Containerizable[0], scan, ContainerOs.Windows).Content;

        Assert.Contains("FROM mcr.microsoft.com/dotnet/aspnet:8.0-nanoserver-ltsc2022 AS base", content);
        Assert.Contains("FROM mcr.microsoft.com/dotnet/sdk:8.0-nanoserver-ltsc2022 AS build", content);
        Assert.DoesNotContain("USER $APP_UID", content);
    }

    [Fact]
    public void Dotnet6_web_project_runs_as_root_on_port_80()
    {
        using var solution = new TestSolution()
            .AddProject("src/Api/Api.csproj", TestSolution.WebProject("net6.0"));

        var scan = SolutionScanner.Scan(solution.Root, includeTestProjects: false);
        var content = DockerfileGenerator.Generate(scan.Containerizable[0], scan, ContainerOs.Linux).Content;

        Assert.Contains("FROM mcr.microsoft.com/dotnet/aspnet:6.0 AS base", content);
        Assert.Contains("EXPOSE 80", content);
        Assert.DoesNotContain("USER $APP_UID", content);
    }
}
