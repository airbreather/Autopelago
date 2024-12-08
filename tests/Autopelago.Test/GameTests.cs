using System.Buffers;
using System.Buffers.Text;
using System.Collections.Frozen;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Autopelago;

[TestFixture]
public sealed class GameTests
{
    private delegate TResult SpanFunc<TSource, out TResult>(ReadOnlySpan<TSource> vals);

    private static readonly ItemDefinitionModel s_normalRat = GameDefinitions.Instance.PackRat;

    private static readonly LocationDefinitionModel s_startLocation = GameDefinitions.Instance.StartLocation;

    private static readonly RegionDefinitionModel s_startRegion = s_startLocation.Region;

    private static readonly LocationDefinitionModel s_basketball = GameDefinitions.Instance.LocationsByKey[LocationKey.For("basketball")];

    private static readonly RegionDefinitionModel s_beforeAngryTurtles = GameDefinitions.Instance.AllRegions["before_angry_turtles"];

    private static readonly RegionDefinitionModel s_beforePrawnStars = GameDefinitions.Instance.AllRegions["before_prawn_stars"];

    private static readonly ItemDefinitionModel s_pizzaRat = GameDefinitions.Instance.ProgressionItemsByItemKey["pizza_rat"];

    private static readonly ItemDefinitionModel s_premiumCanOfPrawnFood = GameDefinitions.Instance.ProgressionItemsByItemKey["premium_can_of_prawn_food"];

    private static readonly Prng.State s_highRolls = EnsureSeedProducesInitialD20Sequence("ZcuBXfRkZixzx/eQAL1UiHpMG3kLbaDksoajUfxCis8="u8, [20, 20, 20, 20, 20, 20, 20, 20, 20, 20, 20]);

    private static readonly Prng.State s_lowRolls = EnsureSeedProducesInitialD20Sequence("Sr8rXn/wy4+RmchoEi8DdYc99ConsS+Fj2g7IoicNns="u8, [1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1]);

    private static readonly FrozenDictionary<string, ItemDefinitionModel> s_singleAuraItems =
        new[] { "well_fed", "upset_tummy", "lucky", "unlucky", "energized", "sluggish", "distracted", "stylish", "startled", "smart", "conspiratorial", "confident" }
            .Select(aura => GameDefinitions.Instance.AllItems.First(i => i.AurasGranted.SequenceEqual([aura])))
            .ToFrozenDictionary(i => i.AurasGranted[0]);

    [Test]
    public void FirstAttemptsShouldMakeSense()
    {
        Prng.State seed = EnsureSeedProducesInitialD20Sequence(56061, [8, 13, 18, 9, 13]);
        Prng.State prngState = seed;

        Game game = new(seed);

        // we're on the first location. we should fail three times and then yield.
        _ = Prng.NextD20(ref prngState);
        _ = Prng.NextD20(ref prngState);
        _ = Prng.NextD20(ref prngState);

        game.Advance();
        Assert.Multiple(() =>
        {
            Assert.That(game.CheckedLocations, Is.Empty);
            Assert.That(game.PrngState, Is.EqualTo(prngState));
            Assert.That(game.MercyModifier, Is.EqualTo(1));
        });

        // the next attempt should succeed despite rolling the same as the previous step because the
        // cumulative penalty has worn off and the mercy modifier adds +1.
        _ = Prng.NextD20(ref prngState);
        _ = Prng.NextD20(ref prngState);

        game.Advance();
        Assert.Multiple(() =>
        {
            Assert.That(game.CheckedLocations.Order.FirstOrDefault(), Is.EqualTo(s_startLocation));
            Assert.That(game.TargetLocation, Is.EqualTo(s_startRegion.Locations[1]));

            // because they succeeded on their first attempt, they have just enough actions to reach and
            // then make a feeble attempt at the next location on the route
            Assert.That(game.CurrentLocation, Is.EqualTo(game.TargetLocation));
            Assert.That(game.CheckedLocations, Has.Count.EqualTo(1));
            Assert.That(game.PrngState, Is.EqualTo(prngState));
        });
    }

    [Test]
    public void ShouldOnlyTryBasketballWithAtLeastFiveRats([Range(0, 7)] int ratCount)
    {
        Game game = new(s_highRolls);
        game.InitializeCheckedLocations(s_startRegion.Locations);
        game.InitializeReceivedItems(Enumerable.Repeat(s_normalRat, ratCount));
        game.ArbitrarilyModifyState(g => g.CurrentLocation, s_startRegion.Locations[^1]);
        game.ArbitrarilyModifyState(g => g.TargetLocation, s_startRegion.Locations[^1]);
        game.Advance();
        Assert.That(game.CheckedLocations.Order, ratCount < 5 ? Does.Not.Contain(s_basketball) : Contains.Item(s_basketball));
    }

    [Test]
    public void ShouldHeadFurtherAfterCompletingBasketball([Values] bool unblockAngryTurtlesFirst)
    {
        Game game = new(s_highRolls);
        game.InitializeReceivedItems([.. Enumerable.Repeat(s_normalRat, 5), unblockAngryTurtlesFirst ? s_pizzaRat : s_premiumCanOfPrawnFood]);
        game.InitializeCheckedLocations(s_startRegion.Locations);
        game.ArbitrarilyModifyState(g => g.CurrentLocation, s_basketball);
        game.ArbitrarilyModifyState(g => g.TargetLocation, s_basketball);

        game.Advance();

        // because we roll so well, we can actually use our three actions to complete two checks:
        // basketball, then move, then complete that first location that we moved to.
        Assert.Multiple(() =>
        {
            Assert.That(game.CurrentLocation.Region, Is.EqualTo(s_beforePrawnStars).Or.EqualTo(s_beforeAngryTurtles));
            Assert.That(game.CurrentLocation.Key.N, Is.EqualTo(0));
            Assert.That(game.TargetLocation.Key.N, Is.EqualTo(1));
        });
    }

    [Test]
    public void GameShouldBeWinnable([Random(100, Distinct = true)] ulong seed)
    {
        Game game = new(Prng.State.Start(seed));
        int advancesSoFar = 0;
        List<ItemDefinitionModel> newReceivedItems = [];
        while (true)
        {
            int prevCheckedLocationsCount = game.CheckedLocations.Count;
            game.Advance();

            if (game.IsCompleted)
            {
                break;
            }

            foreach (LocationDefinitionModel newCheckedLocation in game.CheckedLocations.Order.Skip(prevCheckedLocationsCount))
            {
                newReceivedItems.Add(newCheckedLocation.UnrandomizedItem!);
            }

            if (newReceivedItems.Count > 0)
            {
                game.ReceiveItems([.. newReceivedItems]);
                newReceivedItems.Clear();
            }

            ++advancesSoFar;
            Assert.That(advancesSoFar, Is.LessThan(40_000), "If you can't win in 40k steps, then you're useless.");
        }
    }

    [Test]
    public void LuckyAuraShouldForceSuccess([Values(1, 2, 3)] int effectCount)
    {
        Game game = new(s_lowRolls);
        game.ReceiveItems([.. Enumerable.Repeat(s_singleAuraItems["lucky"], effectCount)]);
        game.Advance();
        game.Advance();
        game.Advance();
        Assert.That(game.CheckedLocations, Has.Count.EqualTo(effectCount));
    }

    [Test]
    public void UnluckyAuraShouldReduceModifier()
    {
        Prng.State seed = EnsureSeedProducesInitialD20Sequence(1070077, [13, 18, 20, 12, 13]);
        Game game = new(seed);
        game.ReceiveItems([.. Enumerable.Repeat(s_singleAuraItems["unlucky"], 4)]);

        // normally, a 13 as your first roll should pass, but with Unlucky it's not enough. the 18
        // also fails because -5 from the aura and -5 from the second attempt. even a natural 20
        // can't save you from a -15, so this first Advance call should utterly fail.
        game.Advance();
        Assert.Multiple(() =>
        {
            Assert.That(game.CheckedLocations, Is.Empty);

            // remember, after the first roll fails on a turn and no subsequent rolls pass during
            // that same turn, then the next turn's rolls get +1.
            Assert.That(game.MercyModifier, Is.EqualTo(1));
        });

        // the 12+1 burns the final Unlucky buff, so following it up with 13+1 overcomes the mere -5
        // from trying a second time on the same Advance call.
        game.Advance();
        Assert.Multiple(() =>
        {
            Assert.That(game.CheckedLocations, Has.Count.EqualTo(1));

            // our first roll failed, but then a roll passed, so this modifier should be reset.
            Assert.That(game.MercyModifier, Is.Zero);
        });
    }

    [Test]
    public void PositiveEnergyFactorShouldGiveFreeMovement()
    {
        // with an "energy factor" of 5, you can make up to a total of 6 checks in two rounds before
        // needing to spend any actions to move, if you are lucky enough.
        Game game = new(s_lowRolls);
        game.ReceiveItems([
            s_singleAuraItems["energized"],
            .. Enumerable.Repeat(s_singleAuraItems["lucky"], 9),
        ]);

        game.Advance();
        Assert.That(game.CheckedLocations, Has.Count.EqualTo(3));

        game.Advance();
        Assert.That(game.CheckedLocations, Has.Count.EqualTo(6));

        // the energy factor wears off after that, though. in fact, the next round, there's only
        // enough actions to do "move, check, move".
        game.Advance();
        Assert.That(game.CheckedLocations, Has.Count.EqualTo(7));

        // one more round: "check, move, check"
        game.Advance();
        Assert.That(game.CheckedLocations, Has.Count.EqualTo(9));
    }

    [Test]
    public void NegativeEnergyFactorShouldEncumberMovement()
    {
        Prng.State seed = EnsureSeedProducesInitialD20Sequence(13033555434, [20, 20, 1, 20, 20, 20, 20, 1]);
        Game game = new(seed);
        game.ArbitrarilyModifyState(g => g.EnergyFactor, -3);

        // 3 actions are "check, move, (movement penalty)".
        game.Advance();
        Assert.That(game.CheckedLocations, Has.Count.EqualTo(1));

        // 3 actions are "check, move, (movement penalty)" again.
        game.Advance();
        Assert.That(game.CheckedLocations, Has.Count.EqualTo(2));

        // 3 actions are "fail, check, move".
        game.Advance();
        Assert.That(game.CheckedLocations, Has.Count.EqualTo(3));

        // 3 actions are "(movement penalty), check, move".
        game.Advance();
        Assert.That(game.CheckedLocations, Has.Count.EqualTo(4));

        // 3 actions are "check, move, check".
        game.Advance();
        Assert.That(game.CheckedLocations, Has.Count.EqualTo(6));
    }

    [Test]
    public void PositiveFoodFactorShouldGrantOneExtraAction()
    {
        Game game = new(s_highRolls);
        game.ArbitrarilyModifyState(g => g.FoodFactor, 2);

        // 4 actions are "check, move, check, move".
        game.Advance();
        Assert.Multiple(() =>
        {
            Assert.That(game.CheckedLocations, Has.Count.EqualTo(2));
            Assert.That(game.CurrentLocation.Key.N, Is.EqualTo(2));
        });

        // 4 actions are "check, move, check, move".
        game.Advance();
        Assert.Multiple(() =>
        {
            Assert.That(game.CheckedLocations, Has.Count.EqualTo(4));
            Assert.That(game.CurrentLocation.Key.N, Is.EqualTo(4));
        });

        // 3 actions are "check, move, check".
        game.Advance();
        Assert.Multiple(() =>
        {
            Assert.That(game.CheckedLocations, Has.Count.EqualTo(6));
            Assert.That(game.CurrentLocation.Key.N, Is.EqualTo(5));
            Assert.That(game.TargetLocation.Key.N, Is.EqualTo(6));
        });

        game.ReceiveItems([s_singleAuraItems["well_fed"]]);

        // 4 actions are "move, check, move, check".
        game.Advance();
        Assert.Multiple(() =>
        {
            Assert.That(game.CheckedLocations, Has.Count.EqualTo(8));
            Assert.That(game.CurrentLocation.Key.N, Is.EqualTo(7));
            Assert.That(game.TargetLocation.Key.N, Is.EqualTo(8));
            Assert.That(game.FoodFactor, Is.EqualTo(4));
        });
    }

    [Test]
    public void NegativeFoodFactorShouldSubtractOneAction()
    {
        Game game = new(s_highRolls);
        game.ArbitrarilyModifyState(g => g.FoodFactor, -2);

        // 2 actions are "check, move".
        game.Advance();
        Assert.Multiple(() =>
        {
            Assert.That(game.CheckedLocations, Has.Count.EqualTo(1));
            Assert.That(game.CurrentLocation.Key.N, Is.EqualTo(1));
        });

        // 2 actions are "check, move".
        game.Advance();
        Assert.Multiple(() =>
        {
            Assert.That(game.CheckedLocations, Has.Count.EqualTo(2));
            Assert.That(game.CurrentLocation.Key.N, Is.EqualTo(2));
        });

        // 3 actions are "check, move, check".
        game.Advance();
        Assert.Multiple(() =>
        {
            Assert.That(game.CheckedLocations, Has.Count.EqualTo(4));
            Assert.That(game.CurrentLocation.Key.N, Is.EqualTo(3));
            Assert.That(game.TargetLocation.Key.N, Is.EqualTo(4));
        });

        game.ReceiveItems([s_singleAuraItems["upset_tummy"]]);

        // 2 actions are "move, check".
        game.Advance();
        Assert.Multiple(() =>
        {
            Assert.That(game.CheckedLocations, Has.Count.EqualTo(5));
            Assert.That(game.CurrentLocation.Key.N, Is.EqualTo(4));
            Assert.That(game.TargetLocation.Key.N, Is.EqualTo(5));
            Assert.That(game.FoodFactor, Is.EqualTo(-4));
        });
    }

    [Test]
    public void DistractionCounterShouldWasteEntireRound()
    {
        Game game = new(s_highRolls);

        // distraction should also burn through your food factor.
        game.ReceiveItems([.. Enumerable.Repeat(s_singleAuraItems["well_fed"], 1)]);

        // counter won't go above 3, so get a distraction before each round
        for (int i = 0; i < 5; i++)
        {
            // 0 actions
            game.ReceiveItems([s_singleAuraItems["distracted"]]);
            game.Advance();
        }

        // 3 actions are "check, move, check"
        game.Advance();
        Assert.Multiple(() =>
        {
            Assert.That(game.CheckedLocations, Has.Count.EqualTo(2));
            Assert.That(game.CurrentLocation.Key.N, Is.EqualTo(1));
            Assert.That(game.TargetLocation.Key.N, Is.EqualTo(2));
        });
    }

    [Test]
    public void StyleFactorShouldImproveModifier()
    {
        Prng.State seed = EnsureSeedProducesInitialD20Sequence(81622, [6, 11]);
        Game game = new(seed);
        game.ReceiveItems([.. Enumerable.Repeat(s_singleAuraItems["stylish"], 2)]);

        // 3 actions are "check, move, check".
        game.Advance();
        Assert.Multiple(() =>
        {
            Assert.That(game.CheckedLocations, Has.Count.EqualTo(2));
            Assert.That(game.CurrentLocation.Key.N, Is.EqualTo(1));
            Assert.That(game.TargetLocation.Key.N, Is.EqualTo(2));
        });
    }

    [Test]
    public void TestGoMode()
    {
        Game game = new(s_lowRolls);

        // give it all randomized items except the last one.
        ItemDefinitionModel finalRandomizedItem = GameDefinitions.Instance.ProgressionItemsByItemKey["mongoose_in_a_combat_spacecraft"];
        game.ReceiveItems([
            .. GameDefinitions.Instance.LocationsByKey.Values
                .Where(l => l is { RewardIsFixed: false, UnrandomizedItem: not null })
                .Select(l => l.UnrandomizedItem!)
                .Where(i => i != finalRandomizedItem),
        ]);

        // make a couple of steps where we have all items except the very last one. this is SOMEWHAT
        // YAML-dependent, but seriously, if you advance 2 times with rolls forced to be natural 1,
        // and that somehow brings you out of the starting region, then that's a BIG change.
        for (int i = 0; i < 2; i++)
        {
            game.Advance();
            Assert.That(game.TargetLocation.Key.RegionKey, Is.EqualTo(GameDefinitions.Instance.StartRegion.Key));
            game.ArbitrarilyModifyState(g => g.PrngState, s_lowRolls);
        }

        // now give it that last randomized item and see it shoot for the moon all the way through.
        game.ReceiveItems([finalRandomizedItem]);
        HashSet<LocationKey> fixedRewardsGranted = [];
        int advancesSoFar = 0;
        while (!game.IsCompleted)
        {
            game.ArbitrarilyModifyState(g => g.PrngState, s_highRolls);
            game.Advance();
            Assert.That(game.TargetLocation.Region, Is.InstanceOf<LandmarkRegionDefinitionModel>());
            foreach (LocationDefinitionModel checkedLocation in game.CheckedLocations.Order)
            {
                if (fixedRewardsGranted.Add(checkedLocation.Key) && checkedLocation is { RewardIsFixed: true, UnrandomizedItem: { } unrandomizedItem })
                {
                    game.ReceiveItems([unrandomizedItem]);
                }
            }

            ++advancesSoFar;
            Assert.That(advancesSoFar, Is.LessThan(1_000_000), "If you can't win in a million steps, then you're useless.");
        }
    }

    [Test]
    public void PriorityLocationsShouldShiftTarget()
    {
        Game game = new(s_lowRolls);

        LocationDefinitionModel prawnStars = GameDefinitions.Instance.LocationsByName["Prawn Stars"];
        Assert.That(game.TargetLocation.Key, Is.EqualTo(new LocationKey { RegionKey = "Menu", N = 0 }));

        // prioritize Prawn Stars
        Assert.That(game.AddPriorityLocation(prawnStars), Is.EqualTo(AddPriorityLocationResult.AddedUnreachable));
        game.Advance();

        // should NOT be targeting Prawn Stars now, because we can't reach it out the gate.
        Assert.That(game.TargetLocation, Is.Not.EqualTo(prawnStars));

        // just restart it, giving it what's needed to reach Prawn Stars
        game = new(s_lowRolls);
        game.InitializeCheckedLocations([s_basketball]);
        game.InitializeReceivedItems([.. Enumerable.Range(0, 5).Select(_ => s_normalRat), s_premiumCanOfPrawnFood]);
        Assert.That(game.AddPriorityLocation(prawnStars), Is.EqualTo(AddPriorityLocationResult.AddedReachable));
        Assert.That(game.AddPriorityLocation(prawnStars), Is.EqualTo(AddPriorityLocationResult.AlreadyPrioritized));

        game.Advance();

        // NOW that's what we should be targeting
        Assert.That(game.TargetLocation, Is.EqualTo(prawnStars));

        // teleport the rat over to Prawn Stars and have it do its thing (remember it's rolling all
        // natural 1s today).
        game.ArbitrarilyModifyState(g => g.CurrentLocation, prawnStars);
        game.Advance();

        // it should still be there, and it should still be our priority location.
        Assert.That(game.PriorityLocations, Is.EqualTo(new[] { prawnStars }));

        // now roll natural 20s.
        game.ArbitrarilyModifyState(g => g.PrngState, s_highRolls);
        game.Advance();

        Assert.That(game.PriorityLocations, Is.Empty);
    }

    [Test]
    public void StartledShouldMovePlayerTowardsStart()
    {
        // force the first steps to move it towards the last reachable location in this region
        Game game = new(s_highRolls);
        game.ArbitrarilyModifyState(g => g.PriorityLocations, new([GameDefinitions.Instance.StartRegion.Locations[^1]]));

        game.Advance();
        LocationDefinitionModel middleLocation = game.CurrentLocation;

        game.Advance();
        if (game.CurrentLocation == game.TargetLocation)
        {
            Assert.Inconclusive("YAML was changed too much: there aren't enough locations in the starting region for this test.");
        }

        // even though it's all high rolls, we shouldn't have any checks because the rat is hard-prioritizing.
        Assert.That(game.CheckedLocations, Is.Empty);

        game.ReceiveItems([s_singleAuraItems["startled"]]);

        // it used all its movement to get from middleLocation to here previously, so being startled
        // should cause it to use that same movement to get exactly back to middleLocation again.
        game.Advance();
        Assert.Multiple(() =>
        {
            Assert.That(game.StartledCounter, Is.Zero);
            Assert.That(game.CurrentLocation, Is.EqualTo(middleLocation));
        });
    }

    [Test]
    public void StartledShouldTakePriorityOverDistracted()
    {
        Game game = new(s_highRolls);
        game.ArbitrarilyModifyState(g => g.CurrentLocation, GameDefinitions.Instance.StartRegion.Locations[^1]);
        game.ReceiveItems([
            s_singleAuraItems["startled"],
            .. Enumerable.Repeat(s_singleAuraItems["distracted"], 2),
        ]);

        // first step, we're startled out of our distraction.
        game.Advance();

        LocationDefinitionModel expectedStartleTarget = GameDefinitions.Instance.StartRegion.Locations[^10];
        Assert.Multiple(() =>
        {
            Assert.That(game.StartledCounter, Is.Zero);
            Assert.That(game.DistractionCounter, Is.EqualTo(1));
            Assert.That(game.CurrentLocation, Is.EqualTo(expectedStartleTarget));
        });

        // second step, there's a new distraction that we hadn't gotten to yet.
        game.Advance();

        // distraction burns a whole step
        Assert.That(game.CurrentLocation, Is.EqualTo(expectedStartleTarget));

        // now we're fine
        game.Advance();
        Assert.That(game.CheckedLocations, Has.Count.EqualTo(2));
    }

    [Test]
    [Property("Regression", 100)]
    public void SmartShouldResolveToNearestReachableIfPossible([Values("smart", "conspiratorial")] string aura)
    {
        ArchipelagoItemFlags targetFlags = aura switch
        {
            "smart" => ArchipelagoItemFlags.LogicalAdvancement,
            "conspiratorial" => ArchipelagoItemFlags.Trap,
            _ => throw null!,
        };
        FrozenDictionary<ArchipelagoItemFlags, FrozenSet<LocationKey>> spoilerData = CreateSpoiler([
            (GameDefinitions.Instance.StartLocation, targetFlags),
            (s_beforePrawnStars.Locations[0], targetFlags),
            (s_beforePrawnStars.Locations[^1], targetFlags),
        ]);

        Game game = new(Prng.State.Start());
        game.InitializeSpoilerData(spoilerData);
        game.ArbitrarilyModifyState(g => g.CurrentLocation, GameDefinitions.Instance.StartRegion.Locations[^1]);
        game.ArbitrarilyModifyState(g => g.TargetLocation, GameDefinitions.Instance.StartRegion.Locations[^1]);

        // even though there's a target RIGHT on the other side, we still favor the nearest one that
        // we can already reach with what we currently have.
        game.ReceiveItems([s_singleAuraItems[aura]]);
        Assert.That(game.PriorityPriorityLocations, Is.EqualTo(new[]
        {
            GameDefinitions.Instance.StartLocation,
        }));

        // if there's nothing else that we can reach, then we should NOT target the unreachable one
        // that's just out of reach. it should just fizzle.
        game.ReceiveItems([s_singleAuraItems[aura]]);
        Assert.That(game.PriorityPriorityLocations, Is.EqualTo(new[]
        {
            GameDefinitions.Instance.StartLocation,
        }));

        // #100: it also shouldn't re-prioritize the same location after it's been checked.
        game.CheckLocations([GameDefinitions.Instance.StartLocation]);
        Assert.That(game.PriorityPriorityLocations, Is.Empty);
        game.ReceiveItems([s_singleAuraItems[aura]]);
        Assert.That(game.PriorityPriorityLocations, Is.Empty);
    }

    [Test]
    [Property("Regression", 45)]
    public void PriorityLocationsPastClearableLandmarksShouldBlockThePlayer()
    {
        Game game = new(s_lowRolls);
        game.InitializeReceivedItems(Enumerable.Repeat(s_normalRat, 5));
        game.ArbitrarilyModifyState(g => g.CurrentLocation, GameDefinitions.Instance.StartRegion.Locations[^1]);
        game.ArbitrarilyModifyState(g => g.TargetLocation, GameDefinitions.Instance.StartRegion.Locations[^1]);
        game.ArbitrarilyModifyState(g => g.PriorityLocations, new([s_beforePrawnStars.Locations[1]]));

        for (int i = 0; i < 3; i++)
        {
            game.Advance();
            game.ArbitrarilyModifyState(g => g.PrngState, s_lowRolls);
        }

        Assert.That(game.CurrentLocation, Is.EqualTo(s_basketball));
    }

    [Test]
    public void LongMovesShouldBeAccelerated()
    {
        if (s_startRegion.Locations.Length < 25)
        {
            Assert.Inconclusive("This test is particularly sensitive to changes in the number of locations in the start region. Please re-evaluate.");
        }

        Game game = new(s_highRolls);
        game.InitializeReceivedItems(Enumerable.Repeat(s_normalRat, 5));
        game.InitializeCheckedLocations(s_startRegion.Locations);
        game.ArbitrarilyModifyState(g => g.CurrentLocation, GameDefinitions.Instance.StartLocation);
        game.ArbitrarilyModifyState(g => g.TargetLocation, s_basketball);

        game.ReceiveItems([s_singleAuraItems["energized"]]);
        game.Advance();
        Assert.Multiple(() =>
        {
            Assert.That(
                game.PreviousStepMovementLog.Select(v => v.PreviousLocation),
                Is.EqualTo(s_startRegion.Locations[..24]));
            Assert.That(
                game.PreviousStepMovementLog.Select(v => v.CurrentLocation),
                Is.EqualTo(s_startRegion.Locations[1..25]));
            Assert.That(
                game.CurrentLocation,
                Is.EqualTo(game.PreviousStepMovementLog[^1].CurrentLocation));
        });
    }

    [Test]
    [Property("Regression", 53)]
    public void PriorityLocationChecksShouldBypassUnreachableLocations()
    {
        LocationDefinitionModel lastLocationBeforeBasketball = GameDefinitions.Instance.StartRegion.Locations[^1];
        Game game = new(s_lowRolls);
        game.InitializeCheckedLocations([lastLocationBeforeBasketball]);
        game.ArbitrarilyModifyState(g => g.CurrentLocation, lastLocationBeforeBasketball);
        game.ArbitrarilyModifyState(g => g.TargetLocation, lastLocationBeforeBasketball);
        game.ArbitrarilyModifyState(g => g.PriorityLocations, new([s_basketball, lastLocationBeforeBasketball]));
        game.Advance();

        Assert.That(game.TargetLocation, Is.Not.EqualTo(lastLocationBeforeBasketball));
    }

    [Test]
    public void StartledShouldNotMoveThroughLockedLocations()
    {
        Game game = new(Prng.State.Start());
        game.InitializeReceivedItems([
            .. Enumerable.Repeat(s_normalRat, 40),
            GameDefinitions.Instance.ItemsByName["Priceless Antique"],
            GameDefinitions.Instance.ItemsByName["Pie Rat"],
            GameDefinitions.Instance.ItemsByName["Pizza Rat"],
            GameDefinitions.Instance.ItemsByName["Chef Rat"],
        ]);
        game.InitializeCheckedLocations([
            s_basketball,
            GameDefinitions.Instance.LocationsByName["Angry Turtles"],
            GameDefinitions.Instance.LocationsByName["Restaurant"],
            GameDefinitions.Instance.LocationsByName["Bowling Ball Door"],
        ]);
        game.ArbitrarilyModifyState(g => g.CurrentLocation, GameDefinitions.Instance.LocationsByName["After Pirate Bake Sale #1"]);
        game.ArbitrarilyModifyState(g => g.TargetLocation, GameDefinitions.Instance.LocationsByName["Bowling Ball Door"]);

        for (int i = 0; i < 100; i++)
        {
            game.ReceiveItems([s_singleAuraItems["startled"]]);
            game.Advance();
            Assert.That(
                game.PreviousStepMovementLog.Select(m => m.CurrentLocation),
                Has.None
                    .EqualTo(GameDefinitions.Instance.LocationsByName["Pirate Bake Sale"])
                    .Or.EqualTo(GameDefinitions.Instance.LocationsByName["Prawn Stars"]));
            if (game.CurrentLocation == s_startLocation)
            {
                break;
            }
        }

        Assert.That(game.CurrentLocation, Is.EqualTo(s_startLocation));
    }

    [Test]
    public void ReceiveItemsShouldApplyAuras()
    {
        Game game = new(Prng.State.Start());
        game.ReceiveItems([
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
            Assert.That(game.FoodFactor, Is.EqualTo(-10));
            Assert.That(game.LuckFactor, Is.EqualTo(-1));
            Assert.That(game.StartledCounter, Is.EqualTo(3));
            Assert.That(game.EnergyFactor, Is.EqualTo(10)); // 5 canceled by the first confidence!
            Assert.That(game.StyleFactor, Is.EqualTo(2));
            Assert.That(game.DistractionCounter, Is.Zero); // canceled by the first confidence!
            Assert.That(game.HasConfidence);
        });
    }

    [Test]
    [Property("Regression", 92)]
    public void RegressionTestPathingErrors()
    {
        Game game = new(s_highRolls);
        game.InitializeCheckedLocations([
            GameDefinitions.Instance.LocationsByName["Basketball"],
            GameDefinitions.Instance.LocationsByName["Angry Turtles"],
            GameDefinitions.Instance.LocationsByName["Restaurant"],
            GameDefinitions.Instance.LocationsByName["Bowling Ball Door"],
            GameDefinitions.Instance.LocationsByName["Captured Goldfish"],
            .. GameDefinitions.Instance.FillerRegions["Menu"].Locations, // "Before Basketball"
            .. GameDefinitions.Instance.FillerRegions["before_prawn_stars"].Locations,
            .. GameDefinitions.Instance.FillerRegions["before_angry_turtles"].Locations,
            .. GameDefinitions.Instance.FillerRegions["after_restaurant"].Locations,
            .. GameDefinitions.Instance.FillerRegions["before_captured_goldfish"].Locations,

            .. GameDefinitions.Instance.FillerRegions["after_pirate_bake_sale"].Locations.AsSpan(3..),
            .. GameDefinitions.Instance.FillerRegions["before_computer_interface"].Locations.AsSpan(..3),
        ]);
        game.InitializeReceivedItems([
            GameDefinitions.Instance.ItemsByName["Giant Novelty Scissors"],
            GameDefinitions.Instance.ItemsByName["Ninja Rat"],
            GameDefinitions.Instance.ItemsByName["Chef Rat"],
            GameDefinitions.Instance.ItemsByName["Computer Rat"],
            GameDefinitions.Instance.ItemsByName["Notorious R.A.T."],
            .. Enumerable.Repeat(s_normalRat, 14),
        ]);

        game.ArbitrarilyModifyState(g => g.CurrentLocation, GameDefinitions.Instance.LocationsByName["Before Goldfish #2"]);
        game.ArbitrarilyModifyState(g => g.TargetLocation, GameDefinitions.Instance.LocationsByName["Before Computer Interface #4"]);

        // the rat has everything it needs to make a few location checks. make sure it does that and
        // doesn't instead go into a loop like it was seen doing before.
        int initialCheckedLocationCount = game.CheckedLocations.Count;
        HashSet<LocationKey> locationsVisited = [];
        while (true)
        {
            game.Advance();
            if (game.CheckedLocations.Count > initialCheckedLocationCount)
            {
                break;
            }

            // this part ensures that the test will not loop forever: a LocationDefinitionModel does
            // exist for CurrentLocation, and only <1000 of those ever get created.
            Assert.That(locationsVisited, Does.Not.Contain(game.CurrentLocation.Key));
            locationsVisited.Add(game.CurrentLocation.Key);
        }
    }

    private static FrozenDictionary<ArchipelagoItemFlags, FrozenSet<LocationKey>> CreateSpoiler(ReadOnlySpan<(LocationDefinitionModel Location, ArchipelagoItemFlags Flags)> defined)
    {
        Dictionary<LocationKey, ArchipelagoItemFlags> result = GameDefinitions.Instance.LocationsByName.Values.ToDictionary(l => l.Key, _ => ArchipelagoItemFlags.None);
        foreach ((LocationDefinitionModel location, ArchipelagoItemFlags flags) in defined)
        {
            result[location.Key] = flags;
        }

        return result
            .GroupBy(kvp => kvp.Value, kvp => kvp.Key)
            .ToFrozenDictionary(grp => grp.Key, grp => grp.ToFrozenSet());
    }

    private static Prng.State EnsureSeedProducesInitialD20Sequence(ulong seed, ReadOnlySpan<int> exactVals)
    {
        int[] actual = [.. Rolls(seed, stackalloc int[exactVals.Length])];
        int[] expected = [.. exactVals];
        Assert.That(actual, Is.EqualTo(expected));
        return Prng.State.Start(seed);
    }

    private static Prng.State EnsureSeedProducesInitialD20Sequence(ReadOnlySpan<byte> seed, ReadOnlySpan<int> exactVals)
    {
        Assert.That(seed.Length, Is.EqualTo(Base64.GetMaxEncodedToUtf8Length(Unsafe.SizeOf<Prng.State>())));
        Assert.That(Base64.IsValid(seed));
        Prng.State initialState = default;
        OperationStatus decodeStatus = Base64.DecodeFromUtf8(seed, MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref initialState, 1)), out int bytesConsumed, out int bytesWritten);
        Assert.That(decodeStatus, Is.EqualTo(OperationStatus.Done));
        Assert.That(bytesConsumed, Is.EqualTo(seed.Length));
        Assert.That(bytesWritten, Is.EqualTo(Unsafe.SizeOf<Prng.State>()));

        Prng.State state = initialState;
        int[] actual = [.. Rolls(ref state, stackalloc int[exactVals.Length])];
        int[] expected = [.. exactVals];
        Assert.That(actual, Is.EqualTo(expected));
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
        TestContext.Out.WriteLine($"Prng.State seed = {nameof(EnsureSeedProducesInitialD20Sequence)}({result}, [{string.Join(", ", Rolls(result, stackalloc int[cnt]).ToArray())}]);");
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
