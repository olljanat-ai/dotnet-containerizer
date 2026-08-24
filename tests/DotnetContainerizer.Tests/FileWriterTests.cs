using DotnetContainerizer.Generation;

namespace DotnetContainerizer.Tests;

public class FileWriterTests
{
    [Fact]
    public void Existing_files_are_kept_unless_force_is_used()
    {
        using var solution = new TestSolution().AddFile("Dockerfile", "hand written");
        var file = new GeneratedFile(solution.PathTo("Dockerfile"), "generated");

        Assert.Equal(WriteOutcome.SkippedExisting, new FileWriter(force: false, dryRun: false).Write(file));
        Assert.Equal("hand written", solution.ReadGenerated("Dockerfile"));

        Assert.Equal(WriteOutcome.Overwritten, new FileWriter(force: true, dryRun: false).Write(file));
        Assert.Equal("generated", solution.ReadGenerated("Dockerfile"));
    }

    [Fact]
    public void Identical_content_is_reported_as_unchanged()
    {
        using var solution = new TestSolution().AddFile("Dockerfile", "generated");
        var file = new GeneratedFile(solution.PathTo("Dockerfile"), "generated");

        Assert.Equal(WriteOutcome.Unchanged, new FileWriter(force: false, dryRun: false).Write(file));
        Assert.Equal(WriteOutcome.Unchanged, new FileWriter(force: true, dryRun: false).Write(file));
    }

    [Fact]
    public void Dry_run_does_not_touch_the_disk()
    {
        using var solution = new TestSolution();
        var path = solution.PathTo("src/Api/Dockerfile");

        Assert.Equal(WriteOutcome.Planned, new FileWriter(force: false, dryRun: true).Write(new GeneratedFile(path, "generated")));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Missing_folders_are_created()
    {
        using var solution = new TestSolution();
        var path = solution.PathTo("helm/contoso/templates/deployment.yaml");

        Assert.Equal(WriteOutcome.Created, new FileWriter(force: false, dryRun: false).Write(new GeneratedFile(path, "kind: Deployment")));
        Assert.Equal("kind: Deployment", File.ReadAllText(path));
    }
}
