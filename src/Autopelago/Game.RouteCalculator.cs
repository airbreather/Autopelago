using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Runtime.InteropServices;

namespace Autopelago;

public sealed partial class Game
{
    private readonly HashSet<string> _hardLockedRegions = new(ImmutableCollectionsMarshal.AsArray(GameDefinitions.Instance.AllRegions.Keys)!);

    private readonly HashSet<string> _softLockedRegions = new(ImmutableCollectionsMarshal.AsArray(GameDefinitions.Instance.AllRegions.Keys)!);

    private bool _everCalculatedPathToTarget;
    private bool UpdateTargetLocation()
    {
        LocationKey prevTargetLocation = TargetLocation.Key;
        TargetLocationReason = MoveBestTargetLocation();
        if (TargetLocation.Key == prevTargetLocation && _everCalculatedPathToTarget)
        {
            return false;
        }

        _pathToTarget.Clear();
        bool first = true;
        foreach (LocationDefinitionModel l in GetPath(CurrentLocation, TargetLocation))
        {
            if (first)
            {
                first = false;
            }
            else
            {
                _pathToTarget.Enqueue(l);
            }
        }

        _everCalculatedPathToTarget = true;
        return TargetLocation.Key != prevTargetLocation;
    }

    private TargetLocationReason MoveBestTargetLocation()
    {
        if (StartledCounter > 0)
        {
            TargetLocation = GameDefinitions.Instance.StartLocation;
            return TargetLocationReason.Startled;
        }

        TargetLocationReason result = default;
        LocationDefinitionModel? ultimateTargetLocation = null;
        if (!_hardLockedRegions.Contains(GameDefinitions.Instance.GoalRegion.Key))
        {
            ultimateTargetLocation = GameDefinitions.Instance.GoalLocation;
            result = TargetLocationReason.GoMode;
        }

        if (ultimateTargetLocation is null)
        {
            foreach (LocationKey l in _priorityPriorityLocations)
            {
                if (!_hardLockedRegions.Contains(l.RegionKey))
                {
                    ultimateTargetLocation = GameDefinitions.Instance.LocationsByKey[l];
                    result = TargetLocationReason.PriorityPriority;
                    break;
                }
            }

            if (ultimateTargetLocation is null)
            {
                foreach (LocationKey l in _priorityLocations)
                {
                    if (!_hardLockedRegions.Contains(l.RegionKey))
                    {
                        ultimateTargetLocation = GameDefinitions.Instance.LocationsByKey[l];
                        result = TargetLocationReason.Priority;
                        break;
                    }
                }
            }
        }

        if (ultimateTargetLocation is not null)
        {
            List<LocationDefinitionModel> path = GetPath(CurrentLocation, ultimateTargetLocation);
            foreach (LocationDefinitionModel l in path)
            {
                if (GameDefinitions.Instance.LandmarkRegions.ContainsKey(l.Key.RegionKey) && !_checkedLocations![l.Key])
                {
                    TargetLocation = l;
                    return result;
                }
            }

            TargetLocation = ultimateTargetLocation;
            return result;
        }

        if (FindClosestUncheckedLocation(CurrentLocation) is { } closestReachableUnchecked)
        {
            TargetLocation = closestReachableUnchecked;
            return TargetLocationReason.ClosestReachableUnchecked;
        }

        TargetLocation = CurrentLocation;
        return TargetLocationReason.NowhereUsefulToMove;
    }

    private readonly PriorityQueue<(RegionDefinitionModel Region, Direction Direction), int> _pq = new(GameDefinitions.Instance.AllRegions.Count);
    private readonly HashSet<string> _visitedRegions = new(GameDefinitions.Instance.AllRegions.Count);

    private LocationDefinitionModel? FindClosestUncheckedLocation(LocationDefinitionModel currentLocation)
    {
        // quick short-circuit: often, this will get called while we're already standing on exactly
        // the closest unchecked location (perhaps because we failed at clearing it). let's optimize
        // for that case here, even though it should not affect correctness.
        SmallBitArray checkedLocationsInCurrentRegion = _checkedLocations![currentLocation.Key.RegionKey];
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
        foreach ((RegionDefinitionModel connectedRegion, Direction direction) in GameDefinitions.Instance.ConnectedRegions[currentLocation.Key.RegionKey])
        {
            if (!_hardLockedRegions.Contains(connectedRegion.Key))
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
                    !_hardLockedRegions.Contains(nextConnectedRegion.Key))
                {
                    _pq.Enqueue((nextConnectedRegion, nextDirection), nextExtraDistance);
                }
            }
        }

        return bestLocationKey is LocationKey finalResultKey
            ? GameDefinitions.Instance.LocationsByKey[finalResultKey]
            : null;
    }

    private readonly Queue<(RegionDefinitionModel Region, Direction Direction)> _q = new(GameDefinitions.Instance.AllRegions.Count);
    private readonly List<LocationDefinitionModel> _pathLocations = new(GameDefinitions.Instance.LocationsByName.Count);
    private readonly Dictionary<string, (RegionDefinitionModel Region, Direction Direction)> _prev = new(GameDefinitions.Instance.AllRegions.Count);
    private readonly Stack<(RegionDefinitionModel Region, Direction Direction)> _regionStack = new(GameDefinitions.Instance.AllRegions.Count);
    private readonly List<LocationDefinitionModel> _prevPath = new(GameDefinitions.Instance.LocationsByName.Count);
    private LocationKey _currentLocationForPrevPath = LocationKey.For("nonexistent");
    private LocationKey _targetLocationForPrevPath = LocationKey.For("nonexistent");
    private bool _startledForPrevPath;

    private List<LocationDefinitionModel> GetPath(LocationDefinitionModel currentLocation, LocationDefinitionModel targetLocation)
    {
        bool startled = TargetLocationReason == TargetLocationReason.Startled;
        if ((_currentLocationForPrevPath, _targetLocationForPrevPath, _startledForPrevPath) != (currentLocation.Key, targetLocation.Key, startled))
        {
            if (_currentLocationForPrevPath == currentLocation.Key && _startledForPrevPath == startled && _prevPath.Count > 0)
            {
                for (int i = _prevPath.Count - 1; i >= 0; --i)
                {
                    if (_prevPath[i].Key == targetLocation.Key)
                    {
                        _targetLocationForPrevPath = targetLocation.Key;
                        _startledForPrevPath = startled;
                        CollectionsMarshal.SetCount(_prevPath, i + 1);
                        return _prevPath;
                    }
                }
            }

            _currentLocationForPrevPath = currentLocation.Key;
            _targetLocationForPrevPath = targetLocation.Key;
            _startledForPrevPath = startled;
            _prevPath.Clear();
            if (GetPathCore(currentLocation, targetLocation, startled) is { } path)
            {
                _prevPath.AddRange(CollectionsMarshal.AsSpan(path));
            }
        }

        return _prevPath;
    }

    private List<LocationDefinitionModel>? GetPathCore(LocationDefinitionModel currentLocation, LocationDefinitionModel targetLocation, bool startled)
    {
        _pathLocations.Clear();
        HashSet<string> lockedRegions = startled
            ? _softLockedRegions
            : _hardLockedRegions;
        if (currentLocation.Key.RegionKey == targetLocation.Key.RegionKey)
        {
            LocationKey currentLocationKey = currentLocation.Key;
            if (currentLocationKey.N == targetLocation.Key.N)
            {
                _pathLocations.Add(currentLocation);
                _pathLocations.Add(targetLocation);
                return _pathLocations;
            }

            ImmutableArray<LocationDefinitionModel> currentRegionLocations = currentLocation.Region.Locations;
            if (currentLocation.Key.N > targetLocation.Key.N)
            {
                for (int i = currentLocationKey.N; i >= targetLocation.Key.N; --i)
                {
                    _pathLocations.Add(currentRegionLocations[i]);
                }
            }
            else
            {
                for (int i = currentLocationKey.N; i <= targetLocation.Key.N; i++)
                {
                    _pathLocations.Add(currentRegionLocations[i]);
                }
            }

            return _pathLocations;
        }

        if (lockedRegions.Contains(targetLocation.Key.RegionKey))
        {
            return null;
        }

        _q.Clear();
        _prev.Clear();
        foreach ((RegionDefinitionModel connectedRegion, Direction direction) in GameDefinitions.Instance.ConnectedRegions[currentLocation.Region.Key])
        {
            if (!lockedRegions.Contains(connectedRegion.Key))
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
                        !lockedRegions.Contains(nextConnectedRegion.Key))
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

            while (_regionStack.TryPop(out tup))
            {
                (RegionDefinitionModel nextRegion, Direction direction) = tup;
                if (nextRegion.Key == currentLocation.Key.RegionKey)
                {
                    switch (direction)
                    {
                        case Direction.TowardsGoal:
                            _pathLocations.AddRange(nextRegion.Locations.AsSpan(currentLocation.Key.N..));
                            break;

                        default:
                            int oldCount = _pathLocations.Count;
                            _pathLocations.AddRange(nextRegion.Locations.AsSpan(..(currentLocation.Key.N + 1)));
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
                            _pathLocations.AddRange(nextRegion.Locations.AsSpan());
                            break;

                        default:
                            int oldCount = _pathLocations.Count;
                            _pathLocations.AddRange(nextRegion.Locations.AsSpan());
                            _pathLocations.Reverse(oldCount, _pathLocations.Count - oldCount);
                            break;
                    }
                }
            }

            return _pathLocations;
        }

        return null;
    }

    private readonly Dictionary<string, SmallBitArray> _visitedLocations = GameDefinitions.Instance.AllRegions.Values.ToDictionary(r => r.Key, r => new SmallBitArray(r.Locations.Length));
    private readonly Queue<LocationKey> _qqq = new(GameDefinitions.Instance.LocationsByName.Count);
    private IEnumerable<LocationKey> GetClosestLocationsWithItemFlags(LocationKey currentLocation, ArchipelagoItemFlags flags)
    {
        FrozenSet<LocationKey> spoilerData = _spoilerData![flags];

        // TODO: optimize this, it's getting late.
        foreach (string regionKey in _visitedLocations.Keys)
        {
            (CollectionsMarshal.GetValueRefOrNullRef(_visitedLocations, regionKey)).Clear();
        }

        _qqq.Clear();
        _qqq.Enqueue(currentLocation);
        CollectionsMarshal.GetValueRefOrAddDefault(_visitedLocations, currentLocation.RegionKey, out _)[currentLocation.N] = true;
        while (_qqq.TryDequeue(out LocationKey loc))
        {
            if (spoilerData.Contains(loc) && !_checkedLocations![loc])
            {
                yield return loc;
            }

            foreach ((LocationDefinitionModel connectedLocation, _) in GameDefinitions.Instance.ConnectedLocations[loc])
            {
                if (!(_visitedLocations[connectedLocation.Key.RegionKey][connectedLocation.Key.N] ||
                      _hardLockedRegions.Contains(connectedLocation.Key.RegionKey)))
                {
                    CollectionsMarshal.GetValueRefOrAddDefault(_visitedLocations, connectedLocation.Key.RegionKey, out _)[connectedLocation.Key.N] = true;
                    _qqq.Enqueue(connectedLocation.Key);
                }
            }
        }
    }

    private readonly Queue<(RegionDefinitionModel Region, IReadOnlyList<ItemDefinitionModel> ReceivedItems)> _qq = new(GameDefinitions.Instance.AllRegions.Count);
    private void RecalculateClearable()
    {
        _qq.Clear();
        _visitedRegions.Clear();

        _qq.Enqueue((GameDefinitions.Instance.StartRegion, _receivedItems!));
        while (_qq.TryDequeue(out var tup))
        {
            (RegionDefinitionModel region, IReadOnlyList<ItemDefinitionModel> receivedItems) = tup;
            _visitedRegions.Add(region.Key);

            if (GameDefinitions.Instance.LandmarkRegions.TryGetValue(region.Key, out LandmarkRegionDefinitionModel? landmark))
            {
                if (_checkedLocations![region.Key][0])
                {
                    _hardLockedRegions.Remove(region.Key);
                    _softLockedRegions.Remove(region.Key);
                }

                if (!landmark.Requirement.Satisfied(receivedItems) && landmark.Key != GameDefinitions.Instance.GoalRegion.Key)
                {
                    continue;
                }

                _hardLockedRegions.Remove(region.Key);
            }
            else
            {
                _hardLockedRegions.Remove(region.Key);
                _softLockedRegions.Remove(region.Key);
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
