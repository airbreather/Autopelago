using System.Reactive.Disposables;

namespace Autopelago.BespokeMultiworld;

public sealed class World : IDisposable
{
    private readonly CompositeDisposable _disposables = [];

    public World(Prng.State seed)
    {
        _disposables.Add(Instrumentation = new());
        Game = new(seed, Instrumentation);
    }

    public Game Game { get; }

    public GameInstrumentation Instrumentation { get; }

    public void Dispose()
    {
        _disposables.Dispose();
    }
}
