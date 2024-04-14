using Autopelago.ViewModels;

using Avalonia.ReactiveUI;

namespace Autopelago.Views;

public sealed partial class GameRequirementToolTipView : ReactiveUserControl<GameRequirementToolTipViewModel>
{
    public GameRequirementToolTipView()
    {
        InitializeComponent();
    }
}