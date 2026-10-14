using Avalonia.Data.Converters;
using System.Globalization;

namespace VpsManager.Client.Converters;

public class StepStatusToBackgroundConverter : IValueConverter
{
    private static readonly StatusToColorConverter _inner = new();

    public object? Convert(object? value, System.Type targetType, object? parameter, CultureInfo culture)
    {
        return _inner.Convert(value, targetType, "background", culture);
    }

    public object? ConvertBack(object? value, System.Type targetType, object? parameter, CultureInfo culture)
    {
        throw new System.NotImplementedException();
    }
}
