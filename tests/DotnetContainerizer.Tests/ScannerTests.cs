using DotnetContainerizer.Model;
using DotnetContainerizer.Scanning;

namespace DotnetContainerizer.Tests;

public class ScannerTests
{
    [Fact]
    public void Scan_finds_projects_listed_in_the_solution()
    {
        using var solution = new TestSolution()
            .AddSolution("Contoso", "src/Contoso.Api/Contoso.Api.csproj", "src/Contoso.Worker/Contoso.Worker.csproj")
            .AddProject("src/Contoso.Api/Contoso.Api.csproj", TestSolution.WebProject())
            .AddProject("src/Contoso.Worker/Contoso.Worker.csproj", TestSolution.WorkerProject());

        var result = SolutionScanner.Scan(solution.Root, includeTestProjects: false);

        Assert.Single(result.SolutionPaths);
        Assert.Equal("Contoso", result.Name);
        Assert.Equal(solution.Root, result.ContextRoot);
        Assert.Equal(["Contoso.Api", "Contoso.Worker"], result.Containerizable.Select(project => project.Name));
    }

    [Fact]
    public void Scan_skips_libraries_and_test_projects()
    {
        using var solution = new TestSolution()
            .AddProject("src/Contoso.Api/Contoso.Api.csproj", TestSolution.WebProject())
            .AddProject("src/Contoso.Core/Contoso.Core.csproj", TestSolution.LibraryProject())
            .AddProject("tests/Contoso.Tests/Contoso.Tests.csproj", TestSolution.TestProject());

        var result = SolutionScanner.Scan(solution.Root, includeTestProjects: false);

        Assert.Equal(3, result.Projects.Count);
        var containerizable = Assert.Single(result.Containerizable);
        Assert.Equal("Contoso.Api", containerizable.Name);
        Assert.Equal("class library", result.Projects.Single(p => p.Name == "Contoso.Core").SkipReason);
        Assert.True(result.Projects.Single(p => p.Name == "Contoso.Tests").IsTestProject);
    }

    [Fact]
    public void Scan_finds_projects_without_a_solution_file()
    {
        using var solution = new TestSolution()
            .AddProject("Standalone/Standalone.csproj", TestSolution.ConsoleProject());

        var result = SolutionScanner.Scan(solution.Root, includeTestProjects: false);

        Assert.Empty(result.SolutionPaths);
        Assert.Equal(solution.Root, result.ContextRoot);
        Assert.Equal(ContainerKind.Console, Assert.Single(result.Containerizable).Kind);
    }

    [Fact]
    public void Scan_ignores_build_output_folders()
    {
        using var solution = new TestSolution()
            .AddProject("src/Contoso.Api/Contoso.Api.csproj", TestSolution.WebProject())
            .AddProject("src/Contoso.Api/obj/Debug/Leftover.csproj", TestSolution.WebProject())
            .AddProject("src/Contoso.Api/bin/Release/Leftover.csproj", TestSolution.WebProject());

        var result = SolutionScanner.Scan(solution.Root, includeTestProjects: false);

        Assert.Single(result.Projects);
    }

    [Fact]
    public void Scan_uses_the_solution_folder_as_build_context()
    {
        using var solution = new TestSolution()
            .AddSolution("Contoso", "Contoso.Api/Contoso.Api.csproj")
            .AddProject("Contoso.Api/Contoso.Api.csproj", TestSolution.WebProject());

        var result = SolutionScanner.Scan(Path.Combine(solution.Root, "Contoso.Api"), includeTestProjects: false);

        // The solution file lives above the scanned folder, so the scan folder is the context.
        Assert.Equal(Path.Combine(solution.Root, "Contoso.Api"), result.ContextRoot);
    }

    [Fact]
    public void Scan_can_include_test_projects()
    {
        using var solution = new TestSolution()
            .AddProject("tests/Contoso.Tests/Contoso.Tests.csproj", TestSolution.TestProject());

        var result = SolutionScanner.Scan(solution.Root, includeTestProjects: true);

        Assert.Empty(result.Containerizable);
    }

    [Fact]
    public void Scan_throws_for_a_missing_folder()
    {
        var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("n"));

        Assert.Throws<DirectoryNotFoundException>(() => SolutionScanner.Scan(missing, includeTestProjects: false));
    }

    [Fact]
    public void Launch_settings_property_names_are_case_insensitive()
    {
        using var solution = new TestSolution()
            .AddProject("Web/Web.csproj", TestSolution.WebProject())
            .AddFile("Web/Properties/launchSettings.json", """
                { "Profiles": { "web": { "CommandName": "Project", "ApplicationUrl": "https://localhost:7001" } } }
                """);

        var project = Assert.Single(SolutionScanner.Scan(solution.Root, false).Containerizable);
        Assert.Equal(8081, project.HttpsPort);
    }

    [Fact]
    public void Malformed_launch_settings_do_not_prevent_scanning()
    {
        using var solution = new TestSolution()
            .AddProject("Web/Web.csproj", TestSolution.WebProject("net7.0"))
            .AddFile("Web/Properties/launchSettings.json", "{ not json");

        var project = Assert.Single(SolutionScanner.Scan(solution.Root, false).Containerizable);
        Assert.Null(project.HttpsPort);
    }

    [Fact]
    public void Scan_does_not_follow_directory_symbolic_links()
    {
        using var solution = new TestSolution()
            .AddProject("src/App/App.csproj", TestSolution.ConsoleProject());
        var link = solution.PathTo("src/App/recursive");

        try
        {
            Directory.CreateSymbolicLink(link, solution.Root);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            return;
        }

        Assert.Single(SolutionScanner.Scan(solution.Root, false).Projects);
    }

    [Fact]
    public void Scan_reads_xml_solution_paths_with_single_quotes()
    {
        using var solution = new TestSolution()
            .AddFile("Example.slnx", "<Solution><Project Path='src/App/App.csproj' /></Solution>")
            .AddProject("src/App/App.csproj", TestSolution.ConsoleProject());

        Assert.Single(SolutionScanner.Scan(solution.Root, false).Containerizable);
    }

    [Fact]
    public void Scan_reads_target_framework_from_directory_build_props()
    {
        using var solution = new TestSolution()
            .AddFile("Directory.Build.props", "<Project><PropertyGroup><TargetFramework>net9.0</TargetFramework></PropertyGroup></Project>")
            .AddProject("src/App/App.csproj", "<Project Sdk='Microsoft.NET.Sdk'><PropertyGroup><OutputType>Exe</OutputType></PropertyGroup></Project>");

        var project = Assert.Single(SolutionScanner.Scan(solution.Root, false).Containerizable);
        Assert.Equal("net9.0", project.TargetFramework);
    }
}
