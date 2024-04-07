using Autopelago.ViewModels;

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.ReactiveUI;

namespace Autopelago.Views;

public partial class GameStateView : ReactiveUserControl<GameStateViewModel>
{
    private readonly GridLength _initialColumn0Width;

    private readonly GridLength _initialColumn2Width;

    public GameStateView()
    {
        InitializeComponent();
        _initialColumn0Width = MainGrid.ColumnDefinitions[0].Width;
        _initialColumn2Width = MainGrid.ColumnDefinitions[2].Width;
    }

    private void OnMainGridSplitterDoubleTapped(object? sender, TappedEventArgs e)
    {
        MainGrid.ColumnDefinitions[0].Width = _initialColumn0Width;
        MainGrid.ColumnDefinitions[2].Width = _initialColumn2Width;
    }
}
