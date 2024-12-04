using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Runtime.InteropServices;

namespace Autopelago;

public sealed partial class Game
{
    private readonly HashSet<string> _clearableLandmarks = new(GameDefinitions.Instance.LandmarkRegions.Count);

    private readonly HashSet<string> _checkableRegions = new(GameDefinitions.Instance.AllRegions.Count) { GameDefinitions.Instance.StartRegion.Key };

    private int _lastReceivedItemsCount;

    private int _lastCheckedLocationsCount;

    private bool UpdateTargetLocation()
    {
        LocationDefinitionModel prevTargetLocation = TargetLocation;
        TargetLocationReason = MoveBestTargetLocation();
        _targetLocationPathEnumerator ??= GetPath(CurrentLocation, TargetLocation)!.GetEnumerator();
        if (TargetLocation == prevTargetLocation)
        {
            return false;
        }

        using IEnumerator<LocationDefinitionModel> _ = _targetLocationPathEnumerator;
        _targetLocationPathEnumerator = GetPath(CurrentLocation, TargetLocation)!.GetEnumerator();
        return true;
    }

    private TargetLocationReason MoveBestTargetLocation()
    {
        if (StartledCounter > 0)
        {
            TargetLocation = GameDefinitions.Instance.StartLocation;
            return TargetLocationReason.Startled;
        }

        if (CanReachGoal() && GetPath(CurrentLocation, GameDefinitions.Instance.GoalLocation) is { } path0)
        {
            TargetLocation = path0.Prepend(CurrentLocation).FirstOrDefault(p => p.Region is LandmarkRegionDefinitionModel && !CheckedLocations[p]) ?? GameDefinitions.Instance.GoalLocation;
            return TargetLocationReason.GoMode;
        }

        foreach (LocationDefinitionModel priorityPriorityLocation in _priorityPriorityLocations)
        {
            if (GetPath(CurrentLocation, priorityPriorityLocation) is not { } path)
            {
                continue;
            }

            TargetLocation = path.Prepend(CurrentLocation).FirstOrDefault(p => p.Region is LandmarkRegionDefinitionModel && !CheckedLocations[p]) ?? priorityPriorityLocation;
            return TargetLocationReason.PriorityPriority;
        }

        foreach (LocationDefinitionModel priorityLocation in _priorityLocations)
        {
            if (GetPath(CurrentLocation, priorityLocation) is not { } path)
            {
                continue;
            }

            TargetLocation = path.Prepend(CurrentLocation).FirstOrDefault(p => p.Region is LandmarkRegionDefinitionModel && !CheckedLocations[p]) ?? priorityLocation;;
            return TargetLocationReason.Priority;
        }

        if (FindClosestUncheckedLocation(CurrentLocation) is { } closestReachableUnchecked)
        {
            TargetLocation = closestReachableUnchecked;
            return TargetLocationReason.ClosestReachableUnchecked;
        }

        TargetLocation = CurrentLocation;
        return TargetLocationReason.NowhereUsefulToMove;
    }

    private bool CanReach(LocationDefinitionModel location)
    {
        // TODO: optimize. this isn't speed-critical, and I'd rather release quickly.
        return GetPath(GameDefinitions.Instance.StartLocation, location) is not null;
    }

    private readonly PriorityQueue<(RegionDefinitionModel Region, Direction Direction), int> _pq = new(GameDefinitions.Instance.AllRegions.Count);
    private readonly HashSet<string> _visitedRegions = new(GameDefinitions.Instance.AllRegions.Count);

    private LocationDefinitionModel? FindClosestUncheckedLocation(LocationDefinitionModel currentLocation)
    {
        return
            FindClosestUncheckedLocation(currentLocation, false) ??
            FindClosestUncheckedLocation(currentLocation, true);
    }

    private LocationDefinitionModel? FindClosestUncheckedLocation(LocationDefinitionModel currentLocation, bool relyOnMercyFactor)
    {
        RecalculateAccessibility();

        // quick short-circuit: often, this will get called while we're already standing on exactly
        // the closest unchecked location (perhaps because we failed at clearing it). let's optimize
        // for that case here, even though it should not affect correctness.
        SmallBitArray checkedLocationsInCurrentRegion = _checkedLocations![currentLocation.Key.RegionKey];
        if (!checkedLocationsInCurrentRegion[currentLocation.Key.N] && (relyOnMercyFactor || _checkableRegions.Contains(currentLocation.Key.RegionKey)))
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
        if (!checkedLocationsInCurrentRegion.HasAllSet && (relyOnMercyFactor || _checkableRegions.Contains(currentLocation.Key.RegionKey)))
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
        foreach ((RegionDefinitionModel connectedRegion, Direction direction) in GameDefinitions.Instance.ConnectedRegions[currentLocation.Key.RegionKey])
        {
            if (GameDefinitions.Instance.FillerRegions.ContainsKey(connectedRegion.Key) ||
                _checkedLocations![connectedRegion.Key][0] ||
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
            SmallBitArray checkedLocationsInConnectedRegion = _checkedLocations![connectedRegion.Key];
            if (!checkedLocationsInConnectedRegion.HasAllSet && (relyOnMercyFactor || _checkableRegions.Contains(connectedRegion.Key)))
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
            foreach ((RegionDefinitionModel nextConnectedRegion, Direction nextDirection) in GameDefinitions.Instance.ConnectedRegions[connectedRegion.Key])
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
                    (GameDefinitions.Instance.FillerRegions.ContainsKey(nextConnectedRegion.Key) ||
                     _checkedLocations![nextConnectedRegion.Key][0] ||
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

    private readonly Queue<ImmutableList<(RegionDefinitionModel Region, Direction? Direction)>> _qqq = new(GameDefinitions.Instance.AllRegions.Count);
    private IEnumerable<LocationDefinitionModel> GetStartledPath(LocationDefinitionModel currentLocation)
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
        _qqq.Clear();
        _visitedRegions.Clear();
        _qqq.Enqueue([(currentLocation.Region, null)]);
        _visitedRegions.Add(currentLocation.Key.RegionKey);
        while (_qqq.TryDequeue(out ImmutableList<(RegionDefinitionModel Region, Direction? Direction)>? regionPath))
        {
            foreach ((RegionDefinitionModel connectedRegion, Direction direction) in GameDefinitions.Instance.ConnectedRegions[regionPath[^1].Region.Key])
            {
                if (!GameDefinitions.Instance.FillerRegions.ContainsKey(connectedRegion.Key) &&
                    !_checkedLocations![connectedRegion.Key][0])
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

                if (_visitedRegions.Add(connectedRegion.Key))
                {
                    _qqq.Enqueue(regionPath.Add((connectedRegion, direction)));
                }
            }
        }
    }

    private bool CanReachGoal()
    {
        return _clearableLandmarks.Contains(GameDefinitions.Instance.GoalRegion.Key);
    }

    private readonly Queue<(RegionDefinitionModel Region, Direction Direction)> _q = new(GameDefinitions.Instance.AllRegions.Count);
    private readonly List<LocationDefinitionModel> _pathLocations = new(GameDefinitions.Instance.LocationsByName.Count);
    private readonly Dictionary<string, (RegionDefinitionModel Region, Direction Direction)> _prev = new(GameDefinitions.Instance.LocationsByName.Count);
    private readonly Stack<(RegionDefinitionModel Region, Direction Direction)> _regionStack = new(GameDefinitions.Instance.AllRegions.Count);
    private IEnumerable<LocationDefinitionModel>? _prevPath;
    private LocationKey _currentLocationForPrevPath = LocationKey.For("nonexistent");
    private LocationKey _targetLocationForPrevPath = LocationKey.For("nonexistent");

    private IEnumerable<LocationDefinitionModel>? GetPath(LocationDefinitionModel currentLocation, LocationDefinitionModel targetLocation)
    {
        if ((_currentLocationForPrevPath, _targetLocationForPrevPath) != (currentLocation.Key, targetLocation.Key))
        {
            _currentLocationForPrevPath = currentLocation.Key;
            _targetLocationForPrevPath = targetLocation.Key;
            _prevPath = GetPathCore(currentLocation, targetLocation);
        }

        return _prevPath;
    }
    private IEnumerable<LocationDefinitionModel>? GetPathCore(LocationDefinitionModel currentLocation, LocationDefinitionModel targetLocation)
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
            !_checkedLocations![landmark.Key][0] &&
            !_clearableLandmarks.Contains(landmark.Key))
        {
            return null;
        }

        _q.Clear();
        _prev.Clear();
        foreach ((RegionDefinitionModel connectedRegion, Direction direction) in GameDefinitions.Instance.ConnectedRegions[currentLocation.Region.Key])
        {
            if (GameDefinitions.Instance.FillerRegions.ContainsKey(connectedRegion.Key) ||
                _checkedLocations![connectedRegion.Key][0] ||
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
                foreach ((RegionDefinitionModel nextConnectedRegion, Direction nextDirection) in GameDefinitions.Instance.ConnectedRegions[connectedRegion.Key])
                {
                    if (nextConnectedRegion.Key != currentLocation.Key.RegionKey &&
                        _prev.TryAdd(nextConnectedRegion.Key, (connectedRegion, nextDirection)) &&
                        (GameDefinitions.Instance.FillerRegions.ContainsKey(nextConnectedRegion.Key) ||
                         _checkedLocations![nextConnectedRegion.Key][0] ||
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

    private readonly Dictionary<string, SmallBitArray> _visitedLocations = GameDefinitions.Instance.AllRegions.Values.ToDictionary(r => r.Key, r => new SmallBitArray(r.Locations.Length));
    private IEnumerable<LocationDefinitionModel> GetClosestLocationsWithItemFlags(LocationDefinitionModel currentLocation, ArchipelagoItemFlags flags)
    {
        RecalculateAccessibility();

        FrozenSet<LocationKey> spoilerData = _spoilerData![flags];

        // TODO: optimize this, it's getting late.
        foreach (string regionKey in _visitedLocations.Keys)
        {
            (CollectionsMarshal.GetValueRefOrNullRef(_visitedLocations, regionKey)).Clear();
        }

        Queue<LocationDefinitionModel> q = [];
        q.Enqueue(currentLocation);
        CollectionsMarshal.GetValueRefOrAddDefault(_visitedLocations, currentLocation.Key.RegionKey, out _)[currentLocation.Key.N] = true;
        while (q.TryDequeue(out LocationDefinitionModel? loc))
        {
            if (spoilerData.Contains(loc.Key) && !_checkedLocations![loc.Key])
            {
                yield return loc;
            }

            foreach ((LocationDefinitionModel connectedLocation, _) in GameDefinitions.Instance.ConnectedLocations[loc.Key])
            {
                if (!_visitedLocations[connectedLocation.Key.RegionKey][connectedLocation.Key.N] &&
                    (GameDefinitions.Instance.FillerRegions.ContainsKey(connectedLocation.Key.RegionKey) ||
                     _checkedLocations![connectedLocation.Key.RegionKey][0] ||
                     _clearableLandmarks.Contains(connectedLocation.Key.RegionKey)))
                {
                    CollectionsMarshal.GetValueRefOrAddDefault(_visitedLocations, connectedLocation.Key.RegionKey, out _)[connectedLocation.Key.N] = true;
                    q.Enqueue(connectedLocation);
                }
            }
        }
    }

    private void RecalculateAccessibility()
    {
        while (_lastReceivedItemsCount < _receivedItems!.Count)
        {
            if (GameDefinitions.Instance.ProgressionItemNames.Contains(_receivedItems[_lastReceivedItemsCount++].Name))
            {
                RecalculateClearable();
                break;
            }
        }

        _lastReceivedItemsCount = _receivedItems.Count;

        ReadOnlyCollection<LocationDefinitionModel> checkedLocationsOrder = _checkedLocations!.Order;
        while (_lastCheckedLocationsCount < _checkedLocations.Count)
        {
            _clearableLandmarks.Remove(checkedLocationsOrder[_lastCheckedLocationsCount++].Key.RegionKey);
        }
    }

    private readonly Queue<(RegionDefinitionModel Region, IReadOnlyList<ItemDefinitionModel> ReceivedItems)> _qq = new(GameDefinitions.Instance.AllRegions.Count);
    private void RecalculateClearable()
    {
        int rollModifier = GetPermanentRollModifier(RatCount);
        _qq.Clear();
        _visitedRegions.Clear();

        _qq.Enqueue((GameDefinitions.Instance.StartRegion, _receivedItems!));
        while (_qq.TryDequeue(out var tup))
        {
            (RegionDefinitionModel region, IReadOnlyList<ItemDefinitionModel> receivedItems) = tup;
            _visitedRegions.Add(region.Key);
            if (20 + rollModifier >= region.AbilityCheckDC)
            {
                _checkableRegions.Add(region.Key);
            }

            if (GameDefinitions.Instance.LandmarkRegions.TryGetValue(region.Key, out LandmarkRegionDefinitionModel? landmark) &&
                !_checkedLocations![region.Key][0] &&
                !_clearableLandmarks.Contains(region.Key))
            {
                if (!landmark.Requirement.Satisfied(receivedItems))
                {
                    continue;
                }

                _clearableLandmarks.Add(region.Key);
                if (landmark.Locations[0].RewardIsFixed)
                {
                    receivedItems = [.. receivedItems, landmark.Locations[0].UnrandomizedItem!];
                }
            }

            foreach ((RegionDefinitionModel connectedRegion, _) in GameDefinitions.Instance.ConnectedRegions[region.Key])
            {
                if (_visitedRegions.Add(connectedRegion.Key))
                {
                    _qq.Enqueue((connectedRegion, receivedItems));
                }
            }
        }
    }
}
