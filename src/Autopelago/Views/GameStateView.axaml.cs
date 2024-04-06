using Autopelago.ViewModels;

using Avalonia.ReactiveUI;

namespace Autopelago.Views;

public partial class GameStateView : ReactiveUserControl<GameStateViewModel>
{
    public GameStateView()
    {
        InitializeComponent();
    }
}