using DotnetContainerizer.Generation;

namespace DotnetContainerizer.Cli;

/// <summary>Command line switches, parsed by hand to keep the tool dependency free.</summary>
internal sealed class CliOptions
{
    public string Path { get; private set; } = ".";

    public ContainerOs Os { get; private set; } = ContainerOs.Linux;

    public bool IncludeTests { get; private set; }

    public bool Force { get; private set; }

    public bool DryRun { get; private set; }

    public bool ListOnly { get; private set; }

    public bool Verbose { get; private set; }

    public bool ShowHelp { get; private set; }

    public bool ShowVersion { get; private set; }

    public static string HelpText => """
        dotnet-containerize - adds container support to existing .NET projects.

        Usage:
          dotnet-containerize [path] [options]

        Arguments:
          path                     Folder to scan for solution and project files. Default: current folder.

        Options:
          -p, --path <folder>      Same as the path argument.
              --os <linux|windows> Container operating system. Default: linux.
              --include-tests      Generate container assets for test projects as well.
          -f, --force              Overwrite files that already exist.
              --dry-run            Report what would be written without touching the disk.
          -l, --list               Only list the discovered projects.
          -v, --verbose            Print details about skipped projects.
          -h, --help               Show this help text.
              --version            Show the tool version.

        Examples:
          dotnet-containerize
          dotnet-containerize ./src --dry-run
          dotnet-containerize --force --os windows

        """;

    /// <summary>Parses <paramref name="args"/>. Returns false and writes to <paramref name="error"/> on bad input.</summary>
    public static bool TryParse(string[] args, TextWriter error, out CliOptions options)
    {
        options = new CliOptions();
        var pathFromArgument = false;

        for (var i = 0; i < args.Length; i++)
        {
            var argument = args[i];
            switch (argument)
            {
                case "-h" or "--help" or "-?":
                    options.ShowHelp = true;
                    break;
                case "--version":
                    options.ShowVersion = true;
                    break;
                case "-p" or "--path":
                    if (!TryReadValue(args, ref i, error, out var path))
                    {
                        return false;
                    }

                    options.Path = path;
                    break;
                case "--os":
                    if (!TryReadValue(args, ref i, error, out var os))
                    {
                        return false;
                    }

                    switch (os.ToLowerInvariant())
                    {
                        case "linux":
                            options.Os = ContainerOs.Linux;
                            break;
                        case "windows" or "win":
                            options.Os = ContainerOs.Windows;
                            break;
                        default:
                            error.WriteLine($"Unknown container OS '{os}'. Use 'linux' or 'windows'.");
                            return false;
                    }

                    break;
                case "--include-tests":
                    options.IncludeTests = true;
                    break;
                case "-f" or "--force":
                    options.Force = true;
                    break;
                case "--dry-run":
                    options.DryRun = true;
                    break;
                case "-l" or "--list":
                    options.ListOnly = true;
                    break;
                case "-v" or "--verbose":
                    options.Verbose = true;
                    break;
                default:
                    if (argument.StartsWith('-'))
                    {
                        error.WriteLine($"Unknown option '{argument}'. Run with --help to see the supported options.");
                        return false;
                    }

                    if (pathFromArgument)
                    {
                        error.WriteLine($"Unexpected argument '{argument}'. Only one path can be given.");
                        return false;
                    }

                    options.Path = argument;
                    pathFromArgument = true;
                    break;
            }
        }

        return true;
    }

    private static bool TryReadValue(string[] args, ref int index, TextWriter error, out string value)
    {
        if (index + 1 >= args.Length)
        {
            error.WriteLine($"Option '{args[index]}' needs a value.");
            value = string.Empty;
            return false;
        }

        value = args[++index];
        return true;
    }
}
