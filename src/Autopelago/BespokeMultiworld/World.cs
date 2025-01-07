namespace Autopelago.BespokeMultiworld;

public sealed class World : IDisposable
{
    public World(Prng.State seed, LocationKey victoryLocation)
    {
        Game = new(seed, Instrumentation = new());
        Game.InitializeVictoryLocation(victoryLocation);
    }

    public void Dispose()
    {
        Game.Dispose();
        Instrumentation.Dispose();
    }

    public Game Game { get; }

    public GameInstrumentation Instrumentation { get; }
}
