using Autopelago.ViewModels;
using Autopelago.Views;

using Avalonia.Controls;
using Avalonia.Controls.Templates;

namespace Autopelago;

public sealed class ViewLocator : IDataTemplate
{
    public bool Match(object? data)
    {
        return data is
            GameStateViewModel or
            MainWindowViewModel or
            SettingsSelectionViewModel or
            GameRequirementToolTipViewModel or
            ErrorViewModel or
            EndingFanfareViewModel or
            PlayerTokenViewModel or
            ConfirmItemHintViewModel;
    }

    public Control Build(object? param)
    {
        return param switch
        {
            GameStateViewModel => new GameStateView(),
            MainWindowViewModel => new MainWindowView(),
            SettingsSelectionViewModel => new SettingsSelectionView(),
            GameRequirementToolTipViewModel => new GameRequirementToolTipView(),
            ErrorViewModel => new ErrorView(),
            EndingFanfareViewModel => new EndingFanfareView(),
            PlayerTokenViewModel => new PlayerTokenView(),
            ConfirmItemHintViewModel => new ConfirmItemHintView(),
            _ => throw new InvalidOperationException("Match returned false."),
        };
    }
}
