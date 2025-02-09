using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Skia.Helpers;

using SkiaSharp;

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

    public static SKBitmap ToDesaturated(this SKBitmap @this)
    {
        SKBitmap result = @this.Copy();
        using (SKCanvas canvas = new(result))
        {
            for (int y = 0; y < result.Height; y++)
            {
                for (int x = 0; x < result.Width; x++)
                {
                    SKColor px = @this.GetPixel(x, y);
                    px.ToHsl(out float h, out float s, out float l);
                    canvas.DrawPoint(x, y, SKColor.FromHsl(h, s * 0.1f, l * 0.4f).WithAlpha(px.Alpha));
                }
            }
        }

        result.SetImmutable();
        return result;
    }

    public static Bitmap ToAvaloniaDesaturated(this SKBitmap @this)
    {
        using SKBitmap desaturated = ToDesaturated(@this);
        using MemoryStream ms = new();
        using SKImage img = SKImage.FromBitmap(desaturated);
        return img.ToAvalonia();
    }

    public static Bitmap ToAvalonia(this SKImage @this)
    {
        MemoryStream ms = new();
        ImageSavingHelper.SaveImage(@this, ms);
        ms.Position = 0;
        return new(ms);
    }

    public static string Roll(this WeightedRandomItems<WeightedString> items, Random? random = null)
    {
        return items.QueryByRoll((random ?? Random.Shared).NextDouble()).Message;
    }
}
