using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Runtime.InteropServices;

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
        for (int i = 0; i < loc.Length; i++)
        {
            dist[(i * loc.Length) + i] = 0;
            prev[(i * loc.Length) + i] = i;
            foreach (RegionExitDefinitionModel connected in defs.AllRegions[loc[i]].Exits)
            {
                if (!locLookup.TryGetValue(connected.RegionKey, out int j))
                {
                    continue;
                }

                dist[(i * loc.Length) + j] = 1;
                dist[(j * loc.Length) + i] = 1;
                prev[(i * loc.Length) + j] = i;
                prev[(j * loc.Length) + i] = j;
            }
        }

        for (int k = 0; k < loc.Length; k++)
        {
            for (int i = 0; i < loc.Length; i++)
            {
                for (int j = 0; j < loc.Length; j++)
                {
                    if (dist[(i * loc.Length) + j] > checked(dist[(i * loc.Length) + k] + dist[(k * loc.Length) + j]))
                    {
                        dist[(i * loc.Length) + j] = dist[(i * loc.Length) + k] + dist[(k * loc.Length) + j];
                        prev[(i * loc.Length) + j] = prev[(k * loc.Length) + j];
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

    public bool Equals(ShortestPaths? other)
    {
        return
            other is not null &&
            _loc.SequenceEqual(other._loc) &&
            _dist.SequenceEqual(other._dist) &&
            _prev.SequenceEqual(other._prev);
    }

    public override int GetHashCode()
    {
        // length doesn't seem selective enough. probably more risky to
        HashCode hc = default;
        hc.Add(_loc.Length);
        foreach (string loc in _loc)
        {
            hc.AddBytes(MemoryMarshal.AsBytes(loc.AsSpan()));
        }

        hc.AddBytes(MemoryMarshal.AsBytes(_dist.AsSpan()));
        hc.AddBytes(MemoryMarshal.AsBytes(_prev.AsSpan()));
        return hc.ToHashCode();
    }

    public readonly record struct Path
    {
        private readonly ShortestPaths? _parent;

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

        private Path(LocationDefinitionModel from)
        {
            _parent = null;
            _from = from;
            _to = from;
            _i = 0;
            _j = 0;
            _locations = new(() => [from]);
        }

        public ImmutableArray<LocationDefinitionModel> Locations => _locations.Value;

        public static Path Only(LocationDefinitionModel location) => new(location);

        public bool Equals(Path other)
        {
            return
                _parent == other._parent &&
                _from == other._from &&
                _to == other._to &&
                _i == other._i &&
                _j == other._j;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(
                _parent,
                _from,
                _to,
                _i,
                _j
            );
        }

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
            RegionDefinitionModel fromRegion = _parent!._defs.AllRegions[_from.Key.RegionKey];
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
            while (j != _i)
            {
                stack.Push(j = _parent._prev[(_i * _parent._loc.Length) + j]);
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
