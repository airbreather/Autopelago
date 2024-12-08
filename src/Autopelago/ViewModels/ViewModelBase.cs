using Avalonia.Media.Imaging;

using ReactiveUI;

using SkiaSharp;

namespace Autopelago.ViewModels;

public abstract class ViewModelBase : ReactiveObject
{
    protected static SKBitmap ToDesaturated(SKBitmap bmp)
    {
        SKBitmap result = bmp.Copy();
        using (SKCanvas canvas = new(result))
        {
            for (int y = 0; y < result.Height; y++)
            {
                for (int x = 0; x < result.Width; x++)
                {
                    SKColor px = bmp.GetPixel(x, y);
                    px.ToHsl(out float h, out float s, out float l);
                    canvas.DrawPoint(x, y, SKColor.FromHsl(h, s * 0.1f, l * 0.4f).WithAlpha(px.Alpha));
                }
            }
        }

        result.SetImmutable();
        return result;
    }

    protected static Bitmap ToAvaloniaDesaturated(SKBitmap bmp)
    {
        using SKBitmap desaturated = ToDesaturated(bmp);
        using MemoryStream ms = new();
        using SKImage img = SKImage.FromBitmap(desaturated);
        Avalonia.Skia.Helpers.ImageSavingHelper.SaveImage(img, ms);
        ms.Position = 0;
        return new(ms);
    }
}
