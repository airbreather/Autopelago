using System.Collections.Concurrent;

using Xunit.Abstractions;

namespace Autopelago;

public sealed class PlayerTests
{
    private delegate TResult SpanFunc<TSource, TResult>(ReadOnlySpan<TSource> vals);

    private static readonly ItemDefinitionModel s_normalRat = GameDefinitions.Instance.Items.NormalRat;

    private static readonly LocationDefinitionModel s_startLocation = GameDefinitions.Instance.StartLocation;

    private static readonly RegionDefinitionModel s_startRegion = s_startLocation.Region;

    private static readonly LocationDefinitionModel s_basketball = GameDefinitions.Instance.LocationsByKey[LocationKey.For("basketball")];

    private static readonly RegionDefinitionModel s_beforeMinotaur = GameDefinitions.Instance.Regions.AllRegions["before_minotaur"];

    private readonly ITestOutputHelper _output;

    public PlayerTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void FirstAttemptsShouldMakeSense()
    {
        // used a seed whose first few rolls happen to be right on the pass/fail thresholds.
        ulong seed = EnsureSeedProducesInitialD20Sequence(12999128, [9, 14, 19, 10, 14]);
        Prng.State prngState = Prng.State.Start(seed);
        Game.State state = Game.State.Start(prngState);

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
        Assert.Equal(s_startLocation, state.CheckedLocations.FirstOrDefault());
        Assert.Equal(GameDefinitions.Instance.LocationsByKey[s_startLocation.Key with { N = s_startLocation.Key.N + 1 }], state.TargetLocation);

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
            ReceivedItems = [.. Enumerable.Repeat(s_normalRat, ratCount)],
            CheckedLocations = [.. s_startRegion.Locations],
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

    [Fact]
    public void ShouldHeadFurtherAfterCompletingBasketball()
    {
        Game.State state = Game.State.Start();
        state = state with
        {
            ReceivedItems = [.. Enumerable.Repeat(s_normalRat, 5)],
            CheckedLocations = [.. s_startRegion.Locations, s_basketball],
            CurrentLocation = s_basketball,
            TargetLocation = s_beforeMinotaur.Locations[0],
        };

        Player player = new();

        state = player.Advance(state);

        Assert.Equal(s_beforeMinotaur, state.CurrentLocation.Region);
    }

    private static ulong EnsureSeedProducesInitialD20Sequence(ulong seed, ReadOnlySpan<int> exactVals)
    {
        Assert.Equal(exactVals, Rolls(seed, stackalloc int[exactVals.Length]));
        return seed;
    }

    private static ReadOnlySpan<int> Rolls(ulong searchSeed, Span<int> rolls)
    {
        Prng.State state = Prng.State.Start(searchSeed);
        for (int i = 0; i < rolls.Length; i++)
        {
            rolls[i] = Prng.NextD20(ref state);
        }

        return rolls;
    }

    private ulong SearchForPrngSeed(int cnt, SpanFunc<int, bool> isMatch)
    {
        ulong? seed = null;
        Parallel.ForEach(Partitioner.Create(0, 2_000_000_000, 1_000_000), (part, state) =>
        {
            if (state.ShouldExitCurrentIteration)
            {
                return;
            }

            Span<int> rolls = stackalloc int[cnt];
            for (int i = part.Item1; i < part.Item2; i++)
            {
                if (isMatch(Rolls((ulong)i, rolls)))
                {
                    seed = (ulong)i;
                    state.Stop();
                    break;
                }
            }
        });

        if (seed is not ulong result)
        {
            throw new InvalidOperationException("The match function was so specific that it rejected all of the 2 billion random seeds that we tried. Try being a little bit more lenient, perhaps?");
        }

        _output.WriteLine($"ulong seed = {nameof(EnsureSeedProducesInitialD20Sequence)}({result}, [{string.Join(", ", Rolls(result, stackalloc int[cnt]).ToArray())}]);");
        return result;
    }
}
