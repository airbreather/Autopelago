using System.Collections.Frozen;
using System.Reactive.Disposables;
using System.Reactive.Linq;

using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.ReactiveUI;

using ReactiveUI.Fody.Helpers;

using SkiaSharp;

namespace Autopelago.ViewModels;

public sealed class CheckableLocationViewModel : ViewModelBase, IDisposable
{
    private const int HalfWidth = 8;

    private static readonly Lazy<IObservable<long>> s_timer = new(() => Observable.Interval(TimeSpan.FromMilliseconds(500), AvaloniaScheduler.Instance));

    private static readonly FrozenDictionary<string, Point> s_canvasLocations = new[]
    {
        KeyValuePair.Create("basketball", new Point(59 - HalfWidth, 77 - HalfWidth)),
        KeyValuePair.Create("prawn_stars", new Point(103 - HalfWidth, 34 - HalfWidth)),
        KeyValuePair.Create("minotaur", new Point(103 - HalfWidth, 120 - HalfWidth)),
        KeyValuePair.Create("pirate_bake_sale", new Point(166 - HalfWidth, 34 - HalfWidth)),
        KeyValuePair.Create("restaurant", new Point(166 - HalfWidth, 120 - HalfWidth)),
        KeyValuePair.Create("bowling_ball_door", new Point(254 - HalfWidth, 77 - HalfWidth)),
        KeyValuePair.Create("captured_goldfish", new Point(290 - HalfWidth, 106 - HalfWidth)),
    }.ToFrozenDictionary();

    private readonly CompositeDisposable _disposables = [];

    public CheckableLocationViewModel(string locationKey)
    {
        CanvasLocation = s_canvasLocations[locationKey];
        (Bitmap[] saturated, Bitmap[] desaturated) = ReadFrames(locationKey);
        foreach (Bitmap frame in saturated)
        {
            _disposables.Add(frame);
        }

        foreach (Bitmap frame in desaturated)
        {
            _disposables.Add(frame);
        }

        _disposables.Add(s_timer.Value
            .Subscribe(i =>
            {
                int nextFrameIndex = (int)(i % saturated.Length);
                Image = saturated[nextFrameIndex];
                DesaturatedImage = desaturated[nextFrameIndex];
            }));
    }

    public Point CanvasLocation { get; }

    [Reactive]
    public Bitmap? Image { get; private set; }

    [Reactive]
    public Bitmap? DesaturatedImage { get; private set; }

    [Reactive]
    public bool Checked { get; set; }

    public void Dispose()
    {
        _disposables.Dispose();
    }

    private static (Bitmap[] Saturated, Bitmap[] Desaturated) ReadFrames(string locationKey)
    {
        using Stream data = AssetLoader.Open(new($"avares://Autopelago/Assets/Images/{locationKey}.webp"));
        using SKCodec codec = SKCodec.Create(data);
        SKImageInfo imageInfo = codec.Info;
        SKCodecFrameInfo[] frameInfo = codec.FrameInfo;
        Bitmap[] saturated = new Bitmap[frameInfo.Length];
        Bitmap[] desaturated = new Bitmap[frameInfo.Length];
        for (int i = 0; i < frameInfo.Length; i++)
        {
            if (frameInfo[i].Duration != 500)
            {
                throw new NotSupportedException("These were all supposed to be 500ms.");
            }

            using SKBitmap bmp = new(imageInfo);
            codec.GetPixels(imageInfo, bmp.GetPixels(), new(i));
            bmp.SetImmutable();
            using SKImage img = SKImage.FromBitmap(bmp);
            using MemoryStream ms = new();
            using SKData encoded = img.Encode();
            encoded.SaveTo(ms);
            ms.Position = 0;
            saturated[i] = new(ms);
            desaturated[i] = ToDesaturated(bmp);
        }

        return (saturated, desaturated);
    }
}
