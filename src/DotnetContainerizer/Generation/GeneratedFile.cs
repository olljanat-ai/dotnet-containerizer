namespace DotnetContainerizer.Generation;

/// <summary>A file the tool wants to create, before anything is written to disk.</summary>
/// <param name="Path">Absolute path of the file.</param>
/// <param name="Content">Full file content.</param>
internal sealed record GeneratedFile(string Path, string Content);

internal enum WriteOutcome
{
    Created,
    Overwritten,
    SkippedExisting,
    Unchanged,
    Planned,
}
