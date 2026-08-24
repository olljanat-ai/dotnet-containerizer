using DotnetContainerizer.Cli;
using DotnetContainerizer.Generation;

namespace DotnetContainerizer.Tests;

public class CliOptionsTests
{
    [Fact]
    public void Defaults_scan_the_current_folder_for_linux_containers()
    {
        Assert.True(CliOptions.TryParse([], TextWriter.Null, out var options));

        Assert.Equal(".", options.Path);
        Assert.Equal(ContainerOs.Linux, options.Os);
        Assert.False(options.Force);
        Assert.False(options.DryRun);
    }

    [Fact]
    public void Path_can_be_positional_or_named()
    {
        Assert.True(CliOptions.TryParse(["./src"], TextWriter.Null, out var positional));
        Assert.Equal("./src", positional.Path);

        Assert.True(CliOptions.TryParse(["--path", "./src"], TextWriter.Null, out var named));
        Assert.Equal("./src", named.Path);
    }

    [Fact]
    public void Switches_are_parsed()
    {
        Assert.True(CliOptions.TryParse(
            ["--os", "windows", "--force", "--dry-run", "--include-tests", "--list", "--verbose"],
            TextWriter.Null,
            out var options));

        Assert.Equal(ContainerOs.Windows, options.Os);
        Assert.True(options.Force);
        Assert.True(options.DryRun);
        Assert.True(options.IncludeTests);
        Assert.True(options.ListOnly);
        Assert.True(options.Verbose);
    }

    [Theory]
    [InlineData("--unknown")]
    [InlineData("--os")]
    [InlineData("--os", "freebsd")]
    public void Invalid_input_is_reported(params string[] args)
    {
        var error = new StringWriter();

        Assert.False(CliOptions.TryParse(args, error, out _));
        Assert.NotEmpty(error.ToString());
    }

    [Fact]
    public void Two_paths_are_rejected()
    {
        Assert.False(CliOptions.TryParse(["one", "two"], TextWriter.Null, out _));
    }
}
