using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Runtime.InteropServices;

using Avalonia.Media.Imaging;
using Avalonia.Platform;

using SkiaSharp;

namespace Autopelago;

public enum PlayerTokenKind
{
    Player1,
    Player2,
    Player4,
}

public static class PlayerTokens
{
    private static readonly FrozenDictionary<PlayerTokenKind, Uri> s_imagePath = new Dictionary<PlayerTokenKind, Uri>
    {
        [PlayerTokenKind.Player1] = new("avares://Autopelago/Assets/Images/pack_rat.webp"),
        [PlayerTokenKind.Player2] = new("avares://Autopelago/Assets/Images/player2.webp"),
        [PlayerTokenKind.Player4] = new("avares://Autopelago/Assets/Images/player4.webp"),
    }.ToFrozenDictionary();

    private static readonly SKBitmap s_player1 = LoadImmutable(PlayerTokenKind.Player1);

    private static readonly SKBitmap s_player2 = LoadImmutable(PlayerTokenKind.Player2);

    private static readonly SKBitmap s_player4 = LoadImmutable(PlayerTokenKind.Player4);

    private static readonly SKColor s_lightColor = SKColor.Parse("#382E26");

    private static readonly SKColor s_darkColor = SKColor.Parse("#1A130F");

    private static readonly float s_shiftH = 21 - 26;

    private static readonly float s_shiftS = 42 - 32;

    private static readonly float s_shiftV = 10 - 21;

    private static readonly ImmutableArray<SKPoint> s_player1DarkPixelMask;

    private static readonly ImmutableArray<SKPoint> s_player1LightPixelMask = GetLightPixelMask(s_player1, out s_player1DarkPixelMask);

    private static readonly ImmutableArray<SKPoint> s_player2DarkPixelMask;

    private static readonly ImmutableArray<SKPoint> s_player2LightPixelMask = GetLightPixelMask(s_player2, out s_player2DarkPixelMask);

    private static readonly ImmutableArray<SKPoint> s_player4DarkPixelMask;

    private static readonly ImmutableArray<SKPoint> s_player4LightPixelMask = GetLightPixelMask(s_player4, out s_player4DarkPixelMask);

    private static ImmutableArray<SKPoint> GetLightPixelMask(SKBitmap bitmap, out ImmutableArray<SKPoint> darkPixelMask)
    {
        List<SKPoint> lightPixelMaskList = [];
        List<SKPoint> darkPixelMaskList = [];
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                SKColor pixel = bitmap.GetPixel(x, y);
                if (pixel == s_lightColor)
                {
                    lightPixelMaskList.Add(new(x, y));
                }
                else if (pixel == s_darkColor)
                {
                    darkPixelMaskList.Add(new(x, y));
                }
            }
        }

        darkPixelMask = [.. darkPixelMaskList];
        return [.. lightPixelMaskList];
    }

    public static WriteableBitmap For(PlayerTokenKind kind, SKColor lightColor)
    {
        WriteableBitmap result = WriteableBitmap.Decode(AssetLoader.Open(s_imagePath[kind]));
        DrawTo(result, kind, lightColor);
        return result;
    }

    public static void DrawTo(WriteableBitmap bitmap, PlayerTokenKind kind, SKColor lightColor)
    {
        using SKPaint lightPaint = new();
        using SKPaint darkPaint = new();
        lightPaint.Color = lightColor;
        darkPaint.Color = ToDarkColor(lightColor);
        (ImmutableArray<SKPoint> lightMask, ImmutableArray<SKPoint> darkMask) = kind switch
        {
            PlayerTokenKind.Player1 => (s_player1LightPixelMask, s_player1DarkPixelMask),
            PlayerTokenKind.Player2 => (s_player2LightPixelMask, s_player2DarkPixelMask),
            PlayerTokenKind.Player4 => (s_player4LightPixelMask, s_player4DarkPixelMask),
        };
        using ILockedFramebuffer fb = bitmap.Lock();
        using SKBitmap wrapped = Wrap(fb);
        using SKCanvas canvas = new(wrapped);
        canvas.DrawPoints(SKPointMode.Points, ImmutableCollectionsMarshal.AsArray(lightMask), lightPaint);
        canvas.DrawPoints(SKPointMode.Points, ImmutableCollectionsMarshal.AsArray(darkMask), darkPaint);
    }

    public static SKColor ToDarkColor(SKColor lightColor)
    {
        lightColor.ToHsv(out float h, out float s, out float v);
        h += s_shiftH;
        s += s_shiftS;
        v += s_shiftV;

        if (h >= 360) h -= 360;
        if (h < 0) h += 360;
        if (s > 100) s = 100;
        if (s < 0) s = 0;
        if (v > 100) v = 100;
        if (v < 0) v = 0;

        return SKColor.FromHsv(h, s, v);
    }

    private static SKBitmap LoadImmutable(PlayerTokenKind kind)
    {
        SKBitmap result = SKBitmap.Decode(AssetLoader.Open(s_imagePath[kind]));
        result.SetImmutable();
        return result;
    }

    private static SKBitmap Wrap(ILockedFramebuffer fb)
    {
        if (fb.Format != PixelFormat.Bgra8888)
        {
            throw new NotSupportedException("i forgor");
        }

        SKImageInfo imageInfo = new(fb.Size.Width, fb.Size.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        SKBitmap bitmap = new(imageInfo);
        try
        {
            bitmap.InstallPixels(imageInfo, fb.Address);
            return bitmap;
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }
}
