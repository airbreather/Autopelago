using System.Net.WebSockets;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Reactive.Threading.Tasks;

using Avalonia.Controls;
using Avalonia.ReactiveUI;

namespace Autopelago;

public sealed class GameStateObservableProvider
{
    private readonly ReplaySubject<Game> _currentGameState = new(1);

    private readonly AsyncSubject<Unit> _gameComplete = new();

    private readonly AsyncSubject<Exception> _unhandledException = new();

    private readonly BehaviorSubject<bool> _paused = new(false);

    private readonly Settings _settings;

    private readonly TimeProvider _timeProvider;

    public GameStateObservableProvider(Settings settings)
        : this(settings, TimeProvider.System)
    {
    }

    public GameStateObservableProvider(Settings settings, TimeProvider timeProvider)
    {
        _settings = settings;
        _timeProvider = timeProvider;
        CurrentGameState = _currentGameState.AsObservable();
        GameComplete = _gameComplete.AsObservable();
        UnhandledException = _unhandledException.AsObservable();
        Paused = _paused.AsObservable();

        if (Design.IsDesignMode)
        {
            Game g = new(Prng.State.Start());

            int prevCheckedLocationsCount = 0;
            Observable.Interval(TimeSpan.FromSeconds(1), AvaloniaScheduler.Instance)
                .Subscribe(_ =>
                {
                    g.Advance();
                    g.ReceiveItems([
                        .. g.CheckedLocations
                            .Order
                            .Skip(prevCheckedLocationsCount)
                            .Where(l => l.UnrandomizedItem is not null)
                            .Select(l => l.UnrandomizedItem!),
                    ]);
                    prevCheckedLocationsCount = g.CheckedLocations.Count;
                    _currentGameState.OnNext(g);
                });
        }
    }

    public IObservable<Game> CurrentGameState { get; }

    public IObservable<Unit> GameComplete { get; }

    public IObservable<Exception> UnhandledException { get; }

    public IObservable<bool> Paused { get; }

    public void TogglePause()
    {
        _paused.OnNext(!_paused.Value);
    }

    public async void RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await RunAsyncCore(cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            _unhandledException.OnNext(e);
            _unhandledException.OnCompleted();
        }
    }

    private async Task RunAsyncCore(CancellationToken cancellationToken)
    {
        ArchipelagoConnection connection = new(_settings);
        using ClientWebSocket socket = await connection.ConnectAsync(cancellationToken);

        ArchipelagoPacketProvider packets = new();

        // set up to receive the game and the context from the initialization process.
        GameInitializer gameInitializer = new(_settings, _unhandledException.AsObserver());
        Task<GameAndContext> gameAndContextTask = gameInitializer.InitializedGame.ToTask(cancellationToken);
        IDisposable unregisterGameInitializer = await packets.RegisterHandlerAsync(gameInitializer);
        Task runPacketLoopTask = packets.RunToCompletionAsync(socket, cancellationToken)
            .ContinueWith(t =>
            {
                if (t.IsFaulted)
                {
                    _unhandledException.OnNext(t.Exception.Flatten());
                    _unhandledException.OnCompleted();
                }
            }, cancellationToken, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

        GameAndContext gameAndContext = await gameAndContextTask;
        unregisterGameInitializer.Dispose();
        GameUpdatePacketHandler updatePacketHandler = new(_settings, gameAndContext.Game, gameAndContext.Context);
        using IDisposable unregisterUpdatePacketHandler = await packets.RegisterHandlerAsync(updatePacketHandler);

        using PlayLoopRunner playLoopRunner = new(gameAndContext.Game, gameAndContext.Context, packets, _settings, _timeProvider);
        Task runPlayLoopTask = playLoopRunner.RunPlayLoopAsync(Paused, cancellationToken)
            .ContinueWith(t =>
            {
                if (t.IsFaulted)
                {
                    _unhandledException.OnNext(t.Exception.Flatten());
                    _unhandledException.OnCompleted();
                }
            }, cancellationToken, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

        using IDisposable gameUpdatesSubscription = Observable
            .Merge(AvaloniaScheduler.Instance, updatePacketHandler.GameUpdates, playLoopRunner.GameUpdates)
            .Do(game =>
            {
                if (game.IsCompleted)
                {
                    _gameComplete.OnNext(Unit.Default);
                    _gameComplete.OnCompleted();
                }
            })
            .Subscribe(_currentGameState);

        await Task.WhenAll(runPacketLoopTask, runPlayLoopTask);
    }
}
