namespace Autopelago.BespokeMultiworld;

public sealed class World
{
    public World(Prng.State seed)
    {
        Game = new(seed);
    }

    public Game Game { get; }
}
