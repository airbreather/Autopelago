using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

using CommunityToolkit.HighPerformance;

namespace Autopelago;

public sealed record ShortestPaths
{
    private readonly ImmutableArray<LocationDefinitionModel> _loc;

    private readonly FrozenDictionary<LocationDefinitionModel, int> _locLookup;

    private readonly ImmutableArray<uint> _dist;

    private readonly ImmutableArray<int> _prev;

    public ShortestPaths(GameDefinitions defs, ReceivedItems receivedItems)
        : this(defs, CanExitThrough(defs, receivedItems))
    {
    }

    public ShortestPaths(GameDefinitions defs, CheckedLocations checkedLocations)
        : this(defs, CanExitThrough(defs, checkedLocations))
    {
    }

    private static Func<RegionExitDefinitionModel, bool> CanExitThrough(GameDefinitions defs, ReceivedItems receivedItems)
    {
        return exit => !(defs.AllRegions[exit.RegionKey] is LandmarkRegionDefinitionModel landmark && !landmark.Requirement.Satisfied(receivedItems));
    }

    private static Func<RegionExitDefinitionModel, bool> CanExitThrough(GameDefinitions defs, CheckedLocations checkedLocations)
    {
        FrozenSet<string> openRegions = [.. defs.FillerRegions.Keys, .. checkedLocations.InCheckedOrder.Select(l => l.Key.RegionKey)];
        return exit => openRegions.Contains(exit.RegionKey);
    }

    private ShortestPaths(GameDefinitions defs, Func<RegionExitDefinitionModel, bool> canExitThrough)
    {
        List<LocationDefinitionModel> locList = [];
        Dictionary<LocationDefinitionModel, int> locLookup = [];

        Queue<RegionDefinitionModel> regions = [];
        regions.Enqueue(defs.StartRegion);
        HashSet<string> regionsSoFar = [defs.StartRegion.Key];
        while (regions.TryDequeue(out RegionDefinitionModel? region))
        {
            if (!regionsSoFar.Add(region.Key))
            {
                continue;
            }

            locList.EnsureCapacity(locList.Count + region.Locations.Length);
            foreach (LocationDefinitionModel location in region.Locations)
            {
                locLookup.Add(location, locList.Count);
                locList.Add(location);
            }

            foreach (RegionExitDefinitionModel exit in region.Exits)
            {
                if (regionsSoFar.Contains(exit.RegionKey))
                {
                    continue;
                }

                if (canExitThrough(exit))
                {
                    regions.Enqueue(defs.AllRegions[exit.RegionKey]);
                }
            }
        }

        ImmutableArray<LocationDefinitionModel> loc = _loc = [.. locList];
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
            foreach (LocationDefinitionModel connected in defs.ConnectedLocations[loc[i]])
            {
                if (!locLookup.TryGetValue(connected, out int j))
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
        return _locLookup.TryGetValue(from, out int i) && _locLookup.TryGetValue(to, out int j) && PathExists(i, j)
            ? new(this, i, j)
            : null;
    }

    public bool TryGetPath(LocationDefinitionModel from, LocationDefinitionModel to, [NotNullWhen(true)] out Path? path)
    {
        path = GetPathOrNull(from, to);
        return path is not null;
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

        private readonly int _i;

        private readonly int _j;

        internal Path(ShortestPaths parent, int i, int j)
        {
            _parent = parent;
            _i = i;
            _j = j;
        }

        public ImmutableArray<LocationDefinitionModel> Locations
        {
            get
            {
                Stack<int> stack = [];
                stack.Push(_j);
                int j = _j;
                ReadOnlySpan2D<int> prev = _parent.Prev;
                while (j != _i)
                {
                    stack.Push(j = prev[_i, j]);
                }

                LocationDefinitionModel[] result = new LocationDefinitionModel[stack.Count];
                foreach (ref LocationDefinitionModel slot in result.AsSpan())
                {
                    slot = _parent._loc[stack.Pop()];
                }

                return ImmutableCollectionsMarshal.AsImmutableArray(result);
            }
        }
    }
}
