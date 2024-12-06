using Autopelago.ViewModels;

using Avalonia.ReactiveUI;

namespace Autopelago.Views;

public sealed partial class PlayerTokenView : ReactiveUserControl<PlayerTokenViewModel>
{
    public PlayerTokenView()
    {
        InitializeComponent();
    }
}
