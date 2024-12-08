using System.Reactive.Disposables;
using System.Reactive.Linq;

using Autopelago.ViewModels;

using Avalonia.Interactivity;
using Avalonia.ReactiveUI;

using ReactiveUI;

namespace Autopelago.Views;

public sealed partial class SettingsSelectionView : ReactiveUserControl<SettingsSelectionViewModel>
{
    private readonly CompositeDisposable _disposables = [];

    public SettingsSelectionView()
    {
        InitializeComponent();

        this.ObservableForProperty(x => x.ViewModel)
            .Where(v => v.Value is not null)
            .Select(v => v.Value!.PlayerToken.ObservableForProperty(x => x.Color))
            .Switch()
            .Subscribe(_ => this.PlayerTokenImage.InvalidateVisual())
            .DisposeWith(_disposables);
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        _disposables.Dispose();
    }
}
