using System.Reactive;
using System.Reactive.Subjects;

using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace Autopelago.ViewModels;

public sealed class MainWindowViewModel : ViewModelBase, IDisposable
{
    private readonly Subject<Unit> _shouldSaveSettings = new();

    private readonly IDisposable _connectCommandSubscription;

    public MainWindowViewModel()
    {
        GameStateViewModel? gameStateViewModel = null;
        ReactiveCommand<Unit, Unit> backToMainMenuCommand = ReactiveCommand.Create(() =>
        {
            if (gameStateViewModel is not null)
            {
                gameStateViewModel.Dispose();
                gameStateViewModel = null;
            }

            ContentViewModel = SettingsSelection;
        });
        Error = new() { BackToMainMenuCommand = backToMainMenuCommand };
        EndingFanfare = new() { BackToMapCommand = ReactiveCommand.Create(() => { ContentViewModel = gameStateViewModel!; }) };

        _connectCommandSubscription = SettingsSelection.ConnectCommand
            .Subscribe(settings =>
            {
                _shouldSaveSettings.OnNext(Unit.Default);
                gameStateViewModel = new(settings)
                {
                    SlotName = settings.Slot,
                    BackToMainMenuCommand = backToMainMenuCommand,
                    EndingFanfareCommand = ReactiveCommand.Create(() => { ContentViewModel = EndingFanfare; }),
                };
                gameStateViewModel.ConnectionRefused.Subscribe(connectionRefused =>
                {
                    gameStateViewModel.Dispose();
                    Error.Message = string.Join(Environment.NewLine, connectionRefused.Errors);
                    ContentViewModel = Error;
                });
                gameStateViewModel.UnhandledException.Subscribe(ex =>
                {
                    gameStateViewModel.Dispose();
                    Error.Message = $"{ex}";
                    ContentViewModel = Error;
                });
                ContentViewModel = gameStateViewModel;
                gameStateViewModel.Begin();
            });

        ContentViewModel = SettingsSelection;
    }

    public IObservable<Unit> ShouldSaveSettings => _shouldSaveSettings;

    public SettingsSelectionViewModel SettingsSelection { get; } = new();

    public ErrorViewModel Error { get; }

    public EndingFanfareViewModel EndingFanfare { get; }

    [Reactive]
    public ViewModelBase ContentViewModel { get; set; }

    public void Dispose()
    {
        _connectCommandSubscription.Dispose();
    }
}
