using System.Collections;
using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Collections.ObjectModel;

namespace Autopelago;

public sealed class RouteCalculator
{
    private readonly FrozenDictionary<ArchipelagoItemFlags, FrozenSet<LocationKey>> _spoilerData;

    private readonly ReadOnlyCollection<ItemDefinitionModel> _receivedItems;

    private readonly CheckedLocations _checkedLocations;

    private readonly FrozenDictionary<string, FillerRegionDefinitionModel> _fillerRegions = GameDefinitions.Instance.FillerRegions;

    private readonly FrozenDictionary<string, LandmarkRegionDefinitionModel> _landmarkRegions = GameDefinitions.Instance.LandmarkRegions;

    private readonly FrozenDictionary<RegionDefinitionModel, ImmutableArray<(RegionDefinitionModel Region, Direction Direction)>> _connectedRegions = GameDefinitions.Instance.ConnectedRegions;

    private readonly FrozenSet<ItemDefinitionModel> _progressionItems = [.. GameDefinitions.Instance.ProgressionItems.Values];

    private readonly HashSet<string> _clearableLandmarks = [];

    private readonly PriorityQueue<(RegionDefinitionModel Region, Direction Direction), int> _pq = new();

    private readonly Queue<(RegionDefinitionModel Region, Direction Direction)> _q = new();

    private readonly HashSet<string> _visitedRegions = [];

    private readonly Dictionary<string, (RegionDefinitionModel Region, Direction Direction)> _prev = [];

    private readonly Stack<(RegionDefinitionModel Region, Direction Direction)> _regionStack = [];

    private readonly List<LocationDefinitionModel> _pathLocations = [];

    private int _lastReceivedItemsCount;

    private int _lastCheckedLocationsCount;

    public RouteCalculator(FrozenDictionary<LocationKey, ArchipelagoItemFlags> spoilerData, ReadOnlyCollection<ItemDefinitionModel> receivedItems, CheckedLocations checkedLocations)
    {
        _spoilerData = spoilerData.GroupBy(kvp => kvp.Value, kvp => kvp.Key).ToFrozenDictionary(g => g.Key, g => g.ToFrozenSet());
        _receivedItems = receivedItems;
        _checkedLocations = checkedLocations;
    }

    public bool CanReach(LocationDefinitionModel location)
    {
        // TODO: optimize. this isn't speed-critical, and I'd rather release quickly.
        return GetPath(GameDefinitions.Instance.StartLocation, location) is not null;
    }

    public LocationDefinitionModel? FindClosestUncheckedLocation(LocationDefinitionModel currentLocation)
    {
        RecalculateAccessibility();

        // quick short-circuit: often, this will get called while we're already standing on exactly
        // the closest unchecked location (perhaps because we failed at clearing it). let's optimize
        // for that case here, even though it should not affect correctness.
        SmallBitArray checkedLocationsInCurrentRegion = _checkedLocations[currentLocation.Key.RegionKey];
        if (!checkedLocationsInCurrentRegion[currentLocation.Key.N])
        {
            return currentLocation;
        }

        // we're not already LITERALLY standing on an unchecked location, so do the full version.
        int backwardLocationsInCurrentRegion = currentLocation.Key.N;
        int forwardLocationsInCurrentRegion = currentLocation.Region.Locations.Length - currentLocation.Key.N - 1;

        int bestDistance = int.MaxValue;
        LocationKey? bestLocationKey = null;

        // in probably a VERY large majority of cases when we find ourselves in a filler region, the
        // closest unchecked location will either be in that same filler region, one of the (up to)
        // two adjacent landmark regions, or far enough away that there's an entire filler region's
        // worth of checked locations between the two.
        if (!checkedLocationsInCurrentRegion.HasAllSet)
        {
            // this won't necessarily be the final answer, but it will be a solid upper bound.
            for (int i = currentLocation.Key.N + 1; i < checkedLocationsInCurrentRegion.Length; i++)
            {
                if (checkedLocationsInCurrentRegion[i])
                {
                    continue;
                }

                bestDistance = i - currentLocation.Key.N;
                bestLocationKey = currentLocation.Key with { N = i };
                break;
            }

            for (int i = currentLocation.Key.N - 1; i >= 0; --i)
            {
                if (checkedLocationsInCurrentRegion[i])
                {
                    continue;
                }

                int distance = currentLocation.Key.N - i;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestLocationKey = currentLocation.Key with { N = i };
                }

                break;
            }
        }

        if (bestDistance < backwardLocationsInCurrentRegion && bestDistance < forwardLocationsInCurrentRegion)
        {
            // we're in a filler region, and the closest unchecked location in our own region is
            // closer than either of the (up to) two landmarks that we're joining.
            return GameDefinitions.Instance.LocationsByKey[bestLocationKey.GetValueOrDefault()];
        }

        // by this point, at least one of the following is true:
        //
        // 1. we are standing on a landmark that has already been checked.
        // 2. we are standing in a filler region whose locations have ALL been checked.
        // 3. we are standing in a filler region whose closest unchecked location is further away
        //    than the landmark (if any) at the end of the region in the other direction.
        //
        // in all of those cases, we must examine at least one region other than the one that we're
        // currently in (minimally, to prove that there's no region in some direction).
        _pq.Clear();
        _visitedRegions.Clear();
        _visitedRegions.Add(currentLocation.Key.RegionKey);
        foreach ((RegionDefinitionModel connectedRegion, Direction direction) in _connectedRegions[currentLocation.Region])
        {
            if (_fillerRegions.ContainsKey(connectedRegion.Key) ||
                _checkedLocations[connectedRegion.Key][0] ||
                _clearableLandmarks.Contains(connectedRegion.Key))
            {
                _visitedRegions.Add(connectedRegion.Key);
                _pq.Enqueue((connectedRegion, direction), direction switch
                {
                    Direction.TowardsGoal => forwardLocationsInCurrentRegion,
                    _ => backwardLocationsInCurrentRegion,
                } + 1); // +1 gets us to the first location in the next region.
            }
        }

        while (_pq.TryDequeue(out var tup, out int extraDistance))
        {
            if (extraDistance >= bestDistance)
            {
                return GameDefinitions.Instance.LocationsByKey[bestLocationKey.GetValueOrDefault()];
            }

            (RegionDefinitionModel connectedRegion, Direction direction) = tup;
            SmallBitArray checkedLocationsInConnectedRegion = _checkedLocations[connectedRegion.Key];
            if (!checkedLocationsInConnectedRegion.HasAllSet)
            {
                if (direction == Direction.TowardsGoal)
                {
                    for (int i = 0; i < checkedLocationsInConnectedRegion.Length && extraDistance + i < bestDistance; i++)
                    {
                        if (checkedLocationsInConnectedRegion[i])
                        {
                            continue;
                        }

                        bestDistance = extraDistance + i;
                        bestLocationKey = new() { RegionKey = connectedRegion.Key, N = i };
                    }
                }
                else
                {
                    for (int i = 0; i < checkedLocationsInConnectedRegion.Length && extraDistance + i < bestDistance; i++)
                    {
                        if (checkedLocationsInConnectedRegion[^(i + 1)])
                        {
                            continue;
                        }

                        bestDistance = extraDistance + i;
                        bestLocationKey = new() { RegionKey = connectedRegion.Key, N = (^(i + 1)).GetOffset(checkedLocationsInConnectedRegion.Length) };
                        break;
                    }
                }
            }

            // routes from here will take at least one step into the next region, so add that here.
            // the exact details of how far we will need to walk through the current region to get
            // to the next one will depend on the direction we face when entering the next region.
            ++extraDistance;
            foreach ((RegionDefinitionModel nextConnectedRegion, Direction nextDirection) in _connectedRegions[connectedRegion])
            {
                // at the time of writing, we technically don't need this to be conditional: only
                // landmark regions can connect to more than one other region in a given direction,
                // and it costs the same to route through them regardless of which direction a path
                // turns through them. it costs practically nothing to do this check, though, so I'm
                // going to do it if for no other reason than to make it possible to fix a few odd
                // cases in the sewer level that look different on the map than the actual path that
                // the game implements by creating new filler regions that connect to other filler
                // regions to make everything visually consistent.
                //
                // the -1 part below is because the path up to this point has already gotten us to
                // the first location in the region we'd be coming from, so the only thing that we
                // need to do is add on the number of ADDITIONAL locations within our region that we
                // need to follow to get to the location in the current region that exits to the
                // connected region.
                int nextExtraDistance = nextDirection == direction
                    ? extraDistance + checkedLocationsInConnectedRegion.Length - 1
                    : extraDistance;

                if (nextExtraDistance >= bestDistance)
                {
                    continue;
                }

                if (_visitedRegions.Add(nextConnectedRegion.Key) &&
                    (_fillerRegions.ContainsKey(nextConnectedRegion.Key) ||
                     _checkedLocations[nextConnectedRegion.Key][0] ||
                     _clearableLandmarks.Contains(nextConnectedRegion.Key)))
                {
                    _pq.Enqueue((nextConnectedRegion, nextDirection), nextExtraDistance);
                }
            }
        }

        return bestLocationKey is LocationKey finalResultKey
            ? GameDefinitions.Instance.LocationsByKey[finalResultKey]
            : null;
    }

    public IEnumerable<LocationDefinitionModel> GetStartledPath(LocationDefinitionModel currentLocation)
    {
        RecalculateAccessibility();

        if (currentLocation.Key.RegionKey == GameDefinitions.Instance.StartRegion.Key)
        {
            // trivial.
            for (int i = currentLocation.Key.N - 1; i >= 0; --i)
            {
                yield return GameDefinitions.Instance.LocationsByKey[currentLocation.Key with { N = i }];
            }

            yield break;
        }

        // here's the thing about "Startled". I think it's technically possible that a simple BFS by
        // just the regions (regardless of the number of locations in each) is not guaranteed to
        // yield a shortest path to the start. but that doesn't matter. the player is spooked. they
        // aren't necessarily making the best decisions at the time either. so I'm going to cheat:
        // find a shortest path by number of regions, then yield the locations along that path. it's
        // absolutely not worth the extra complication to do any better than that.
        Queue<ImmutableList<(RegionDefinitionModel Region, Direction? Direction)>> q = new();
        q.Enqueue([(currentLocation.Region, null)]);
        HashSet<string> visitedRegions = [currentLocation.Key.RegionKey];
        while (q.TryDequeue(out ImmutableList<(RegionDefinitionModel Region, Direction? Direction)>? regionPath))
        {
            foreach ((RegionDefinitionModel connectedRegion, Direction direction) in _connectedRegions[regionPath[^1].Region])
            {
                if (!_fillerRegions.ContainsKey(connectedRegion.Key) &&
                    !_checkedLocations[connectedRegion.Key][0])
                {
                    continue;
                }

                if (connectedRegion.Key == GameDefinitions.Instance.StartRegion.Key)
                {
                    // found it. follow the path from the current region. it starts off a little
                    // complicated because we probably start in the middle.
                    ImmutableArray<LocationDefinitionModel> startRegionLocations = currentLocation.Region.Locations;
                    switch (regionPath.ElementAtOrDefault(1).Direction ?? direction)
                    {
                        case Direction.TowardsGoal:
                            for (int i = currentLocation.Key.N + 1; i < startRegionLocations.Length; i++)
                            {
                                yield return startRegionLocations[i];
                            }

                            break;

                        default:
                            for (int i = currentLocation.Key.N - 1; i >= 0; --i)
                            {
                                yield return startRegionLocations[i];
                            }

                            break;
                    }

                    // OK, now just yield all the full regions all the way through.
                    foreach ((RegionDefinitionModel nextRegion, Direction? nextDirection) in regionPath.Skip(1).Append((connectedRegion, direction)))
                    {
                        foreach (LocationDefinitionModel loc in nextDirection == Direction.TowardsGoal ? nextRegion.Locations : nextRegion.Locations.Reverse())
                        {
                            yield return loc;
                        }
                    }

                    yield break;
                }

                if (visitedRegions.Add(connectedRegion.Key))
                {
                    q.Enqueue(regionPath.Add((connectedRegion, direction)));
                }
            }
        }
    }

    public bool CanReachGoal()
    {
        return _clearableLandmarks.Contains(GameDefinitions.Instance.GoalRegion.Key);
    }

    public IEnumerable<LocationDefinitionModel>? GetPath(LocationDefinitionModel currentLocation, LocationDefinitionModel targetLocation)
    {
        if (currentLocation.Key.RegionKey == targetLocation.Key.RegionKey)
        {
            LocationKey currentLocationKey = currentLocation.Key;
            if (currentLocationKey.N == targetLocation.Key.N)
            {
                return [targetLocation];
            }

            ImmutableArray<LocationDefinitionModel> currentRegionLocations = currentLocation.Region.Locations;
            if (currentLocation.Key.N > targetLocation.Key.N)
            {
                return Enumerable.Range(1, currentLocationKey.N - targetLocation.Key.N)
                    .Select(n => currentRegionLocations[currentLocationKey.N - n]);
            }

            return Enumerable.Range(1, targetLocation.Key.N - currentLocationKey.N)
                .Select(n => currentRegionLocations[currentLocationKey.N + n]);
        }

        RecalculateAccessibility();
        if (targetLocation.Region is LandmarkRegionDefinitionModel landmark &&
            !_checkedLocations[landmark.Key][0] &&
            !_clearableLandmarks.Contains(landmark.Key))
        {
            return null;
        }

        _q.Clear();
        _prev.Clear();
        foreach ((RegionDefinitionModel connectedRegion, Direction direction) in _connectedRegions[currentLocation.Region])
        {
            if (_fillerRegions.ContainsKey(connectedRegion.Key) ||
                _checkedLocations[connectedRegion.Key][0] ||
                _clearableLandmarks.Contains(connectedRegion.Key))
            {
                _q.Enqueue((connectedRegion, direction));
                _prev.Add(connectedRegion.Key, (currentLocation.Region, direction));
            }
        }

        while (_q.TryDequeue(out var tup))
        {
            (RegionDefinitionModel connectedRegion, _) = tup;
            if (connectedRegion.Key != targetLocation.Key.RegionKey)
            {
                foreach ((RegionDefinitionModel nextConnectedRegion, Direction nextDirection) in _connectedRegions[connectedRegion])
                {
                    if (nextConnectedRegion.Key != currentLocation.Key.RegionKey &&
                        _prev.TryAdd(nextConnectedRegion.Key, (connectedRegion, nextDirection)) &&
                        (_fillerRegions.ContainsKey(nextConnectedRegion.Key) ||
                         _checkedLocations[nextConnectedRegion.Key][0] ||
                         _clearableLandmarks.Contains(nextConnectedRegion.Key)))
                    {
                        _q.Enqueue((nextConnectedRegion, nextDirection));
                    }
                }

                continue;
            }

            _regionStack.Clear();
            do
            {
                _regionStack.Push(tup);
            } while (_prev.TryGetValue(tup.Region.Key, out tup));

            _pathLocations.Clear();
            while (_regionStack.TryPop(out tup))
            {
                (RegionDefinitionModel nextRegion, Direction direction) = tup;
                if (nextRegion.Key == currentLocation.Key.RegionKey)
                {
                    switch (direction)
                    {
                        case Direction.TowardsGoal:
                            _pathLocations.AddRange(nextRegion.Locations.AsSpan((currentLocation.Key.N + 1)..));
                            break;

                        default:
                            int oldCount = _pathLocations.Count;
                            _pathLocations.AddRange(nextRegion.Locations.AsSpan(..currentLocation.Key.N));
                            _pathLocations.Reverse(oldCount, _pathLocations.Count - oldCount);
                            break;
                    }
                }
                else if (nextRegion.Key == targetLocation.Key.RegionKey)
                {
                    switch (direction)
                    {
                        case Direction.TowardsGoal:
                            _pathLocations.AddRange(nextRegion.Locations.AsSpan(..(targetLocation.Key.N + 1)));
                            break;

                        default:
                            int oldCount = _pathLocations.Count;
                            _pathLocations.AddRange(nextRegion.Locations.AsSpan(targetLocation.Key.N..));
                            _pathLocations.Reverse(oldCount, _pathLocations.Count - oldCount);
                            break;
                    }
                }
                else
                {
                    switch (direction)
                    {
                        case Direction.TowardsGoal:
                            _pathLocations.AddRange(nextRegion.Locations);
                            break;

                        default:
                            int oldCount = _pathLocations.Count;
                            _pathLocations.AddRange(nextRegion.Locations);
                            _pathLocations.Reverse(oldCount, _pathLocations.Count - oldCount);
                            break;
                    }
                }
            }

            return _pathLocations.ToArray();
        }

        return null;
    }

    public IEnumerable<LocationDefinitionModel> GetClosestLocationsWithItemFlags(LocationDefinitionModel currentLocation, ArchipelagoItemFlags flags)
    {
        RecalculateAccessibility();

        FrozenSet<LocationKey> spoilerData = _spoilerData[flags];

        // TODO: optimize this, it's getting late.
        FrozenDictionary<string, BitArray> visitedLocations = GameDefinitions.Instance.AllRegions.Values.ToFrozenDictionary(r => r.Key, r => new BitArray(r.Locations.Length));
        Queue<LocationDefinitionModel> q = [];
        q.Enqueue(currentLocation);
        visitedLocations[currentLocation.Key.RegionKey][currentLocation.Key.N] = true;
        while (q.TryDequeue(out LocationDefinitionModel? loc))
        {
            if (spoilerData.Contains(loc.Key))
            {
                yield return loc;
            }

            foreach ((LocationDefinitionModel connectedLocation, _) in GameDefinitions.Instance.ConnectedLocations[loc])
            {
                if (!visitedLocations[connectedLocation.Key.RegionKey][connectedLocation.Key.N] &&
                    (_fillerRegions.ContainsKey(connectedLocation.Key.RegionKey) ||
                     _checkedLocations[connectedLocation.Key.RegionKey][0] ||
                     _clearableLandmarks.Contains(connectedLocation.Key.RegionKey)))
                {
                    visitedLocations[connectedLocation.Key.RegionKey][connectedLocation.Key.N] = true;
                    q.Enqueue(connectedLocation);
                }
            }
        }
    }

    private void RecalculateAccessibility()
    {
        while (_lastReceivedItemsCount < _receivedItems.Count)
        {
            if (_progressionItems.Contains(_receivedItems[_lastReceivedItemsCount++]))
            {
                RecalculateClearable();
                break;
            }
        }

        _lastReceivedItemsCount = _receivedItems.Count;

        ReadOnlyCollection<LocationDefinitionModel> checkedLocationsOrder = _checkedLocations.Order;
        while (_lastCheckedLocationsCount < _checkedLocations.Count)
        {
            _clearableLandmarks.Remove(checkedLocationsOrder[_lastCheckedLocationsCount++].Key.RegionKey);
        }
    }

    private void RecalculateClearable()
    {
        Queue<(RegionDefinitionModel Region, ImmutableArray<ItemDefinitionModel> ReceivedItems)> q = [];
        q.Enqueue((GameDefinitions.Instance.StartRegion, [.. _receivedItems]));
        HashSet<string> seenRegions = [];
        while (q.TryDequeue(out var tup))
        {
            (RegionDefinitionModel region, ImmutableArray<ItemDefinitionModel> receivedItems) = tup;
            seenRegions.Add(region.Key);
            if (_landmarkRegions.TryGetValue(region.Key, out LandmarkRegionDefinitionModel? landmark) &&
                !_checkedLocations[region.Key][0] &&
                !_clearableLandmarks.Contains(region.Key))
            {
                if (!landmark.Requirement.Satisfied(receivedItems))
                {
                    continue;
                }

                _clearableLandmarks.Add(region.Key);
                if (landmark.Locations[0].RewardIsFixed)
                {
                    receivedItems = receivedItems.Add(landmark.Locations[0].UnrandomizedItem!);
                }
            }

            foreach ((RegionDefinitionModel connectedRegion, _) in _connectedRegions[region])
            {
                if (seenRegions.Add(connectedRegion.Key))
                {
                    q.Enqueue((connectedRegion, receivedItems));
                }
            }
        }
    }
}
