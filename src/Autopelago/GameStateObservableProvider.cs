using System.Reactive.Disposables;
using System.Reactive.Subjects;

namespace Autopelago;

public sealed class GameStateObservableProvider : IAsyncDisposable
{
    private readonly CompositeDisposable _disposables = [];

    private readonly TimeProvider _timeProvider;

    public GameStateObservableProvider(Settings settings, TimeProvider timeProvider)
    {
        MySettings = settings;
        _timeProvider = timeProvider;
        CurrentGameState = new(GameState.Start());
        _disposables.Add(CurrentGameState);
    }

    public Settings MySettings { get; }

    public BehaviorSubject<GameState> CurrentGameState { get; }

    public bool Paused { get; private set; }

    public bool Pause()
    {
        if (Paused)
        {
            return false;
        }

        Paused = true;
        return true;
    }

    public bool Unpause()
    {
        if (!Paused)
        {
            return false;
        }

        Paused = false;
        return true;
    }

    public async ValueTask DisposeAsync()
    {
        await Helper.ConfigureAwaitFalse();
        _disposables.Dispose();
    }
}
