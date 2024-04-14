using Autopelago.ViewModels;
using Autopelago.Views;

using Avalonia.Controls;
using Avalonia.Controls.Templates;

namespace Autopelago;

public sealed class ViewLocator : IDataTemplate
{
    public Control? Build(object? param)
    {
        Control? control = param switch
        {
            GameStateViewModel => new GameStateView(),
            MainWindowViewModel => new MainWindowView(),
            SettingsSelectionViewModel => new SettingsSelectionView(),
            GameRequirementToolTipViewModel => new GameRequirementToolTipView(),
            _ => null,
        };

        if (control is not null)
        {
            control.DataContext = param;
        }

        return control;
    }

    public bool Match(object? data)
    {
        return data is GameStateViewModel or MainWindowViewModel or SettingsSelectionViewModel or GameRequirementToolTipViewModel;
    }
}
