using Xunit.Sdk;

namespace Autopelago;

public sealed class PlayerTests
{
    [Fact]
    public void FirstAttemptsShouldMakeSense()
    {
        // used a fixed seed and a PRNG whose outputs are completely defined.
        const ulong Seed = 13;
        Prng.State prngState = Prng.State.Start(Seed);
        ReadOnlySpan<int> expectedRolls = [ 2, 2, 12, 12, 4, 5 ];
        for (int i = 0; i < expectedRolls.Length; i++)
        {
            if (expectedRolls[i] != Prng.NextD20(ref prngState))
            {
                throw SkipException.ForSkip("PRNG behavior has changed. time to find a different seed.");
            }
        }

        Game.State state = Game.State.Start(Seed);

        // follow along with how this should work.
        prngState = Prng.State.Start(Seed);

        Player player = new();

        // we're on the first location. we should fail three times and then yield.
        _ = Prng.NextD20(ref prngState);
        _ = Prng.NextD20(ref prngState);
        _ = Prng.NextD20(ref prngState);

        state = player.Advance(state);
        Assert.Empty(state.CheckedLocations);
        Assert.Equal(prngState, state.PrngState);

        // the next attempt should succeed, despite still rolling no higher than a 12, because it's
        // our *first* attempt of this step.
        _ = Prng.NextD20(ref prngState);
        _ = Prng.NextD20(ref prngState);

        state = player.Advance(state);
        LocationDefinitionModel startLocation = GameDefinitions.Instance.StartLocation;
        Assert.Equal(startLocation, state.CheckedLocations.FirstOrDefault());
        Assert.Equal(GameDefinitions.Instance.LocationsByKey[startLocation.Key with { N = startLocation.Key.N + 1 }], state.TargetLocation);

        // because they succeeded on their first attempt, they have just enough actions to reach and
        // then make a feeble attempt at the next location on the route
        Assert.Equal(state.TargetLocation, state.CurrentLocation);
        Assert.Single(state.CheckedLocations);
        Assert.Equal(prngState, state.PrngState);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void ShouldOnlyTryBasketballWithAtLeastFiveRats(int ratCount)
    {
        Game.State state = Game.State.Start();
        state = state with
        {
            ReceivedItems = [.. Enumerable.Repeat(GameDefinitions.Instance.Items.NormalRat, ratCount)],
            CheckedLocations = [.. GameDefinitions.Instance.StartLocation.Region.Locations],
        };

        Player player = new();
        if (ratCount < 5)
        {
            Assert.Equal(state, player.Advance(state));
        }
        else
        {
            Assert.NotEqual(state, player.Advance(state));
        }
    }
}
