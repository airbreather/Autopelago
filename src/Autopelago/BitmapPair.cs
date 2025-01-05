using Avalonia.Media.Imaging;
using Avalonia.Platform;

using SkiaSharp;

namespace Autopelago;

public sealed class BitmapPair : IDisposable
{
    private readonly SKBitmap _skA;

    private readonly SKBitmap _skB;

    public BitmapPair(SKBitmap a, SKBitmap b)
    {
        _skA = a;
        AImage = SKImage.FromBitmap(a);
        A = AImage.ToAvalonia();
        if (ReferenceEquals(a, b))
        {
            _skB = a;
            BImage = AImage;
            B = A;
        }
        else
        {
            _skB = b;
            BImage = SKImage.FromBitmap(b);
            B = BImage.ToAvalonia();
        }
    }

    public BitmapPair(string yamlKey)
    {
        using Stream data = AssetLoader.Open(new($"avares://Autopelago/Assets/Images/{yamlKey}.webp"));
        using SKCodec codec = SKCodec.Create(data);
        SKImageInfo imageInfo = codec.Info;
        SKCodecFrameInfo[] frameInfo = codec.FrameInfo;
        SKBitmap[] bitmaps = new SKBitmap[2];
        SKImage[] images = new SKImage[2];
        Bitmap[] avaloniaImages = new Bitmap[2];
        if (frameInfo.Length is not (0 or 2))
        {
            throw new NotSupportedException("These were all supposed to be 1- or 2-frame images.");
        }

        for (int i = 0; i < frameInfo.Length; i++)
        {
            if (frameInfo[i].Duration != 500)
            {
                throw new NotSupportedException("These were all supposed to be 500ms.");
            }

            SKBitmap bmp = new(imageInfo);
            codec.GetPixels(imageInfo, bmp.GetPixels(), new(i));
            bmp.SetImmutable();
            bitmaps[i] = bmp;
            images[i] = SKImage.FromBitmap(bmp);
            avaloniaImages[i] = images[i].ToAvalonia();
        }

        if (frameInfo.Length == 0)
        {
            SKBitmap bmp = new(imageInfo);
            codec.GetPixels(imageInfo, bmp.GetPixels());
            bmp.SetImmutable();
            bitmaps[0] = bitmaps[1] = bmp;
            images[0] = images[1] = SKImage.FromBitmap(bmp);
            avaloniaImages[0] = avaloniaImages[1] = images[0].ToAvalonia();
        }

        _skA = bitmaps[0];
        AImage = images[0];
        A = avaloniaImages[0];
        _skB = bitmaps[1];
        BImage = images[1];
        B = avaloniaImages[1];
    }

    public void Dispose()
    {
        _skA.Dispose();
        A.Dispose();
        AImage.Dispose();
        if (!ReferenceEquals(_skA, _skB))
        {
            _skB.Dispose();
            B.Dispose();
            BImage.Dispose();
        }
    }

    public Bitmap A { get; }

    public Bitmap B { get; }

    public SKImage AImage { get; }

    public SKImage BImage { get; }

    public BitmapPair ToDesaturated()
    {
        SKBitmap a = _skA.ToDesaturated();
        SKBitmap b = ReferenceEquals(A, B)
            ? a
            : _skB.ToDesaturated();
        return new(a, b);
    }
}
