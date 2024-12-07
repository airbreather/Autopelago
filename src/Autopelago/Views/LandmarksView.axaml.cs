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
    private static readonly object s_pauseMessage = new();

    private static readonly object s_resumeMessage = new();

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

        _customVisual = compositor.CreateCustomVisual(new CustomVisualHandler());
        _customVisual.Size = new(Width, Height);
        _customVisual.ClipToBounds = false;
        ElementComposition.SetElementChildVisual(this, _customVisual);
        IObservable<GameStateViewModel> viewModelChanges = this.ObservableForProperty(x => x.ViewModel, skipInitial: false)
            .Where(v => v.Value is not null)
            .Select(v => v.Value!);

        viewModelChanges
            .Subscribe(v => _customVisual.SendHandlerMessage(v))
            .DisposeWith(_disposables);
        viewModelChanges
            .Select(v => v.ObservableForProperty(x => x.Paused, skipInitial: false))
            .Switch()
            .Subscribe(v => _customVisual.SendHandlerMessage(v.Value ? s_pauseMessage : s_resumeMessage))
            .DisposeWith(_disposables);
    }

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct LandmarkState
    {
        public required int Index { get; init; }

        public required SKBitmap SKImageA { get; init; }

        public required SKBitmap SKImageB { get; init; }

        public required SKBitmap SKQImageA { get; init; }

        public required SKBitmap SKQImageB { get; init; }

        private readonly Rect _bounds;

        public required Rect Bounds
        {
            get => _bounds;
            init => _bounds = value;
        }

        private readonly Rect _qBounds;
        public required Rect QBounds
        {
            get => _qBounds;
            init => _qBounds = value;
        }

        public SKRect SKBounds => ToSKRect(in _bounds);

        public SKRect SKQBounds => ToSKRect(in _qBounds);

        private static SKRect ToSKRect(in Rect rect)
        {
            return new((float)rect.Left, (float)rect.Top, (float)rect.Right, (float)rect.Bottom);
        }
    }

    private sealed class CustomVisualHandler : CompositionCustomVisualHandler
    {
        private static readonly BitmapPair s_emptyImagePair = CreateEmptyImagePair();

        private LandmarkState[]? _landmarkStates;

        private TimeSpan _lastUpdate;

        private bool _showA = true;

        private TimeSpan? _pausedAt;

        public override void OnRender(ImmediateDrawingContext drawingContext)
        {
            if (drawingContext.TryGetFeature(typeof(ISkiaSharpApiLeaseFeature)) is not ISkiaSharpApiLeaseFeature skia ||
                _landmarkStates is not { } landmarkStates)
            {
                return;
            }

            using ISkiaSharpApiLease l = skia.Lease();
            SKCanvas canvas = l.SkCanvas;

            foreach (LandmarkState landmarkState in landmarkStates)
            {
                canvas.DrawBitmap(_showA ? landmarkState.SKQImageA : landmarkState.SKQImageB, landmarkState.SKQBounds);
                canvas.DrawBitmap(_showA ? landmarkState.SKImageA : landmarkState.SKImageB, landmarkState.SKBounds);
            }
        }

        public override void OnMessage(object message)
        {
            if (message == s_pauseMessage)
            {
                _pausedAt = CompositionNow;
                return;
            }

            if (message == s_resumeMessage)
            {
                if (_pausedAt is TimeSpan pausedAt)
                {
                    _lastUpdate += CompositionNow - pausedAt;
                }

                _pausedAt = null;
                return;
            }

            if (message is not GameStateViewModel viewModel)
            {
                return;
            }

            LandmarkState[] bounds = _landmarkStates = new LandmarkState[viewModel.LandmarkRegions.Length];
            Size size = new(16, 16);
            Size qSize = new(12, 12);
            Vector toQ = new(2, -13);
            for (int i = 0; i < bounds.Length; i++)
            {
                LandmarkRegionViewModel landmark = viewModel.LandmarkRegions[i];
                bounds[i] = new()
                {
                    Index = i,
                    Bounds = new(landmark.CanvasLocation, size),
                    QBounds = new(landmark.CanvasLocation + toQ, qSize),
                    SKImageA = (landmark.ShowSaturatedImage ? landmark.SaturatedImages : landmark.DesaturatedImages).A,
                    SKImageB = (landmark.ShowSaturatedImage ? landmark.SaturatedImages : landmark.DesaturatedImages).B,
                    SKQImageA = (landmark.ShowYellowQuestImage ? landmark.YellowQuestImages : landmark.ShowGrayQuestImage ? landmark.GrayQuestImages : s_emptyImagePair).A,
                    SKQImageB = (landmark.ShowYellowQuestImage ? landmark.YellowQuestImages : landmark.ShowGrayQuestImage ? landmark.GrayQuestImages : s_emptyImagePair).B,
                };
            }

            RegisterForNextAnimationFrameUpdate();
            _lastUpdate = CompositionNow;
        }

        public override void OnAnimationFrameUpdate()
        {
            RegisterForNextAnimationFrameUpdate();
            if (_landmarkStates is not { } landmarkStates || _pausedAt.HasValue)
            {
                return;
            }

            if (CompositionNow - _lastUpdate > TimeSpan.FromMilliseconds(500))
            {
                _showA = !_showA;
                foreach (LandmarkState landmarkState in landmarkStates)
                {
                    Invalidate(landmarkState.QBounds);
                    Invalidate(landmarkState.Bounds);
                }

                _lastUpdate = CompositionNow;
            }
        }

        private static BitmapPair CreateEmptyImagePair()
        {
            SKBitmap result = new(16, 16, SKColorType.Bgra8888, SKAlphaType.Unpremul);
            result.SetImmutable();
            return new() { A = result, B = result };
        }
    }
}
