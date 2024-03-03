namespace Autopelago;

public sealed class PlayerTests
{
    [Fact]
    public void FirstAttemptsShouldMakeSense()
    {
        // used a fixed seed and a PRNG whose outputs are completely defined.
        Game.State state = Game.State.Start(seed: 1);

        Player player = new();

        // we start on the first location, so we should roll like this:
        bool firstSucceeds = Game.State.NextD20(ref state) >= 10;

        state = player.Advance(state);
    }
}
