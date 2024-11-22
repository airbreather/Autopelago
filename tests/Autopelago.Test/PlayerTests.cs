using System.Buffers;
using System.Buffers.Text;
using System.Collections.Frozen;
using System.Collections.Immutable;
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
            Assert.That(state.CheckedLocations.InCheckedOrder.FirstOrDefault(), Is.EqualTo(s_startLocation));
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
        GameState state = GameState.Start(s_highRolls);
        state = state with
        {
            CurrentLocation = s_startRegion.Locations[^1],
            TargetLocation = s_startRegion.Locations[^1],
            CheckedLocations = new() { InCheckedOrder = [.. s_startRegion.Locations] },
            ReceivedItems = new() { InReceivedOrder = [.. Enumerable.Repeat(s_normalRat, ratCount)] },
        };

        Player player = new();
        state = player.Advance(state);
        Assert.That(state.CheckedLocations.InCheckedOrder, ratCount < 5 ? Does.Not.Contain(s_basketball) : Contains.Item(s_basketball));
    }

    [Test]
    public void ShouldHeadFurtherAfterCompletingBasketball([Values] bool unblockAngryTurtlesFirst)
    {
        GameState state = GameState.Start(s_highRolls);
        state = state with
        {
            ReceivedItems = new() { InReceivedOrder = [.. Enumerable.Repeat(s_normalRat, 5), unblockAngryTurtlesFirst ? s_pizzaRat : s_premiumCanOfPrawnFood] },
            CheckedLocations = new() { InCheckedOrder = [.. s_startRegion.Locations] },
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

            foreach (LocationDefinitionModel newCheckedLocation in state.CheckedLocations.InCheckedOrder.Skip(prev.CheckedLocations.Count))
            {
                newReceivedItems.Add(newCheckedLocation.UnrandomizedItem!);
            }

            if (newReceivedItems.Count > 0)
            {
                state = state with { ReceivedItems = new() { InReceivedOrder = state.ReceivedItems.InReceivedOrder.AddRange(newReceivedItems) } };
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
            ReceivedItems = new()
            {
                InReceivedOrder = [..
                    GameDefinitions.Instance.LocationsByKey.Values
                        .Where(l => l is { RewardIsFixed: false, UnrandomizedItem: not null })
                        .Select(l => l.UnrandomizedItem!)
                        .Where(i => i != finalRandomizedItem),
                ],
            },
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
        state = state with { ReceivedItems = new() { InReceivedOrder = state.ReceivedItems.InReceivedOrder.Add(finalRandomizedItem) } };
        HashSet<LocationKey> fixedRewardsGranted = [];
        int advancesSoFar = 0;
        while (!state.IsCompleted)
        {
            state = player.Advance(state with { PrngState = s_highRolls });
            Assert.That(state.TargetLocation.Region, Is.InstanceOf<LandmarkRegionDefinitionModel>());
            foreach (LocationDefinitionModel checkedLocation in state.CheckedLocations.InCheckedOrder)
            {
                if (fixedRewardsGranted.Add(checkedLocation.Key) && checkedLocation is { RewardIsFixed: true, UnrandomizedItem: { } unrandomizedItem })
                {
                    state = state with { ReceivedItems = new() { InReceivedOrder = state.ReceivedItems.InReceivedOrder.Add(unrandomizedItem) } };
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
        Assert.That(state.TargetLocation.Key, Is.EqualTo(new LocationKey { RegionKey = "Menu", N = 0 }));

        // prioritize Prawn Stars
        state = state with { PriorityLocations = [prawnStars] };

        Player player = new();
        GameState scrubbedState = player.Advance(state);

        // should NOT be targeting Prawn Stars now, because we can't reach it out the gate.
        Assert.That(scrubbedState.TargetLocation, Is.Not.EqualTo(prawnStars));

        // give what's needed to reach Prawn Stars
        state = player.Advance(state with
        {
            CheckedLocations = new() { InCheckedOrder = [s_basketball] },
            ReceivedItems = new() { InReceivedOrder = [.. Enumerable.Range(0, 5).Select(_ => s_normalRat), s_premiumCanOfPrawnFood] },
        });

        // NOW that's what we should be targeting
        Assert.That(state.TargetLocation, Is.EqualTo(prawnStars));

        // teleport the rat over to Prawn Stars and have it do its thing (remember it's rolling all
        // natural 1s today).
        state = player.Advance(state with { CurrentLocation = state.TargetLocation });

        // it should still be there, and it should still be our priority location.
        Assert.That(state.PriorityLocations, Is.EqualTo(new[] { prawnStars }));

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
            PriorityLocations = [GameDefinitions.Instance.StartRegion.Locations[^1]],
        };

        state = player.Advance(state);
        LocationDefinitionModel middleLocation = state.CurrentLocation;

        state = player.Advance(state);
        if (state.CurrentLocation == state.TargetLocation)
        {
            Assert.Inconclusive("YAML was changed too much: there aren't enough locations in the starting region for this test.");
        }

        // even though it's all high rolls, we shouldn't have any checks because the rat is hard-prioritizing.
        Assert.That(state.CheckedLocations, Is.Empty);

        state = state with { StartledCounter = state.StartledCounter + 1 };

        // it used all its movement to get from middleLocation to here previously, so being startled
        // should cause it to use that same movement to get exactly back to middleLocation again.
        state = player.Advance(state);
        Assert.Multiple(() =>
        {
            Assert.That(state.StartledCounter, Is.Zero);
            Assert.That(state.CurrentLocation, Is.EqualTo(middleLocation));
        });
    }

    [Test]
    public void StartledShouldTakePriorityOverDistracted()
    {
        GameState state = GameState.Start(s_highRolls) with
        {
            CurrentLocation = GameDefinitions.Instance.StartRegion.Locations[^1],
            StartledCounter = 1,
            DistractionCounter = 2,
        };

        Player player = new();

        // first step, we're startled out of our distraction.
        state = player.Advance(state);

        LocationDefinitionModel expectedStartleTarget = GameDefinitions.Instance.StartRegion.Locations[^10];
        Assert.Multiple(() =>
        {
            Assert.That(state.StartledCounter, Is.Zero);
            Assert.That(state.DistractionCounter, Is.EqualTo(1));
            Assert.That(state.CurrentLocation, Is.EqualTo(expectedStartleTarget));
        });

        // second step, there's a new distraction that we hadn't gotten to yet.
        state = player.Advance(state);

        // distraction burns a whole step
        Assert.That(state.CurrentLocation, Is.EqualTo(expectedStartleTarget));

        // now we're fine
        state = player.Advance(state);
        Assert.That(state.CheckedLocations, Has.Count.EqualTo(2));
    }

    [Test]
    public void SmartShouldResolveToNearestReachableIfPossible([Values("smart", "conspiratorial")] string aura)
    {
        GameState state = GameState.Start() with
        {
            CurrentLocation = GameDefinitions.Instance.StartRegion.Locations[^1],
            TargetLocation = GameDefinitions.Instance.StartRegion.Locations[^1],
        };

        ArchipelagoItemFlags targetFlags = aura switch
        {
            "smart" => ArchipelagoItemFlags.LogicalAdvancement,
            "conspiratorial" => ArchipelagoItemFlags.Trap,
            _ => throw null!,
        };
        FrozenDictionary<LocationDefinitionModel, ArchipelagoItemFlags> spoilerData = CreateSpoiler(
        [
            (GameDefinitions.Instance.StartLocation, targetFlags),
            (s_beforePrawnStars.Locations[0], targetFlags),
            (s_beforePrawnStars.Locations[^1], targetFlags),
        ]);

        ItemDefinitionModel auraItem = GameDefinitions.Instance.AllItems.First(i => i.AurasGranted.Length == 1 && i.AurasGranted[0] == aura);

        // even though there's a target RIGHT on the other side, we still favor the nearest one that
        // we can already reach with what we currently have.
        Player player = new();
        state = player.ReceiveItems(state, [auraItem], spoilerData);
        Assert.That(state.PriorityPriorityLocations, Is.EqualTo(new[]
        {
            GameDefinitions.Instance.GoalLocation,
            GameDefinitions.Instance.StartLocation,
        }));

        // if there's nothing else that we can reach, then we should NOT target the unreachable one
        // that's just out of reach. it should just fizzle.
        state = player.ReceiveItems(state, [auraItem], spoilerData);
        Assert.That(state.PriorityPriorityLocations, Is.EqualTo(new[]
        {
            GameDefinitions.Instance.GoalLocation,
            GameDefinitions.Instance.StartLocation,
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
            ReceivedItems = new()
            {
                InReceivedOrder = [.. Enumerable.Repeat(s_normalRat, 5)],
            },
            PriorityLocations =
            [
                s_beforePrawnStars.Locations[1],
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
            ReceivedItems = new()
            {
                InReceivedOrder = [.. Enumerable.Repeat(s_normalRat, 5)],
            },
            CheckedLocations = new()
            {
                InCheckedOrder = [.. s_startRegion.Locations],
            },
            PrngState = s_highRolls,
            EnergyFactor = -100,
        };

        if (s_startRegion.Locations.Length != 18)
        {
            Assert.Inconclusive("This test is particularly sensitive to changes in the number of locations in the start region. Please re-evaluate.");
        }

        Player player = new();
        state = player.Advance(state);
        Assert.Multiple(() =>
        {
            Assert.That(
                state.PreviousStepMovementLog.Select(v => v.PreviousLocation),
                Is.EqualTo(s_startRegion.Locations[..6]));
            Assert.That(
                state.PreviousStepMovementLog.Select(v => v.CurrentLocation),
                Is.EqualTo(s_startRegion.Locations[1..7]));
            Assert.That(
                state.CurrentLocation,
                Is.EqualTo(state.PreviousStepMovementLog[^1].CurrentLocation));
        });
        state = player.Advance(state);
        Assert.Multiple(() =>
        {
            Assert.That(
                state.PreviousStepMovementLog.Select(v => v.PreviousLocation),
                Is.EqualTo(s_startRegion.Locations[6..9]));
            Assert.That(
                state.PreviousStepMovementLog.Select(v => v.CurrentLocation),
                Is.EqualTo(s_startRegion.Locations[7..10]));
            Assert.That(
                state.CurrentLocation,
                Is.EqualTo(state.PreviousStepMovementLog[^1].CurrentLocation));
        });
        state = player.Advance(state);
        Assert.Multiple(() =>
        {
            Assert.That(
                state.PreviousStepMovementLog.Select(v => v.PreviousLocation),
                Is.EqualTo(s_startRegion.Locations[9..15]));
            Assert.That(
                state.PreviousStepMovementLog.Select(v => v.CurrentLocation),
                Is.EqualTo(s_startRegion.Locations[10..16]));
            Assert.That(
                state.CurrentLocation,
                Is.EqualTo(state.PreviousStepMovementLog[^1].CurrentLocation));
        });
        state = player.Advance(state with { EnergyFactor = 0 });
        Assert.Multiple(() =>
        {
            ImmutableArray<LocationDefinitionModel> expectedCurrentLocationSequence =
            [
                .. s_startRegion.Locations[16..],
                s_basketball,
            ];
            Assert.That(
                state.PreviousStepMovementLog.Select(v => v.PreviousLocation),
                Is.EqualTo(s_startRegion.Locations[15..]));
            Assert.That(
                state.PreviousStepMovementLog.Select(v => v.CurrentLocation),
                Is.EqualTo(expectedCurrentLocationSequence));
            Assert.That(
                state.CurrentLocation,
                Is.EqualTo(state.PreviousStepMovementLog[^1].CurrentLocation));
            Assert.That(state.CheckedLocations.InCheckedOrder, Contains.Item(s_basketball));
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
                s_basketball,
                lastLocationBeforeBasketball,
            ],
            CheckedLocations = new()
            {
                InCheckedOrder = [lastLocationBeforeBasketball],
            },
            PrngState = s_lowRolls,
        };

        Player player = new();
        state = player.Advance(state);

        Assert.That(state.TargetLocation, Is.Not.EqualTo(lastLocationBeforeBasketball));
    }

    [Test]
    public void StartledShouldNotMoveThroughLockedLocations()
    {
        GameState state = GameState.Start() with
        {
            CurrentLocation = GameDefinitions.Instance.LocationsByName["After Pirate Bake Sale #1"],
            TargetLocation = GameDefinitions.Instance.LocationsByName["Bowling Ball Door"],
            ReceivedItems = new()
            {
                InReceivedOrder =
                [
                    .. Enumerable.Repeat(s_normalRat, 40),
                    GameDefinitions.Instance.ItemsByName["Priceless Antique"],
                    GameDefinitions.Instance.ItemsByName["Pie Rat"],
                    GameDefinitions.Instance.ItemsByName["Pizza Rat"],
                    GameDefinitions.Instance.ItemsByName["Chef Rat"],
                ],
            },
            CheckedLocations = new()
            {
                InCheckedOrder =
                [
                    s_basketball,
                    GameDefinitions.Instance.LocationsByName["Angry Turtles"],
                    GameDefinitions.Instance.LocationsByName["Restaurant"],
                    GameDefinitions.Instance.LocationsByName["Bowling Ball Door"],
                ],
            },
        };

        Player player = new();
        for (int i = 0; i < 100; i++)
        {
            state = player.Advance(state with { StartledCounter = 1 });
            Assert.That(
                state.PreviousStepMovementLog.Select(m => m.CurrentLocation),
                Has.None
                    .EqualTo(GameDefinitions.Instance.LocationsByName["Pirate Bake Sale"])
                    .Or.EqualTo(GameDefinitions.Instance.LocationsByName["Prawn Stars"]));
            if (state.CurrentLocation == s_startLocation)
            {
                break;
            }
        }

        Assert.That(state.CurrentLocation, Is.EqualTo(s_startLocation));
    }

    [Test]
    public void ReceiveItemsShouldApplyAuras()
    {
        GameState state = GameState.Start();
        Player player = new();
        state = player.ReceiveItems(state, [
            // upset_tummy, upset_tummy, upset_tummy, unlucky, startled, startled, startled, sluggish
            GameDefinitions.Instance.ItemsByName["Rat Poison"],

            // well_fed, energized, energized, energized
            GameDefinitions.Instance.ItemsByName["Bag of Powdered Sugar"],

            // confidence
            GameDefinitions.Instance.ItemsByName["Weapons-grade Folding Chair"],

            // stylish, distracted, sluggish
            GameDefinitions.Instance.ItemsByName["Itchy Iron Wool Sweater"],

            // confidence
            GameDefinitions.Instance.ItemsByName["Weapons-grade Folding Chair"],
        ]);

        Assert.Multiple(() =>
        {
            Assert.That(state.FoodFactor, Is.EqualTo(-10));
            Assert.That(state.LuckFactor, Is.EqualTo(-1));
            Assert.That(state.StartledCounter, Is.EqualTo(3));
            Assert.That(state.EnergyFactor, Is.EqualTo(10)); // 5 canceled by the first confidence!
            Assert.That(state.StyleFactor, Is.EqualTo(2));
            Assert.That(state.DistractionCounter, Is.Zero); // canceled by the first confidence!
            Assert.That(state.HasConfidence);
        });
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
