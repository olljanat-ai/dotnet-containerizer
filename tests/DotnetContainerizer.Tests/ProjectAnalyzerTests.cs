using DotnetContainerizer.Model;
using DotnetContainerizer.Scanning;

namespace DotnetContainerizer.Tests;

public class ProjectAnalyzerTests
{
    [Fact]
    public void Test_package_references_using_update_are_recognized()
    {
        using var solution = new TestSolution().AddProject("Tests/Tests.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net8.0</TargetFramework><OutputType>Exe</OutputType></PropertyGroup>
              <ItemGroup><PackageReference Update="Microsoft.NET.Test.Sdk" Version="17.12.0" /></ItemGroup>
            </Project>
            """);

        var project = ProjectAnalyzer.Analyze(solution.PathTo("Tests/Tests.csproj"), solution.Root);
        Assert.True(project.IsTestProject);
    }

    [Theory]
    [InlineData("net8.0", 8)]
    [InlineData("net9.0", 9)]
    [InlineData("netcoreapp3.1", 3)]
    public void Supported_frameworks_are_recognized(string moniker, int major)
    {
        var framework = ProjectAnalyzer.ParseFramework(moniker);

        Assert.NotNull(framework);
        Assert.Equal(major, framework.Major);
    }

    [Theory]
    [InlineData("net48")]
    [InlineData("net472")]
    [InlineData("net8.0-windows")]
    [InlineData("netstandard2.0")]
    public void Unsupported_frameworks_are_rejected(string moniker) =>
        Assert.Null(ProjectAnalyzer.ParseFramework(moniker));

    [Fact]
    public void Web_projects_expose_the_dotnet8_default_ports()
    {
        using var solution = new TestSolution()
            .AddProject("Api/Api.csproj", TestSolution.WebProject())
            .AddFile("Api/Properties/launchSettings.json", TestSolution.HttpsLaunchSettings);

        var project = Analyze(solution, "Api/Api.csproj");

        Assert.Equal(ContainerKind.AspNet, project.Kind);
        Assert.Equal(8080, project.HttpPort);
        Assert.Equal(8081, project.HttpsPort);
    }

    [Fact]
    public void Web_projects_without_https_profile_expose_only_http()
    {
        using var solution = new TestSolution()
            .AddProject("Api/Api.csproj", TestSolution.WebProject())
            .AddFile("Api/Properties/launchSettings.json", TestSolution.HttpOnlyLaunchSettings);

        var project = Analyze(solution, "Api/Api.csproj");

        Assert.Equal(8080, project.HttpPort);
        Assert.Null(project.HttpsPort);
    }

    [Fact]
    public void Web_projects_on_dotnet6_expose_port_80()
    {
        using var solution = new TestSolution()
            .AddProject("Api/Api.csproj", TestSolution.WebProject("net6.0"));

        var project = Analyze(solution, "Api/Api.csproj");

        Assert.Equal(80, project.HttpPort);
    }

    [Fact]
    public void Multi_targeted_projects_use_the_newest_framework()
    {
        using var solution = new TestSolution()
            .AddProject("Tool/Tool.csproj", """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFrameworks>net6.0;net8.0</TargetFrameworks>
                    <OutputType>Exe</OutputType>
                  </PropertyGroup>
                </Project>
                """);

        var project = Analyze(solution, "Tool/Tool.csproj");

        Assert.Equal("net8.0", project.TargetFramework);
        Assert.Equal("8.0", project.FrameworkVersion);
    }

    [Fact]
    public void Assembly_name_override_is_used_for_the_entry_point()
    {
        using var solution = new TestSolution()
            .AddProject("Tool/Tool.csproj", TestSolution.ConsoleProject(assemblyName: "contoso-tool"));

        var project = Analyze(solution, "Tool/Tool.csproj");

        Assert.Equal("contoso-tool", project.AssemblyName);
    }

    [Fact]
    public void Desktop_and_legacy_projects_are_skipped()
    {
        using var solution = new TestSolution()
            .AddProject("App/App.csproj", """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net8.0-windows</TargetFramework>
                    <OutputType>WinExe</OutputType>
                  </PropertyGroup>
                </Project>
                """)
            .AddProject("Legacy/Legacy.csproj", """
                <?xml version="1.0" encoding="utf-8"?>
                <Project ToolsVersion="15.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
                  <PropertyGroup>
                    <OutputType>Exe</OutputType>
                    <TargetFrameworkVersion>v4.8</TargetFrameworkVersion>
                  </PropertyGroup>
                </Project>
                """);

        Assert.Equal(ContainerKind.None, Analyze(solution, "App/App.csproj").Kind);
        Assert.Equal("not an SDK-style project", Analyze(solution, "Legacy/Legacy.csproj").SkipReason);
    }

    [Fact]
    public void Blazor_webassembly_projects_are_skipped()
    {
        using var solution = new TestSolution()
            .AddProject("Client/Client.csproj", """
                <Project Sdk="Microsoft.NET.Sdk.BlazorWebAssembly">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                  </PropertyGroup>
                </Project>
                """);

        var project = Analyze(solution, "Client/Client.csproj");

        Assert.Equal(ContainerKind.None, project.Kind);
        Assert.Contains("Blazor", project.SkipReason);
    }

    [Fact]
    public void Console_projects_referencing_aspnetcore_are_treated_as_web_applications()
    {
        using var solution = new TestSolution()
            .AddProject("Api/Api.csproj", """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                    <OutputType>Exe</OutputType>
                  </PropertyGroup>
                  <ItemGroup>
                    <FrameworkReference Include="Microsoft.AspNetCore.App" />
                  </ItemGroup>
                </Project>
                """);

        Assert.Equal(ContainerKind.AspNet, Analyze(solution, "Api/Api.csproj").Kind);
    }

    [Theory]
    [InlineData("Contoso.Web.API", "contoso-web-api")]
    [InlineData("OrderProcessor", "order-processor")]
    [InlineData("My_App 2", "my-app-2")]
    [InlineData("Café.Api", "caf-api")]
    public void Component_names_are_dns_friendly(string projectName, string expected) =>
        Assert.Equal(expected, Naming.ToKebabCase(projectName));

    [Fact]
    public void Long_component_names_are_shortened_to_a_stable_dns_label()
    {
        var name = Naming.ToKebabCase(new string('a', 80));

        Assert.Equal(63, name.Length);
        Assert.Equal(name, Naming.ToKebabCase(new string('a', 80)));
        Assert.NotEqual(name, Naming.ToKebabCase(new string('a', 79) + "b"));
    }

    private static ProjectInfo Analyze(TestSolution solution, string relativePath) =>
        ProjectAnalyzer.Analyze(solution.PathTo(relativePath), solution.Root);
}
