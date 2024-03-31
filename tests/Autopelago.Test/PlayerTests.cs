namespace Autopelago;

[TestFixture]
public sealed class PlayerTests
{
    private delegate TResult SpanFunc<TSource, TResult>(ReadOnlySpan<TSource> vals);
    private static readonly ItemDefinitionModel s_normalRat = GameDefinitions.Instance.NormalRat;

    private static readonly LocationDefinitionModel s_startLocation = GameDefinitions.Instance.StartLocation;

    private static readonly RegionDefinitionModel s_startRegion = s_startLocation.Region;

    private static readonly LocationDefinitionModel s_lastLocationBeforeBasketball = GameDefinitions.Instance.StartRegion.Locations[^1];

    private static readonly LocationDefinitionModel s_basketball = GameDefinitions.Instance.LocationsByKey[LocationKey.For("basketball")];

    private static readonly RegionDefinitionModel s_beforeMinotaur = GameDefinitions.Instance.AllRegions["before_minotaur"];

    private static readonly RegionDefinitionModel s_beforePrawnStars = GameDefinitions.Instance.AllRegions["before_prawn_stars"];

    private static readonly ItemDefinitionModel s_redMatadorCape = GameDefinitions.Instance.ProgressionItems["red_matador_cape"];

    private static readonly ItemDefinitionModel s_premiumCanOfPrawnFood = GameDefinitions.Instance.ProgressionItems["premium_can_of_prawn_food"];

    [Test]
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
        Assert.Multiple(() =>
        {
            Assert.That(state.CheckedLocations, Is.Empty);
            Assert.That(state.PrngState, Is.EqualTo(prngState));
        });

        // the next attempt should succeed, despite only rolling 1 higher than the first roll of the
        // previous step (and a few points lower than the subsequent rolls of that step), because of
        // the cumulative penalty that gets applied to attempts after the first.
        _ = Prng.NextD20(ref prngState);
        _ = Prng.NextD20(ref prngState);

        state = player.Advance(state);
        Assert.Multiple(() =>
        {
            Assert.That(state.CheckedLocations.FirstOrDefault(), Is.EqualTo(s_startLocation));
            Assert.That(state.TargetLocation, Is.EqualTo(s_startRegion.Locations[1]));

            // because they succeeded on their first attempt, they have just enough actions to reach and
            // then make a feeble attempt at the next location on the route
            Assert.That(state.CurrentLocation, Is.EqualTo(state.TargetLocation));
            Assert.That(state.CheckedLocations, Has.Count.EqualTo(1));
            Assert.That(state.PrngState, Is.EqualTo(prngState));
        });
    }

    [Test]
    public void ShouldOnlyTryBasketballWithAtLeastFiveRats([Range(0, 7)] int ratCount)
    {
        Game.State state = Game.State.Start();
        state = state with
        {
            ReceivedItems = [.. Enumerable.Repeat(s_normalRat, ratCount)],
            CheckedLocations = [.. s_startRegion.Locations],
        };

        Player player = new();
        Assert.That(player.Advance(state), ratCount < 5 ? Is.EqualTo(state) : Is.Not.EqualTo(state));
    }

    [Test]
    public void ShouldHeadFurtherAfterCompletingBasketball([Values(true, false)] bool unblockMinotaurFirst)
    {
        ulong seed = EnsureSeedProducesInitialD20Sequence(2449080649, [20, 20, 20, 20, 20, 20, 20, 20]);

        Game.State state = Game.State.Start(seed);
        state = state with
        {
            ReceivedItems = [.. Enumerable.Repeat(s_normalRat, 5), unblockMinotaurFirst ? s_redMatadorCape : s_premiumCanOfPrawnFood],
            CheckedLocations = [.. s_startRegion.Locations],
            CurrentLocation = s_basketball,
            TargetLocation = s_basketball,
        };

        Player player = new();

        state = player.Advance(state);

        // it's entirely counterintuitive, but correct according to the current logic: if we're at a
        // branching path, and we can go further down branch A than branch B before getting BK'd,
        // then we actually prefer to take branch B first so that we won't have as far to backtrack
        // if nothing else comes in for us.
        RegionDefinitionModel expectedRegion = unblockMinotaurFirst ? s_beforePrawnStars : s_beforeMinotaur;

        // because we roll so well, we can actually use our three actions to complete two checks:
        // basketball, then move, then complete that first location that we moved to.
        Assert.Multiple(() =>
        {
            Assert.That(state.CurrentLocation, Is.EqualTo(expectedRegion.Locations[0]));
            Assert.That(state.TargetLocation, Is.EqualTo(expectedRegion.Locations[1]));
        });
    }

    [Test]
    public void GameShouldBeWinnable([Random(100, Distinct = true)] ulong seed)
    {
        Game.State state = Game.State.Start(seed);
        Player player = new();
        ulong steps = 0;
        while (!state.IsCompleted)
        {
            ++steps;
            Game.State prev = state;
            state = player.Advance(state);
            Assert.That(state, Is.Not.EqualTo(prev));

            state = state with { ReceivedItems = [.. state.ReceivedItems, .. state.CheckedLocations.Except(prev.CheckedLocations).Select(loc => loc.UnrandomizedItem)] };
        }
    }

    [Test]
    public void LuckyAuraShouldForceSuccess([Values(1, 2, 3)] int effectCount)
    {
        ulong seed = EnsureSeedProducesInitialD20Sequence(8626806680, [1, 1, 1, 1, 1, 1, 1, 1]);
        Game.State state = Game.State.Start(seed);

        state = state with { ActiveAuraEffects = [.. Enumerable.Repeat(LuckyEffect.Instance, effectCount)] };
        Player player = new();

        state = player.Advance(state);
        state = player.Advance(state);
        state = player.Advance(state);
        Assert.That(state.CheckedLocations, Has.Count.EqualTo(effectCount));
    }

    [Test]
    public void UnluckyAuraShouldReduceModifier()
    {
        ulong seed = EnsureSeedProducesInitialD20Sequence(2242996, [14, 19, 20, 14, 15]);
        Game.State state = Game.State.Start(seed);

        state = state with { ActiveAuraEffects = [.. Enumerable.Repeat(UnluckyEffect.Instance, 4)] };
        Player player = new();

        // normally, a 14 as your first roll should pass, but with Unlucky it's not enough. the 19
        // also fails because -5 from the aura and -5 from the second attempt. even a natural 20
        // can't save you from a -15, so this first Advance call should utterly fail.
        state = player.Advance(state);
        Assert.That(state.CheckedLocations, Is.Empty);

        // the 14 burns the final Unlucky buff, so following it up with a 15 overcomes the mere -5
        // from trying a second time on the same Advance call.
        state = player.Advance(state);
        Assert.That(state.CheckedLocations, Has.Count.EqualTo(1));
    }

    [Test]
    public void PositiveEnergyFactorShouldGiveFreeMovement()
    {
        ulong seed = EnsureSeedProducesInitialD20Sequence(8626806680, [1, 1, 1, 1, 1, 1, 1, 1]);
        Game.State state = Game.State.Start(seed);

        // with an "energy factor" of 5, you can make up to a total of 6 checks in two rounds before
        // needing to spend any actions to move, if you are lucky enough.
        state = state with
        {
            EnergyFactor = 5,
            ActiveAuraEffects = [.. Enumerable.Repeat(LuckyEffect.Instance, 9) ],
        };

        Player player = new();

        state = player.Advance(state);
        Assert.That(state.CheckedLocations, Has.Count.EqualTo(3));

        state = player.Advance(state);
        Assert.That(state.CheckedLocations, Has.Count.EqualTo(6));

        // the energy factor wears off after that, though. in fact, the next round, there's only
        // enough actions to do "move, check, move".
        state = player.Advance(state);
        Assert.That(state.CheckedLocations, Has.Count.EqualTo(7));

        // one more round: "check, move, check"
        state = player.Advance(state);
        Assert.That(state.CheckedLocations, Has.Count.EqualTo(9));
    }

    [Test]
    public void NegativeEnergyFactorShouldEncumberMovement()
    {
        ulong seed = EnsureSeedProducesInitialD20Sequence(13033555434, [20, 20, 1, 20, 20, 20, 20, 1]);
        Game.State state = Game.State.Start(seed);

        state = state with { EnergyFactor = -3 };

        Player player = new();

        // 3 actions are "check, move, (movement penalty)".
        state = player.Advance(state);
        Assert.That(state.CheckedLocations, Has.Count.EqualTo(1));

        // 3 actions are "check, move, (movement penalty)" again.
        state = player.Advance(state);
        Assert.That(state.CheckedLocations, Has.Count.EqualTo(2));

        // 3 actions are "fail, check, move".
        state = player.Advance(state);
        Assert.That(state.CheckedLocations, Has.Count.EqualTo(3));

        // 3 actions are "(movement penalty), check, move".
        state = player.Advance(state);
        Assert.That(state.CheckedLocations, Has.Count.EqualTo(4));

        // 3 actions are "check, move, check".
        state = player.Advance(state);
        Assert.That(state.CheckedLocations, Has.Count.EqualTo(6));
    }

    private static ulong EnsureSeedProducesInitialD20Sequence(ulong seed, ReadOnlySpan<int> exactVals)
    {
        int[] actual = [.. Rolls(seed, stackalloc int[exactVals.Length])];
        int[] expected = [.. exactVals];
        Assume.That(actual, Is.EqualTo(expected));
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
        // seed 8626806680: eight natural 1s in a row.
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
        TestContext.WriteLine($"ulong seed = {nameof(EnsureSeedProducesInitialD20Sequence)}({result}, [{string.Join(", ", Rolls(result, stackalloc int[cnt]).ToArray())}]);");
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
