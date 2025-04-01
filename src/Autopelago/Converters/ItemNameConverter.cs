using System.Globalization;

using Avalonia;
using Avalonia.Data.Converters;

namespace Autopelago.Converters;

public sealed class ItemNameConverter : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values is not [ItemDefinitionModel item, bool lactoseIntolerant])
        {
            return AvaloniaProperty.UnsetValue;
        }

        return lactoseIntolerant
            ? item.LactoseIntolerantName
            : item.NormalName;
    }
}
