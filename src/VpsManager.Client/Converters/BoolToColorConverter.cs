using Avalonia.Data.Converters;
using Avalonia.Media;
using System.Globalization;

namespace VpsManager.Client.Converters;

public class BoolToColorConverter : IValueConverter
{
    public object? Convert(object? value, System.Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isActive)
        {
            return isActive
                ? ResolveBrush("SuccessBrush", Brushes.SpringGreen)
                : ResolveBrush("TextMutedBrush", Brushes.Gray);
        }
        return ResolveBrush("TextMutedBrush", Brushes.Gray);
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

    public object? ConvertBack(object? value, System.Type targetType, object? parameter, CultureInfo culture)
    {
        throw new System.NotImplementedException();
    }
}