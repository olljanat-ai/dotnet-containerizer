namespace DotnetContainerizer.Model;

/// <summary>Result of scanning a folder for solution and project files.</summary>
internal sealed class ScanResult
{
    public required string RootPath { get; init; }

    /// <summary>
    /// Root of the Docker build context. This is the solution folder when a single solution
    /// was found, otherwise the scanned folder.
    /// </summary>
    public required string ContextRoot { get; init; }

    /// <summary>
    /// Root of the source repository, used for pipeline and chart paths. This is the closest folder
    /// with a <c>.git</c> directory at or above the build context, otherwise the build context itself.
    /// </summary>
    public required string RepositoryRoot { get; init; }

    /// <summary>Solution files found while scanning, in discovery order.</summary>
    public required IReadOnlyList<string> SolutionPaths { get; init; }

    public required IReadOnlyList<ProjectInfo> Projects { get; init; }

    public string? PrimarySolutionPath => SolutionPaths.Count > 0 ? SolutionPaths[0] : null;

    /// <summary>Solution name, or the context folder name when there is no solution file.</summary>
    public string Name => PrimarySolutionPath is { } solution
        ? Path.GetFileNameWithoutExtension(solution)
        : new DirectoryInfo(ContextRoot).Name;

    public IReadOnlyList<ProjectInfo> Containerizable { get; init; } = [];
}
