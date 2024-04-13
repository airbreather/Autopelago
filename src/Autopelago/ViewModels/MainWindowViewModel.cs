using ReactiveUI.Fody.Helpers;

namespace Autopelago.ViewModels;

public sealed class MainWindowViewModel : ViewModelBase, IDisposable
{
    private readonly IDisposable _connectCommandSubscription;

    public MainWindowViewModel()
    {
        _connectCommandSubscription = SettingsSelection.ConnectCommand
            .Subscribe(settings => ContentViewModel = new GameStateViewModel
            {
                SlotName = settings.Slot,
            });

        ContentViewModel = SettingsSelection;
    }

    public SettingsSelectionViewModel SettingsSelection { get; } = new();

    [Reactive]
    public ViewModelBase ContentViewModel { get; set; }

    public void Dispose()
    {
        _connectCommandSubscription.Dispose();
    }
}
