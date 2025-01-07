using System.Diagnostics;

namespace Autopelago;

[DebuggerDisplay("{GameDefinitions.Instance[this].Name}")]
[DebuggerTypeProxy(typeof(RegionKeyDebugView))]
public readonly record struct RegionKey
{
    private readonly byte _n;
    public required int N
    {
        get => _n;
        init => _n = (byte)value;
    }

    private readonly struct RegionKeyDebugView
    {
        public RegionKeyDebugView(RegionKey key)
        {
            Region = GameDefinitions.Instance[key];
        }

        [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
        public RegionDefinitionModel Region { get; }
    }
}

[DebuggerDisplay("{GameDefinitions.Instance[this].Name}")]
[DebuggerTypeProxy(typeof(LocationKeyDebugView))]
public readonly record struct LocationKey
{
    public static readonly LocationKey Nonexistent = new() { N = ushort.MaxValue };

    private readonly ushort _n;
    public required int N
    {
        get => _n;
        init => _n = (ushort)value;
    }

    private readonly struct LocationKeyDebugView
    {
        public LocationKeyDebugView(LocationKey key)
        {
            Location = GameDefinitions.Instance[key];
        }

        [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
        public LocationDefinitionModel Location { get; }
    }
}

[DebuggerDisplay("{GameDefinitions.Instance[this].Name}")]
[DebuggerTypeProxy(typeof(RegionLocationKeyDebugView))]
public readonly record struct RegionLocationKey
{
    public required RegionKey Region { get; init; }

    private readonly byte _n;
    public required int N
    {
        get => _n;
        init => _n = (byte)value;
    }

    public static RegionLocationKey For(RegionKey region)
    {
        return For(region, 0);
    }

    public static RegionLocationKey For(RegionKey region, int n)
    {
        return new()
        {
            Region = region,
            N = n,
        };
    }

    private readonly struct RegionLocationKeyDebugView
    {
        public RegionLocationKeyDebugView(RegionLocationKey key)
        {
            Location = GameDefinitions.Instance[key];
        }

        [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
        public LocationDefinitionModel Location { get; }
    }
}

[DebuggerDisplay("{GameDefinitions.Instance[this].Name}")]
[DebuggerTypeProxy(typeof(ItemKeyDebugView))]
public readonly record struct ItemKey
{
    private readonly ushort _n;
    public required int N
    {
        get => _n;
        init => _n = (ushort)value;
    }

    private readonly struct ItemKeyDebugView
    {
        public ItemKeyDebugView(ItemKey key)
        {
            Item = GameDefinitions.Instance[key];
        }

        [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
        public ItemDefinitionModel Item { get; }
    }
}
