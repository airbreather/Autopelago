using System.Buffers.Text;
using System.Collections.Frozen;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Autopelago;

public sealed class GameTests
{
    private delegate TResult SpanFunc<TSource, out TResult>(ReadOnlySpan<TSource> vals);

    private static readonly Prng.State s_highRolls = EnsureSeedProducesInitialD20Sequence("ZcuBXfRkZixzx/eQAL1UiHpMG3kLbaDksoajUfxCis8="u8, [20, 20, 20, 20, 20, 20, 20, 20, 20, 20, 20]);

    private static readonly Prng.State s_lowRolls = EnsureSeedProducesInitialD20Sequence("Sr8rXn/wy4+RmchoEi8DdYc99ConsS+Fj2g7IoicNns="u8, [1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1]);

    private static readonly FrozenDictionary<string, ItemKey> s_singleAuraItems =
        new[] { "well_fed", "upset_tummy", "lucky", "unlucky", "energized", "sluggish", "distracted", "stylish", "startled", "smart", "conspiratorial", "confident" }
            .Select(aura => GameDefinitions.Instance.AllItems.First(i => i.AurasGranted.SequenceEqual([aura])))
            .ToFrozenDictionary(i => i.AurasGranted[0], i => i.Key);

    public static IEnumerable<Prng.State> UnrandomizedTestCases()
    {
        Prng.State seedSeed = Prng.State.Start();
        for (int i = 0; i < 100; i++)
        {
            yield return (seedSeed);
            Prng.Next(ref seedSeed);
        }
    }

    [Test]
    public async ValueTask FirstAttemptsShouldMakeSense()
    {
        Prng.State seed = EnsureSeedProducesInitialD20Sequence(56061, [8, 13, 18, 9, 13]);
        Prng.State prngState = seed;

        using Game game = new(seed);

        // we're on the first location. we should fail three times and then yield.
        _ = Prng.NextD20(ref prngState);
        _ = Prng.NextD20(ref prngState);
        _ = Prng.NextD20(ref prngState);

        game.Advance();
        using (Assert.Multiple())
        {
            await Assert.That(game.CheckedLocations.Count).IsEqualTo(0);
            await Assert.That(game.PrngState).IsEqualTo(prngState);
            await Assert.That(game.MercyModifier).IsEqualTo(1);
        }

        // the next attempt should succeed despite rolling the same as the previous step because the
        // cumulative penalty has worn off and the mercy modifier adds +1.
        _ = Prng.NextD20(ref prngState);
        _ = Prng.NextD20(ref prngState);

        game.Advance();
        using (Assert.Multiple())
        {
            await Assert.That(game.CheckedLocations.FirstOrDefault()).IsEqualTo(GameDefinitions.Instance.StartLocation);
            await Assert.That(game.TargetLocation).IsEqualTo(GameDefinitions.Instance[GameDefinitions.Instance.StartRegion].Locations[1]);

            // because they succeeded on their first attempt, they have just enough actions to reach and
            // then make a feeble attempt at the next location on the route
            await Assert.That(game.CurrentLocation).IsEqualTo(game.TargetLocation);
            await Assert.That(game.CheckedLocations.Count).IsEqualTo(1);
            await Assert.That(game.PrngState).IsEqualTo(prngState);
        }
    }

    [Test]
    [MatrixDataSource]
    public async ValueTask ShouldOnlyTryBasketballWithAtLeastFiveRats([Matrix(0, 7)] int ratCount)
    {
        ItemKey packRat = GameDefinitions.Instance.ItemsByName["Pack Rat"];
        LocationKey basketball = GameDefinitions.Instance.LocationsByName["Basketball"];

        using Game game = new(s_highRolls);
        game.InitializeCheckedLocations(GameDefinitions.Instance[GameDefinitions.Instance.StartRegion].Locations);
        game.InitializeReceivedItems(Enumerable.Repeat(packRat, ratCount));
        game.ArbitrarilyModifyState(g => g.CurrentLocation, GameDefinitions.Instance[GameDefinitions.Instance.StartRegion].Locations[^1]);
        game.ArbitrarilyModifyState(g => g.TargetLocation, GameDefinitions.Instance[GameDefinitions.Instance.StartRegion].Locations[^1]);
        game.Advance();
        if (ratCount < 5)
        {
            await Assert.That(game.CheckedLocations).DoesNotContain(basketball);
        }
        else
        {
            await Assert.That(game.CheckedLocations).Contains(basketball);
        }
    }

    [Test]
    [MatrixDataSource]
    public async ValueTask ShouldHeadFurtherAfterCompletingBasketball([Matrix(true, false)] bool unblockAngryTurtlesFirst)
    {
        ItemKey packRat = GameDefinitions.Instance.ItemsByName["Pack Rat"];
        ItemKey pizzaRat = GameDefinitions.Instance.ItemsByName["Pizza Rat"];
        ItemKey premiumCanOfPrawnFood = GameDefinitions.Instance.ItemsByName["Premium Can of Prawn Food"];
        LocationKey basketball = GameDefinitions.Instance.LocationsByName["Basketball"];
        RegionKey beforePrawnStars = GameDefinitions.Instance.RegionDefinition[GameDefinitions.Instance.LocationsByName["Prawn Stars"]].Connected.Backward[0];
        RegionKey beforeAngryTurtles = GameDefinitions.Instance.RegionDefinition[GameDefinitions.Instance.LocationsByName["Angry Turtles"]].Connected.Backward[0];

        using Game game = new(s_highRolls);
        game.InitializeReceivedItems([
            .. Enumerable.Repeat(packRat, 5),
            unblockAngryTurtlesFirst ? pizzaRat : premiumCanOfPrawnFood,
        ]);
        game.InitializeCheckedLocations(GameDefinitions.Instance[GameDefinitions.Instance.StartRegion].Locations);
        game.ArbitrarilyModifyState(g => g.CurrentLocation, basketball);
        game.ArbitrarilyModifyState(g => g.TargetLocation, basketball);

        game.Advance();

        // because we roll so well, we can actually use our three actions to complete two checks:
        // basketball, then move, then complete that first location that we moved to.
        using (Assert.Multiple())
        {
            await Assert.That(GameDefinitions.Instance.Region[game.CurrentLocation])
                .IsEqualTo(beforePrawnStars)
                .Or
                .IsEqualTo(beforeAngryTurtles);
            await Assert.That(GameDefinitions.Instance[game.CurrentLocation].RegionLocationKey.N).IsEqualTo(0);
            await Assert.That(GameDefinitions.Instance[game.TargetLocation].RegionLocationKey.N).IsEqualTo(1);
        }
    }

    [Test]
    [MethodDataSource(nameof(UnrandomizedTestCases))]
    public async ValueTask GameShouldBeWinnable(Prng.State seed)
    {
        using Game game = new(seed);
        game.InitializeVictoryLocation(Prng.NextDouble(ref seed) switch
        {
            < 1 / 3.0 => GameDefinitions.Instance.LocationsByName["Captured Goldfish"],
            < 2 / 3.0 => GameDefinitions.Instance.LocationsByName["Secret Cache"],
            _ => GameDefinitions.Instance.LocationsByName["Snakes on a Planet"],
        });
        int advancesSoFar = 0;
        List<ItemKey> newReceivedItems = [];
        while (true)
        {
            int prevCheckedLocationsCount = game.CheckedLocations.Count;
            game.Advance();

            if (game.IsCompleted)
            {
                break;
            }

            foreach (LocationKey newCheckedLocation in game.CheckedLocations.Skip(prevCheckedLocationsCount))
            {
                newReceivedItems.Add(GameDefinitions.Instance[newCheckedLocation].UnrandomizedItem);
            }

            if (newReceivedItems.Count > 0)
            {
                game.ReceiveItems([.. newReceivedItems]);
                newReceivedItems.Clear();
            }

            ++advancesSoFar;
            await Assert.That(advancesSoFar).IsLessThan(40_000).Because("If you can't win in 40k steps, then you're useless.");
        }
    }

    [Test]
    [MatrixDataSource]
    public async ValueTask LuckyAuraShouldForceSuccess([Matrix(1, 2, 3)] int effectCount)
    {
        using Game game = new(s_lowRolls);
        game.ReceiveItems([.. Enumerable.Repeat(s_singleAuraItems["lucky"], effectCount)]);
        game.Advance();
        game.Advance();
        game.Advance();
        await Assert.That(game.CheckedLocations.Count).IsEqualTo(effectCount);
    }

    [Test]
    public async ValueTask UnluckyAuraShouldReduceModifier()
    {
        Prng.State seed = EnsureSeedProducesInitialD20Sequence(1070077, [13, 18, 20, 12, 13]);
        using Game game = new(seed);
        game.ReceiveItems([.. Enumerable.Repeat(s_singleAuraItems["unlucky"], 4)]);

        // normally, a 13 as your first roll should pass, but with Unlucky it's not enough. the 18
        // also fails because -5 from the aura and -5 from the second attempt. even a natural 20
        // can't save you from a -15, so this first Advance call should utterly fail.
        game.Advance();
        using (Assert.Multiple())
        {
            await Assert.That(game.CheckedLocations.Count).IsEqualTo(0);

            // remember, after the first roll fails on a turn and no subsequent rolls pass during
            // that same turn, then the next turn's rolls get +1.
            await Assert.That(game.MercyModifier).IsEqualTo(1);
        }

        // the 12+1 burns the final Unlucky buff, so following it up with 13+1 overcomes the mere -5
        // from trying a second time on the same Advance call.
        game.Advance();
        using (Assert.Multiple())
        {
            await Assert.That(game.CheckedLocations.Count).IsEqualTo(1);

            // our first roll failed, but then a roll passed, so this modifier should be reset.
            await Assert.That(game.MercyModifier).IsEqualTo(0);
        }
    }

    [Test]
    public async ValueTask PositiveEnergyFactorShouldGiveExtraMovement()
    {
        ItemKey packRat = GameDefinitions.Instance.ItemsByName["Pack Rat"];
        ItemKey premiumCanOfPrawnFood = GameDefinitions.Instance.ItemsByName["Premium Can of Prawn Food"];
        ItemKey pieRat = GameDefinitions.Instance.ItemsByName["Pie Rat"];

        LocationKey basketball = GameDefinitions.Instance.LocationsByName["Basketball"];
        LocationKey prawnStars = GameDefinitions.Instance.LocationsByName["Prawn Stars"];
        LocationKey pirateBakeSale = GameDefinitions.Instance.LocationsByName["Pirate Bake Sale"];

        using Game game = new(s_lowRolls);
        game.InitializeCheckedLocations([basketball, prawnStars]);
        game.ReceiveItems([
            s_singleAuraItems["energized"],
            .. Enumerable.Repeat(packRat, 5),
            premiumCanOfPrawnFood,
            pieRat,
        ]);
        game.AddPriorityLocation(pirateBakeSale);

        for (int i = 0; i < 5; i++)
        {
            game.PrngState = s_lowRolls;
            game.Advance();
        }

        await Assert.That(game.CurrentLocation).IsEqualTo(pirateBakeSale);
    }

    [Test]
    public async ValueTask NegativeEnergyFactorShouldEncumberMovement()
    {
        Prng.State seed = EnsureSeedProducesInitialD20Sequence(13033555434, [20, 20, 1, 20, 20, 20, 20, 1]);
        using Game game = new(seed);
        game.ArbitrarilyModifyState(g => g.EnergyFactor, -3);

        // 3 actions are "check, move, (movement penalty)".
        game.Advance();
        await Assert.That(game.CheckedLocations.Count).IsEqualTo(1);

        // 3 actions are "check, move, (movement penalty)" again.
        game.Advance();
        await Assert.That(game.CheckedLocations.Count).IsEqualTo(2);

        // 3 actions are "fail, check, move".
        game.Advance();
        await Assert.That(game.CheckedLocations.Count).IsEqualTo(3);

        // 3 actions are "(movement penalty), check, move".
        game.Advance();
        await Assert.That(game.CheckedLocations.Count).IsEqualTo(4);

        // 3 actions are "check, move, check".
        game.Advance();
        await Assert.That(game.CheckedLocations.Count).IsEqualTo(6);
    }

    [Test]
    public async ValueTask PositiveFoodFactorShouldGrantOneExtraAction()
    {
        using Game game = new(s_highRolls);
        game.ArbitrarilyModifyState(g => g.FoodFactor, 2);

        // 4 actions are "check, move, check, move".
        game.Advance();
        using (Assert.Multiple())
        {
            await Assert.That(game.CheckedLocations.Count).IsEqualTo(2);
            await Assert.That(GameDefinitions.Instance[game.CurrentLocation].RegionLocationKey.N).IsEqualTo(2);
        }

        // 4 actions are "check, move, check, move".
        game.Advance();
        using (Assert.Multiple())
        {
            await Assert.That(game.CheckedLocations.Count).IsEqualTo(4);
            await Assert.That(GameDefinitions.Instance[game.CurrentLocation].RegionLocationKey.N).IsEqualTo(4);
        }

        // 3 actions are "check, move, check".
        game.Advance();
        using (Assert.Multiple())
        {
            await Assert.That(game.CheckedLocations.Count).IsEqualTo(6);
            await Assert.That(GameDefinitions.Instance[game.CurrentLocation].RegionLocationKey.N).IsEqualTo(5);
            await Assert.That(GameDefinitions.Instance[game.TargetLocation].RegionLocationKey.N).IsEqualTo(6);
        }

        game.ReceiveItems([s_singleAuraItems["well_fed"]]);

        // 4 actions are "move, check, move, check".
        game.Advance();
        using (Assert.Multiple())
        {
            await Assert.That(game.CheckedLocations.Count).IsEqualTo(8);
            await Assert.That(GameDefinitions.Instance[game.CurrentLocation].RegionLocationKey.N).IsEqualTo(7);
            await Assert.That(GameDefinitions.Instance[game.TargetLocation].RegionLocationKey.N).IsEqualTo(8);
            await Assert.That(game.FoodFactor).IsEqualTo(4);
        }
    }

    [Test]
    public async ValueTask NegativeFoodFactorShouldSubtractOneAction()
    {
        using Game game = new(s_highRolls);
        game.ArbitrarilyModifyState(g => g.FoodFactor, -2);

        // 2 actions are "check, move".
        game.Advance();
        using (Assert.Multiple())
        {
            await Assert.That(game.CheckedLocations.Count).IsEqualTo(1);
            await Assert.That(GameDefinitions.Instance[game.CurrentLocation].RegionLocationKey.N).IsEqualTo(1);
        }

        // 2 actions are "check, move".
        game.Advance();
        using (Assert.Multiple())
        {
            await Assert.That(game.CheckedLocations.Count).IsEqualTo(2);
            await Assert.That(GameDefinitions.Instance[game.CurrentLocation].RegionLocationKey.N).IsEqualTo(2);
        }

        // 3 actions are "check, move, check".
        game.Advance();
        using (Assert.Multiple())
        {
            await Assert.That(game.CheckedLocations.Count).IsEqualTo(4);
            await Assert.That(GameDefinitions.Instance[game.CurrentLocation].RegionLocationKey.N).IsEqualTo(3);
            await Assert.That(GameDefinitions.Instance[game.TargetLocation].RegionLocationKey.N).IsEqualTo(4);
        }

        game.ReceiveItems([s_singleAuraItems["upset_tummy"]]);

        // 2 actions are "move, check".
        game.Advance();
        using (Assert.Multiple())
        {
            await Assert.That(game.CheckedLocations.Count).IsEqualTo(5);
            await Assert.That(GameDefinitions.Instance[game.CurrentLocation].RegionLocationKey.N).IsEqualTo(4);
            await Assert.That(GameDefinitions.Instance[game.TargetLocation].RegionLocationKey.N).IsEqualTo(5);
            await Assert.That(game.FoodFactor).IsEqualTo(-4);
        }
    }

    [Test]
    public async ValueTask DistractionCounterShouldWasteEntireRound()
    {
        using Game game = new(s_highRolls);

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
        using (Assert.Multiple())
        {
            await Assert.That(game.CheckedLocations.Count).IsEqualTo(2);
            await Assert.That(GameDefinitions.Instance[game.CurrentLocation].RegionLocationKey.N).IsEqualTo(1);
            await Assert.That(GameDefinitions.Instance[game.TargetLocation].RegionLocationKey.N).IsEqualTo(2);
        }
    }

    [Test]
    public async ValueTask StyleFactorShouldImproveModifier()
    {
        Prng.State seed = EnsureSeedProducesInitialD20Sequence(81622, [6, 11]);
        using Game game = new(seed);
        game.ReceiveItems([.. Enumerable.Repeat(s_singleAuraItems["stylish"], 2)]);

        // 3 actions are "check, move, check".
        game.Advance();
        using (Assert.Multiple())
        {
            await Assert.That(game.CheckedLocations.Count).IsEqualTo(2);
            await Assert.That(GameDefinitions.Instance[game.CurrentLocation].RegionLocationKey.N).IsEqualTo(1);
            await Assert.That(GameDefinitions.Instance[game.TargetLocation].RegionLocationKey.N).IsEqualTo(2);
        }
    }

    [Test]
    public async ValueTask TestGoMode()
    {
        using Game game = new(s_lowRolls);

        // give it all randomized items except the last one.
        ItemKey finalRandomizedItem = GameDefinitions.Instance.ProgressionItemsByYamlKey["mongoose_in_a_combat_spacecraft"];
        game.ReceiveItems([
            .. GameDefinitions.Instance.AllLocations
                .Select(l => l.UnrandomizedItem)
                .Where(i => i != finalRandomizedItem),
        ]);

        // make a couple of steps where we have all items except the very last one. this is SOMEWHAT
        // YAML-dependent, but seriously, if you advance 2 times with rolls forced to be natural 1,
        // and that somehow brings you out of the starting region, then that's a BIG change.
        for (int i = 0; i < 2; i++)
        {
            game.Advance();
            await Assert.That(GameDefinitions.Instance.Region[game.TargetLocation]).IsEqualTo(GameDefinitions.Instance.StartRegion);
            game.PrngState = s_lowRolls;
        }

        // now give it that last randomized item and see it shoot for the moon all the way through.
        game.ReceiveItems([finalRandomizedItem]);
        int advancesSoFar = 0;
        while (true)
        {
            game.PrngState = s_highRolls;
            game.Advance();
            if (game.HasCompletedGoal)
            {
                break;
            }

            await Assert.That(GameDefinitions.Instance.RegionDefinition[game.TargetLocation]).IsAssignableTo<LandmarkRegionDefinitionModel>();
            ++advancesSoFar;
            await Assert.That(advancesSoFar).IsLessThan(1_000_000).Because("If you can't win in a million steps, then you're useless.");
        }
    }

    [Test]
    public async ValueTask PriorityLocationsShouldShiftTarget()
    {
        ItemKey packRat = GameDefinitions.Instance.ItemsByName["Pack Rat"];
        ItemKey premiumCanOfPrawnFood = GameDefinitions.Instance.ItemsByName["Premium Can of Prawn Food"];

        LocationKey basketball = GameDefinitions.Instance.LocationsByName["Basketball"];
        LocationKey prawnStars = GameDefinitions.Instance.LocationsByName["Prawn Stars"];
        using Game scrubbedGame = new(s_lowRolls);

        await Assert.That(scrubbedGame.TargetLocation).IsEqualTo(GameDefinitions.Instance.StartLocation);

        // prioritize Prawn Stars
        await Assert.That(scrubbedGame.AddPriorityLocation(prawnStars)).IsEqualTo(AddPriorityLocationResult.AddedUnreachable);
        scrubbedGame.Advance();

        // should NOT be targeting Prawn Stars now, because we can't reach it out the gate.
        await Assert.That(scrubbedGame.TargetLocation).IsNotEqualTo(prawnStars);

        // just restart it, giving it what's needed to reach Prawn Stars
        using Game game = new(s_lowRolls);
        game.InitializeCheckedLocations([basketball]);
        game.InitializeReceivedItems([.. Enumerable.Repeat(packRat, 5), premiumCanOfPrawnFood]);
        await Assert.That(game.AddPriorityLocation(prawnStars)).IsEqualTo(AddPriorityLocationResult.AddedReachable);
        await Assert.That(game.AddPriorityLocation(prawnStars)).IsEqualTo(AddPriorityLocationResult.AlreadyPrioritized);

        game.Advance();

        // NOW that's what we should be targeting
        await Assert.That(game.TargetLocation).IsEqualTo(prawnStars);

        // teleport the rat over to Prawn Stars and have it do its thing (remember it's rolling all
        // natural 1s today).
        game.ArbitrarilyModifyState(g => g.CurrentLocation, prawnStars);
        game.Advance();

        // it should still be there, and it should still be our priority location.
        await Assert.That(game.PriorityLocations).IsEquivalentTo([prawnStars]);

        // now roll natural 20s.
        game.PrngState = s_highRolls;
        game.Advance();

        await Assert.That(game.PriorityLocations.Count).IsEqualTo(0);
    }

    [Test]
    public async ValueTask StartledShouldMovePlayerTowardsStart()
    {
        // force the first steps to move it towards the last reachable location in this region
        using Game game = new(s_highRolls);
        game.AddPriorityLocation(GameDefinitions.Instance[GameDefinitions.Instance.StartRegion].Locations[^1]);

        game.Advance();
        LocationKey middleLocation = game.CurrentLocation;

        game.Advance();
        await Assert.That(game.CurrentLocation).IsNotEqualTo(game.TargetLocation)
            .Because("YAML was changed too much: there aren't enough locations in the starting region for this test.");

        // even though it's all high rolls, we shouldn't have any checks because the rat is hard-prioritizing.
        await Assert.That(game.CheckedLocations.Count).IsEqualTo(0);

        game.ReceiveItems([s_singleAuraItems["startled"]]);

        // it used all its movement to get from middleLocation to here previously, so being startled
        // should cause it to use that same movement to get exactly back to middleLocation again.
        game.Advance();
        using (Assert.Multiple())
        {
            await Assert.That(game.StartledCounter).IsEqualTo(0);
            await Assert.That(game.CurrentLocation).IsEqualTo(middleLocation);
        }
    }

    [Test]
    public async ValueTask StartledShouldTakePriorityOverDistracted()
    {
        using Game game = new(s_highRolls);
        game.ArbitrarilyModifyState(g => g.CurrentLocation, GameDefinitions.Instance[GameDefinitions.Instance.StartRegion].Locations[^1]);
        game.ReceiveItems([
            s_singleAuraItems["startled"],
            .. Enumerable.Repeat(s_singleAuraItems["distracted"], 2),
        ]);

        // first step, we're startled out of our distraction.
        game.Advance();

        LocationKey expectedStartleTarget = GameDefinitions.Instance[GameDefinitions.Instance.StartRegion].Locations[^10];
        using (Assert.Multiple())
        {
            await Assert.That(game.StartledCounter).IsEqualTo(0);
            await Assert.That(game.DistractionCounter).IsEqualTo(1);
            await Assert.That(game.CurrentLocation).IsEqualTo(expectedStartleTarget);
        }

        // second step, there's a new distraction that we hadn't gotten to yet.
        game.Advance();

        // distraction burns a whole step
        await Assert.That(game.CurrentLocation).IsEqualTo(expectedStartleTarget);

        // now we're fine
        game.Advance();
        await Assert.That(game.CheckedLocations.Count).IsEqualTo(2);
    }

    [Test]
    [MatrixDataSource]
    [Property("Regression", "100")]
    public async ValueTask SmartShouldResolveToNearestReachableIfPossible([Matrix("smart", "conspiratorial")] string aura)
    {
        RegionKey beforePrawnStars = GameDefinitions.Instance.RegionDefinition[GameDefinitions.Instance.LocationsByName["Prawn Stars"]].Connected.Backward[0];

        ArchipelagoItemFlags targetFlags = aura switch
        {
            "smart" => ArchipelagoItemFlags.LogicalAdvancement,
            "conspiratorial" => ArchipelagoItemFlags.Trap,
            _ => throw null!,
        };

        FrozenDictionary<ArchipelagoItemFlags, BitArray384> spoilerData = CreateSpoiler([
            (GameDefinitions.Instance.StartLocation, targetFlags),
            (GameDefinitions.Instance[beforePrawnStars].Locations[0], targetFlags),
            (GameDefinitions.Instance[beforePrawnStars].Locations[^1], targetFlags),
        ]);

        using Game game = new(Prng.State.Start());
        game.InitializeSpoilerData(spoilerData);
        game.ArbitrarilyModifyState(g => g.CurrentLocation, GameDefinitions.Instance[GameDefinitions.Instance.StartRegion].Locations[^1]);
        game.ArbitrarilyModifyState(g => g.TargetLocation, GameDefinitions.Instance[GameDefinitions.Instance.StartRegion].Locations[^1]);

        // even though there's a target RIGHT on the other side, we still favor the nearest one that
        // we can already reach with what we currently have.
        game.ReceiveItems([s_singleAuraItems[aura]]);
        await Assert.That(game.PriorityPriorityLocations).IsEquivalentTo(
        [
            GameDefinitions.Instance.StartLocation,
        ]);

        // if there's nothing else that we can reach, then we should NOT target the unreachable one
        // that's just out of reach. it should just fizzle.
        game.ReceiveItems([s_singleAuraItems[aura]]);
        await Assert.That(game.PriorityPriorityLocations).IsEquivalentTo(
        [
            GameDefinitions.Instance.StartLocation,
        ]);

        // #100: it also shouldn't re-prioritize the same location after it's been checked.
        game.CheckLocations([GameDefinitions.Instance.StartLocation]);
        await Assert.That(game.PriorityPriorityLocations.Count).IsEqualTo(0);
        game.ReceiveItems([s_singleAuraItems[aura]]);
        await Assert.That(game.PriorityPriorityLocations.Count).IsEqualTo(0);
    }

    [Test]
    [Property("Regression", "45")]
    public async ValueTask PriorityLocationsPastClearableLandmarksShouldBlockThePlayer()
    {
        ItemKey packRat = GameDefinitions.Instance.ItemsByName["Pack Rat"];
        LocationKey basketball = GameDefinitions.Instance.LocationsByName["Basketball"];
        RegionKey beforePrawnStars = GameDefinitions.Instance.RegionDefinition[GameDefinitions.Instance.LocationsByName["Prawn Stars"]].Connected.Backward[0];

        using Game game = new(s_lowRolls);
        game.InitializeReceivedItems(Enumerable.Repeat(packRat, 5));
        game.ArbitrarilyModifyState(g => g.CurrentLocation, GameDefinitions.Instance[GameDefinitions.Instance.StartRegion].Locations[^1]);
        game.ArbitrarilyModifyState(g => g.TargetLocation, GameDefinitions.Instance[GameDefinitions.Instance.StartRegion].Locations[^1]);
        game.AddPriorityLocation(GameDefinitions.Instance[beforePrawnStars].Locations[1]);

        for (int i = 0; i < 3; i++)
        {
            game.Advance();
            game.PrngState = s_lowRolls;
        }

        await Assert.That(game.CurrentLocation).IsEqualTo(basketball);
    }

    [Test]
    public async ValueTask LongMovesShouldBeAccelerated()
    {
        ItemKey packRat = GameDefinitions.Instance.ItemsByName["Pack Rat"];
        LocationKey basketball = GameDefinitions.Instance.LocationsByName["Basketball"];

        RegionDefinitionModel startRegion = GameDefinitions.Instance[GameDefinitions.Instance.StartRegion];
        await Assert.That(startRegion.Locations.Length).IsGreaterThanOrEqualTo(9)
            .Because("This test is particularly sensitive to changes in the number of locations in the start region. Please re-evaluate.");

        using Game game = new(s_highRolls);
        game.InitializeReceivedItems(Enumerable.Repeat(packRat, 5));
        game.InitializeCheckedLocations(GameDefinitions.Instance[GameDefinitions.Instance.StartRegion].Locations);
        game.AddPriorityLocation(basketball);

        game.Advance();
        using (Assert.Multiple())
        {
            await Assert.That(
                game.PreviousStepMovementLog.Select(v => v.PreviousLocation))
                .IsEquivalentTo(startRegion.Locations[..6]);
            await Assert.That(
                game.PreviousStepMovementLog.Select(v => v.CurrentLocation))
                .IsEquivalentTo(startRegion.Locations[1..7]);
            await Assert.That(
                game.CurrentLocation)
                .IsEquivalentTo(game.PreviousStepMovementLog[^1].CurrentLocation);
        }
    }

    [Test]
    [Property("Regression", "53")]
    public async ValueTask PriorityLocationChecksShouldBypassUnreachableLocations()
    {
        RegionDefinitionModel startRegion = GameDefinitions.Instance[GameDefinitions.Instance.StartRegion];
        LocationKey lastLocationBeforeBasketball = startRegion.Locations[^1];
        LocationKey basketball = GameDefinitions.Instance.LocationsByName["Basketball"];
        using Game game = new(s_lowRolls);
        game.InitializeCheckedLocations([lastLocationBeforeBasketball]);
        game.ArbitrarilyModifyState(g => g.CurrentLocation, lastLocationBeforeBasketball);
        game.ArbitrarilyModifyState(g => g.TargetLocation, lastLocationBeforeBasketball);
        game.AddPriorityLocation(basketball);
        game.AddPriorityLocation(lastLocationBeforeBasketball);
        game.Advance();

        await Assert.That(game.TargetLocation).IsNotEqualTo(lastLocationBeforeBasketball);
    }

    [Test]
    public async ValueTask StartledShouldNotMoveThroughLockedLocations()
    {
        ItemKey packRat = GameDefinitions.Instance.ItemsByName["Pack Rat"];
        LocationKey basketball = GameDefinitions.Instance.LocationsByName["Basketball"];

        using Game game = new(Prng.State.Start());
        game.InitializeReceivedItems([
            .. Enumerable.Repeat(packRat, 40),
            GameDefinitions.Instance.ItemsByName["Priceless Antique"],
            GameDefinitions.Instance.ItemsByName["Pie Rat"],
            GameDefinitions.Instance.ItemsByName["Pizza Rat"],
            GameDefinitions.Instance.ItemsByName["Chef Rat"],
        ]);
        game.InitializeCheckedLocations([
            basketball,
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
            await Assert.That(
                game.PreviousStepMovementLog.Select(m => m.CurrentLocation))
                .DoesNotContain(GameDefinitions.Instance.LocationsByName["Pirate Bake Sale"])
                .And
                .DoesNotContain(GameDefinitions.Instance.LocationsByName["Prawn Stars"]);
            if (game.CurrentLocation == GameDefinitions.Instance.StartLocation)
            {
                break;
            }
        }

        await Assert.That(game.CurrentLocation).IsEqualTo(GameDefinitions.Instance.StartLocation);
    }

    [Test]
    public async ValueTask ReceiveItemsShouldApplyAuras()
    {
        using Game game = new(Prng.State.Start());
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

        using (Assert.Multiple())
        {
            await Assert.That(game.FoodFactor).IsEqualTo(-10);
            await Assert.That(game.LuckFactor).IsEqualTo(-1);
            await Assert.That(game.StartledCounter).IsEqualTo(3);
            await Assert.That(game.EnergyFactor).IsEqualTo(10); // 5 canceled by the first confidence!
            await Assert.That(game.StyleFactor).IsEqualTo(2);
            await Assert.That(game.DistractionCounter).IsEqualTo(0); // canceled by the first confidence!
            await Assert.That(game.HasConfidence).IsEqualTo(true);
        }
    }

    [Test]
    [Property("Regression", "92")]
    public async ValueTask RegressionTestPathingErrors()
    {
        ItemKey packRat = GameDefinitions.Instance.ItemsByName["Pack Rat"];
        using Game game = new(s_highRolls);
        game.InitializeCheckedLocations([
            GameDefinitions.Instance.LocationsByName["Basketball"],
            GameDefinitions.Instance.LocationsByName["Angry Turtles"],
            GameDefinitions.Instance.LocationsByName["Restaurant"],
            GameDefinitions.Instance.LocationsByName["Bowling Ball Door"],
            GameDefinitions.Instance.LocationsByName["Captured Goldfish"],
            .. GameDefinitions.Instance[GameDefinitions.Instance.RegionsByYamlKey["Menu"]].Locations, // "Before Basketball"
            .. GameDefinitions.Instance[GameDefinitions.Instance.RegionsByYamlKey["before_prawn_stars"]].Locations,
            .. GameDefinitions.Instance[GameDefinitions.Instance.RegionsByYamlKey["before_angry_turtles"]].Locations,
            .. GameDefinitions.Instance[GameDefinitions.Instance.RegionsByYamlKey["after_restaurant"]].Locations,
            .. GameDefinitions.Instance[GameDefinitions.Instance.RegionsByYamlKey["before_captured_goldfish"]].Locations,

            .. GameDefinitions.Instance[GameDefinitions.Instance.RegionsByYamlKey["after_pirate_bake_sale"]].Locations.AsSpan(3..),
            .. GameDefinitions.Instance[GameDefinitions.Instance.RegionsByYamlKey["before_computer_interface"]].Locations.AsSpan(..3),
        ]);
        game.InitializeReceivedItems([
            GameDefinitions.Instance.ItemsByName["Giant Novelty Scissors"],
            GameDefinitions.Instance.ItemsByName["Ninja Rat"],
            GameDefinitions.Instance.ItemsByName["Chef Rat"],
            GameDefinitions.Instance.ItemsByName["Computer Rat"],
            GameDefinitions.Instance.ItemsByName["Notorious R.A.T."],
            .. Enumerable.Repeat(packRat, 14),
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
            await Assert.That(locationsVisited).DoesNotContain(game.CurrentLocation);
            locationsVisited.Add(game.CurrentLocation);
        }
    }

    private static FrozenDictionary<ArchipelagoItemFlags, BitArray384> CreateSpoiler(ReadOnlySpan<(LocationKey Location, ArchipelagoItemFlags Flags)> defined)
    {
        Dictionary<ArchipelagoItemFlags, BitArray384> result = new()
        {
            [ArchipelagoItemFlags.None] = new(GameDefinitions.Instance.AllLocations.Length),
            [ArchipelagoItemFlags.ImportantNonAdvancement] = new(GameDefinitions.Instance.AllLocations.Length),
            [ArchipelagoItemFlags.LogicalAdvancement] = new(GameDefinitions.Instance.AllLocations.Length),
            [ArchipelagoItemFlags.Trap] = new(GameDefinitions.Instance.AllLocations.Length),
        };
        foreach ((LocationKey location, ArchipelagoItemFlags flags) in defined)
        {
            ref BitArray384 spoilerArray = ref CollectionsMarshal.GetValueRefOrNullRef(result, flags);
            if (Unsafe.IsNullRef(ref spoilerArray))
            {
                throw new InvalidOperationException("Not one of the recognized flags!");
            }

            spoilerArray[location.N] = true;
        }

        return result.ToFrozenDictionary();
    }

    private static Prng.State EnsureSeedProducesInitialD20Sequence(ulong seed, ReadOnlySpan<int> exactVals)
    {
        if (!exactVals.SequenceEqual(Rolls(seed, stackalloc int[exactVals.Length])))
        {
            throw new InvalidDataException($"Seed '{seed}' no longer matches!");
        }

        return Prng.State.Start(seed);
    }

    private static Prng.State EnsureSeedProducesInitialD20Sequence(ReadOnlySpan<byte> seed, ReadOnlySpan<int> exactVals)
    {
        Prng.State initialState = default;
        Base64.DecodeFromUtf8(seed, MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref initialState, 1)), out _, out _);
        Prng.State state = initialState;
        if (!exactVals.SequenceEqual(Rolls(ref state, stackalloc int[exactVals.Length])))
        {
            throw new InvalidDataException($"Seed '{Encoding.UTF8.GetString(seed)}' no longer matches!");
        }

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
        TestContext.Current!.OutputWriter.WriteLine($"Prng.State seed = {nameof(EnsureSeedProducesInitialD20Sequence)}({result}, [{string.Join(", ", Rolls(result, stackalloc int[cnt]).ToArray())}]);");
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
