using System.Globalization;

using Avalonia.Data.Converters;

namespace Autopelago.Converters;

public sealed class BooleanToStringConverter : IValueConverter
{
    public string TrueValue { get; set; } = "";

    public string FalseValue { get; set; } = "";

    public string OtherValue { get; set; } = "";

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            false => FalseValue,
            true => TrueValue,
            _ => OtherValue,
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string s)
        {
            if (s == TrueValue)
            {
                return true;
            }

            if (s == FalseValue)
            {
                return false;
            }
        }

        return null;
    }
}
