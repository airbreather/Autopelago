using Avalonia.Media.Imaging;

using ReactiveUI;

using SkiaSharp;

namespace Autopelago.ViewModels;

public abstract class ViewModelBase : ReactiveObject
{
    protected static Bitmap ToDesaturated(SKBitmap bmp)
    {
        for (int y = 0; y < bmp.Height; y++)
        {
            for (int x = 0; x < bmp.Width; x++)
            {
                SKColor px = bmp.GetPixel(x, y);
                px.ToHsl(out float h, out float s, out float l);
                bmp.SetPixel(x, y, SKColor.FromHsl(h, s * 0.1f, l * 0.4f).WithAlpha(px.Alpha));
            }
        }

        bmp.SetImmutable();

        using MemoryStream ms = new();
        using SKImage img = SKImage.FromBitmap(bmp);
        Avalonia.Skia.Helpers.ImageSavingHelper.SaveImage(img, ms);
        ms.Position = 0;
        return new(ms);
    }
}
