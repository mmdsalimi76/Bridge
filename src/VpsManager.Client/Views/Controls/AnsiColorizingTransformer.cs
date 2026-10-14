using System;
using System.Collections.Generic;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using VpsManager.Core.Helpers;

namespace VpsManager.Client.Views.Controls;

/// <summary>
/// Renders ANSI SGR colours in log output. Uses <see cref="AnsiColorParser"/> so color
/// interpretation lives in one place; this type only maps colors onto editor spans.
/// </summary>
public sealed class AnsiColorizingTransformer : DocumentColorizingTransformer
{
    private readonly Dictionary<string, IBrush> _brushes = new(StringComparer.OrdinalIgnoreCase);

    protected override void ColorizeLine(DocumentLine line)
    {
        var text = CurrentContext.Document.GetText(line.Offset, line.Length);
        if (!AnsiColorParser.ContainsAnsiCodes(text))
        {
            return;
        }

        foreach (var span in AnsiColorParser.ParseSpans(text))
        {
            if (span.Length <= 0)
            {
                continue;
            }

            var brush = ResolveBrush(span.ColorHex);
            var start = line.Offset + span.RawStart;
            var end = start + span.Length;
            ChangeLinePart(start, end, element => element.TextRunProperties.SetForegroundBrush(brush));
        }
    }

    private IBrush ResolveBrush(string colorHex)
    {
        if (_brushes.TryGetValue(colorHex, out var cached))
        {
            return cached;
        }

        var brush = Color.TryParse(colorHex, out var color)
            ? (IBrush)new SolidColorBrush(color)
            : Brushes.Gainsboro;

        _brushes[colorHex] = brush;
        return brush;
    }
}
