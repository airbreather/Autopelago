using System.Collections.Immutable;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Runtime.InteropServices;

using Autopelago.ViewModels;

using Avalonia;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.ReactiveUI;
using Avalonia.Rendering.Composition;
using Avalonia.Skia;

using ReactiveUI;

using SkiaSharp;

namespace Autopelago.Views;

public partial class LandmarksView : ReactiveUserControl<GameStateViewModel>
{
    private readonly CompositeDisposable _disposables = [];

    private CompositionCustomVisual? _customVisual;

    public LandmarksView()
    {
        InitializeComponent();
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);
        _disposables.Dispose();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (ElementComposition.GetElementVisual(this)?.Compositor is not { } compositor ||
            _customVisual?.Compositor == compositor)
        {
            return;
        }

        _customVisual = compositor.CreateCustomVisual(new LandmarksVisualHandler());
        _customVisual.Size = new(Width, Height);
        ElementComposition.SetElementChildVisual(this, _customVisual);
        IObservable<GameStateViewModel> viewModelChanges = this.ObservableForProperty(x => x.ViewModel, skipInitial: false)
            .Where(v => v.Value is not null)
            .Select(v => v.Value!);

        viewModelChanges
            .Subscribe(v =>
            {
                if (!v.TileAnimations)
                {
                    _customVisual.SendHandlerMessage(LandmarksVisualHandler.PauseMessage);
                }

                _customVisual.SendHandlerMessage(v.LandmarkRegions);
            })
            .DisposeWith(_disposables);
        viewModelChanges
            .Where(v => v.TileAnimations)
            .Select(v => v.ObservableForProperty(x => x.Paused, skipInitial: false))
            .Switch()
            .Subscribe(v => _customVisual.SendHandlerMessage(v.Value ? LandmarksVisualHandler.PauseMessage : LandmarksVisualHandler.ResumeMessage))
            .DisposeWith(_disposables);
    }
}

[StructLayout(LayoutKind.Auto)]
public record struct LandmarkState
{
    private static readonly Size s_size = new(16, 16);

    private static readonly Size s_qSize = new(12, 12);

    private static readonly Vector s_toQ = new(2, -13);

    private QuestProgress _lastKnownQuestProgress;

    public required LandmarkRegionViewModel Landmark { get; init; }

    public readonly Rect Bounds => new(Landmark.CanvasLocation, s_size);

    public readonly Rect QBounds => new(Landmark.CanvasLocation + s_toQ, s_qSize);

    public bool CheckProgress()
    {
        if (_lastKnownQuestProgress >= QuestProgress.Unlocked)
        {
            return false;
        }

        if ((_lastKnownQuestProgress == QuestProgress.Unlockable && Landmark.Checked) ||
            (_lastKnownQuestProgress == QuestProgress.Locked && Landmark.ShowYellowQuestImage))
        {
            ++_lastKnownQuestProgress;
            return true;
        }

        return false;
    }

    public ReadOnlySpan<Rect> RectsToInvalidateBig(Span<Rect> rects)
    {
        rects[0] = Bounds;
        if (_lastKnownQuestProgress == QuestProgress.ErasedMarker)
        {
            return rects[..1];
        }

        rects[1] = QBounds;
        if (Landmark.Checked)
        {
            // merely invalidating it erases the marker, so this is the one thing we can do in here.
            _lastKnownQuestProgress = QuestProgress.ErasedMarker;
        }

        return rects;
    }

    public readonly void RenderAlone(SKCanvas canvas, Size size, bool showA)
    {
        canvas.DrawImage(showA ? Landmark.SaturatedImages.AImage : Landmark.SaturatedImages.BImage, ToSKRect(new(size)));
    }

    public readonly void RenderBig(SKCanvas canvas, bool showA)
    {
        switch ((Landmark.Checked, Landmark.ShowGrayQuestImage, Landmark.ShowYellowQuestImage))
        {
            case (true, _, _):
                break;

            case (_, true, _):
                canvas.DrawImage(showA ? Landmark.GrayQuestImages.AImage : Landmark.GrayQuestImages.BImage, ToSKRect(QBounds));
                break;

            case (_, _, true):
                canvas.DrawImage(showA ? Landmark.YellowQuestImages.AImage : Landmark.YellowQuestImages.BImage, ToSKRect(QBounds));
                break;
        }

        switch ((Landmark.ShowDesaturatedImage, Landmark.ShowSaturatedImage))
        {
            case (true, _):
                canvas.DrawImage(showA ? Landmark.DesaturatedImages.AImage : Landmark.DesaturatedImages.BImage, ToSKRect(Bounds));
                break;

            case (_, true):
                canvas.DrawImage(showA ? Landmark.SaturatedImages.AImage : Landmark.SaturatedImages.BImage, ToSKRect(Bounds));
                break;
        }
    }

    private static SKRect ToSKRect(Rect rect)
    {
        return new((float)rect.Left, (float)rect.Top, (float)rect.Right, (float)rect.Bottom);
    }

    private enum QuestProgress : byte
    {
        Locked,
        Unlockable,
        Unlocked,
        ErasedMarker,
    }
}

public sealed class LandmarksVisualHandler : CompositionCustomVisualHandler
{
    public static readonly object PauseMessage = new();

    public static readonly object ResumeMessage = new();

    private LandmarkState[] _landmarkStates = [];

    private Size? _size;

    private bool _initialized;

    private TimeSpan _lastUpdate;

    private bool _showA = true;

    private TimeSpan? _pausedAt;

    public override void OnRender(ImmediateDrawingContext drawingContext)
    {
        if (!_initialized || drawingContext.TryGetFeature(typeof(ISkiaSharpApiLeaseFeature)) is not ISkiaSharpApiLeaseFeature skia)
        {
            return;
        }

        using ISkiaSharpApiLease l = skia.Lease();
        SKCanvas canvas = l.SkCanvas;

        if (_size is Size aloneSize)
        {
            foreach (LandmarkState landmarkState in _landmarkStates)
            {
                landmarkState.RenderAlone(canvas, aloneSize, _showA);
            }
        }
        else
        {
            foreach (LandmarkState landmarkState in _landmarkStates)
            {
                landmarkState.RenderBig(canvas, _showA);
            }
        }
    }

    public override void OnMessage(object message)
    {
        if (message == PauseMessage)
        {
            _pausedAt = CompositionNow;
            return;
        }

        if (message == ResumeMessage)
        {
            if (_pausedAt is TimeSpan pausedAt)
            {
                _lastUpdate += CompositionNow - pausedAt;
            }

            _pausedAt = null;
            return;
        }

        if (message is Size size)
        {
            _size = size;
            return;
        }

        if (message is not ImmutableArray<LandmarkRegionViewModel> landmarks)
        {
            return;
        }

        _landmarkStates = new LandmarkState[landmarks.Length];
        for (int i = 0; i < _landmarkStates.Length; i++)
        {
            _landmarkStates[i] = new() { Landmark = landmarks[i] };
        }

        _initialized = true;
        _lastUpdate = CompositionNow;
        RegisterForNextAnimationFrameUpdate();
    }

    public override void OnAnimationFrameUpdate()
    {
        RegisterForNextAnimationFrameUpdate();
        if (!_initialized)
        {
            return;
        }

        bool tickFrame = false;
        if (!_pausedAt.HasValue)
        {
            if (CompositionNow - _lastUpdate > TimeSpan.FromMilliseconds(500))
            {
                _showA = !_showA;
                if (_size.HasValue)
                {
                    Invalidate();
                    _lastUpdate = CompositionNow;
                    return;
                }

                tickFrame = true;
            }
        }

        Span<Rect> buf = stackalloc Rect[2];
        foreach (ref LandmarkState landmarkState in _landmarkStates.AsSpan())
        {
            if (landmarkState.CheckProgress() || tickFrame)
            {
                foreach (Rect toInvalidate in landmarkState.RectsToInvalidateBig(buf))
                {
                    Invalidate(toInvalidate);
                }
            }
        }

        if (tickFrame)
        {
            _lastUpdate = CompositionNow;
        }
    }
}
