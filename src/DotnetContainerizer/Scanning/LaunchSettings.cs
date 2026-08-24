using System.Text.Json;

namespace DotnetContainerizer.Scanning;

/// <summary>Reads the bits of <c>Properties/launchSettings.json</c> the generators care about.</summary>
internal static class LaunchSettings
{
    /// <summary>Tells whether any project launch profile serves HTTPS.</summary>
    public static bool HasHttpsProfile(string projectDirectory)
    {
        var path = Path.Combine(projectDirectory, "Properties", "launchSettings.json");
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (!document.RootElement.TryGetProperty("profiles", out var profiles)
                || profiles.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            foreach (var profile in profiles.EnumerateObject())
            {
                if (profile.Value.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                if (profile.Value.TryGetProperty("commandName", out var command)
                    && !string.Equals(command.GetString(), "Project", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (profile.Value.TryGetProperty("applicationUrl", out var urls)
                    && urls.GetString() is { } value
                    && value.Contains("https://", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        catch (JsonException)
        {
            // A malformed launchSettings.json only means we fall back to the default ports.
        }
        catch (IOException)
        {
        }

        return false;
    }
}
