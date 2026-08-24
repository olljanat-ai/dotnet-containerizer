using System.Text;

namespace DotnetContainerizer.Model;

internal static class Naming
{
    /// <summary>
    /// Converts a .NET style project name into a DNS friendly, lower case name,
    /// e.g. <c>Contoso.Web.API</c> becomes <c>contoso-web-api</c>.
    /// </summary>
    public static string ToKebabCase(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "app";
        }

        var builder = new StringBuilder(value.Length + 4);
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (char.IsLetterOrDigit(c))
            {
                var previousIsLower = i > 0 && (char.IsLower(value[i - 1]) || char.IsDigit(value[i - 1]));
                var nextIsLower = i + 1 < value.Length && char.IsLower(value[i + 1]);
                if (char.IsUpper(c) && builder.Length > 0 && (previousIsLower || nextIsLower))
                {
                    Append(builder, '-');
                }

                builder.Append(char.ToLowerInvariant(c));
            }
            else
            {
                Append(builder, '-');
            }
        }

        return builder.ToString().Trim('-') is { Length: > 0 } result ? result : "app";

        static void Append(StringBuilder builder, char separator)
        {
            if (builder.Length > 0 && builder[^1] != separator)
            {
                builder.Append(separator);
            }
        }
    }
}
