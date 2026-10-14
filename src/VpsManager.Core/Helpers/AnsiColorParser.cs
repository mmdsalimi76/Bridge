using System.Text;
using System.Text.RegularExpressions;
using System.Linq;

namespace VpsManager.Core.Helpers;

/// <summary>A run of text with the ANSI color that applies to it.</summary>
public sealed record AnsiSegment(string Text, string ColorHex);

/// <summary>
/// A colored run expressed as an offset and length within the original (still encoded)
/// text, which is what renderers need to color a document line correctly.
/// </summary>
public sealed record AnsiSpan(int RawStart, int Length, string ColorHex);

/// <summary>
/// Parses ANSI escape codes for terminal color formatting and converts them to HTML-like markup
/// for use with AvaloniaEdit syntax highlighting.
/// </summary>
public static class AnsiColorParser
{
    // ANSI color mappings to hex colors
    private static readonly Dictionary<int, string> ColorMap = new()
    {
        // Normal colors
        { 30, "#71808B" }, // Black (mapped to visible slate gray for dark terminals)
        { 31, "#CD3131" }, // Red
        { 32, "#0DBC79" }, // Green
        { 33, "#E5E510" }, // Yellow
        { 34, "#2472C8" }, // Blue
        { 35, "#BC3FBC" }, // Magenta
        { 36, "#11A8CD" }, // Cyan
        { 37, "#E5E5E5" }, // White
        // Bright colors
        { 90, "#666666" }, // Bright Black (Gray)
        { 91, "#F14C4C" }, // Bright Red
        { 92, "#23D18B" }, // Bright Green
        { 93, "#F5F543" }, // Bright Yellow
        { 94, "#3B8EEA" }, // Bright Blue
        { 95, "#D670D6" }, // Bright Magenta
        { 96, "#29B8DB" }, // Bright Cyan
        { 97, "#FFFFFF" }, // Bright White
    };

    // ANSI escape code regex patterns
    private static readonly Regex AnsiRegex = new(@"\x1B\[[0-9;]*m", RegexOptions.Compiled);

    /// <summary>Color used for text with no active ANSI code.</summary>
    public const string DefaultColor = "#E5E5E5";

    /// <summary>
    /// Converts ANSI-colored text to plain text with embedded color markers.
    /// Format: <color:#hex>text</color>
    /// </summary>
    public static string ConvertToMarkup(string ansiText)
    {
        if (string.IsNullOrEmpty(ansiText))
            return string.Empty;

        var result = new StringBuilder();
        var currentPosition = 0;
        var currentColor = "#E5E5E5"; // Default white

        foreach (Match match in AnsiRegex.Matches(ansiText))
        {
            // Append text before this escape code
            if (match.Index > currentPosition)
            {
                var text = ansiText.Substring(currentPosition, match.Index - currentPosition);
                if (!string.IsNullOrEmpty(text))
                {
                    result.Append($"<color:{currentColor}>{EscapeXml(text)}</color>");
                }
            }

            // Parse the escape code
            var code = match.Value.Substring(2, match.Value.Length - 3); // Remove ESC[ and m
            currentColor = ParseAnsiCode(code, currentColor);

            currentPosition = match.Index + match.Length;
        }

        // Append remaining text
        if (currentPosition < ansiText.Length)
        {
            var text = ansiText.Substring(currentPosition);
            if (!string.IsNullOrEmpty(text))
            {
                result.Append($"<color:{currentColor}>{EscapeXml(text)}</color>");
            }
        }

        return result.ToString();
    }

    private static string ParseAnsiCode(string code, string currentColor)
    {
        var parts = code.Split(';');
        var color = currentColor;

        foreach (var part in parts)
        {
            if (int.TryParse(part, out var num))
            {
                switch (num)
                {
                    case 0: // Reset
                        color = "#E5E5E5";
                        break;
                    case 1: // Bold (we'll use a brighter color)
                        // Keep current color but mark as bold (could be enhanced)
                        break;
                    case 4: // Underline
                        // Could be enhanced with underline support
                        break;
                    case 7: // Invert
                        // Could be enhanced with inversion
                        break;
                    default:
                        if (ColorMap.TryGetValue(num, out var hexColor))
                        {
                            color = hexColor;
                        }
                        break;
                }
            }
        }

        return color;
    }

    private static string EscapeXml(string text)
    {
        return text
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&apos;");
    }

    /// <summary>
    /// Splits ANSI-colored text into (text, color) segments so callers can render real
    /// colored output instead of stripping the formatting.
    /// </summary>
    public static IReadOnlyList<AnsiSpan> ParseSpans(string ansiText)
    {
        var spans = new List<AnsiSpan>();
        if (string.IsNullOrEmpty(ansiText))
        {
            return spans;
        }

        var currentPosition = 0;
        var currentColor = DefaultColor;

        foreach (Match match in AnsiRegex.Matches(ansiText))
        {
            if (match.Index > currentPosition)
            {
                spans.Add(new AnsiSpan(currentPosition, match.Index - currentPosition, currentColor));
            }

            currentColor = ParseAnsiCode(match.Value.Substring(2, match.Value.Length - 3), currentColor);
            currentPosition = match.Index + match.Length;
        }

        if (currentPosition < ansiText.Length)
        {
            spans.Add(new AnsiSpan(currentPosition, ansiText.Length - currentPosition, currentColor));
        }

        return spans;
    }

    /// <summary>
    /// Splits ANSI-colored text into (text, color) pairs, with the escape codes removed.
    /// </summary>
    public static IReadOnlyList<AnsiSegment> ParseSegments(string ansiText)
    {
        ArgumentNullException.ThrowIfNull(ansiText);

        return ParseSpans(ansiText)
            .Select(span => new AnsiSegment(ansiText.Substring(span.RawStart, span.Length), span.ColorHex))
            .ToList();
    }

    /// <summary>
    /// Strips all ANSI escape codes from text, returning plain text only.
    /// </summary>
    public static string StripAnsiCodes(string ansiText)
    {
        if (string.IsNullOrEmpty(ansiText))
            return string.Empty;

        return AnsiRegex.Replace(ansiText, string.Empty);
    }

    /// <summary>
    /// Detects if text contains ANSI escape codes.
    /// </summary>
    public static bool ContainsAnsiCodes(string text)
    {
        return !string.IsNullOrEmpty(text) && AnsiRegex.IsMatch(text);
    }
}