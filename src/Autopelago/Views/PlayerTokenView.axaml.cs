using System.Reactive.Disposables;
using System.Reactive.Linq;

using Autopelago.ViewModels;

using Avalonia.Interactivity;
using Avalonia.ReactiveUI;

using ReactiveUI;

namespace Autopelago.Views;

public sealed partial class PlayerTokenView : ReactiveUserControl<PlayerTokenViewModel>
{
    private readonly CompositeDisposable _disposables = [];

    public PlayerTokenView()
    {
        InitializeComponent();

        this.ObservableForProperty(x => x.ViewModel)
            .Where(v => v.Value is not null)
            .Select(v => v.Value!.ObservableForProperty(x => x.Color))
            .Switch()
            .Subscribe(_ =>
            {
                this.Player1Image.InvalidateVisual();
                this.Player2Image.InvalidateVisual();
                this.Player4Image.InvalidateVisual();
            })
            .DisposeWith(_disposables);
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        _disposables.Dispose();
    }
}
