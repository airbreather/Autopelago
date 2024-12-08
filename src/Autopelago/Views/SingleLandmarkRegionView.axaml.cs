using System.Collections.Immutable;
using System.Reactive.Disposables;
using System.Reactive.Linq;

using Autopelago.ViewModels;

using Avalonia;
using Avalonia.Interactivity;
using Avalonia.ReactiveUI;
using Avalonia.Rendering.Composition;

using ReactiveUI;

namespace Autopelago.Views;

public sealed partial class SingleLandmarkRegionView : ReactiveUserControl<LandmarkRegionViewModel>
{
    private readonly CompositeDisposable _disposables = [];

    private CompositionCustomVisual? _customVisual;

    public SingleLandmarkRegionView()
    {
        InitializeComponent();

        LayoutUpdated += OnLayoutUpdated;
        void OnLayoutUpdated(object? sender, EventArgs e)
        {
            _customVisual?.SendHandlerMessage(new Size(Width, Height));
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

        _customVisual = compositor.CreateCustomVisual(new LandmarksVisualHandler());
        _customVisual.Size = new(Width, Height);
        _customVisual.SendHandlerMessage(new Size(Width, Height));
        ElementComposition.SetElementChildVisual(this, _customVisual);
        this.ObservableForProperty(x => x.ViewModel, skipInitial: false)
            .Where(v => v.Value is not null)
            .Subscribe(v => _customVisual.SendHandlerMessage(ImmutableArray.Create(v.Value!)))
            .DisposeWith(_disposables);
    }
}
