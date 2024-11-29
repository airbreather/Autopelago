using Avalonia;
using Avalonia.Controls;

namespace Autopelago;

public static class Helper
{
    public static IEnumerable<PixelRect> Intersecting(this Screens screens, PixelRect bounds)
    {
        return screens.All.Select(s => s.Bounds).Where(b => b.Intersects(bounds));
    }

    public static string FormatMyWay(this TimeSpan duration)
    {
        if (duration.TotalDays >= 1)
        {
            return duration.ToString(@"d\:hh\:mm\:ss");
        }

        if (duration.TotalHours >= 1)
        {
            return duration.ToString(@"h\:mm\:ss");
        }

        return duration.ToString(@"m\:ss");
    }
}
