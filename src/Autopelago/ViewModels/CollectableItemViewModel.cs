using Avalonia.Media.Imaging;
using Avalonia.Platform;

using ReactiveUI;

using SkiaSharp;

namespace Autopelago.ViewModels;

public sealed class CollectableItemViewModel : ViewModelBase
{
    public required string ItemKey { get; init; }

    public required ItemDefinitionModel Model { get; init; }

    public Bitmap Image => new(AssetLoader.Open(new($"avares://Autopelago/Assets/Images/{ItemKey}.webp")));

    public Bitmap Grayscale
    {
        get
        {
            using SKBitmap bmp = SKBitmap.Decode(AssetLoader.Open(new($"avares://Autopelago/Assets/Images/{ItemKey}.webp")));
            for (int y = 0; y < bmp.Height; y++)
            {
                for (int x = 0; x < bmp.Width; x++)
                {
                    SKColor px = bmp.GetPixel(x, y);
                    px.ToHsl(out float h, out float s, out float l);
                    bmp.SetPixel(x, y, SKColor.FromHsl(h, s * 0.1f, l * 0.4f).WithAlpha(px.Alpha));
                }
            }

            MemoryStream ms = new();
            Avalonia.Skia.Helpers.ImageSavingHelper.SaveImage(SKImage.FromBitmap(bmp), ms);
            ms.Position = 0;
            return new(ms);
        }
    }

    private bool _collected;

    public bool Collected
    {
        get => _collected;
        set => this.RaiseAndSetIfChanged(ref _collected, value);
    }
}
