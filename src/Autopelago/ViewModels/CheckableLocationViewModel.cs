using System.Collections.Frozen;
using System.Reactive.Disposables;
using System.Reactive.Linq;

using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.ReactiveUI;

using ReactiveUI;

using SkiaSharp;

namespace Autopelago.ViewModels;

public sealed class CheckableLocationViewModel : ViewModelBase, IDisposable
{
    private static readonly Lazy<IObservable<long>> s_timer = new(() => Observable.Interval(TimeSpan.FromMilliseconds(500), AvaloniaScheduler.Instance));

    private static readonly FrozenDictionary<string, Point> s_canvasLocations = new[]
    {
        KeyValuePair.Create("basketball", new Point(196, 268)),
    }.ToFrozenDictionary();

    private readonly IDisposable _propertyChangedSubscription;

    private IDisposable _prevDisposables = Disposable.Empty;

    public CheckableLocationViewModel()
    {
        _canvasLocation = this.WhenAnyValue(x => x.LocationKey)
            .Select(l => s_canvasLocations.GetValueOrDefault(l))
            .ToProperty(this, x => x.CanvasLocation);

        _propertyChangedSubscription = this.WhenAnyValue(x => x.LocationKey)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ObserveOn(AvaloniaScheduler.Instance)
            .Subscribe(locationKey =>
            {
                _prevDisposables.Dispose();
                Bitmap[] frames = ReadFrames(locationKey);
                CompositeDisposable disposables = new();
                _prevDisposables = disposables;
                foreach (Bitmap frame in frames)
                {
                    disposables.Add(frame);
                }

                int nextFrameIndex = 0;
                Frame = frames[nextFrameIndex];
                disposables.Add(s_timer.Value
                    .StartWith(-1)
                    .Subscribe(_ =>
                    {
                        Frame = frames[nextFrameIndex++];
                        if (nextFrameIndex == frames.Length)
                        {
                            nextFrameIndex = 0;
                        }
                    }));
            });
    }

    private string _locationKey = "";
    public string LocationKey
    {
        get => _locationKey;
        set => this.RaiseAndSetIfChanged(ref _locationKey, value);
    }

    private Bitmap? _frame;
    public Bitmap? Frame
    {
        get => _frame;
        private set => this.RaiseAndSetIfChanged(ref _frame, value);
    }

    private readonly ObservableAsPropertyHelper<Point> _canvasLocation;
    public Point CanvasLocation => _canvasLocation.Value;

    public void Dispose()
    {
        using (_propertyChangedSubscription)
        using (_prevDisposables)
        {
            _prevDisposables = Disposable.Empty;
        }
    }

    private static Bitmap[] ReadFrames(string locationKey)
    {
        using Stream data = AssetLoader.Open(new($"avares://Autopelago/Assets/Images/{locationKey}.webp"));
        using SKCodec codec = SKCodec.Create(data);
        SKImageInfo imageInfo = codec.Info;
        SKCodecFrameInfo[] frameInfo = codec.FrameInfo;
        Bitmap[] frames = new Bitmap[frameInfo.Length];
        for (int i = 0; i < frameInfo.Length; i++)
        {
            if (frameInfo[i].Duration != 500)
            {
                throw new NotImplementedException("These were all supposed to be 500ms.");
            }

            using SKBitmap bmp = new(imageInfo);
            codec.GetPixels(imageInfo, bmp.GetPixels(), new(i));
            bmp.SetImmutable();
            using SKImage img = SKImage.FromBitmap(bmp);
            using MemoryStream ms = new();
            using SKData encoded = img.Encode();
            encoded.SaveTo(ms);
            ms.Position = 0;
            frames[i] = new(ms);
        }

        return frames;
    }
}
