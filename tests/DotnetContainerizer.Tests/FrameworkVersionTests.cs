using DotnetContainerizer.Generation;
using DotnetContainerizer.Scanning;

namespace DotnetContainerizer.Tests;

/// <summary>Solutions that mix framework versions have to work without any switch being passed.</summary>
public class FrameworkVersionTests
{
    private static readonly GenerationSettings Settings = new()
    {
        Registry = "contoso.azurecr.io",
        ServiceConnection = "contoso-acr",
        ImagePrefix = "contoso",
        ChartName = "contoso",
    };

    [Fact]
    public void Every_project_is_built_on_the_image_of_its_own_framework()
    {
        using var solution = new TestSolution()
            .AddProject("src/Legacy.Api/Legacy.Api.csproj", TestSolution.WebProject("net6.0"))
            .AddProject("src/Next.Worker/Next.Worker.csproj", TestSolution.WorkerProject("net10.0"));

        var scan = SolutionScanner.Scan(solution.Root, includeTestProjects: false);
        var legacy = Dockerfile(scan, "Legacy.Api");
        var next = Dockerfile(scan, "Next.Worker");

        Assert.Contains("FROM mcr.microsoft.com/dotnet/aspnet:6.0 AS base", legacy);
        Assert.Contains("FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build", legacy);
        Assert.Contains("FROM mcr.microsoft.com/dotnet/runtime:10.0 AS base", next);
        Assert.Contains("FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build", next);
    }

    [Fact]
    public void The_pipeline_installs_an_sdk_for_every_framework_in_the_solution()
    {
        using var solution = new TestSolution()
            .AddProject("src/Legacy.Api/Legacy.Api.csproj", TestSolution.WebProject("net6.0"))
            .AddProject("src/Contoso.Api/Contoso.Api.csproj", TestSolution.WebProject())
            .AddProject("tests/Contoso.Tests/Contoso.Tests.csproj", TestSolution.TestProject("net9.0"));

        var pipeline = Pipeline(solution);

        Assert.Contains("version: 6.0.x", pipeline);
        Assert.Contains("version: 8.0.x", pipeline);
        Assert.Contains("version: 9.0.x", pipeline);
    }

    [Fact]
    public void Sdk_versions_are_ordered_as_numbers_not_as_text()
    {
        using var solution = new TestSolution()
            .AddProject("src/Contoso.Api/Contoso.Api.csproj", TestSolution.WebProject())
            .AddProject("src/Next.Worker/Next.Worker.csproj", TestSolution.WorkerProject("net10.0"));

        var pipeline = Pipeline(solution);
        var lines = pipeline.Replace("\r\n", "\n").Split('\n');

        // Sorted as text "8.0" would beat "10.0" and the newest SDK would be the wrong one.
        Assert.True(
            Array.IndexOf(lines, "              version: 8.0.x") < Array.IndexOf(lines, "              version: 10.0.x"),
            "SDK versions must be installed in numeric order");
        Assert.Contains("dotnetVersion: '10.0.x'", pipeline);
    }

    [Fact]
    public void A_multi_targeted_project_is_containerized_on_its_newest_framework()
    {
        using var solution = new TestSolution()
            .AddProject("src/Contoso.Tool/Contoso.Tool.csproj", """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFrameworks>net8.0;net10.0;net6.0</TargetFrameworks>
                    <OutputType>Exe</OutputType>
                  </PropertyGroup>
                </Project>
                """);

        var scan = SolutionScanner.Scan(solution.Root, includeTestProjects: false);

        Assert.Contains("FROM mcr.microsoft.com/dotnet/runtime:10.0 AS base", Dockerfile(scan, "Contoso.Tool"));
    }

    private static string Dockerfile(Model.ScanResult scan, string projectName) =>
        DockerfileGenerator
            .Generate(scan.Containerizable.Single(project => project.Name == projectName), scan, Settings)
            .Content;

    private static string Pipeline(TestSolution solution)
    {
        var scan = SolutionScanner.Scan(solution.Root, includeTestProjects: false);
        return AzurePipelineGenerator.Generate(scan, Settings)
            .Single(file => Path.GetFileName(file.Path) == AzurePipelineGenerator.PipelineFileName)
            .Content;
    }
}
