using System.Text;

namespace DotnetContainerizer.Generation;

/// <summary>Writes generated files, honouring the overwrite and dry run switches.</summary>
internal sealed class FileWriter(bool force, bool dryRun)
{
    private static readonly UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);

    public WriteOutcome Write(GeneratedFile file)
    {
        var exists = File.Exists(file.Path);
        if (exists && !force)
        {
            var existing = File.ReadAllText(file.Path);
            return existing == file.Content ? WriteOutcome.Unchanged : WriteOutcome.SkippedExisting;
        }

        if (exists && File.ReadAllText(file.Path) == file.Content)
        {
            return WriteOutcome.Unchanged;
        }

        if (dryRun)
        {
            return WriteOutcome.Planned;
        }

        var directory = Path.GetDirectoryName(file.Path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(file.Path, file.Content, Utf8WithoutBom);
        return exists ? WriteOutcome.Overwritten : WriteOutcome.Created;
    }
}
