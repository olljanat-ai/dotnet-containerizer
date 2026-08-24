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

    /// <summary>Login server of the Azure Container Registry used in the generated pipeline.</summary>
    public string Registry { get; private set; } = "myregistry.azurecr.io";

    /// <summary>Name of the Azure DevOps Docker registry service connection.</summary>
    public string ServiceConnection { get; private set; } = "acr-service-connection";

    /// <summary>Image repository prefix. Defaults to the solution name when not given.</summary>
    public string? ImagePrefix { get; private set; }

    public bool NoDockerfile { get; private set; }

    public bool NoPipeline { get; private set; }

    public bool NoHelm { get; private set; }

    /// <summary>Helm chart name. Defaults to the solution name when not given.</summary>
    public string? ChartName { get; private set; }

    /// <summary>Kubernetes namespace the generated chart is deployed into.</summary>
    public string Namespace { get; private set; } = "default";

    /// <summary>Azure DevOps Kubernetes service connection used by the deploy stage.</summary>
    public string KubernetesServiceConnection { get; private set; } = "aks-service-connection";

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
              --registry <server>  ACR login server for the pipeline. Default: myregistry.azurecr.io.
              --service-connection <name>
                                   Azure DevOps Docker registry service connection. Default: acr-service-connection.
              --image-prefix <name>
                                   Image repository prefix. Default: the solution name.
              --no-dockerfile      Do not generate Dockerfiles.
              --no-pipeline        Do not generate the Azure DevOps pipeline.
              --chart-name <name>  Helm chart name. Default: the solution name.
              --namespace <name>   Kubernetes namespace to deploy into. Default: default.
              --kubernetes-connection <name>
                                   Azure DevOps Kubernetes service connection. Default: aks-service-connection.
              --no-helm            Do not generate the Helm chart.
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
          dotnet-containerize --registry contoso.azurecr.io --image-prefix contoso

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
                case "--registry":
                    if (!TryReadValue(args, ref i, error, out var registry))
                    {
                        return false;
                    }

                    options.Registry = registry;
                    break;
                case "--service-connection":
                    if (!TryReadValue(args, ref i, error, out var serviceConnection))
                    {
                        return false;
                    }

                    options.ServiceConnection = serviceConnection;
                    break;
                case "--image-prefix":
                    if (!TryReadValue(args, ref i, error, out var imagePrefix))
                    {
                        return false;
                    }

                    options.ImagePrefix = imagePrefix;
                    break;
                case "--no-dockerfile":
                    options.NoDockerfile = true;
                    break;
                case "--no-pipeline":
                    options.NoPipeline = true;
                    break;
                case "--no-helm":
                    options.NoHelm = true;
                    break;
                case "--chart-name":
                    if (!TryReadValue(args, ref i, error, out var chartName))
                    {
                        return false;
                    }

                    options.ChartName = chartName;
                    break;
                case "--namespace":
                    if (!TryReadValue(args, ref i, error, out var kubernetesNamespace))
                    {
                        return false;
                    }

                    options.Namespace = kubernetesNamespace;
                    break;
                case "--kubernetes-connection":
                    if (!TryReadValue(args, ref i, error, out var kubernetesConnection))
                    {
                        return false;
                    }

                    options.KubernetesServiceConnection = kubernetesConnection;
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
