using System.Reactive.Disposables;
using System.Reactive.Linq;

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

public sealed partial class BitmapPairView : ReactiveUserControl<BitmapPair>
{
    private readonly CompositeDisposable _disposables = [];

    private CompositionCustomVisual? _customVisual;

    public BitmapPairView()
    {
        InitializeComponent();

        LayoutUpdated += OnLayoutUpdated;
        void OnLayoutUpdated(object? sender, EventArgs args)
        {
            if (_customVisual is { } customVisual)
            {
                customVisual.Size = new(this.Width, this.Height);
                if (ViewModel is { ToDraw: { } toDraw })
                {
                    customVisual.SendHandlerMessage(toDraw);
                }
            }
        }
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
        ElementComposition.SetElementChildVisual(this, _customVisual);
        _disposables.Add(this.ObservableForProperty(x => x.ViewModel, skipInitial: false)
            .Where(v => v.Value is not null)
            .Select(v => v.Value!.ObservableForProperty(x => x.ToDraw, skipInitial: false))
            .Switch()
            .Subscribe(p => _customVisual.SendHandlerMessage(p.Value)));
    }

    private sealed class CustomVisualHandler : CompositionCustomVisualHandler
    {
        private SKBitmap? _draw;

        public override void OnRender(ImmediateDrawingContext drawingContext)
        {
            if (drawingContext.TryGetFeature(typeof(ISkiaSharpApiLeaseFeature)) is not ISkiaSharpApiLeaseFeature skia ||
                _draw is not { } draw)
            {
                return;
            }

            var sz = EffectiveSize;
            using var l = skia.Lease();
            l.SkCanvas.DrawBitmap(draw, SKRect.Create(default(SKPoint), new((float)sz.X, (float)sz.Y)));
        }

        public override void OnMessage(object message)
        {
            if (message is SKBitmap bmp)
            {
                _draw = bmp;
                RegisterForNextAnimationFrameUpdate();
            }
        }

        public override void OnAnimationFrameUpdate()
        {
            Invalidate();
        }
    }
}
