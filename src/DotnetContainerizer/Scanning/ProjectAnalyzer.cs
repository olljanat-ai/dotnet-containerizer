using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using DotnetContainerizer.Model;

namespace DotnetContainerizer.Scanning;

/// <summary>Reads a project file and decides whether and how it can be containerized.</summary>
internal static partial class ProjectAnalyzer
{
    private const int LowestSupportedMajorVersion = 3;

    private static readonly string[] TestPackagePrefixes =
    [
        "Microsoft.NET.Test.Sdk", "xunit", "NUnit", "MSTest",
    ];

    public static ProjectInfo Analyze(string projectPath, string contextRoot)
    {
        var name = Path.GetFileNameWithoutExtension(projectPath);
        var directory = Path.GetDirectoryName(projectPath)!;
        var relativePath = Paths.RelativeTo(contextRoot, projectPath);

        XDocument document;
        try
        {
            document = XDocument.Load(projectPath);
        }
        catch (Exception exception) when (exception is System.Xml.XmlException or IOException)
        {
            return Skip(projectPath, name, directory, relativePath, name, "project file could not be read");
        }

        var root = document.Root;
        var sdk = root?.Attribute("Sdk")?.Value
            ?? root?.Elements().FirstOrDefault(e => e.Name.LocalName == "Sdk")?.Attribute("Name")?.Value;
        if (root is null || string.IsNullOrEmpty(sdk))
        {
            return Skip(projectPath, name, directory, relativePath, name, "not an SDK-style project");
        }

        var properties = ReadInheritedProperties(directory);
        MergeProperties(root, properties);
        var assemblyName = properties.GetValueOrDefault("AssemblyName") is { Length: > 0 } configured
            ? configured
            : name;
        var outputType = properties.GetValueOrDefault("OutputType");
        var packageReferences = root.Descendants()
            .Where(static element => element.Name.LocalName == "PackageReference")
            .Select(ItemIdentity)
            .ToList();
        var isTestProject = string.Equals(properties.GetValueOrDefault("IsTestProject"), "true", StringComparison.OrdinalIgnoreCase)
            || packageReferences.Any(static reference =>
                TestPackagePrefixes.Any(prefix => reference.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)));

        var projectReferences = root.Descendants()
            .Where(static element => element.Name.LocalName == "ProjectReference")
            .Select(ItemIdentity)
            .Where(static include => !string.IsNullOrWhiteSpace(include))
            .Select(include => Path.GetFullPath(Path.Combine(directory, include!.Replace('\\', Path.DirectorySeparatorChar))))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var frameworks = ReadTargetFrameworks(properties);
        var framework = SelectFramework(frameworks);

        ProjectInfo Build(ContainerKind kind, string? skipReason, int? httpPort = null, int? httpsPort = null) => new()
        {
            FullPath = projectPath,
            Name = name,
            Directory = directory,
            RelativePath = relativePath,
            AssemblyName = assemblyName,
            Sdk = sdk,
            OutputType = outputType,
            TargetFramework = framework?.Moniker,
            FrameworkVersion = framework?.Version,
            FrameworkMajorVersion = framework?.Major ?? 0,
            IsTestProject = isTestProject,
            ProjectReferences = projectReferences,
            Kind = kind,
            SkipReason = skipReason,
            HttpPort = httpPort,
            HttpsPort = httpsPort,
        };

        if (isTestProject)
        {
            return Build(ContainerKind.None, "test project");
        }

        if (sdk.Contains("BlazorWebAssembly", StringComparison.OrdinalIgnoreCase))
        {
            return Build(ContainerKind.None, "Blazor WebAssembly project, publishes static assets");
        }

        if (framework is null)
        {
            var monikers = frameworks.Count > 0 ? string.Join(";", frameworks) : "unknown";
            return Build(ContainerKind.None, $"unsupported target framework '{monikers}'");
        }

        if (string.Equals(outputType, "Library", StringComparison.OrdinalIgnoreCase))
        {
            return Build(ContainerKind.None, "class library");
        }

        if (string.Equals(outputType, "WinExe", StringComparison.OrdinalIgnoreCase))
        {
            return Build(ContainerKind.None, "desktop application");
        }

        var isWeb = sdk.Contains(".Web", StringComparison.OrdinalIgnoreCase)
            || root.Descendants().Any(static element => element.Name.LocalName == "FrameworkReference"
                && string.Equals(element.Attribute("Include")?.Value, "Microsoft.AspNetCore.App", StringComparison.OrdinalIgnoreCase))
            || packageReferences.Any(static reference => reference.StartsWith("Microsoft.AspNetCore.", StringComparison.OrdinalIgnoreCase));

        if (isWeb)
        {
            var https = LaunchSettings.HasHttpsProfile(directory);
            var (http, secure) = DefaultPorts(framework.Major);
            return Build(ContainerKind.AspNet, skipReason: null, http, https ? secure : null);
        }

        var isExecutable = string.Equals(outputType, "Exe", StringComparison.OrdinalIgnoreCase)
            || sdk.Contains("Worker", StringComparison.OrdinalIgnoreCase);
        return isExecutable
            ? Build(ContainerKind.Console, skipReason: null)
            : Build(ContainerKind.None, "class library");
    }

    /// <summary>
    /// Container ports follow the Visual Studio defaults: .NET 8 and later run as a non root user
    /// and listen on 8080/8081, earlier versions listen on 80/443.
    /// </summary>
    internal static (int Http, int Https) DefaultPorts(int majorVersion) =>
        majorVersion >= 8 ? (8080, 8081) : (80, 443);

    private static ProjectInfo Skip(
        string projectPath,
        string name,
        string directory,
        string relativePath,
        string assemblyName,
        string reason) => new()
        {
            FullPath = projectPath,
            Name = name,
            Directory = directory,
            RelativePath = relativePath,
            AssemblyName = assemblyName,
            Kind = ContainerKind.None,
            SkipReason = reason,
        };

    private static Dictionary<string, string> ReadInheritedProperties(string projectDirectory)
    {
        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var files = new Stack<string>();
        for (var directory = new DirectoryInfo(projectDirectory); directory is not null; directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, "Directory.Build.props");
            if (File.Exists(path))
            {
                files.Push(path);
            }
        }

        foreach (var file in files)
        {
            try
            {
                if (XDocument.Load(file).Root is { } root)
                {
                    MergeProperties(root, properties);
                }
            }
            catch (Exception exception) when (exception is System.Xml.XmlException or IOException or UnauthorizedAccessException)
            {
                // MSBuild may still evaluate the project; ignore props files that cannot be inspected safely.
            }
        }

        return properties;
    }

    private static void MergeProperties(XElement root, IDictionary<string, string> properties)
    {
        foreach (var group in root.Elements().Where(static element => element.Name.LocalName == "PropertyGroup"))
        {
            // Conditional property groups are ignored, the unconditional values describe the default build.
            if (group.Attribute("Condition") is not null)
            {
                continue;
            }

            foreach (var property in group.Elements())
            {
                properties[property.Name.LocalName] = property.Value.Trim();
            }
        }

    }

    private static List<string> ReadTargetFrameworks(IReadOnlyDictionary<string, string> properties)
    {
        var value = properties.GetValueOrDefault("TargetFrameworks")
            ?? properties.GetValueOrDefault("TargetFramework")
            ?? string.Empty;

        return value
            .Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
    }

    private static string ItemIdentity(XElement element) =>
        element.Attribute("Include")?.Value ?? element.Attribute("Update")?.Value ?? string.Empty;

    /// <summary>Picks the newest .NET (Core) target framework a container image exists for.</summary>
    private static FrameworkTarget? SelectFramework(IEnumerable<string> monikers) => monikers
        .Select(ParseFramework)
        .OfType<FrameworkTarget>()
        .OrderByDescending(static framework => framework.Major)
        .ThenByDescending(static framework => framework.Minor)
        .FirstOrDefault();

    internal static FrameworkTarget? ParseFramework(string moniker)
    {
        var match = FrameworkRegex().Match(moniker.Trim());
        if (!match.Success)
        {
            return null;
        }

        var major = int.Parse(match.Groups["major"].Value, CultureInfo.InvariantCulture);
        var minor = match.Groups["minor"].Success
            ? int.Parse(match.Groups["minor"].Value, CultureInfo.InvariantCulture)
            : 0;

        // net47, net481 and friends are .NET Framework and have no cross platform runtime image.
        if (!match.Groups["minor"].Success || major < LowestSupportedMajorVersion)
        {
            return null;
        }

        // Platform specific monikers such as net8.0-windows cannot run in a Linux container.
        if (match.Groups["platform"].Success)
        {
            return null;
        }

        return new FrameworkTarget(moniker.Trim(), $"{major}.{minor}", major, minor);
    }

    internal sealed record FrameworkTarget(string Moniker, string Version, int Major, int Minor);

    [GeneratedRegex("^net(coreapp)?(?<major>\\d+)(\\.(?<minor>\\d+))?(?<platform>-[a-z0-9.]+)?$", RegexOptions.IgnoreCase)]
    private static partial Regex FrameworkRegex();
}
