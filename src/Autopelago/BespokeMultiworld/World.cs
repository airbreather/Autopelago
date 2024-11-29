namespace Autopelago.BespokeMultiworld;

public sealed class World : IDisposable
{
    public World(Prng.State seed)
    {
        Game = new(seed, Instrumentation = new());
    }

    public void Dispose()
    {
        Instrumentation.Dispose();
    }

    public Game Game { get; }

    public GameInstrumentation Instrumentation { get; }
}
