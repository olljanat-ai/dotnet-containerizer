namespace DotnetContainerizer.Scanning;

internal static class Paths
{
    /// <summary>Returns <paramref name="path"/> relative to <paramref name="basePath"/> using forward slashes.</summary>
    public static string RelativeTo(string basePath, string path)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(basePath), Path.GetFullPath(path));
        return relative.Replace('\\', '/');
    }
}
