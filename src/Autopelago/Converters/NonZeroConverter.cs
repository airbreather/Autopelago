using System.Globalization;

using Avalonia.Data.Converters;

namespace Autopelago.Converters;

public sealed class NonZeroConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            0 => false,
            _ => true,
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
