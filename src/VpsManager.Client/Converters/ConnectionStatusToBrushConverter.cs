using System;
using Avalonia.Data.Converters;
using Avalonia.Media;
using System.Globalization;

namespace VpsManager.Client.Converters;

public sealed class ConnectionStatusToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var status = value as string ?? string.Empty;
        var param = parameter as string ?? string.Empty;

        var isConnected = status.StartsWith("Connected", StringComparison.OrdinalIgnoreCase)
            || status.Equals("Healthy", StringComparison.OrdinalIgnoreCase);
        var isConnecting = status.StartsWith("Connecting", StringComparison.OrdinalIgnoreCase)
            || status.Equals("Unknown", StringComparison.OrdinalIgnoreCase)
            || status.Equals("Degraded", StringComparison.OrdinalIgnoreCase);

        string key;
        if (param.Equals("Indicator", StringComparison.OrdinalIgnoreCase))
        {
            key = isConnected ? "SuccessBrush" : isConnecting ? "WarningBrush" : "DangerBrush";
            return ResolveBrush(key, isConnected ? Brushes.LimeGreen : isConnecting ? Brushes.Orange : Brushes.Crimson);
        }
        else if (param.Equals("Border", StringComparison.OrdinalIgnoreCase))
        {
            key = isConnected ? "SuccessBorderBrush" : isConnecting ? "WarningBorderBrush" : "DangerBorderBrush";
            return ResolveBrush(key, ResolveBrush("DividerBrush", Brushes.DimGray));
        }
        else if (param.Equals("Text", StringComparison.OrdinalIgnoreCase))
        {
            key = isConnected ? "SuccessTextBrush" : isConnecting ? "WarningTextBrush" : "DangerTextBrush";
            return ResolveBrush(key, Brushes.White);
        }
        else // "Background" or default
        {
            key = isConnected ? "SuccessSurfaceBrush" : isConnecting ? "WarningSurfaceBrush" : "DangerSurfaceBrush";
            return ResolveBrush(key, isConnected ? Brushes.DarkGreen : Brushes.DarkRed);
        }
    }

    private static IBrush ResolveBrush(string key, IBrush fallback)
    {
        if (Avalonia.Application.Current != null
            && Avalonia.Application.Current.TryGetResource(key, null, out var res)
            && res is IBrush brush)
        {
            return brush;
        }
        return fallback;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
