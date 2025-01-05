using System.Reactive;
using System.Reactive.Subjects;

using ReactiveUI;
using ReactiveUI.SourceGenerators;

using Serilog;

namespace Autopelago.ViewModels;

public sealed partial class MainWindowViewModel : ViewModelBase, IDisposable
{
    private readonly Subject<Unit> _shouldSaveSettings = new();

    private readonly IDisposable _connectCommandSubscription;

    [Reactive] private ViewModelBase _contentViewModel;

    public MainWindowViewModel()
    {
        CancellationTokenSource cts = new();
        GameStateViewModel? gameStateViewModel = null;
        ReactiveCommand<Unit, Unit> backToMainMenuCommand = ReactiveCommand.Create(() =>
        {
            CancellationTokenSource oldCts = cts;
            cts = new();
            oldCts.Cancel();

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
                GameStateObservableProvider provider = new(settings, TimeProvider.System);

                _shouldSaveSettings.OnNext(Unit.Default);
                gameStateViewModel = new(provider)
                {
                    BackToMainMenuCommand = backToMainMenuCommand,
                };
                provider.GameComplete.Subscribe(_ => ContentViewModel = EndingFanfare);
                provider.UnhandledException.Subscribe(ex =>
                {
                    gameStateViewModel?.Dispose();
                    gameStateViewModel = null;
                    Error.Message = $"{ex}";
                    CancellationTokenSource oldCts = cts;
                    cts = new();
                    oldCts.Cancel();
                    ContentViewModel = Error;
                    Log.Error(ex, "Unhandled exception");
                });
                ContentViewModel = gameStateViewModel;
                provider.RunAsync(cts.Token);
            });

        ContentViewModel = SettingsSelection;
    }

    public void Dispose()
    {
        _connectCommandSubscription.Dispose();
    }

    public IObservable<Unit> ShouldSaveSettings => _shouldSaveSettings;

    public SettingsSelectionViewModel SettingsSelection { get; } = new();

    public ErrorViewModel Error { get; }

    public EndingFanfareViewModel EndingFanfare { get; }
}
