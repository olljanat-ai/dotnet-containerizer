using DotnetContainerizer.Generation;
using DotnetContainerizer.Scanning;

namespace DotnetContainerizer.Tests;

/// <summary>The hardening defaults, and what turning them off with --no-hardening leaves behind.</summary>
public class HardeningTests
{
    private static GenerationSettings Settings(bool hardened = true, ContainerOs os = ContainerOs.Linux) => new()
    {
        Registry = "contoso.azurecr.io",
        ServiceConnection = "contoso-acr",
        ImagePrefix = "contoso",
        ChartName = "contoso",
        Hardened = hardened,
        Os = os,
    };

    [Fact]
    public void Hardening_is_on_unless_it_is_turned_off()
    {
        Assert.False(CliOptionsFor([]).NoHardening);
        Assert.True(CliOptionsFor(["--no-hardening"]).NoHardening);
    }

    [Theory]
    [InlineData("net6.0")]
    [InlineData("net7.0")]
    public void Images_older_than_dotnet8_get_a_non_root_account(string targetFramework)
    {
        var dockerfile = Dockerfile(TestSolution.WebProject(targetFramework), Settings());

        Assert.Contains("adduser --system --uid 1654 --group --no-create-home app", dockerfile);
        Assert.Contains("USER app", dockerfile);
        Assert.DoesNotContain("USER $APP_UID", dockerfile);
    }

    [Fact]
    public void Dotnet8_and_newer_use_the_app_uid_of_the_base_image()
    {
        var dockerfile = Dockerfile(TestSolution.WebProject(), Settings());

        Assert.Contains("USER $APP_UID", dockerfile);
        Assert.DoesNotContain("adduser", dockerfile);
    }

    [Fact]
    public void A_non_root_image_listens_above_port_1024_on_every_framework_version()
    {
        // A non root user cannot bind port 80, so the hardened image has to move the port.
        var legacy = Dockerfile(TestSolution.WebProject("net6.0"), Settings());
        Assert.Contains("ENV ASPNETCORE_URLS=http://+:8080", legacy);
        Assert.Contains("EXPOSE 8080", legacy);
        Assert.DoesNotContain("EXPOSE 80\n", legacy.Replace("\r\n", "\n"));

        var current = Dockerfile(TestSolution.WebProject(), Settings());
        Assert.Contains("ENV ASPNETCORE_HTTP_PORTS=8080", current);
    }

    [Fact]
    public void Without_hardening_the_visual_studio_defaults_are_kept()
    {
        var legacy = Dockerfile(TestSolution.WebProject("net6.0"), Settings(hardened: false));

        Assert.DoesNotContain("adduser", legacy);
        Assert.DoesNotContain("ASPNETCORE_URLS", legacy);
        Assert.Contains("EXPOSE 80", legacy);
    }

    [Fact]
    public void Windows_images_run_as_the_unprivileged_container_user()
    {
        var dockerfile = Dockerfile(TestSolution.WorkerProject(), Settings(os: ContainerOs.Windows));

        Assert.Contains("USER ContainerUser", dockerfile);
    }

    [Fact]
    public void Chart_locks_the_pod_down_by_default()
    {
        var values = Values(Settings());

        Assert.Contains("runAsNonRoot: true", values);
        Assert.Contains("seccompProfile:", values);
        Assert.Contains("type: RuntimeDefault", values);
        Assert.Contains("allowPrivilegeEscalation: false", values);
        Assert.Contains("privileged: false", values);
        Assert.Contains("readOnlyRootFilesystem: true", values);
        Assert.Contains("automountServiceAccountToken: false", values);
        Assert.Contains("      - ALL", values);
    }

    [Fact]
    public void A_read_only_root_filesystem_still_gets_a_writable_tmp()
    {
        var deployment = Template(Settings(), "deployment.yaml");

        Assert.Contains("if or $root.Values.securityContext.readOnlyRootFilesystem $component.volumeMounts", deployment);
        Assert.Contains("mountPath: /tmp", deployment);
        Assert.Contains("emptyDir: {}", deployment);
    }

    [Fact]
    public void Turning_hardening_off_never_makes_the_chart_less_safe_than_the_baseline()
    {
        var values = Values(Settings(hardened: false));

        // These stay on, they cost nothing and no .NET workload needs them off.
        Assert.Contains("runAsNonRoot: true", values);
        Assert.Contains("allowPrivilegeEscalation: false", values);
        Assert.Contains("privileged: false", values);
        Assert.Contains("      - ALL", values);

        // These are the ones that need an application to cooperate, so they go back to the defaults.
        Assert.Contains("readOnlyRootFilesystem: false", values);
        Assert.DoesNotContain("seccompProfile", values);
        Assert.Contains("automountServiceAccountToken: true", values);
    }

    [Fact]
    public void Network_policy_is_generated_but_left_switched_off()
    {
        // Enforcement depends on the CNI plugin, so switching it on is the cluster owner's call.
        Assert.Contains("""
            networkPolicy:
              enabled: false
            """.Replace("\r\n", "\n"), Values(Settings()).Replace("\r\n", "\n"));
        Assert.Contains("kind: NetworkPolicy", Template(Settings(), "networkpolicy.yaml"));
    }

    [Fact]
    public void Pipeline_fails_the_build_on_vulnerable_packages()
    {
        var pipeline = Pipeline(Settings());

        Assert.Contains("Audit NuGet packages for known vulnerabilities", pipeline);
        Assert.Contains("package --vulnerable --include-transitive", pipeline);
        Assert.Contains("exit 1", pipeline);
        Assert.DoesNotContain("Audit NuGet packages", Pipeline(Settings(hardened: false)));
    }

    [Fact]
    public void The_audit_runs_in_powershell_on_windows_agents()
    {
        var pipeline = Pipeline(Settings(os: ContainerOs.Windows));

        Assert.Contains("- powershell: |", pipeline);
        Assert.DoesNotContain("- bash: |", pipeline);
    }

    private static DotnetContainerizer.Cli.CliOptions CliOptionsFor(string[] args)
    {
        Assert.True(DotnetContainerizer.Cli.CliOptions.TryParse(args, TextWriter.Null, out var options));
        return options;
    }

    private static string Dockerfile(string projectContent, GenerationSettings settings)
    {
        using var solution = new TestSolution().AddProject("src/Contoso.App/Contoso.App.csproj", projectContent);
        var scan = SolutionScanner.Scan(solution.Root, includeTestProjects: false);
        return DockerfileGenerator.Generate(scan.Containerizable[0], scan, settings).Content;
    }

    private static string Values(GenerationSettings settings) => Chart(settings, "values.yaml");

    private static string Template(GenerationSettings settings, string fileName) => Chart(settings, fileName);

    private static string Chart(GenerationSettings settings, string fileName)
    {
        using var solution = new TestSolution()
            .AddProject("src/Contoso.Api/Contoso.Api.csproj", TestSolution.WebProject());
        var scan = SolutionScanner.Scan(solution.Root, includeTestProjects: false);
        return HelmChartGenerator.Generate(scan, settings)
            .Single(file => Path.GetFileName(file.Path) == fileName)
            .Content;
    }

    private static string Pipeline(GenerationSettings settings)
    {
        using var solution = new TestSolution()
            .AddProject("src/Contoso.Api/Contoso.Api.csproj", TestSolution.WebProject());
        var scan = SolutionScanner.Scan(solution.Root, includeTestProjects: false);
        return AzurePipelineGenerator.Generate(scan, settings)
            .Single(file => Path.GetFileName(file.Path) == AzurePipelineGenerator.PipelineFileName)
            .Content;
    }
}
