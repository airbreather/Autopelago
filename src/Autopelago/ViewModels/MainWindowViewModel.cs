using ReactiveUI;
using ReactiveUI.Fody.Helpers;

using Serilog;

namespace Autopelago.ViewModels;

public sealed class MainWindowViewModel : ViewModelBase, IDisposable
{
    private readonly IDisposable _connectCommandSubscription;

    public MainWindowViewModel()
    {
        Error = new() { BackToMainMenuCommand = ReactiveCommand.Create(() => { ContentViewModel = SettingsSelection; }) };
        _connectCommandSubscription = SettingsSelection.ConnectCommand
            .Subscribe(settings =>
            {
                GameStateViewModel gameStateViewModel = new(settings)
                {
                    SlotName = settings.Slot,
                };
                gameStateViewModel.ConnectionRefused.Subscribe(connectionRefused =>
                {
                    gameStateViewModel.Dispose();
                    Error.Error = string.Join(Environment.NewLine, connectionRefused.Errors);
                    ContentViewModel = Error;
                });
                gameStateViewModel.UnhandledException.Subscribe(ex =>
                {
                    gameStateViewModel.Dispose();
                    Error.Error = $"{ex}";
                    ContentViewModel = Error;
                });
                ContentViewModel = gameStateViewModel;
                gameStateViewModel.Begin();
            });

        ContentViewModel = SettingsSelection;
    }

    public SettingsSelectionViewModel SettingsSelection { get; } = new();

    public ErrorViewModel Error { get; }

    [Reactive]
    public ViewModelBase ContentViewModel { get; set; }

    public void Dispose()
    {
        _connectCommandSubscription.Dispose();
    }
}
