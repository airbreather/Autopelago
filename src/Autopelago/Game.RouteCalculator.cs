using System.Collections;
using System.Runtime.InteropServices;

namespace Autopelago;

public sealed partial class Game
{
    private bool _everCalculatedPathToTarget;
    private bool UpdateTargetLocation()
    {
        LocationKey prevTargetLocation = TargetLocation;
        TargetLocationReason = MoveBestTargetLocation();
        if (TargetLocation == prevTargetLocation && _everCalculatedPathToTarget)
        {
            return false;
        }

        _pathToTarget.Clear();
        bool first = true;
        foreach (LocationKey l in GetPath(CurrentLocation, TargetLocation))
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
        return TargetLocation != prevTargetLocation;
    }

    private TargetLocationReason MoveBestTargetLocation()
    {
        if (StartledCounter > 0)
        {
            TargetLocation = GameDefinitions.Instance.StartLocation;
            return TargetLocationReason.Startled;
        }

        TargetLocationReason result = default;
        LocationKey? priorityTargetLocation = null;
        if (!_hardLockedRegions[GameDefinitions.Instance.GoalRegion.N])
        {
            priorityTargetLocation = GameDefinitions.Instance.GoalLocation;
            result = TargetLocationReason.GoMode;
        }

        if (priorityTargetLocation is null)
        {
            foreach (LocationKey l in _priorityPriorityLocations)
            {
                if (!_hardLockedRegions[GameDefinitions.Instance.RegionKey[l].N])
                {
                    priorityTargetLocation = l;
                    result = TargetLocationReason.PriorityPriority;
                    break;
                }
            }

            if (priorityTargetLocation is null)
            {
                foreach (LocationKey l in _priorityLocations)
                {
                    if (!_hardLockedRegions[GameDefinitions.Instance.RegionKey[l].N])
                    {
                        priorityTargetLocation = l;
                        result = TargetLocationReason.Priority;
                        break;
                    }
                }
            }
        }

        if (priorityTargetLocation is LocationKey ultimateTargetLocation)
        {
            List<LocationKey> path = GetPath(CurrentLocation, ultimateTargetLocation);
            foreach (LocationKey l in path)
            {
                if (GameDefinitions.Instance.TryGetLandmarkRegion(l, out _) && !_checkedLocations[l.N])
                {
                    TargetLocation = l;
                    return result;
                }
            }

            TargetLocation = ultimateTargetLocation;
            return result;
        }

        if (FindClosestUncheckedLocation(CurrentLocation) is LocationKey closestReachableUnchecked)
        {
            TargetLocation = closestReachableUnchecked;
            return TargetLocationReason.ClosestReachableUnchecked;
        }

        TargetLocation = CurrentLocation;
        return TargetLocationReason.NowhereUsefulToMove;
    }

    private LocationKey? FindClosestUncheckedLocation(LocationKey currentLocation)
    {
        // quick short-circuit: often, this will get called while we're already standing on exactly
        // the closest unchecked location (perhaps because we failed at clearing it). let's optimize
        // for that case here, even though it should not affect correctness.
        if (!_checkedLocations[currentLocation.N])
        {
            return currentLocation;
        }

        // we're not already LITERALLY standing on an unchecked location, so do the full version.
        ref readonly LocationDefinitionModel currentLocationDefinition = ref GameDefinitions.Instance[currentLocation];
        RegionLocationKey currentRegionLocation = currentLocationDefinition.RegionLocationKey;
        ref readonly RegionDefinitionModel currentRegionDefinition = ref GameDefinitions.Instance[currentRegionLocation.Region];
        int backwardLocationsInCurrentRegion = currentRegionLocation.N;
        int forwardLocationsInCurrentRegion = currentRegionDefinition.Locations.Length - currentRegionLocation.N - 1;

        int bestDistance = int.MaxValue;
        LocationKey? bestLocationKey = null;

        // in probably a VERY large majority of cases when we find ourselves in a filler region, the
        // closest unchecked location will either be in that same filler region, one of the (up to)
        // two adjacent landmark regions, or far enough away that there's an entire filler region's
        // worth of checked locations between the two.
        if (_regionUncheckedLocationsCount[currentRegionLocation.Region.N] > 0)
        {
            // this won't necessarily be the final answer, but it will be a solid upper bound.
            for (int i = 1; i <= forwardLocationsInCurrentRegion; i++)
            {
                if (_checkedLocations[currentLocation.N + i])
                {
                    continue;
                }

                bestDistance = i;
                bestLocationKey = new() { N = currentLocation.N + i };
                break;
            }

            for (int i = 1; i <= backwardLocationsInCurrentRegion && i < bestDistance; i++)
            {
                if (_checkedLocations[currentLocation.N - i])
                {
                    continue;
                }

                bestDistance = i;
                bestLocationKey = new() { N = currentLocation.N - i };
                break;
            }
        }

        if (bestDistance < backwardLocationsInCurrentRegion && bestDistance < forwardLocationsInCurrentRegion)
        {
            // we're in a filler region, and the closest unchecked location in our own region is
            // closer than either of the (up to) two landmarks that we're joining.
            return bestLocationKey;
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
        using var borrowedPq = Borrow<PriorityQueue<(RegionKey ConnectedRegion, Direction Direction), int>>();
        PriorityQueue<(RegionKey ConnectedRegion, Direction Direction), int> pq = borrowedPq.Value;
        pq.Clear();
        using var visitedRegionsBorrow = BorrowRegionsBitArray();
        BitArray visitedRegions = visitedRegionsBorrow.Value;
        visitedRegions.SetAll(false);
        visitedRegions[currentRegionLocation.Region.N] = true;
        foreach ((RegionKey connected, Direction direction) in currentRegionDefinition.Connected.All)
        {
            if (!_hardLockedRegions[connected.N])
            {
                visitedRegions[currentRegionLocation.Region.N] = true;
                pq.Enqueue((connected, direction), direction switch
                {
                    Direction.TowardsGoal => forwardLocationsInCurrentRegion,
                    _ => backwardLocationsInCurrentRegion,
                } + 1); // +1 gets us to the first location in the next region.
            }
        }

        while (pq.TryDequeue(out var tup, out int extraDistance))
        {
            if (extraDistance >= bestDistance)
            {
                return bestLocationKey;
            }

            (RegionKey connectedRegion, Direction direction) = tup;
            ref readonly RegionDefinitionModel connectedRegionDefinition = ref GameDefinitions.Instance[connectedRegion];
            if (_regionUncheckedLocationsCount[connectedRegion.N] > 0)
            {
                if (direction == Direction.TowardsGoal)
                {
                    for (int i = 0; i < connectedRegionDefinition.Locations.Length && extraDistance + i < bestDistance; i++)
                    {
                        if (_checkedLocations[connectedRegionDefinition.Locations[i].N])
                        {
                            continue;
                        }

                        bestDistance = extraDistance + i;
                        bestLocationKey = connectedRegionDefinition.Locations[i];
                    }
                }
                else
                {
                    for (int i = 0; i < connectedRegionDefinition.Locations.Length && extraDistance + i < bestDistance; i++)
                    {
                        if (_checkedLocations[connectedRegionDefinition.Locations[^(i + 1)].N])
                        {
                            continue;
                        }

                        bestDistance = extraDistance + i;
                        bestLocationKey = connectedRegionDefinition.Locations[^(i + 1)];
                        break;
                    }
                }
            }

            // routes from here will take at least one step into the next region, so add that here.
            // the exact details of how far we will need to walk through the current region to get
            // to the next one will depend on the direction we face when entering the next region.
            ++extraDistance;
            foreach ((RegionKey nextConnectedRegion, Direction nextDirection) in connectedRegionDefinition.Connected.All)
            {
                if (visitedRegions[nextConnectedRegion.N] || _hardLockedRegions[nextConnectedRegion.N])
                {
                    continue;
                }

                visitedRegions[nextConnectedRegion.N] = true;

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
                    ? extraDistance + connectedRegionDefinition.Locations.Length - 1
                    : extraDistance;

                if (nextExtraDistance < bestDistance)
                {
                    pq.Enqueue((nextConnectedRegion, nextDirection), nextExtraDistance);
                }
            }
        }

        return bestLocationKey;
    }

    private LocationKey _currentLocationForPrevPath = LocationKey.Nonexistent;
    private LocationKey _targetLocationForPrevPath = LocationKey.Nonexistent;
    private bool _startledForPrevPath;

    private List<LocationKey> GetPath(LocationKey currentLocation, LocationKey targetLocation)
    {
        bool startled = TargetLocationReason == TargetLocationReason.Startled;
        if ((_currentLocationForPrevPath, _targetLocationForPrevPath, _startledForPrevPath) != (currentLocation, targetLocation, startled))
        {
            if (_currentLocationForPrevPath == currentLocation && _startledForPrevPath == startled && _prevPath.Count > 0)
            {
                for (int i = _prevPath.Count - 1; i >= 0; --i)
                {
                    if (_prevPath[i] == targetLocation)
                    {
                        _targetLocationForPrevPath = targetLocation;
                        _startledForPrevPath = startled;
                        CollectionsMarshal.SetCount(_prevPath, i + 1);
                        return _prevPath;
                    }
                }
            }

            _currentLocationForPrevPath = currentLocation;
            _targetLocationForPrevPath = targetLocation;
            _startledForPrevPath = startled;
            _prevPath.Clear();
            using var pathBorrowed = GetPathCore(currentLocation, targetLocation, startled);
            if (pathBorrowed?.Value is { } path)
            {
                _prevPath.AddRange(CollectionsMarshal.AsSpan(path));
            }
        }

        return _prevPath;
    }

    private Borrowed<List<LocationKey>>? GetPathCore(LocationKey currentLocation, LocationKey targetLocation, bool startled)
    {
        if (currentLocation == targetLocation)
        {
            var trivialResultBorrow = Borrow<List<LocationKey>>();
            trivialResultBorrow.Value.Clear();
            trivialResultBorrow.Value.AddRange([currentLocation, targetLocation]);
            return trivialResultBorrow;
        }

        ref readonly LocationDefinitionModel currentLocationDefinition = ref GameDefinitions.Instance[currentLocation];
        ref readonly LocationDefinitionModel targetLocationDefinition = ref GameDefinitions.Instance[targetLocation];
        RegionLocationKey currentRegionLocation = currentLocationDefinition.RegionLocationKey;
        RegionLocationKey targetRegionLocation = targetLocationDefinition.RegionLocationKey;
        BitArray lockedRegions = startled
            ? _softLockedRegions
            : _hardLockedRegions;
        if (lockedRegions[targetRegionLocation.Region.N])
        {
            return null;
        }

        var resultBorrow = Borrow<List<LocationKey>>();
        List<LocationKey> result = resultBorrow.Value;
        result.Clear();
        if (currentRegionLocation.Region == targetRegionLocation.Region)
        {
            if (currentLocation.N > targetLocation.N)
            {
                int count = currentLocation.N - targetLocation.N + 1;
                result.EnsureCapacity(count);
                for (int i = 0; i < count; i++)
                {
                    result.Add(new() { N = currentLocation.N - i });
                }
            }
            else
            {
                int count = targetLocation.N - currentLocation.N + 1;
                for (int i = 0; i < count; i++)
                {
                    result.Add(new() { N = currentLocation.N + i });
                }
            }

            return resultBorrow;
        }

        using var qBorrow = Borrow<Queue<(RegionKey ConnectedRegion, Direction Direction)>>();
        Queue<(RegionKey ConnectedRegion, Direction Direction)> q = qBorrow.Value;
        q.Clear();

        using var prevBorrow = Borrow<Dictionary<RegionKey, (RegionKey ConnectedRegion, Direction Direction)>>();
        Dictionary<RegionKey, (RegionKey ConnectedRegion, Direction Direction)> prev = prevBorrow.Value;
        prev.Clear();

        foreach ((RegionKey connectedRegion, Direction direction) in GameDefinitions.Instance[currentRegionLocation.Region].Connected.All)
        {
            if (!lockedRegions[connectedRegion.N])
            {
                q.Enqueue((connectedRegion, direction));
                prev.Add(connectedRegion, (currentRegionLocation.Region, direction));
            }
        }

        while (q.TryDequeue(out var tup))
        {
            (RegionKey connectedRegion, _) = tup;
            if (connectedRegion != targetRegionLocation.Region)
            {
                ref readonly RegionDefinitionModel connectedRegionDefinition = ref GameDefinitions.Instance[connectedRegion];
                foreach ((RegionKey nextConnectedRegion, Direction nextDirection) in connectedRegionDefinition.Connected.All)
                {
                    if (nextConnectedRegion != currentRegionLocation.Region &&
                        prev.TryAdd(nextConnectedRegion, (connectedRegion, nextDirection)) &&
                        !lockedRegions[nextConnectedRegion.N])
                    {
                        q.Enqueue((nextConnectedRegion, nextDirection));
                    }
                }

                continue;
            }

            using var regionStackBorrow = Borrow<Stack<(RegionKey ConnectedRegion, Direction Direction)>>();
            Stack<(RegionKey ConnectedRegion, Direction Direction)> regionStack = regionStackBorrow.Value;
            regionStack.Clear();
            do
            {
                regionStack.Push(tup);
            } while (prev.TryGetValue(tup.ConnectedRegion, out tup));

            while (regionStack.TryPop(out tup))
            {
                (RegionKey nextRegion, Direction direction) = tup;
                ref readonly RegionDefinitionModel nextRegionDefinition = ref GameDefinitions.Instance[nextRegion];
                if (nextRegion == currentRegionLocation.Region)
                {
                    switch (direction)
                    {
                        case Direction.TowardsGoal:
                            result.AddRange(nextRegionDefinition.Locations.AsSpan(currentRegionLocation.N..));
                            break;

                        default:
                            int oldCount = result.Count;
                            result.AddRange(nextRegionDefinition.Locations.AsSpan(..(currentRegionLocation.N + 1)));
                            result.Reverse(oldCount, result.Count - oldCount);
                            break;
                    }
                }
                else if (nextRegion == targetRegionLocation.Region)
                {
                    switch (direction)
                    {
                        case Direction.TowardsGoal:
                            result.AddRange(nextRegionDefinition.Locations.AsSpan(..(targetRegionLocation.N + 1)));
                            break;

                        default:
                            int oldCount = result.Count;
                            result.AddRange(nextRegionDefinition.Locations.AsSpan(targetRegionLocation.N..));
                            result.Reverse(oldCount, result.Count - oldCount);
                            break;
                    }
                }
                else
                {
                    switch (direction)
                    {
                        case Direction.TowardsGoal:
                            result.AddRange(nextRegionDefinition.Locations.AsSpan());
                            break;

                        default:
                            int oldCount = result.Count;
                            result.AddRange(nextRegionDefinition.Locations.AsSpan());
                            result.Reverse(oldCount, result.Count - oldCount);
                            break;
                    }
                }
            }

            return resultBorrow;
        }

        resultBorrow.Dispose();
        return null;
    }

    private IEnumerable<LocationKey> GetClosestLocationsWithItemFlags(LocationKey currentLocation, ArchipelagoItemFlags flags)
    {
        ReadOnlyBitArray spoilerData = _spoilerData![flags];

        // TODO: optimize this, it's getting late.
        using var visitedLocationsBorrow = BorrowLocationsBitArray();
        BitArray visitedLocations = visitedLocationsBorrow.Value;
        using var qBorrow = Borrow<Queue<LocationKey>>();
        Queue<LocationKey> q = qBorrow.Value;
        q.Clear();
        q.Enqueue(currentLocation);
        visitedLocations[currentLocation.N] = true;
        while (q.TryDequeue(out LocationKey loc))
        {
            if (spoilerData[loc.N] && !_checkedLocations[loc.N])
            {
                yield return loc;
            }

            foreach ((LocationKey connectedLocation, _) in GameDefinitions.Instance[loc].Connected.All)
            {
                if (!(visitedLocations[connectedLocation.N] ||
                      _hardLockedRegions[GameDefinitions.Instance.RegionKey[connectedLocation].N]))
                {
                    visitedLocations[connectedLocation.N] = true;
                    q.Enqueue(connectedLocation);
                }
            }
        }
    }

    private void RecalculateClearable()
    {
        using var qBorrow = Borrow<Queue<RegionKey>>();
        Queue<RegionKey> q = qBorrow.Value;
        q.Clear();

        using var visitedRegionsBorrow = BorrowRegionsBitArray();
        BitArray visitedRegions = visitedRegionsBorrow.Value;

        q.Enqueue(GameDefinitions.Instance.StartRegion);
        while (q.TryDequeue(out RegionKey region))
        {
            visitedRegions[region.N] = true;

            if (_checkedLocations[region.N])
            {
                _hardLockedRegions[region.N] = false;
                _softLockedRegions[region.N] = false;
            }

            if (GameDefinitions.Instance[region] is LandmarkRegionDefinitionModel landmark)
            {
                if (!landmark.Requirement.Satisfied(_receivedItems) && landmark.Key != GameDefinitions.Instance.GoalRegion)
                {
                    continue;
                }

                _hardLockedRegions[region.N] = false;
            }
            else
            {
                _hardLockedRegions[region.N] = false;
                _softLockedRegions[region.N] = false;
            }

            foreach ((RegionKey connectedRegion, _) in GameDefinitions.Instance[region].Connected.All)
            {
                if (!visitedRegions[connectedRegion.N])
                {
                    visitedRegions[connectedRegion.N] = true;
                    q.Enqueue(connectedRegion);
                }
            }
        }
    }
}
