using System.Text.RegularExpressions;
using System.Xml.Linq;
using DotnetContainerizer.Model;

namespace DotnetContainerizer.Scanning;

/// <summary>Finds solution and project files below a folder.</summary>
internal static partial class SolutionScanner
{
    private static readonly string[] ProjectExtensions = [".csproj", ".fsproj", ".vbproj"];

    private static readonly string[] IgnoredDirectories =
    [
        "bin", "obj", ".git", ".vs", ".vscode", "node_modules", "packages", "TestResults", "artifacts",
    ];

    public static ScanResult Scan(string rootPath, bool includeTestProjects)
    {
        var root = Path.GetFullPath(rootPath);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"Folder '{root}' does not exist.");
        }

        var solutions = EnumerateFiles(root)
            .Where(static file => file.EndsWith(".sln", StringComparison.OrdinalIgnoreCase)
                || file.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase))
            .OrderBy(static file => file.Count(c => c is '/' or '\\'))
            .ThenBy(static file => file, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var projectPaths = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        // Projects referenced by a solution file, even when they live outside the scanned folder.
        foreach (var solution in solutions)
        {
            foreach (var project in ReadProjectsFromSolution(solution))
            {
                projectPaths.Add(project);
            }
        }

        // Projects that are not part of any solution are containerized as well.
        foreach (var file in EnumerateFiles(root))
        {
            if (ProjectExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
            {
                projectPaths.Add(Path.GetFullPath(file));
            }
        }

        var contextRoot = solutions.Count == 1 ? Path.GetDirectoryName(solutions[0])! : root;
        var projects = projectPaths
            .Select(path => ProjectAnalyzer.Analyze(path, contextRoot))
            .OrderBy(static project => project.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new ScanResult
        {
            RootPath = root,
            ContextRoot = contextRoot,
            RepositoryRoot = FindRepositoryRoot(contextRoot),
            SolutionPaths = solutions,
            Projects = projects,
            Containerizable = projects
                .Where(project => project.IsContainerizable && (includeTestProjects || !project.IsTestProject))
                .ToList(),
        };
    }

    /// <summary>Walks up from <paramref name="contextRoot"/> looking for the folder that holds .git.</summary>
    private static string FindRepositoryRoot(string contextRoot)
    {
        var directory = new DirectoryInfo(contextRoot);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, ".git"))
                || File.Exists(Path.Combine(directory.FullName, ".git")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return contextRoot;
    }

    private static IEnumerable<string> EnumerateFiles(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            string[] files;
            string[] subDirectories;
            try
            {
                files = Directory.GetFiles(directory);
                subDirectories = Directory.GetDirectories(directory);
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
            {
                continue;
            }

            foreach (var file in files)
            {
                yield return file;
            }

            foreach (var subDirectory in subDirectories)
            {
                var name = Path.GetFileName(subDirectory);
                if (!IgnoredDirectories.Contains(name, StringComparer.OrdinalIgnoreCase)
                    && !IsReparsePoint(subDirectory))
                {
                    pending.Push(subDirectory);
                }
            }
        }
    }

    private static bool IsReparsePoint(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    private static IEnumerable<string> ReadProjectsFromSolution(string solutionPath)
    {
        var directory = Path.GetDirectoryName(solutionPath)!;
        string content;
        try
        {
            content = File.ReadAllText(solutionPath);
        }
        catch (IOException)
        {
            yield break;
        }

        IEnumerable<string> paths;
        if (solutionPath.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                paths = XDocument.Parse(content).Descendants()
                    .Where(static element => element.Name.LocalName == "Project")
                    .Select(static element => element.Attribute("Path")?.Value)
                    .OfType<string>()
                    .ToList();
            }
            catch (System.Xml.XmlException)
            {
                yield break;
            }
        }
        else
        {
            paths = SolutionProjectRegex().Matches(content).Cast<Match>()
                .Select(static match => match.Groups["path"].Value);
        }

        foreach (var path in paths)
        {
            var relative = path.Replace('\\', Path.DirectorySeparatorChar);
            if (!ProjectExtensions.Contains(Path.GetExtension(relative), StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            var full = Path.GetFullPath(Path.Combine(directory, relative));
            if (File.Exists(full))
            {
                yield return full;
            }
        }
    }

    [GeneratedRegex("Project\\(\"\\{[^}]*\\}\"\\)\\s*=\\s*\"[^\"]*\",\\s*\"(?<path>[^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex SolutionProjectRegex();

}
