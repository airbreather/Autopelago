namespace Autopelago.BespokeMultiworld;

public sealed class World
{
    public World(Prng.State seed)
    {
        Game = new(seed, Instrumentation = new());
    }

    public Game Game { get; }

    public GameInstrumentation Instrumentation { get; }
}
