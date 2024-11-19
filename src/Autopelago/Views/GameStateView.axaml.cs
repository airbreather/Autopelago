using Autopelago.ViewModels;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.ReactiveUI;
using Avalonia.VisualTree;

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

    private void OnPointerMovedOverControlWithToolTip(object? sender, PointerEventArgs e)
    {
        Control ctrl = (Control)sender!;
        PixelPoint pt = ctrl.PointToScreen(e.GetPosition(ctrl));
        if (ctrl.FindAncestorOfType<Window>() is not Window main)
        {
            return;
        }

        if (main.Screens.ScreenFromWindow(main) is not Screen s)
        {
            return;
        }

        ToolTip.SetPlacement(ctrl, pt.Y < s.Bounds.Height / 2 ? PlacementMode.Bottom : PlacementMode.Top);
    }

    private void OnPlayerToolTipOpening(object? sender, CancelRoutedEventArgs e)
    {
        ViewModel?.NextRatThought();
    }
}
