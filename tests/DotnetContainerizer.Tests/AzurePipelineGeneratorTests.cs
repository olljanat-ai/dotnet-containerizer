using DotnetContainerizer.Generation;
using DotnetContainerizer.Scanning;

namespace DotnetContainerizer.Tests;

public class AzurePipelineGeneratorTests
{
    private static readonly GenerationSettings Settings = new()
    {
        Registry = "contoso.azurecr.io",
        ServiceConnection = "contoso-acr",
        ImagePrefix = "contoso",
        ChartName = "contoso",
    };

    [Fact]
    public void Pipeline_has_one_build_job_per_containerizable_project()
    {
        using var solution = new TestSolution()
            .AddSolution("Contoso", "src/Contoso.Api/Contoso.Api.csproj", "src/Contoso.Worker/Contoso.Worker.csproj")
            .AddProject("src/Contoso.Api/Contoso.Api.csproj", TestSolution.WebProject())
            .AddProject("src/Contoso.Worker/Contoso.Worker.csproj", TestSolution.WorkerProject())
            .AddProject("src/Contoso.Core/Contoso.Core.csproj", TestSolution.LibraryProject());

        var scan = SolutionScanner.Scan(solution.Root, includeTestProjects: false);
        var pipeline = Single(AzurePipelineGenerator.Generate(scan, Settings), AzurePipelineGenerator.PipelineFileName);

        Assert.Contains("containerRegistry: 'contoso.azurecr.io'", pipeline.Content);
        Assert.Contains("dockerRegistryServiceConnection: 'contoso-acr'", pipeline.Content);
        Assert.Contains("imagePrefix: 'contoso'", pipeline.Content);
        Assert.Contains("name: build_contoso_api", pipeline.Content);
        Assert.Contains("repository: $(imagePrefix)/contoso-api", pipeline.Content);
        Assert.Contains("dockerfile: src/Contoso.Api/Dockerfile", pipeline.Content);
        Assert.Contains("name: build_contoso_worker", pipeline.Content);
        Assert.DoesNotContain("contoso-core", pipeline.Content);
    }

    [Fact]
    public void Pipeline_runs_the_tests_before_building_images()
    {
        using var solution = new TestSolution()
            .AddSolution("Contoso", "src/Contoso.Api/Contoso.Api.csproj", "tests/Contoso.Tests/Contoso.Tests.csproj")
            .AddProject("src/Contoso.Api/Contoso.Api.csproj", TestSolution.WebProject())
            .AddProject("tests/Contoso.Tests/Contoso.Tests.csproj", TestSolution.TestProject());

        var scan = SolutionScanner.Scan(solution.Root, includeTestProjects: false);
        var pipeline = Single(AzurePipelineGenerator.Generate(scan, Settings), AzurePipelineGenerator.PipelineFileName);

        Assert.Contains("- job: Test", pipeline.Content);
        Assert.Contains("projects: 'Contoso.sln'", pipeline.Content);
        Assert.Contains("dependsOn:\n            - Test", pipeline.Content.Replace("\r\n", "\n"));
    }

    [Fact]
    public void Pipeline_deploys_the_chart_after_the_images_are_pushed()
    {
        using var solution = new TestSolution()
            .AddProject("src/Contoso.Api/Contoso.Api.csproj", TestSolution.WebProject());

        var scan = SolutionScanner.Scan(solution.Root, includeTestProjects: false);
        var files = AzurePipelineGenerator.Generate(scan, Settings).ToList();
        var pipeline = Single(files, AzurePipelineGenerator.PipelineFileName);
        var deploy = Single(files, AzurePipelineGenerator.DeployTemplateFileName);

        Assert.Contains("- stage: Deploy", pipeline.Content);
        Assert.Contains("dependsOn: Build", pipeline.Content);
        Assert.Contains("helmChartPath: 'helm/contoso'", pipeline.Content);
        Assert.Contains("kubernetesServiceConnection: 'aks-service-connection'", pipeline.Content);
        Assert.Contains("command: upgrade", deploy.Content);
        Assert.Contains("--set image.tag=$(tag)", deploy.Content);
    }

    [Fact]
    public void Pipeline_without_helm_has_no_deploy_stage()
    {
        using var solution = new TestSolution()
            .AddProject("src/Contoso.Api/Contoso.Api.csproj", TestSolution.WebProject());

        var scan = SolutionScanner.Scan(solution.Root, includeTestProjects: false);
        var settings = new GenerationSettings
        {
            Registry = Settings.Registry,
            ServiceConnection = Settings.ServiceConnection,
            ImagePrefix = Settings.ImagePrefix,
            ChartName = Settings.ChartName,
            IncludeHelm = false,
        };

        var files = AzurePipelineGenerator.Generate(scan, settings).ToList();

        Assert.Equal(2, files.Count);
        Assert.DoesNotContain("- stage: Deploy", Single(files, AzurePipelineGenerator.PipelineFileName).Content);
    }

    [Fact]
    public void Pipeline_without_test_projects_has_no_test_job()
    {
        using var solution = new TestSolution()
            .AddProject("src/Contoso.Api/Contoso.Api.csproj", TestSolution.WebProject());

        var scan = SolutionScanner.Scan(solution.Root, includeTestProjects: false);
        var pipeline = Single(AzurePipelineGenerator.Generate(scan, Settings), AzurePipelineGenerator.PipelineFileName);

        Assert.DoesNotContain("- job: Test", pipeline.Content);
        Assert.DoesNotContain("dependsOn:\n            - Test", pipeline.Content.Replace("\r\n", "\n"));
    }

    [Fact]
    public void Pipeline_and_template_land_in_the_repository_root()
    {
        using var solution = new TestSolution()
            .AddFile(".git/HEAD", "ref: refs/heads/main")
            .AddSolution("code/Contoso", "src/Contoso.Api/Contoso.Api.csproj")
            .AddProject("code/src/Contoso.Api/Contoso.Api.csproj", TestSolution.WebProject());

        var scan = SolutionScanner.Scan(Path.Combine(solution.Root, "code"), includeTestProjects: false);
        var files = AzurePipelineGenerator.Generate(scan, Settings).ToList();

        Assert.Equal(solution.PathTo("azure-pipelines.yml"), files[0].Path);
        Assert.Equal(solution.PathTo(".azuredevops/templates/build-image.yml"), files[1].Path);

        // Paths in the pipeline are relative to the checkout folder, not to the solution folder.
        Assert.Contains("dockerfile: code/src/Contoso.Api/Dockerfile", files[0].Content);
        Assert.Contains("buildContext: $(Build.SourcesDirectory)/code", files[0].Content);
    }

    [Fact]
    public void Build_template_pushes_only_outside_pull_requests()
    {
        using var solution = new TestSolution()
            .AddProject("src/Contoso.Api/Contoso.Api.csproj", TestSolution.WebProject());

        var scan = SolutionScanner.Scan(solution.Root, includeTestProjects: false);
        var template = Single(AzurePipelineGenerator.Generate(scan, Settings), AzurePipelineGenerator.BuildTemplateFileName);

        Assert.Contains("command: buildAndPush", template.Content);
        Assert.Contains("condition: ne(variables['Build.Reason'], 'PullRequest')", template.Content);
        Assert.Contains("condition: eq(variables['Build.Reason'], 'PullRequest')", template.Content);
        Assert.Contains("containerRegistry: $(dockerRegistryServiceConnection)", template.Content);
    }

    [Fact]
    public void Windows_containers_build_on_a_windows_agent()
    {
        using var solution = new TestSolution()
            .AddProject("src/Contoso.Api/Contoso.Api.csproj", TestSolution.WebProject());

        var scan = SolutionScanner.Scan(solution.Root, includeTestProjects: false);
        var settings = new GenerationSettings
        {
            Registry = Settings.Registry,
            ServiceConnection = Settings.ServiceConnection,
            ImagePrefix = Settings.ImagePrefix,
            ChartName = Settings.ChartName,
            Os = ContainerOs.Windows,
        };

        var pipeline = Single(AzurePipelineGenerator.Generate(scan, settings), AzurePipelineGenerator.PipelineFileName);

        Assert.Contains("vmImageName: 'windows-latest'", pipeline.Content);
    }

    private static GeneratedFile Single(IEnumerable<GeneratedFile> files, string fileName) =>
        files.Single(file => Path.GetFileName(file.Path) == fileName);
}
