using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Runtime.InteropServices;

using CommunityToolkit.HighPerformance;

namespace Autopelago;

public sealed record ShortestPaths
{
    private readonly GameDefinitions _defs;

    private readonly ImmutableArray<string> _loc;

    private readonly FrozenDictionary<string, int> _locLookup;

    private readonly ImmutableArray<uint> _dist;

    private readonly ImmutableArray<int> _prev;

    public ShortestPaths(GameDefinitions defs, ReceivedItems receivedItems)
        : this(defs, CanEnter(defs, receivedItems))
    {
    }

    public ShortestPaths(GameDefinitions defs, CheckedLocations checkedLocations)
        : this(defs, CanEnter(defs, checkedLocations))
    {
    }

    private static Func<RegionDefinitionModel, bool> CanEnter(GameDefinitions defs, ReceivedItems receivedItems)
    {
        return region =>
        {
            if (region is not LandmarkRegionDefinitionModel landmark)
            {
                return true;
            }

            if (landmark.Requirement.Satisfied(receivedItems) || landmark == defs.GoalRegion)
            {
                return true;
            }

            return false;
        };
    }

    private static Func<RegionDefinitionModel, bool> CanEnter(GameDefinitions defs, CheckedLocations checkedLocations)
    {
        FrozenSet<string> openRegions = [.. defs.FillerRegions.Keys, .. checkedLocations.InCheckedOrder.Select(l => l.Key.RegionKey)];
        return region => openRegions.Contains(region.Key);
    }

    private ShortestPaths(GameDefinitions defs, Func<RegionDefinitionModel, bool> canEnter)
    {
        _defs = defs;

        List<string> locList = [];
        Dictionary<string, int> locLookup = [];

        Queue<RegionDefinitionModel> regions = [];
        regions.Enqueue(defs.StartRegion);
        HashSet<string> regionsSoFar = [];
        while (regions.TryDequeue(out RegionDefinitionModel? region))
        {
            if (!regionsSoFar.Add(region.Key))
            {
                continue;
            }

            locLookup.Add(region.Key, locList.Count);
            locList.Add(region.Key);

            foreach (RegionDefinitionModel next in defs.ConnectedRegions[region])
            {
                if (regionsSoFar.Contains(next.Key))
                {
                    continue;
                }

                if (canEnter(next))
                {
                    regions.Enqueue(next);
                }
            }
        }

        ImmutableArray<string> loc = _loc = [.. locList];
        _locLookup = locLookup.ToFrozenDictionary();

        uint[] dist = new uint[loc.Length * loc.Length];
        dist.AsSpan().Fill(uint.MaxValue / 2);
        int[] prev = new int[loc.Length * loc.Length];
        prev.AsSpan().Fill(-1);

        Span2D<uint> dist2D = new(dist, loc.Length, loc.Length);
        Span2D<int> prev2D = new(prev, loc.Length, loc.Length);
        for (int i = 0; i < loc.Length; i++)
        {
            dist2D[i, i] = 0;
            prev2D[i, i] = i;
            foreach (RegionExitDefinitionModel connected in defs.AllRegions[loc[i]].Exits)
            {
                if (!locLookup.TryGetValue(connected.RegionKey, out int j))
                {
                    continue;
                }

                dist2D[i, j] = 1;
                dist2D[j, i] = 1;
                prev2D[i, j] = i;
                prev2D[j, i] = j;
            }
        }

        for (int k = 0; k < loc.Length; k++)
        {
            for (int i = 0; i < loc.Length; i++)
            {
                for (int j = 0; j < loc.Length; j++)
                {
                    if (dist2D[i, j] > checked(dist2D[i, k] + dist2D[k, j]))
                    {
                        dist2D[i, j] = dist2D[i, k] + dist2D[k, j];
                        prev2D[i, j] = prev2D[k, j];
                    }
                }
            }
        }

        _dist = ImmutableCollectionsMarshal.AsImmutableArray(dist);
        _prev = ImmutableCollectionsMarshal.AsImmutableArray(prev);
    }

    public Path? GetPathOrNull(LocationDefinitionModel from, LocationDefinitionModel to)
    {
        return _locLookup.TryGetValue(from.Key.RegionKey, out int i) && _locLookup.TryGetValue(to.Key.RegionKey, out int j) && PathExists(i, j)
            ? new(this, from, to, i, j)
            : null;
    }

    private bool PathExists(int i, int j)
    {
        return
            (uint)i < (uint)_loc.Length &&
            (uint)j < (uint)_loc.Length &&
            _dist[(i * _loc.Length) + j] < uint.MaxValue / 2;
    }

    private ReadOnlySpan2D<int> Prev => new(ImmutableCollectionsMarshal.AsArray(_prev)!, _loc.Length, _loc.Length);

    public readonly record struct Path
    {
        private readonly ShortestPaths _parent;

        private readonly LocationDefinitionModel _from;

        private readonly LocationDefinitionModel _to;

        private readonly int _i;

        private readonly int _j;

        private readonly Lazy<ImmutableArray<LocationDefinitionModel>> _locations;

        internal Path(ShortestPaths parent, LocationDefinitionModel from, LocationDefinitionModel to, int i, int j)
        {
            _parent = parent;
            _from = from;
            _to = to;
            _i = i;
            _j = j;
            Path self = this;
            _locations = new(() => self.GetLocations());
        }

        public ImmutableArray<LocationDefinitionModel> Locations => _locations.Value;

        private static bool IsForward(GameDefinitions defs, RegionDefinitionModel from, RegionDefinitionModel to)
        {
            if (defs.ConnectedLocations[from.Locations[^1]].Contains(to.Locations[0]))
            {
                return true;
            }

            if (defs.ConnectedLocations[from.Locations[0]].Contains(to.Locations[^1]))
            {
                return false;
            }

            // the regions are not connected.
            throw new InvalidOperationException("Floyd-Warshall not correct (this is a programming error).");
        }

        private ImmutableArray<LocationDefinitionModel> GetLocations()
        {
            RegionDefinitionModel fromRegion = _parent._defs.AllRegions[_from.Key.RegionKey];
            RegionDefinitionModel toRegion = _parent._defs.AllRegions[_to.Key.RegionKey];
            if (fromRegion == toRegion)
            {
                ReadOnlySpan<LocationDefinitionModel> sameRegionLocs = _from.Key.N > _to.Key.N
                    ? fromRegion.Locations.AsSpan((_to.Key.N)..(_from.Key.N + 1))
                    : fromRegion.Locations.AsSpan((_from.Key.N)..(_to.Key.N + 1));
                LocationDefinitionModel[] sameRegionResult = [..sameRegionLocs];
                if (_from.Key.N > _to.Key.N)
                {
                    Array.Reverse(sameRegionResult);
                }

                return ImmutableCollectionsMarshal.AsImmutableArray(sameRegionResult);
            }

            Stack<int> stack = [];
            stack.Push(_j);
            int j = _j;
            ReadOnlySpan2D<int> prev = _parent.Prev;
            while (j != _i)
            {
                stack.Push(j = prev[_i, j]);
            }

            RegionDefinitionModel[] regions = new RegionDefinitionModel[stack.Count];
            for (int i = 0; i < regions.Length; i++)
            {
                regions[i] = _parent._defs.AllRegions[_parent._loc[stack.Pop()]];
            }

            // this will initially contain all the locations in the "from" region leading up to
            // the "from" location, and all the locations in the "to" region coming after the
            // "to" location. we'll trim at the end: we need to do an array copy anyway.
            List<LocationDefinitionModel> fullResult = [];
            bool forward = IsForward(_parent._defs, fromRegion, regions[1]);
            for (int i = 0; i < regions.Length; i++)
            {
                RegionDefinitionModel region = regions[i];
                ImmutableArray<LocationDefinitionModel> locations = region.Locations;
                fullResult.AddRange(locations);
                if (!forward)
                {
                    fullResult.Reverse(fullResult.Count - locations.Length, locations.Length);
                }

                if (i < regions.Length - 1)
                {
                    forward = IsForward(_parent._defs, region, regions[i + 1]);
                }
            }

            return [.. fullResult[fullResult.IndexOf(_from)..(fullResult.LastIndexOf(_to) + 1)]];
        }
    }
}
