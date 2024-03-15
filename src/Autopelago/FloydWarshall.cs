using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Runtime.InteropServices;

namespace Autopelago;

public sealed class FloydWarshall
{
    private readonly ImmutableArray<LocationDefinitionModel> _locs;

    private readonly FrozenDictionary<LocationDefinitionModel, int> _locIndex;

    private readonly Dist _dist;

    private readonly Path _path;

    private FloydWarshall(ImmutableArray<LocationDefinitionModel> locs, FrozenDictionary<LocationDefinitionModel, int> locIndex, int[][] dist, int[][] prev)
    {
        _locs = locs;
        _locIndex = locIndex;
        _dist = new(dist);
        _path = new(prev);
    }

    public static FloydWarshall Compute(IEnumerable<RegionDefinitionModel> regions)
    {
        FrozenDictionary<string, RegionDefinitionModel> regionMap = regions.ToFrozenDictionary(r => r.Key);
        ImmutableArray<LocationDefinitionModel> locs = [.. regionMap.Values.SelectMany(r => r.Locations)];
        FrozenDictionary<LocationDefinitionModel, int> locIndex = locs.Select(KeyValuePair.Create).ToFrozenDictionary();
        int[][] dist = new int[locs.Length][];
        int[][] prev = new int[locs.Length][];
        for (int i = 0; i < locs.Length; i++)
        {
            dist[i] = new int[locs.Length];
            Array.Fill(dist[i], int.MaxValue / 2);
            dist[i][i] = 0;
            prev[i] = new int[locs.Length];
            Array.Fill(prev[i], -1);
            prev[i][i] = i;
        }

        foreach (RegionDefinitionModel region in regionMap.Values)
        {
            for (int i = 1; i < region.Locations.Length; i++)
            {
                int s = locIndex[region.Locations[i - 1]];
                int t = locIndex[region.Locations[i]];
                ref int d = ref dist[s][t];
                if (d > 1)
                {
                    d = 1;
                    prev[s][t] = s;
                }

                d = ref dist[t][s];
                if (d > 1)
                {
                    d = 1;
                    prev[t][s] = t;
                }
            }

            int xs = locIndex[region.Locations[^1]];
            foreach (RegionExitDefinitionModel exitSpec in region.Exits)
            {
                RegionDefinitionModel exit = regionMap[exitSpec.RegionKey];
                int et = locIndex[exit.Locations[0]];
                ref int d = ref dist[xs][et];
                if (d > 1)
                {
                    d = 1;
                    prev[xs][et] = xs;
                }

                d = ref dist[et][xs];
                if (d > 1)
                {
                    d = 1;
                    prev[et][xs] = et;
                }
            }
        }

        for (int k = 0; k < locs.Length; k++)
        {
            int[] dk = dist[k];
            int[] pk = prev[k];

            for (int i = 0; i < locs.Length; i++)
            {
                int[] di = dist[i];
                int[] pi = prev[i];

                ref int dik = ref di[k];
                for (int j = 0; j < locs.Length; j++)
                {
                    ref int dij = ref di[j];
                    int dMax = dik + dk[j];
                    if (dij > dMax)
                    {
                        dij = dMax;
                        pi[j] = pk[j];
                    }
                }
            }
        }

        foreach (int[] p in prev)
        {
            for (int i = 0; i < prev.Length; i++)
            {
                if (p[i] < 0)
                {
                    throw new InvalidOperationException("The entire graph must be connected. This MIGHT just be a programming error.");
                }
            }
        }

        return new(locs, locIndex, dist, prev);
    }

    public int GetDistance(LocationDefinitionModel source, LocationDefinitionModel target)
    {
        return _dist[_locIndex[source], _locIndex[target]];
    }

    public LocationDefinitionModel GetNextOnPath(LocationDefinitionModel source, LocationDefinitionModel target)
    {
        return _locs[_path.GetNextOnPath(_locIndex[source], _locIndex[target])];
    }

    public ImmutableArray<LocationDefinitionModel> GetPath(LocationDefinitionModel source, LocationDefinitionModel target)
    {
        return ImmutableArray.CreateRange(_path[_locIndex[source], _locIndex[target]], i => _locs[i]);
    }

    private readonly struct Dist
    {
        private readonly int[][] _dist;

        public Dist(int[][] dist) => _dist = dist;

        public int this[int s, int t] => _dist[s][t];
    }

    private readonly struct Path
    {
        private readonly int[][] _prev;

        public Path(int[][] prev) => _prev = prev;

        public ImmutableArray<int> this[int s, int t]
        {
            get
            {
                int[] prev = _prev[s];
                int cnt = 1;
                for (int tt = t; tt != s; tt = prev[tt])
                {
                    ++cnt;
                }

                int[] result = new int[cnt];
                do
                {
                    result[--cnt] = t;
                    t = prev[t];
                } while (s != t);

                return ImmutableCollectionsMarshal.AsImmutableArray(result);
            }
        }

        public int GetNextOnPath(int s, int t)
        {
            int[] prev = _prev[s];
            for (; prev[t] != s; t = prev[t]) ;
            return t;
        }
    }
}
