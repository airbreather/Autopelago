using System.Collections.Frozen;
using System.Reactive.Disposables;
using System.Reactive.Linq;

using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.ReactiveUI;

using ReactiveUI;
using ReactiveUI.Fody.Helpers;

using SkiaSharp;

namespace Autopelago.ViewModels;

public sealed class CheckableLocationViewModel : ViewModelBase, IDisposable
{
    private const int HalfWidth = 8;

    private static readonly Lazy<IObservable<long>> s_timer = new(() => Observable.Interval(TimeSpan.FromMilliseconds(500), AvaloniaScheduler.Instance));

    private static readonly Lazy<(Bitmap[] Yellow, Bitmap[] Gray)> s_questFrames = new(() => (ReadFrames("yellow_quest").Saturated, ReadFrames("gray_quest").Saturated));

    private static readonly FrozenDictionary<string, Point> s_canvasLocations = new[]
    {
        KeyValuePair.Create("basketball", new Point(59 - HalfWidth, 77 - HalfWidth)),
        KeyValuePair.Create("prawn_stars", new Point(103 - HalfWidth, 34 - HalfWidth)),
        KeyValuePair.Create("minotaur", new Point(103 - HalfWidth, 120 - HalfWidth)),
        KeyValuePair.Create("pirate_bake_sale", new Point(166 - HalfWidth, 34 - HalfWidth)),
        KeyValuePair.Create("restaurant", new Point(166 - HalfWidth, 120 - HalfWidth)),
        KeyValuePair.Create("bowling_ball_door", new Point(254 - HalfWidth, 77 - HalfWidth)),
        KeyValuePair.Create("captured_goldfish", new Point(290 - HalfWidth, 106 - HalfWidth)),
        KeyValuePair.Create("201", new Point(282 - HalfWidth, 225 - HalfWidth)),
        KeyValuePair.Create("212", new Point(235 - HalfWidth, 179 - HalfWidth)),
        KeyValuePair.Create("206", new Point(235 - HalfWidth, 225 - HalfWidth)),
        KeyValuePair.Create("203", new Point(235 - HalfWidth, 269 - HalfWidth)),
        KeyValuePair.Create("207", new Point(178 - HalfWidth, 179 - HalfWidth)),
        KeyValuePair.Create("205", new Point(178 - HalfWidth, 225 - HalfWidth)),
        KeyValuePair.Create("213", new Point(178 - HalfWidth, 269 - HalfWidth)),
        KeyValuePair.Create("209", new Point(124 - HalfWidth, 179 - HalfWidth)),
        KeyValuePair.Create("202", new Point(124 - HalfWidth, 225 - HalfWidth)),
        KeyValuePair.Create("204", new Point(124 - HalfWidth, 269 - HalfWidth)),
        KeyValuePair.Create("210", new Point(67 - HalfWidth, 179 - HalfWidth)),
        KeyValuePair.Create("208", new Point(67 - HalfWidth, 225 - HalfWidth)),
        KeyValuePair.Create("211", new Point(67 - HalfWidth, 269 - HalfWidth)),
        KeyValuePair.Create("214", new Point(20 - HalfWidth, 225 - HalfWidth)),
        KeyValuePair.Create("301", new Point(25 - HalfWidth, 331 - HalfWidth)),
        KeyValuePair.Create("310", new Point(73 - HalfWidth, 353 - HalfWidth)),
        KeyValuePair.Create("302", new Point(84 - HalfWidth, 402 - HalfWidth)),
        KeyValuePair.Create("309", new Point(54 - HalfWidth, 435 - HalfWidth)),
        KeyValuePair.Create("304", new Point(114 - HalfWidth, 428 - HalfWidth)),
        KeyValuePair.Create("307", new Point(113 - HalfWidth, 334 - HalfWidth)),
        KeyValuePair.Create("305", new Point(149 - HalfWidth, 381 - HalfWidth)),
        KeyValuePair.Create("308", new Point(183 - HalfWidth, 346 - HalfWidth)),
        KeyValuePair.Create("303", new Point(194 - HalfWidth, 399 - HalfWidth)),
        KeyValuePair.Create("306", new Point(232 - HalfWidth, 406 - HalfWidth)),
        KeyValuePair.Create("311", new Point(243 - HalfWidth, 354 - HalfWidth)),
        KeyValuePair.Create("312", new Point(284 - HalfWidth, 319 - HalfWidth)),
    }.ToFrozenDictionary();

    private readonly CompositeDisposable _disposables = [];

    public CheckableLocationViewModel(string locationKey)
    {
        LocationKey = locationKey;
        CanvasLocation = s_canvasLocations[locationKey];

        _disposables.Add(this.WhenAnyValue(x => x.Checked)
            .Subscribe(isChecked => Available &= !isChecked));

        (Bitmap[] saturated, Bitmap[] desaturated) = ReadFrames(locationKey);
        (Bitmap[] yellowQuest, Bitmap[] grayQuest) = s_questFrames.Value;
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
                int nextLocationFrameIndex = (int)(i % saturated.Length);
                Image = saturated[nextLocationFrameIndex];
                DesaturatedImage = desaturated[nextLocationFrameIndex];

                int nextQuestFrameIndex = (int)(i % yellowQuest.Length);
                QuestImage = (Checked ? null : Available ? yellowQuest : grayQuest)?[nextQuestFrameIndex];
            }));
    }

    public string LocationKey { get; }

    public Point CanvasLocation { get; }

    [Reactive]
    public Bitmap? Image { get; private set; }

    [Reactive]
    public Bitmap? DesaturatedImage { get; private set; }

    [Reactive]
    public Bitmap? QuestImage { get; private set; }

    [Reactive]
    public bool Available { get; set; }

    [Reactive]
    public bool Checked { get; set; }

    public void Dispose()
    {
        _disposables.Dispose();
    }

    private static (Bitmap[] Saturated, Bitmap[] Desaturated) ReadFrames(string locationKey)
    {
        if (int.TryParse(locationKey, out _))
        {
            locationKey = "archipelago_item";
        }

        using Stream data = AssetLoader.Open(new($"avares://Autopelago/Assets/Images/{locationKey}.webp"));
        using SKCodec codec = SKCodec.Create(data);
        SKImageInfo imageInfo = codec.Info;
        SKCodecFrameInfo[] frameInfo = codec.FrameInfo;
        Bitmap[] saturated = new Bitmap[Math.Max(1, frameInfo.Length)];
        Bitmap[] desaturated = new Bitmap[Math.Max(1, frameInfo.Length)];
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

        if (frameInfo.Length == 0)
        {
            using SKBitmap bmp = new(imageInfo);
            codec.GetPixels(imageInfo, bmp.GetPixels());
            bmp.SetImmutable();
            using SKImage img = SKImage.FromBitmap(bmp);
            using MemoryStream ms = new();
            using SKData encoded = img.Encode();
            encoded.SaveTo(ms);
            ms.Position = 0;
            saturated[0] = new(ms);
            desaturated[0] = ToDesaturated(bmp);
        }

        return (saturated, desaturated);
    }
}
