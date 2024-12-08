using System.Collections.Immutable;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Runtime.InteropServices;
using System.Windows.Input;

using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

using ReactiveUI;
using ReactiveUI.SourceGenerators;

using SkiaSharp;

namespace Autopelago.ViewModels;

public enum PlayerTokenKind
{
    Player1,
    Player2,
    Player4,
}

public sealed partial class PlayerTokenViewModel : ViewModelBase, IDisposable
{
    private static readonly SKColor s_lightColor = SKColor.Parse("#FF382E26");

    private static readonly SKColor s_darkColor = SKColor.Parse("#FF1A130F");

    private static readonly float s_shiftH = 21 - 26;

    private static readonly float s_shiftS = 42 - 32;

    private static readonly float s_shiftV = 10 - 21;

    private readonly CompositeDisposable _disposables = [];

    private readonly ImmutableArray<SKPoint> _player1LightPixelMask;

    private readonly ImmutableArray<SKPoint> _player1DarkPixelMask;

    private readonly ImmutableArray<SKPoint> _player2LightPixelMask;

    private readonly ImmutableArray<SKPoint> _player2DarkPixelMask;

    private readonly ImmutableArray<SKPoint> _player4LightPixelMask;

    private readonly ImmutableArray<SKPoint> _player4DarkPixelMask;

    [Reactive] private Color _color = Color.Parse("#FF382E26");

    [Reactive] private PlayerTokenKind _playerToken = PlayerTokenKind.Player1;

    [ObservableAsProperty] private bool _isPlayer1;

    [ObservableAsProperty] private bool _isPlayer2;

    [ObservableAsProperty] private bool _isPlayer4;

    [ObservableAsProperty] private Bitmap? _playerTokenIconSource;

    public PlayerTokenViewModel()
    {
        IObservable<PlayerTokenKind> playerTokenChanges =
            this.ObservableForProperty(x => x.PlayerToken, skipInitial: false)
                .Select(p => p.Value);

        _playerTokenIconSourceHelper = playerTokenChanges
            .Select(p => p switch
            {
                PlayerTokenKind.Player1 => Player1,
                PlayerTokenKind.Player2 => Player2,
                PlayerTokenKind.Player4 => Player4,
            })
            .ToProperty(this, x => x.PlayerTokenIconSource)
            .DisposeWith(_disposables);

        _isPlayer1Helper = playerTokenChanges
            .Select(p => p == PlayerTokenKind.Player1)
            .ToProperty(this, x => x.IsPlayer1)
            .DisposeWith(_disposables);

        _isPlayer2Helper = playerTokenChanges
            .Select(p => p == PlayerTokenKind.Player2)
            .ToProperty(this, x => x.IsPlayer2)
            .DisposeWith(_disposables);

        _isPlayer4Helper = playerTokenChanges
            .Select(p => p == PlayerTokenKind.Player4)
            .ToProperty(this, x => x.IsPlayer4)
            .DisposeWith(_disposables);

        using (ILockedFramebuffer fb = Player1.Lock())
        {
            (_player1LightPixelMask, _player1DarkPixelMask) = GetPixelMask(fb);
        }

        using (ILockedFramebuffer fb = Player2.Lock())
        {
            (_player2LightPixelMask, _player2DarkPixelMask) = GetPixelMask(fb);
        }

        using (ILockedFramebuffer fb = Player4.Lock())
        {
            (_player4LightPixelMask, _player4DarkPixelMask) = GetPixelMask(fb);
        }

        this.ObservableForProperty(x => x.Color)
            .Subscribe(c =>
            {
                using SKPaint lightPaint = new();
                SKColor lightColor = lightPaint.Color = new(c.Value.ToUInt32());
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

                using SKPaint darkPaint = new();
                darkPaint.Color = SKColor.FromHsv(h, s, v);

                using (ILockedFramebuffer fb = Player1.Lock())
                {
                    using SKBitmap bitmap = Wrap(fb);
                    using SKCanvas canvas = new(bitmap);
                    canvas.DrawPoints(SKPointMode.Points, ImmutableCollectionsMarshal.AsArray(_player1LightPixelMask), lightPaint);
                    canvas.DrawPoints(SKPointMode.Points, ImmutableCollectionsMarshal.AsArray(_player1DarkPixelMask), darkPaint);
                }

                using (ILockedFramebuffer fb = Player2.Lock())
                {
                    using SKBitmap bitmap = Wrap(fb);
                    using SKCanvas canvas = new(bitmap);
                    canvas.DrawPoints(SKPointMode.Points, ImmutableCollectionsMarshal.AsArray(_player2LightPixelMask), lightPaint);
                    canvas.DrawPoints(SKPointMode.Points, ImmutableCollectionsMarshal.AsArray(_player2DarkPixelMask), darkPaint);
                }

                using (ILockedFramebuffer fb = Player4.Lock())
                {
                    using SKBitmap bitmap = Wrap(fb);
                    using SKCanvas canvas = new(bitmap);
                    canvas.DrawPoints(SKPointMode.Points, ImmutableCollectionsMarshal.AsArray(_player4LightPixelMask), lightPaint);
                    canvas.DrawPoints(SKPointMode.Points, ImmutableCollectionsMarshal.AsArray(_player4DarkPixelMask), darkPaint);
                }
            })
            .DisposeWith(_disposables);
    }

    public void Dispose()
    {
        _disposables.Dispose();
    }

    [ReactiveCommand]
    private void ChoosePlayerToken(PlayerTokenKind playerToken)
    {
        PlayerToken = playerToken;
    }

    public WriteableBitmap Player1 { get; } = WriteableBitmap.Decode(AssetLoader.Open(new("avares://Autopelago/Assets/Images/pack_rat.webp")));

    public WriteableBitmap Player2 { get; } = WriteableBitmap.Decode(AssetLoader.Open(new("avares://Autopelago/Assets/Images/player2.webp")));

    public WriteableBitmap Player4 { get; } = WriteableBitmap.Decode(AssetLoader.Open(new("avares://Autopelago/Assets/Images/player4.webp")));

    public ICommand? ClosePaneCommand { get; init; }

    private static (ImmutableArray<SKPoint> LightPixelMask, ImmutableArray<SKPoint> DarkPixelMask) GetPixelMask(ILockedFramebuffer fb)
    {
        using SKBitmap bitmap = Wrap(fb);
        List<SKPoint> lightPixelMask = [];
        List<SKPoint> darkPixelMask = [];
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                SKColor pixel = bitmap.GetPixel(x, y);
                if (pixel == s_lightColor)
                {
                    lightPixelMask.Add(new(x, y));
                }
                else if (pixel == s_darkColor)
                {
                    darkPixelMask.Add(new(x, y));
                }
            }
        }

        return ([.. lightPixelMask], [.. darkPixelMask]);
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
