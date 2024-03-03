using Xunit.Abstractions;

namespace Autopelago;

public sealed class PlayerTests
{
    private delegate TResult SpanFunc<TSource, TResult>(ReadOnlySpan<TSource> vals);
    private static readonly ItemDefinitionModel s_normalRat = GameDefinitions.Instance.Items.NormalRat;

    private static readonly LocationDefinitionModel s_startLocation = GameDefinitions.Instance.StartLocation;

    private static readonly RegionDefinitionModel s_startRegion = s_startLocation.Region;

    private static readonly LocationDefinitionModel s_lastLocationBeforeBasketball = GameDefinitions.Instance.Regions.AllRegions["menu"].Locations[^1];

    private static readonly LocationDefinitionModel s_basketball = GameDefinitions.Instance.LocationsByKey[LocationKey.For("basketball")];

    private static readonly RegionDefinitionModel s_beforeMinotaur = GameDefinitions.Instance.Regions.AllRegions["before_minotaur"];

    private static readonly RegionDefinitionModel s_beforePrawnStars = GameDefinitions.Instance.Regions.AllRegions["before_prawn_stars"];

    private static readonly ItemDefinitionModel s_redMatadorCape = GameDefinitions.Instance.Items.ProgressionItems["red_matador_cape"];

    private static readonly ItemDefinitionModel s_premiumCanOfPrawnFood = GameDefinitions.Instance.Items.ProgressionItems["premium_can_of_prawn_food"];

    private readonly ITestOutputHelper _output;

    public PlayerTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void FirstAttemptsShouldMakeSense()
    {
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

        // the next attempt should succeed, despite only rolling 1 higher than the first roll of the
        // previous step (and a few points lower than the subsequent rolls of that step), because of
        // the cumulative penalty that gets applied to attempts after the first.
        _ = Prng.NextD20(ref prngState);
        _ = Prng.NextD20(ref prngState);

        state = player.Advance(state);
        Assert.Equal(s_startLocation, state.CheckedLocations.FirstOrDefault());
        Assert.Equal(s_startRegion.Locations[1], state.TargetLocation);

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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ShouldHeadFurtherAfterCompletingBasketball(bool minotaur)
    {
        ulong seed = EnsureSeedProducesInitialD20Sequence(2449080649, [20, 20, 20, 20, 20, 20, 20, 20]);

        Game.State state = Game.State.Start(seed);
        state = state with
        {
            ReceivedItems = [.. Enumerable.Repeat(s_normalRat, 5), minotaur ? s_redMatadorCape : s_premiumCanOfPrawnFood],
            CheckedLocations = [.. s_startRegion.Locations.Except([s_lastLocationBeforeBasketball])],
            CurrentLocation = s_lastLocationBeforeBasketball,
            TargetLocation = s_lastLocationBeforeBasketball,
        };

        Player player = new();

        state = player.Advance(state);

        Assert.Equal(s_basketball, state.CurrentLocation);
        Assert.Equal((minotaur ? s_beforeMinotaur : s_beforePrawnStars).Locations[0], state.TargetLocation);
    }

    private static ulong EnsureSeedProducesInitialD20Sequence(ulong seed, ReadOnlySpan<int> exactVals)
    {
        Assert.Equal(exactVals, Rolls(seed, stackalloc int[exactVals.Length]));
        return seed;
    }

    private static ReadOnlySpan<int> Rolls(ulong searchSeed, Span<int> rolls)
    {
        Prng.State state = Prng.State.Start(searchSeed);
        return Rolls(ref state, rolls);
    }

    private static ReadOnlySpan<int> Rolls(scoped ref Prng.State state, Span<int> rolls)
    {
        for (int i = 0; i < rolls.Length; i++)
        {
            rolls[i] = Prng.NextD20(ref state);
        }

        return rolls;
    }

    private ulong SearchForPrngSeed(int cnt, SpanFunc<int, bool> isMatch)
    {
        // seed 2449080649: eight natural 20s in a row.
        SeedSearch box = new()
        {
            Count = cnt,
            IsMatch = isMatch,
        };
        Thread[] searchThreads = new Thread[Environment.ProcessorCount];
        foreach (ref Thread th in searchThreads.AsSpan())
        {
            th = new(Search) { IsBackground = true };
            th.Start(box);
        }

        foreach (Thread th in searchThreads)
        {
            th.Join();
        }

        ulong result = box.Seed!.Value;
        _output.WriteLine($"ulong seed = {nameof(EnsureSeedProducesInitialD20Sequence)}({result}, [{string.Join(", ", Rolls(result, stackalloc int[cnt]).ToArray())}]);");
        return result;
        static void Search(object? obj)
        {
            SeedSearch search = (SeedSearch)obj!;
            Span<int> rolls = stackalloc int[search.Count];
            while (search.Seed is null)
            {
                for ((ulong i, ulong end) = search.NextRange(); i < end; i++)
                {
                    if (search.IsMatch(Rolls(i, rolls)))
                    {
                        search.Seed = i;
                        return;
                    }
                }
            }
        }
    }

    private sealed record SeedSearch
    {
        private const ulong RangeSize = 10_000;

        private ulong _nextRangeStart;

        public required SpanFunc<int, bool> IsMatch { get; init; }

        public required int Count { get; init; }

        public ulong? Seed { get; set; }

        public (ulong Start, ulong End) NextRange()
        {
            ulong end = Interlocked.Add(ref _nextRangeStart, RangeSize);
            return (end - RangeSize, end);
        }
    }
}
