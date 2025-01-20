using System.Reactive.Linq;

using Avalonia;
using Avalonia.ReactiveUI;

namespace Autopelago.Views;

public sealed partial class BitmapPairView : ReactiveUserControl<BitmapPair>
{
    public static readonly StyledProperty<bool> ShowAProperty =
        AvaloniaProperty.Register<BitmapPairView, bool>(nameof(ShowA), defaultValue: true);

    public BitmapPairView()
    {
        InitializeComponent();
        Observable.Interval(TimeSpan.FromMilliseconds(500), AvaloniaScheduler.Instance)
            .Subscribe(_ => ShowA = !ShowA);
    }

    public bool ShowA
    {
        get => GetValue(ShowAProperty);
        set => SetValue(ShowAProperty, value);
    }
}
