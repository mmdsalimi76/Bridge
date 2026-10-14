using System.Text;

namespace VpsManager.Core.Helpers;

/// <summary>
/// Produces shell-safe representations of values that are interpolated into remote
/// POSIX shell commands. Every profile-driven value that reaches a command line must
/// pass through here so metacharacters cannot alter the command.
/// </summary>
public static class ShellQuote
{
    private const string ExtendedRegexMetacharacters = @"\.[](){}*+?|^$";

    /// <summary>
    /// Wraps a value in single quotes, escaping embedded single quotes. Inside single
    /// quotes the shell interprets nothing, so this neutralizes command substitution,
    /// redirection and globbing.
    /// </summary>
    public static string Quote(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return $"'{value.Replace("'", "'\\''", StringComparison.Ordinal)}'";
    }

    /// <summary>
    /// Escapes POSIX extended-regular-expression metacharacters so a literal value
    /// (such as a filesystem path) cannot be interpreted as a pattern by grep -E.
    /// </summary>
    public static string EscapeExtendedRegex(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (ExtendedRegexMetacharacters.Contains(character))
            {
                builder.Append('\\');
            }

            builder.Append(character);
        }

        return builder.ToString();
    }
}
