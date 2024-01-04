using System.Runtime.InteropServices;

using ArchipelagoClientDotNet;

public sealed class Game(GameDifficultySettings difficultySettings, int seed)
{
    // "non-key item" location count tuning
    private static readonly Dictionary<Region, int> s_numLocationsIn = new()
    {
        [Region.Before8Rats] = 40,
        [Region.After8RatsBeforeA] = 10,
        [Region.After8RatsBeforeB] = 10,
        [Region.AfterABeforeC] = 10,
        [Region.AfterBBeforeD] = 10,
        [Region.AfterCBefore20Rats] = 10,
        [Region.AfterDBefore20Rats] = 10,
        [Region.After20RatsBeforeE] = 20,
        [Region.After20RatsBeforeF] = 20,
    };

    // location IDs
    private const long BASE_ID = 9000;

    // "key item" locations
    private static readonly long s_locationGoal = BASE_ID;

    private static readonly long s_locationA = s_locationGoal + 1;

    private static readonly long s_locationB = s_locationA + 1;

    private static readonly long s_locationC = s_locationB + 1;

    private static readonly long s_locationD = s_locationC + 1;

    private static readonly long s_locationE = s_locationD + 1;

    private static readonly long s_locationF = s_locationE + 1;

    // "non-key item" locations
    private static readonly long[] s_locationsBefore8Rats = Enumerable.Range(1, s_numLocationsIn[Region.Before8Rats]).Select(id => s_locationF + id).ToArray();

    private static readonly long[] s_locationsAfter8RatsBeforeA = Enumerable.Range(1, s_numLocationsIn[Region.After8RatsBeforeA]).Select(id => s_locationsBefore8Rats[^1] + id).ToArray();

    private static readonly long[] s_locationsAfter8RatsBeforeB = Enumerable.Range(1, s_numLocationsIn[Region.After8RatsBeforeB]).Select(id => s_locationsAfter8RatsBeforeA[^1] + id).ToArray();

    private static readonly long[] s_locationsAfterABeforeC = Enumerable.Range(1, s_numLocationsIn[Region.AfterABeforeC]).Select(id => s_locationsAfter8RatsBeforeB[^1] + id).ToArray();

    private static readonly long[] s_locationsAfterBBeforeD = Enumerable.Range(1, s_numLocationsIn[Region.AfterBBeforeD]).Select(id => s_locationsAfterABeforeC[^1] + id).ToArray();

    private static readonly long[] s_locationsAfterCBefore20Rats = Enumerable.Range(1, s_numLocationsIn[Region.AfterCBefore20Rats]).Select(id => s_locationsAfterBBeforeD[^1] + id).ToArray();

    private static readonly long[] s_locationsAfterDBefore20Rats = Enumerable.Range(1, s_numLocationsIn[Region.AfterDBefore20Rats]).Select(id => s_locationsAfterCBefore20Rats[^1] + id).ToArray();

    private static readonly long[] s_locationsAfter20RatsBeforeE = Enumerable.Range(1, s_numLocationsIn[Region.After20RatsBeforeE]).Select(id => s_locationsAfterDBefore20Rats[^1] + id).ToArray();

    private static readonly long[] s_locationsAfter20RatsBeforeF = Enumerable.Range(1, s_numLocationsIn[Region.After20RatsBeforeF]).Select(id => s_locationsAfter20RatsBeforeE[^1] + id).ToArray();

    private static readonly Dictionary<(Region S, Region T), int> s_regionDistances = ToComplete(ToUndirected(new()
    {
        [Region.Before8Rats] = new()
        {
            [Region.After8RatsBeforeA] = true,
            [Region.After8RatsBeforeB] = true,
        },
        [Region.After8RatsBeforeA] = new() { [Region.A] = false },
        [Region.After8RatsBeforeB] = new() { [Region.B] = false },
        [Region.A] = new() { [Region.AfterABeforeC] = true },
        [Region.B] = new() { [Region.AfterBBeforeD] = true },
        [Region.AfterABeforeC] = new() { [Region.C] = false },
        [Region.AfterBBeforeD] = new() { [Region.D] = false },
        [Region.C] = new() { [Region.AfterCBefore20Rats] = true },
        [Region.D] = new() { [Region.AfterDBefore20Rats] = true },
        [Region.AfterCBefore20Rats] = new()
        {
            [Region.After20RatsBeforeE] = true,
            [Region.After20RatsBeforeF] = true,
        },
        [Region.AfterDBefore20Rats] = new()
        {
            [Region.After20RatsBeforeE] = true,
            [Region.After20RatsBeforeF] = true,
        },
        [Region.After20RatsBeforeE] = new() { [Region.E] = false },
        [Region.After20RatsBeforeF] = new() { [Region.F] = false },
        [Region.E] = new() { [Region.TryingForGoal] = true, },
        [Region.F] = new() { [Region.TryingForGoal] = true, },
        [Region.TryingForGoal] = [],
    }));

    public static readonly Dictionary<Region, long[]> s_locationsByRegion = new()
    {
        [Region.Before8Rats] = s_locationsBefore8Rats,
        [Region.After8RatsBeforeA] = s_locationsAfter8RatsBeforeA,
        [Region.After8RatsBeforeB] = s_locationsAfter8RatsBeforeB,
        [Region.A] = [s_locationA],
        [Region.B] = [s_locationB],
        [Region.AfterABeforeC] = s_locationsAfterABeforeC,
        [Region.AfterBBeforeD] = s_locationsAfterBBeforeD,
        [Region.C] = [s_locationC],
        [Region.D] = [s_locationD],
        [Region.AfterCBefore20Rats] = s_locationsAfterCBefore20Rats,
        [Region.AfterDBefore20Rats] = s_locationsAfterDBefore20Rats,
        [Region.After20RatsBeforeE] = s_locationsAfter20RatsBeforeE,
        [Region.After20RatsBeforeF] = s_locationsAfter20RatsBeforeF,
        [Region.E] = [s_locationE],
        [Region.F] = [s_locationF],
        [Region.TryingForGoal] = [s_locationGoal],
    };

    public static readonly Dictionary<long, Region> s_regionByLocation =
    (
        from kvp in s_locationsByRegion
        from location in kvp.Value
        select (location, kvp.Key)
    ).ToDictionary();

    private readonly SemaphoreSlim _syncRoot = new(1, 1);

    private readonly Random _random = new(seed);

    private readonly AsyncEvent<(Region From, Region To)> _movingToRegionEvent = new();

    private readonly AsyncEvent<Region> _movedToRegionEvent = new();

    private readonly AsyncEvent<long> _completedLocationCheckEvent = new();

    private readonly AsyncEvent<Region> _failedLocationCheckEvent = new();

    private readonly HashSet<ItemType> _receivedItems = [];

    private readonly Dictionary<Region, HashSet<long>> _remainingLocationsInRegion = s_locationsByRegion.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToHashSet());

    private int _numConsecutiveFailureAttempts;

    private Region _currentRegion = Region.Before8Rats;

    private Region? _destinationRegion;

    private int _travelStepsRemaining;

    private int _numRats = 1;

    public event AsyncEventHandler<(Region From, Region To)> MovingToRegion
    {
        add => _movingToRegionEvent.Add(value);
        remove => _movingToRegionEvent.Remove(value);
    }

    public event AsyncEventHandler<Region> MovedToRegion
    {
        add => _movedToRegionEvent.Add(value);
        remove => _movedToRegionEvent.Remove(value);
    }

    public event AsyncEventHandler<long> CompletedLocationCheck
    {
        add => _completedLocationCheckEvent.Add(value);
        remove => _completedLocationCheckEvent.Remove(value);
    }

    public event AsyncEventHandler<Region> FailedLocationCheck
    {
        add => _failedLocationCheckEvent.Add(value);
        remove => _failedLocationCheckEvent.Remove(value);
    }

    public async ValueTask<bool> StepAsync(Player player, CancellationToken cancellationToken)
    {
        await Helper.ConfigureAwaitFalse();

        if (_currentRegion == Region.CompletedGoal)
        {
            return false;
        }

        if (_currentRegion == Region.Traveling)
        {
            await TravelStepAsync(player, cancellationToken);
            return true;
        }

        Region bestNextRegion = DetermineNextRegion(player);
        if (bestNextRegion != _currentRegion)
        {
            await _movingToRegionEvent.InvokeAsync(this, (_currentRegion, bestNextRegion), cancellationToken);
            _travelStepsRemaining = s_regionDistances[(_currentRegion, bestNextRegion)];
            _destinationRegion = bestNextRegion;
            _currentRegion = Region.Traveling;
            return true;
        }

        await TryNextCheckAsync(player, cancellationToken);
        return true;
    }

    public async ValueTask ReceiveItemAsync(ItemType itemType, CancellationToken cancellationToken)
    {
        await Helper.ConfigureAwaitFalse();

        ++_numRats;
        _receivedItems.Add(itemType);
    }

    private static Dictionary<(Region S, Region T), int> ToComplete(Dictionary<Region, Dictionary<Region, bool>> g)
    {
        Region[] keys = [..g.Keys];
        Dictionary<(Region S, Region T), int> result = (
            from s in keys
            let ts = g[s]
            from t in keys
            select ((s, t), s == t ? 0 : ts.TryGetValue(t, out bool w) ? w ? 1 : 0 : int.MaxValue / 2)
        ).ToDictionary();
        foreach (Region k in keys)
        {
            foreach (Region i in keys)
            {
                foreach (Region j in keys)
                {
                    ref int slot = ref CollectionsMarshal.GetValueRefOrNullRef(result, (i, j));
                    int candidate = result[(i, k)] + result[(k, j)];
                    if (slot > candidate)
                    {
                        slot = candidate;
                    }
                }
            }
        }

        return result;
    }

    private static Dictionary<Region, Dictionary<Region, bool>> ToUndirected(Dictionary<Region, Dictionary<Region, bool>> g)
    {
        foreach ((Region s, Dictionary<Region, bool> ts) in g)
        {
            foreach (Region t in ts.Keys)
            {
                g[t][s] = true; // the only freebies are going from "AfterFooBeforeBar" to "Bar".
            }
        }

        return g;
    }

    private async ValueTask TryNextCheckAsync(Player player, CancellationToken cancellationToken)
    {
        await Helper.ConfigureAwaitFalse();

        int roll = NextD20(player, player.DiceModifier[_currentRegion]);
        if (roll < difficultySettings.DifficultyClass[_currentRegion])
        {
            ++_numConsecutiveFailureAttempts;
            await _failedLocationCheckEvent.InvokeAsync(this, _currentRegion, cancellationToken);
            return;
        }

        _numConsecutiveFailureAttempts = 0;
        HashSet<long> remainingLocations = _remainingLocationsInRegion[_currentRegion];
        long locationId = remainingLocations.ElementAt(_random.Next(remainingLocations.Count));
        await _completedLocationCheckEvent.InvokeAsync(this, locationId, cancellationToken);
        remainingLocations.Remove(locationId);
    }

    private async ValueTask TravelStepAsync(Player player, CancellationToken cancellationToken)
    {
        await Helper.ConfigureAwaitFalse();

        _travelStepsRemaining -= player.MovementSpeed;
        if (_travelStepsRemaining >= 0)
        {
            return;
        }

        _currentRegion = _destinationRegion!.Value;
        _travelStepsRemaining = 0;
        await _movedToRegionEvent.InvokeAsync(this, _currentRegion, cancellationToken);
    }

    private int NextD20(Player player, int baseDiceModifier)
    {
        return _random.Next(1, 21) + baseDiceModifier + _numConsecutiveFailureAttempts / player.ConsecutiveFailuresBeforeDiceModifierIncrement;
    }

    private Region DetermineNextRegion(Player player)
    {
        // ASSUMPTION: you don't need help to figure out what to do in Traveling or CompletedGoal.
        //
        // if the goal is open, then you should ALWAYS try for it.
        if (_numRats >= 20 &&
            _receivedItems.Overlaps([ItemType.E, ItemType.F]) &&
            (
                _receivedItems.IsSupersetOf([ItemType.A, ItemType.C]) ||
                _receivedItems.IsSupersetOf([ItemType.B, ItemType.D])
            ))
        {
            return Region.TryingForGoal;
        }

        Region bestRegion = _currentRegion;
        int bestRegionDifficultyClass = EffectiveDifficultyClass(bestRegion);
        int bestRegionDistance = 0;

        // if your current region is empty, then you should favor moving ANYWHERE else.
        if (_remainingLocationsInRegion[bestRegion].Count == 0)
        {
            bestRegionDifficultyClass = int.MaxValue;
        }

        HandleUnlockedRegion(Region.Before8Rats);

        if (_numRats < 8)
        {
            return bestRegion;
        }

        HandleUnlockedRegion(Region.After8RatsBeforeA);
        HandleUnlockedRegion(Region.A);
        HandleUnlockedRegion(Region.After8RatsBeforeB);
        HandleUnlockedRegion(Region.B);

        bool receivedCOrD = false;
        if (_receivedItems.Contains(ItemType.A))
        {
            HandleUnlockedRegion(Region.AfterABeforeC);
            HandleUnlockedRegion(Region.C);
            if (_receivedItems.Contains(ItemType.C))
            {
                HandleUnlockedRegion(Region.AfterCBefore20Rats);
                receivedCOrD = true;
            }
        }

        if (_receivedItems.Contains(ItemType.B))
        {
            HandleUnlockedRegion(Region.AfterBBeforeD);
            HandleUnlockedRegion(Region.D);
            if (_receivedItems.Contains(ItemType.D))
            {
                HandleUnlockedRegion(Region.AfterDBefore20Rats);
                receivedCOrD = true;
            }
        }

        if (receivedCOrD && _numRats >= 20)
        {
            HandleUnlockedRegion(Region.After20RatsBeforeE);
            HandleUnlockedRegion(Region.E);
            HandleUnlockedRegion(Region.After20RatsBeforeF);
            HandleUnlockedRegion(Region.F);
        }

        return bestRegion;

        void HandleUnlockedRegion(Region testRegion)
        {
            if (_remainingLocationsInRegion[testRegion].Count == 0)
            {
                return;
            }

            int testDifficultyClass = EffectiveDifficultyClass(testRegion);
            if (testDifficultyClass < bestRegionDifficultyClass ||
                (testDifficultyClass == bestRegionDifficultyClass && s_regionDistances[(_currentRegion, testRegion)] < bestRegionDistance))
            {
                bestRegion = testRegion;
                bestRegionDifficultyClass = testDifficultyClass;
                bestRegionDistance = s_regionDistances[(_currentRegion, bestRegion)];
            }
        }

        int EffectiveDifficultyClass(Region region) => difficultySettings.DifficultyClass[region] - player.DiceModifier[region];
    }
}
