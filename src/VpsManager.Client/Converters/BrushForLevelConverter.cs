using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace VpsManager.Client.Converters;

/// <summary>
/// Maps a level or severity name (Normal, Warning, Elevated, Critical, Info, Healthy,
/// Unhealthy, Unknown) to the matching theme brush so colours stay in one place.
/// </summary>
public sealed class BrushForLevelConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = (value as string ?? string.Empty).Trim().ToLowerInvariant();

        var resourceKey = key switch
        {
            "critical" or "unhealthy" or "failed" or "exited" or "dead" or "stopped" or "error" => "DangerBrush",
            "warning" or "elevated" or "degraded" or "restarting" or "paused" => "WarningBrush",
            "info" or "unknown" or "created" or "not fetched" or "none" => "InfoBrush",
            _ => "SuccessBrush"
        };

        if (Application.Current is { } app
            && app.TryFindResource(resourceKey, out var resource)
            && resource is IBrush brush)
        {
            return brush;
        }

        return FallbackBrush(resourceKey);
    }

    private static IBrush FallbackBrush(string resourceKey) => resourceKey switch
    {
        "DangerBrush" => Brushes.IndianRed,
        "WarningBrush" => Brushes.Goldenrod,
        "InfoBrush" => Brushes.DeepSkyBlue,
        _ => Brushes.SpringGreen
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
