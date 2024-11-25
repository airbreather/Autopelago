using Avalonia;
using Avalonia.Controls;

namespace Autopelago;

public static class Helper
{
    public static IEnumerable<PixelRect> Intersecting(this Screens screens, PixelRect bounds)
    {
        return screens.All.Select(s => s.Bounds).Where(b => b.Intersects(bounds));
    }
}
