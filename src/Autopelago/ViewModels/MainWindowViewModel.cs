using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Reactive.Threading.Tasks;

using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace Autopelago.ViewModels;

public sealed partial class MainWindowViewModel : ViewModelBase, IDisposable
{
    private readonly Subject<Unit> _shouldSaveSettings = new();

    private readonly IDisposable _connectCommandSubscription;

    [Reactive] private ViewModelBase _contentViewModel;

    [Reactive(SetModifier = AccessModifier.Private)] private object? _dialogPopoverContent;

    public MainWindowViewModel()
    {
        CancellationTokenSource cts = new();
        SerialDisposable gameStateViewModelHolder = new();
        ReactiveCommand<Unit, Unit> backToMainMenuCommand = ReactiveCommand.Create(() =>
        {
            CancellationTokenSource oldCts = cts;
            cts = new();
            oldCts.Cancel();

            gameStateViewModelHolder.Disposable = null;
            ContentViewModel = SettingsSelection;
        });
        Error = new() { BackToMainMenuCommand = backToMainMenuCommand };
        EndingFanfare = new() { BackToMapCommand = ReactiveCommand.Create(() => { ContentViewModel = (GameStateViewModel)gameStateViewModelHolder.Disposable!; }) };

        _connectCommandSubscription = SettingsSelection.ConnectCommand
            .Subscribe(settings =>
            {
                GameStateObservableProvider provider = new(settings, TimeProvider.System);

                _shouldSaveSettings.OnNext(Unit.Default);
                GameStateViewModel gameStateViewModel = new(provider)
                {
                    BackToMainMenuCommand = backToMainMenuCommand,
                    ConfirmItemHintCommand = ConfirmItemHintCommand,
                };
                gameStateViewModelHolder.Disposable = gameStateViewModel;

                provider.GameComplete.Subscribe(_ => ContentViewModel = EndingFanfare);
                provider.UnhandledException.Subscribe(ex =>
                {
                    gameStateViewModelHolder.Disposable = null;
                    Error.Message = $"{ex}";
                    CancellationTokenSource oldCts = cts;
                    cts = new();
                    oldCts.Cancel();
                    ContentViewModel = Error;
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

    [ReactiveCommand]
    private async Task<ConfirmItemHintResult> ConfirmItemHintAsync(CollectableItemViewModel item, CancellationToken cancellationToken)
    {
        ConfirmItemHintViewModel confirmViewModel = new()
        {
            Item = item,
        };

        return await ShowDialogAsync(confirmViewModel, confirmViewModel.Result.AsObservable(), cancellationToken);
    }

    private async Task<TOutput> ShowDialogAsync<TInput, TOutput>(TInput input, IObservable<TOutput> close, CancellationToken cancellationToken)
    {
        this.DialogPopoverContent = input;
        try
        {
            return await close.ToTask(cancellationToken);
        }
        finally
        {
            this.DialogPopoverContent = null;
        }
    }
}
