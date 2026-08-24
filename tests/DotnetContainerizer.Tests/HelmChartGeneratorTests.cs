using DotnetContainerizer.Generation;
using DotnetContainerizer.Scanning;

namespace DotnetContainerizer.Tests;

public class HelmChartGeneratorTests
{
    private static readonly GenerationSettings Settings = new()
    {
        Registry = "contoso.azurecr.io",
        ServiceConnection = "contoso-acr",
        ImagePrefix = "contoso",
        ChartName = "contoso",
        Namespace = "contoso-prod",
    };

    [Fact]
    public void Chart_is_written_below_the_repository_root()
    {
        using var solution = new TestSolution()
            .AddFile(".git/HEAD", "ref: refs/heads/main")
            .AddProject("code/src/Contoso.Api/Contoso.Api.csproj", TestSolution.WebProject());

        var scan = SolutionScanner.Scan(Path.Combine(solution.Root, "code"), includeTestProjects: false);
        var files = HelmChartGenerator.Generate(scan, Settings).Select(file => file.Path).ToList();

        Assert.Contains(solution.PathTo("helm/contoso/Chart.yaml"), files);
        Assert.Contains(solution.PathTo("helm/contoso/values.yaml"), files);
        Assert.Contains(solution.PathTo("helm/contoso/templates/deployment.yaml"), files);
        Assert.Contains(solution.PathTo("helm/contoso/templates/service.yaml"), files);
        Assert.Contains(solution.PathTo("helm/contoso/templates/ingress.yaml"), files);
        Assert.Contains(solution.PathTo("helm/contoso/templates/serviceaccount.yaml"), files);
        Assert.Contains(solution.PathTo("helm/contoso/templates/_helpers.tpl"), files);
        Assert.Contains(solution.PathTo("helm/contoso/templates/NOTES.txt"), files);
        Assert.Contains(solution.PathTo("helm/contoso/.helmignore"), files);
    }

    [Fact]
    public void Values_hold_one_component_per_containerizable_project()
    {
        using var solution = new TestSolution()
            .AddSolution("Contoso", "src/Contoso.Api/Contoso.Api.csproj", "src/Contoso.Worker/Contoso.Worker.csproj")
            .AddProject("src/Contoso.Api/Contoso.Api.csproj", TestSolution.WebProject())
            .AddProject("src/Contoso.Worker/Contoso.Worker.csproj", TestSolution.WorkerProject())
            .AddProject("src/Contoso.Core/Contoso.Core.csproj", TestSolution.LibraryProject());

        var values = File(solution, "values.yaml");

        Assert.Contains("registry: contoso.azurecr.io", values);
        Assert.Contains("prefix: contoso", values);
        Assert.Contains("  contoso-api:", values);
        Assert.Contains("    repository: contoso-api", values);
        Assert.Contains("  contoso-worker:", values);
        Assert.DoesNotContain("contoso-core", values);
    }

    [Fact]
    public void Web_components_get_a_service_and_a_container_port()
    {
        using var solution = new TestSolution()
            .AddProject("src/Contoso.Api/Contoso.Api.csproj", TestSolution.WebProject());

        var values = File(solution, "values.yaml");

        Assert.Contains("containerPort: 8080", values);
        Assert.Contains("value: \"8080\"", values);
        Assert.Contains("ASPNETCORE_HTTP_PORTS", values);
        Assert.Contains("      enabled: true", values);
        Assert.Contains("host: contoso-api.example.com", values);
    }

    [Fact]
    public void Worker_components_have_no_service_and_no_port()
    {
        using var solution = new TestSolution()
            .AddProject("src/Contoso.Worker/Contoso.Worker.csproj", TestSolution.WorkerProject());

        var values = File(solution, "values.yaml");

        Assert.DoesNotContain("containerPort", values);
        Assert.DoesNotContain("ASPNETCORE", values);
        Assert.Contains("""
            service:
                  enabled: false
            """.Replace("\r\n", "\n"), values.Replace("\r\n", "\n"));
    }

    [Fact]
    public void Chart_metadata_uses_the_chart_name()
    {
        using var solution = new TestSolution()
            .AddSolution("Contoso", "src/Contoso.Api/Contoso.Api.csproj")
            .AddProject("src/Contoso.Api/Contoso.Api.csproj", TestSolution.WebProject());

        var chart = File(solution, "Chart.yaml");

        Assert.Contains("name: contoso", chart);
        Assert.Contains("apiVersion: v2", chart);
        Assert.Contains("Deploys the components of the Contoso solution.", chart);
    }

    [Fact]
    public void Templates_render_every_enabled_component()
    {
        using var solution = new TestSolution()
            .AddProject("src/Contoso.Api/Contoso.Api.csproj", TestSolution.WebProject());

        var deployment = File(solution, "deployment.yaml");
        var service = File(solution, "service.yaml");

        Assert.Contains("range $name, $component := .Values.components", deployment);
        Assert.Contains("kind: Deployment", deployment);
        Assert.Contains("image: {{ include \"solution.image\"", deployment);
        Assert.Contains("if and $component.enabled $component.service $component.service.enabled", service);
    }

    private static string File(TestSolution solution, string fileName)
    {
        var scan = SolutionScanner.Scan(solution.Root, includeTestProjects: false);
        return HelmChartGenerator.Generate(scan, Settings)
            .Single(file => Path.GetFileName(file.Path) == fileName)
            .Content;
    }
}
