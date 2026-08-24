using System.Reflection;
using DotnetContainerizer.Cli;
using DotnetContainerizer.Generation;
using DotnetContainerizer.Model;
using DotnetContainerizer.Scanning;

namespace DotnetContainerizer;

internal static class Program
{
    public static int Main(string[] args)
    {
        if (!CliOptions.TryParse(args, Console.Error, out var options))
        {
            return 1;
        }

        if (options.ShowHelp)
        {
            Console.Write(CliOptions.HelpText);
            return 0;
        }

        if (options.ShowVersion)
        {
            Console.WriteLine(Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "unknown");
            return 0;
        }

        try
        {
            return Run(options);
        }
        catch (DirectoryNotFoundException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Failed to write the generated files: {exception.Message}");
            return 1;
        }
    }

    private static int Run(CliOptions options)
    {
        var scan = SolutionScanner.Scan(options.Path, options.IncludeTests);
        Report(scan, options);

        if (scan.Containerizable.Count == 0)
        {
            Console.Error.WriteLine("No containerizable projects were found.");
            return 1;
        }

        if (options.ListOnly)
        {
            return 0;
        }

        var settings = new GenerationSettings
        {
            Registry = options.Registry,
            ServiceConnection = options.ServiceConnection,
            ImagePrefix = options.ImagePrefix is { Length: > 0 } prefix ? prefix : Naming.ToKebabCase(scan.Name),
            Os = options.Os,
        };

        var writer = new FileWriter(options.Force, options.DryRun);
        var skipped = 0;

        Console.WriteLine();
        if (!options.NoDockerfile)
        {
            foreach (var project in scan.Containerizable)
            {
                skipped += Write(writer, DockerfileGenerator.Generate(project, scan, options.Os), scan);
            }

            skipped += Write(writer, DockerIgnoreGenerator.Generate(scan), scan);
        }

        if (!options.NoPipeline)
        {
            foreach (var file in AzurePipelineGenerator.Generate(scan, settings))
            {
                skipped += Write(writer, file, scan);
            }
        }

        if (skipped > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"{skipped} file(s) already exist and were left untouched. Re-run with --force to overwrite them.");
        }

        if (options.DryRun)
        {
            Console.WriteLine();
            Console.WriteLine("Dry run, nothing was written.");
        }

        return 0;
    }

    private static void Report(ScanResult scan, CliOptions options)
    {
        Console.WriteLine($"Scanned {scan.RootPath}");
        foreach (var solution in scan.SolutionPaths)
        {
            Console.WriteLine($"  solution: {Paths.RelativeTo(scan.RootPath, solution)}");
        }

        Console.WriteLine($"  build context: {scan.ContextRoot}");
        Console.WriteLine();
        Console.WriteLine($"Containerizable projects ({scan.Containerizable.Count}):");
        foreach (var project in scan.Containerizable)
        {
            var kind = project.Kind == ContainerKind.AspNet ? "asp.net core" : "console/worker";
            var ports = project.HttpPort is { } port
                ? $", port {port}" + (project.HttpsPort is { } securePort ? $"/{securePort}" : string.Empty)
                : string.Empty;
            Console.WriteLine($"  {project.Name} [{kind}, {project.TargetFramework}{ports}] -> {project.RelativePath}");
        }

        var skipped = scan.Projects.Where(project => !scan.Containerizable.Contains(project)).ToList();
        if (skipped.Count > 0 && options.Verbose)
        {
            Console.WriteLine();
            Console.WriteLine($"Skipped projects ({skipped.Count}):");
            foreach (var project in skipped)
            {
                Console.WriteLine($"  {project.Name}: {project.SkipReason ?? "test project"}");
            }
        }
        else if (skipped.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"{skipped.Count} project(s) skipped, run with --verbose to see why.");
        }
    }

    private static int Write(FileWriter writer, GeneratedFile file, ScanResult scan)
    {
        var outcome = writer.Write(file);
        var path = Paths.RelativeTo(scan.RootPath, file.Path);
        var message = outcome switch
        {
            WriteOutcome.Created => $"created    {path}",
            WriteOutcome.Overwritten => $"overwrote  {path}",
            WriteOutcome.Unchanged => $"unchanged  {path}",
            WriteOutcome.Planned => $"would write {path}",
            _ => $"exists     {path} (use --force to overwrite)",
        };

        Console.WriteLine(message);
        return outcome == WriteOutcome.SkippedExisting ? 1 : 0;
    }
}
