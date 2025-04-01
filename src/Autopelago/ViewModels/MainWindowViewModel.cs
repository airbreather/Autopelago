using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Reactive.Threading.Tasks;

using ReactiveUI;
using ReactiveUI.SourceGenerators;

using Serilog;

namespace Autopelago.ViewModels;

public sealed partial class MainWindowViewModel : ViewModelBase, IDisposable
{
    private readonly Subject<Unit> _shouldSaveSettings = new();

    private readonly IDisposable _connectCommandSubscription;

    [Reactive] private ViewModelBase _contentViewModel;

    [Reactive(SetModifier = AccessModifier.Private)] private object? _dialogPopoverContent;

    private GameStateObservableProvider? _provider;

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
                GameStateObservableProvider provider = _provider = new(settings, TimeProvider.System);

                _shouldSaveSettings.OnNext(Unit.Default);
                GameStateViewModel gameStateViewModel = new(provider)
                {
                    BackToMainMenuCommand = backToMainMenuCommand,
                    RequestItemHintCommand = ConfirmItemHintCommand,
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

    [ReactiveCommand]
    private async Task<Unit> ConfirmItemHintAsync(CollectableItemViewModel item, CancellationToken cancellationToken)
    {
        if (_provider is not { } provider)
        {
            return Unit.Default;
        }

        ConfirmItemHintViewModel confirmViewModel = new()
        {
            Item = item,
        };

        if (await ShowDialogAsync(confirmViewModel, confirmViewModel.Result.AsObservable(), cancellationToken) == ConfirmItemHintResult.Ok)
        {
            UserInitiatedActions actions = await provider.GetUserInitiatedActionsAsync();
            await actions.RequestItemHintAsync(item.Model.Key, item.LactoseIntolerant);
        }

        return Unit.Default;
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
