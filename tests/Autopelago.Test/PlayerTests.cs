using System.Buffers;
using System.Buffers.Text;
using System.Collections.Frozen;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Autopelago;

[TestFixture]
public sealed class PlayerTests
{
    private delegate TResult SpanFunc<TSource, out TResult>(ReadOnlySpan<TSource> vals);

    private static readonly ItemDefinitionModel s_normalRat = GameDefinitions.Instance.PackRat;

    private static readonly LocationDefinitionModel s_startLocation = GameDefinitions.Instance.StartLocation;

    private static readonly RegionDefinitionModel s_startRegion = s_startLocation.Region;

    private static readonly LocationDefinitionModel s_basketball = GameDefinitions.Instance.LocationsByKey[LocationKey.For("basketball")];

    private static readonly RegionDefinitionModel s_beforeAngryTurtles = GameDefinitions.Instance.AllRegions["before_angry_turtles"];

    private static readonly RegionDefinitionModel s_beforePrawnStars = GameDefinitions.Instance.AllRegions["before_prawn_stars"];

    private static readonly ItemDefinitionModel s_pizzaRat = GameDefinitions.Instance.ProgressionItems["pizza_rat"];

    private static readonly ItemDefinitionModel s_premiumCanOfPrawnFood = GameDefinitions.Instance.ProgressionItems["premium_can_of_prawn_food"];

    private static readonly Prng.State s_highRolls = EnsureSeedProducesInitialD20Sequence("ZcuBXfRkZixzx/eQAL1UiHpMG3kLbaDksoajUfxCis8="u8, [20, 20, 20, 20, 20, 20, 20, 20, 20, 20, 20]);

    private static readonly Prng.State s_lowRolls = EnsureSeedProducesInitialD20Sequence("Sr8rXn/wy4+RmchoEi8DdYc99ConsS+Fj2g7IoicNns="u8, [1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1]);

    [Test]
    public void FirstAttemptsShouldMakeSense()
    {
        Prng.State seed = EnsureSeedProducesInitialD20Sequence(12999128, [9, 14, 19, 10, 14]);
        Prng.State prngState = seed;
        GameState state = GameState.Start(seed);

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
        GameState state = GameState.Start();
        state = state with
        {
            ReceivedItems = [.. Enumerable.Repeat(s_normalRat, ratCount)],
            CheckedLocations = [.. s_startRegion.Locations],
        };

        Player player = new();
        Assert.That(player.Advance(state), ratCount < 5 ? Is.EqualTo(state) : Is.Not.EqualTo(state));
    }

    [Test]
    public void ShouldHeadFurtherAfterCompletingBasketball([Values] bool unblockAngryTurtlesFirst)
    {
        GameState state = GameState.Start(s_highRolls);
        state = state with
        {
            ReceivedItems = [.. Enumerable.Repeat(s_normalRat, 5), unblockAngryTurtlesFirst ? s_pizzaRat : s_premiumCanOfPrawnFood],
            CheckedLocations = [.. s_startRegion.Locations],
            CurrentLocation = s_basketball,
            TargetLocation = s_basketball,
        };

        Player player = new();

        state = player.Advance(state);

        // because we roll so well, we can actually use our three actions to complete two checks:
        // basketball, then move, then complete that first location that we moved to.
        Assert.Multiple(() =>
        {
            Assert.That(state.CurrentLocation.Region, Is.EqualTo(s_beforePrawnStars).Or.EqualTo(s_beforeAngryTurtles));
            Assert.That(state.CurrentLocation.Key.N, Is.EqualTo(0));
            Assert.That(state.TargetLocation.Key.N, Is.EqualTo(1));
        });
    }

    [Test]
    public void GameShouldBeWinnable([Random(100, Distinct = true)] ulong seed)
    {
        GameState state = GameState.Start(seed);
        Player player = new();
        int advancesSoFar = 0;
        HashSet<LocationKey> prevCheckedLocations = [];
        List<ItemDefinitionModel> newReceivedItems = [];
        while (true)
        {
            GameState prev = state;
            state = player.Advance(state);
            Assert.That(state, Is.Not.EqualTo(prev));

            if (state.IsCompleted)
            {
                break;
            }

            foreach (LocationDefinitionModel newCheckedLocation in state.CheckedLocations)
            {
                if (prevCheckedLocations.Add(newCheckedLocation.Key))
                {
                    newReceivedItems.Add(newCheckedLocation.UnrandomizedItem!);
                }
            }

            if (newReceivedItems.Count > 0)
            {
                state = state with { ReceivedItems = [.. state.ReceivedItems, .. newReceivedItems] };
                newReceivedItems.Clear();
            }

            ++advancesSoFar;
            Assert.That(advancesSoFar, Is.LessThan(1_000_000), "If you can't win in a million steps, then you're useless.");
        }
    }

    [Test]
    public void LuckyAuraShouldForceSuccess([Values(1, 2, 3)] int effectCount)
    {
        Prng.State seed = EnsureSeedProducesInitialD20Sequence(8626806680, [1, 1, 1, 1, 1, 1, 1, 1]);
        GameState state = GameState.Start(seed);

        state = state with { LuckFactor = effectCount };
        Player player = new();

        state = player.Advance(state);
        state = player.Advance(state);
        state = player.Advance(state);
        Assert.That(state.CheckedLocations, Has.Count.EqualTo(effectCount));
    }

    [Test]
    public void UnluckyAuraShouldReduceModifier()
    {
        Prng.State seed = EnsureSeedProducesInitialD20Sequence(2242996, [14, 19, 20, 14, 15]);
        GameState state = GameState.Start(seed);

        state = state with { LuckFactor = -4 };
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
        Prng.State seed = EnsureSeedProducesInitialD20Sequence(8626806680, [1, 1, 1, 1, 1, 1, 1, 1]);
        GameState state = GameState.Start(seed);

        // with an "energy factor" of 5, you can make up to a total of 6 checks in two rounds before
        // needing to spend any actions to move, if you are lucky enough.
        state = state with
        {
            EnergyFactor = 5,
            LuckFactor = 9,
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
        Prng.State seed = EnsureSeedProducesInitialD20Sequence(13033555434, [20, 20, 1, 20, 20, 20, 20, 1]);
        GameState state = GameState.Start(seed);

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

    [Test]
    public void PositiveFoodFactorShouldGrantOneExtraAction()
    {
        GameState state = GameState.Start(s_highRolls);

        state = state with { FoodFactor = 2 };

        Player player = new();

        // 4 actions are "check, move, check, move".
        state = player.Advance(state);
        Assert.Multiple(() =>
        {
            Assert.That(state.CheckedLocations, Has.Count.EqualTo(2));
            Assert.That(state.CurrentLocation.Key.N, Is.EqualTo(2));
        });

        // 4 actions are "check, move, check, move".
        state = player.Advance(state);
        Assert.Multiple(() =>
        {
            Assert.That(state.CheckedLocations, Has.Count.EqualTo(4));
            Assert.That(state.CurrentLocation.Key.N, Is.EqualTo(4));
        });

        // 3 actions are "check, move, check".
        state = player.Advance(state);
        Assert.Multiple(() =>
        {
            Assert.That(state.CheckedLocations, Has.Count.EqualTo(6));
            Assert.That(state.CurrentLocation.Key.N, Is.EqualTo(5));
            Assert.That(state.TargetLocation.Key.N, Is.EqualTo(6));
        });

        state = state with { FoodFactor = 1 };

        // 4 actions are "move, check, move, check".
        state = player.Advance(state);
        Assert.Multiple(() =>
        {
            Assert.That(state.CheckedLocations, Has.Count.EqualTo(8));
            Assert.That(state.CurrentLocation.Key.N, Is.EqualTo(7));
            Assert.That(state.TargetLocation.Key.N, Is.EqualTo(8));
        });
    }

    [Test]
    public void NegativeFoodFactorShouldSubtractOneAction()
    {
        GameState state = GameState.Start(s_highRolls);

        state = state with { FoodFactor = -2 };

        Player player = new();

        // 2 actions are "check, move".
        state = player.Advance(state);
        Assert.Multiple(() =>
        {
            Assert.That(state.CheckedLocations, Has.Count.EqualTo(1));
            Assert.That(state.CurrentLocation.Key.N, Is.EqualTo(1));
        });

        // 2 actions are "check, move".
        state = player.Advance(state);
        Assert.Multiple(() =>
        {
            Assert.That(state.CheckedLocations, Has.Count.EqualTo(2));
            Assert.That(state.CurrentLocation.Key.N, Is.EqualTo(2));
        });

        // 3 actions are "check, move, check".
        state = player.Advance(state);
        Assert.Multiple(() =>
        {
            Assert.That(state.CheckedLocations, Has.Count.EqualTo(4));
            Assert.That(state.CurrentLocation.Key.N, Is.EqualTo(3));
            Assert.That(state.TargetLocation.Key.N, Is.EqualTo(4));
        });

        state = state with { FoodFactor = -1 };

        // 2 actions are "move, check".
        state = player.Advance(state);
        Assert.Multiple(() =>
        {
            Assert.That(state.CheckedLocations, Has.Count.EqualTo(5));
            Assert.That(state.CurrentLocation.Key.N, Is.EqualTo(4));
            Assert.That(state.TargetLocation.Key.N, Is.EqualTo(5));
        });
    }

    [Test]
    public void DistractionCounterShouldWasteEntireRound()
    {
        GameState state = GameState.Start(s_highRolls);

        state = state with
        {
            DistractionCounter = 2,

            // distraction should also burn through your food factor.
            FoodFactor = 2,
        };

        Player player = new();

        // 0 actions
        state = player.Advance(state);

        // 0 actions
        state = player.Advance(state);

        // 3 actions are "check, move, check"
        state = player.Advance(state);
        Assert.Multiple(() =>
        {
            Assert.That(state.CheckedLocations, Has.Count.EqualTo(2));
            Assert.That(state.CurrentLocation.Key.N, Is.EqualTo(1));
            Assert.That(state.TargetLocation.Key.N, Is.EqualTo(2));
        });
    }

    [Test]
    public void StyleFactorShouldImproveModifier()
    {
        Prng.State seed = EnsureSeedProducesInitialD20Sequence(80387, [5, 10]);
        GameState state = GameState.Start(seed);

        state = state with { StyleFactor = 2 };

        Player player = new();

        // 3 actions are "check, move, check".
        state = player.Advance(state);
        Assert.Multiple(() =>
        {
            Assert.That(state.CheckedLocations, Has.Count.EqualTo(2));
            Assert.That(state.CurrentLocation.Key.N, Is.EqualTo(1));
            Assert.That(state.TargetLocation.Key.N, Is.EqualTo(2));
        });
    }

    [Test]
    public void TestGoMode()
    {
        GameState state = GameState.Start();

        // give it all randomized items except the last one.
        ItemDefinitionModel finalRandomizedItem = GameDefinitions.Instance.ProgressionItems["mongoose_in_a_combat_spacecraft"];
        state = state with
        {
            ReceivedItems = [..
                GameDefinitions.Instance.LocationsByKey.Values
                    .Where(l => l is { RewardIsFixed: false, UnrandomizedItem: not null })
                    .Select(l => l.UnrandomizedItem!)
                    .Where(i => i != finalRandomizedItem),
            ],
        };
        Player player = new();

        // make a couple of steps where we have all items except the very last one. this is SOMEWHAT
        // YAML-dependent, but seriously, if you advance 2 times with rolls forced to be natural 1,
        // and that somehow brings you out of the starting region, then that's a BIG change.
        for (int i = 0; i < 2; i++)
        {
            state = player.Advance(state with { PrngState = s_lowRolls });
            Assert.That(state.TargetLocation.Key.RegionKey, Is.EqualTo(GameDefinitions.Instance.StartRegion.Key));
        }

        // now give it that last randomized item and see it shoot for the moon all the way through.
        state = state with { ReceivedItems = state.ReceivedItems.Add(finalRandomizedItem) };
        HashSet<LocationKey> fixedRewardsGranted = [];
        int advancesSoFar = 0;
        while (!state.IsCompleted)
        {
            state = player.Advance(state with { PrngState = s_highRolls });
            Assert.That(state.TargetLocation.Region, Is.InstanceOf<LandmarkRegionDefinitionModel>());
            foreach (LocationDefinitionModel checkedLocation in state.CheckedLocations)
            {
                if (fixedRewardsGranted.Add(checkedLocation.Key) && checkedLocation is { RewardIsFixed: true, UnrandomizedItem: { } unrandomizedItem })
                {
                    state = state with { ReceivedItems = state.ReceivedItems.Add(unrandomizedItem) };
                }
            }

            ++advancesSoFar;
            Assert.That(advancesSoFar, Is.LessThan(1_000_000), "If you can't win in a million steps, then you're useless.");
        }
    }

    [Test]
    public void PriorityLocationsShouldShiftTarget()
    {
        GameState state = GameState.Start(s_lowRolls);

        LocationDefinitionModel prawnStars = GameDefinitions.Instance.LocationsByName["Prawn Stars"];
        PriorityLocationModel prawnStarsPriority = new()
        {
            Location = prawnStars,
            Source = PriorityLocationModel.SourceKind.Player,
        };

        Assert.That(state.TargetLocation.Key, Is.EqualTo(new LocationKey { RegionKey = "Menu", N = 0 }));

        // prioritize Prawn Stars
        state = state with { PriorityLocations = [prawnStarsPriority] };

        Player player = new();
        GameState scrubbedState = player.Advance(state);

        // should NOT be targeting Prawn Stars now, because we can't reach it out the gate.
        Assert.That(scrubbedState.TargetLocation, Is.Not.EqualTo(prawnStars));

        // give what's needed to reach Prawn Stars
        state = player.Advance(state with
        {
            CheckedLocations = [s_basketball],
            ReceivedItems = [.. Enumerable.Range(0, 5).Select(_ => s_normalRat), s_premiumCanOfPrawnFood],
        });

        // NOW that's what we should be targeting
        Assert.That(state.TargetLocation, Is.EqualTo(prawnStars));

        // teleport the rat over to Prawn Stars and have it do its thing (remember it's rolling all
        // natural 1s today).
        state = player.Advance(state with { CurrentLocation = state.TargetLocation });

        // it should still be there, and it should still be our priority location.
        Assert.That(state.PriorityLocations, Is.EqualTo(new[] { prawnStarsPriority }));

        // now roll natural 20s.
        state = player.Advance(state with { PrngState = s_highRolls });

        Assert.That(state.PriorityLocations, Is.Empty);
    }

    [Test]
    public void StartledShouldMovePlayerTowardsStart()
    {
        GameState state = GameState.Start(s_highRolls);
        Player player = new();

        // force the first steps to move it towards the last reachable location in this region
        state = state with
        {
            PriorityLocations = state.PriorityLocations.Add(new()
            {
                Location = GameDefinitions.Instance.StartRegion.Locations[^1],
                Source = PriorityLocationModel.SourceKind.Player,
            }),
        };

        state = player.Advance(state);
        state = player.Advance(state);
        if (state.CurrentLocation == state.TargetLocation)
        {
            Assert.Inconclusive("YAML was changed too much: there aren't enough locations in the starting region for this test.");
        }

        // even though it's all high rolls, we shouldn't have any checks because the rat is hard-prioritizing.
        int stepsAwayBeforeStartle = state.CurrentLocation.Key.N;
        Assert.Multiple(() =>
        {
            Assert.That(state.CheckedLocations, Is.Empty);
            Assert.That(stepsAwayBeforeStartle, Is.GreaterThanOrEqualTo(3));
        });

        state = state.AddStartled(3);
        Assert.That(state.StartledCounter, Is.EqualTo(3));

        // those 3 startled steps should get it to go 3 steps towards the start.
        state = player.Advance(state);
        Assert.Multiple(() =>
        {
            Assert.That(state.StartledCounter, Is.Zero);
            Assert.That(state.CurrentLocation.Key.N, Is.EqualTo(stepsAwayBeforeStartle - 3));
            Assert.That(state.PriorityLocations, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void SmartShouldResolveToNearestReachableIfPossible([Values(PriorityLocationModel.SourceKind.Smart, PriorityLocationModel.SourceKind.Conspiratorial)] PriorityLocationModel.SourceKind smartOrConspiratorial)
    {
        GameState state = GameState.Start() with
        {
            CurrentLocation = GameDefinitions.Instance.StartRegion.Locations[^1],
            TargetLocation = GameDefinitions.Instance.StartRegion.Locations[^1],
        };

        ArchipelagoItemFlags targetFlags = smartOrConspiratorial switch
        {
            PriorityLocationModel.SourceKind.Smart => ArchipelagoItemFlags.LogicalAdvancement,
            PriorityLocationModel.SourceKind.Conspiratorial => ArchipelagoItemFlags.Trap,
            _ => throw null!,
        };
        FrozenDictionary<LocationDefinitionModel, ArchipelagoItemFlags> spoilerData = CreateSpoiler(
        [
            (GameDefinitions.Instance.StartLocation, targetFlags),
            (s_beforePrawnStars.Locations[0], targetFlags),
            (s_beforePrawnStars.Locations[^1], targetFlags),
        ]);

        // even though there's a target RIGHT on the other side, we still favor the nearest one that
        // we can already reach with what we currently have.
        state = state.ResolveSmartAndConspiratorialAuras([smartOrConspiratorial], spoilerData, out _);
        Assert.That(state.PriorityLocations, Is.EqualTo(new[]
        {
            new PriorityLocationModel
            {
                Source = smartOrConspiratorial,
                Location = GameDefinitions.Instance.StartLocation,
            },
        }));

        // but if there's nothing else that we can reach, then we should still be able to get the
        // nearest thing that we can't.
        state = state.ResolveSmartAndConspiratorialAuras([smartOrConspiratorial], spoilerData, out _);
        Assert.That(state.PriorityLocations, Is.EqualTo(new[]
        {
            new PriorityLocationModel
            {
                Source = smartOrConspiratorial,
                Location = GameDefinitions.Instance.StartLocation,
            },
            new PriorityLocationModel
            {
                Source = smartOrConspiratorial,
                Location = s_beforePrawnStars.Locations[0],
            },
        }));

        // finally, if we get another call without there being ANY other targets in the spoiler data
        // that we can go to, it shouldn't *fail*, per se. it should just fizzle out.
        state = state.ResolveSmartAndConspiratorialAuras([smartOrConspiratorial, smartOrConspiratorial], spoilerData, out _);
        Assert.That(state.PriorityLocations, Is.EqualTo(new[]
        {
            new PriorityLocationModel
            {
                Source = smartOrConspiratorial,
                Location = GameDefinitions.Instance.StartLocation,
            },
            new PriorityLocationModel
            {
                Source = smartOrConspiratorial,
                Location = s_beforePrawnStars.Locations[0],
            },
            new PriorityLocationModel
            {
                Source = smartOrConspiratorial,
                Location = s_beforePrawnStars.Locations[^1],
            },
        }));
    }

    [Test]
    [Property("Regression", 45)]
    public void PriorityLocationsPastClearableLandmarksShouldBlockThePlayer()
    {
        GameState state = GameState.Start() with
        {
            CurrentLocation = GameDefinitions.Instance.StartRegion.Locations[^1],
            TargetLocation = GameDefinitions.Instance.StartRegion.Locations[^1],
            ReceivedItems =
            [
                .. Enumerable.Repeat(s_normalRat, 5),
            ],
            PriorityLocations =
            [
                new()
                {
                    Location = s_beforePrawnStars.Locations[1],
                    Source = PriorityLocationModel.SourceKind.Player,
                },
            ],
            PrngState = s_lowRolls,
        };

        Player player = new();
        state = player.Advance(state) with { PrngState = s_lowRolls };
        state = player.Advance(state) with { PrngState = s_lowRolls };
        state = player.Advance(state) with { PrngState = s_lowRolls };

        Assert.That(state.CurrentLocation, Is.EqualTo(s_basketball));
    }

    [Test]
    public void LongMovesShouldBeAccelerated()
    {
        GameState state = GameState.Start() with
        {
            CurrentLocation = GameDefinitions.Instance.StartLocation,
            TargetLocation = s_basketball,
            ReceivedItems =
            [
                .. Enumerable.Repeat(s_normalRat, 5),
            ],
            CheckedLocations =
            [
                .. s_startRegion.Locations,
            ],
            PrngState = s_highRolls,
        };

        if (s_startRegion.Locations.Length != 31)
        {
            Assert.Inconclusive("This test is particularly sensitive to changes in the number of locations in the start region. Please re-evaluate.");
        }

        Player player = new();
        state = player.Advance(state);
        Assert.That(state.CurrentLocation.Key.N, Is.EqualTo(15));
        state = player.Advance(state);
        Assert.That(state.CurrentLocation.Key.N, Is.EqualTo(30));
        state = player.Advance(state);
        Assert.Multiple(() =>
        {
            Assert.That(GameDefinitions.Instance.ConnectedLocations[state.CurrentLocation], Contains.Item(s_basketball));
            Assert.That(state.CheckedLocations, Does.Not.Contain(state.CurrentLocation));
        });
    }

    [Test]
    [Property("Regression", 53)]
    public void PriorityLocationChecksShouldBypassUnreachableLocations()
    {
        LocationDefinitionModel lastLocationBeforeBasketball = GameDefinitions.Instance.StartRegion.Locations[^1];
        GameState state = GameState.Start() with
        {
            CurrentLocation = lastLocationBeforeBasketball,
            TargetLocation = lastLocationBeforeBasketball,
            PriorityLocations =
            [
                new()
                {
                    Location = s_basketball,
                    Source = PriorityLocationModel.SourceKind.Smart,
                },
                new()
                {
                    Location = lastLocationBeforeBasketball,
                    Source = PriorityLocationModel.SourceKind.Conspiratorial,
                },
            ],
            CheckedLocations =
            [
                lastLocationBeforeBasketball,
            ],
            PrngState = s_lowRolls,
        };

        Player player = new();
        state = player.Advance(state);

        Assert.That(state.TargetLocation, Is.Not.EqualTo(lastLocationBeforeBasketball));
    }

    private static FrozenDictionary<LocationDefinitionModel, ArchipelagoItemFlags> CreateSpoiler(ReadOnlySpan<(LocationDefinitionModel Location, ArchipelagoItemFlags Flags)> defined)
    {
        Dictionary<LocationDefinitionModel, ArchipelagoItemFlags> result = GameDefinitions.Instance.LocationsByName.Values.ToDictionary(l => l, _ => ArchipelagoItemFlags.None);
        foreach ((LocationDefinitionModel location, ArchipelagoItemFlags flags) in defined)
        {
            result[location] = flags;
        }

        return result.ToFrozenDictionary();
    }

    private static Prng.State EnsureSeedProducesInitialD20Sequence(ulong seed, ReadOnlySpan<int> exactVals)
    {
        int[] actual = [.. Rolls(seed, stackalloc int[exactVals.Length])];
        int[] expected = [.. exactVals];
        Assume.That(actual, Is.EqualTo(expected));
        return Prng.State.Start(seed);
    }

    private static Prng.State EnsureSeedProducesInitialD20Sequence(ReadOnlySpan<byte> seed, ReadOnlySpan<int> exactVals)
    {
        Assume.That(seed.Length, Is.EqualTo(Base64.GetMaxEncodedToUtf8Length(Unsafe.SizeOf<Prng.State>())));
        Assume.That(Base64.IsValid(seed));
        Prng.State initialState = default;
        OperationStatus decodeStatus = Base64.DecodeFromUtf8(seed, MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref initialState, 1)), out int bytesConsumed, out int bytesWritten);
        Assume.That(decodeStatus, Is.EqualTo(OperationStatus.Done));
        Assume.That(bytesConsumed, Is.EqualTo(seed.Length));
        Assume.That(bytesWritten, Is.EqualTo(Unsafe.SizeOf<Prng.State>()));

        Prng.State state = initialState;
        int[] actual = [.. Rolls(ref state, stackalloc int[exactVals.Length])];
        int[] expected = [.. exactVals];
        Assume.That(actual, Is.EqualTo(expected));
        return initialState;
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

    // ReSharper disable once UnusedMember.Local
    private ulong SearchForPrngSeed(int cnt, SpanFunc<int, bool> isMatch)
    {
        // seed 8070450564757044629: nine natural 20s in a row.
        // seed 4611686022723908115: nine natural 1s in a row.
        //
        // requiring numeric seeds harms throughput, so for greater strings of luck, I've needed to
        // write the search function to use the full state.
        // seed "ZcuBXfRkZixzx/eQAL1UiHpMG3kLbaDksoajUfxCis8=": eleven natural 20s in a row.
        // seed "Sr8rXn/wy4+RmchoEi8DdYc99ConsS+Fj2g7IoicNns=": eleven natural 1s in a row.
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
        TestContext.Out.WriteLine($"ulong seed = {nameof(EnsureSeedProducesInitialD20Sequence)}({result}, [{string.Join(", ", Rolls(result, stackalloc int[cnt]).ToArray())}]);");
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

        private ulong _nextRangeStart = 1;

        private ulong _seed;

        public required SpanFunc<int, bool> IsMatch { get; init; }

        public required int Count { get; init; }

        public ulong? Seed
        {
            get => _seed == 0 ? null : _seed;
            set => _seed = value.GetValueOrDefault();
        }

        public (ulong Start, ulong End) NextRange()
        {
            ulong end = Interlocked.Add(ref _nextRangeStart, RangeSize);
            return (end - RangeSize, end);
        }
    }
}
