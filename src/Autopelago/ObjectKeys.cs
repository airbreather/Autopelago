namespace Autopelago;

public readonly record struct RegionKey
{
    private readonly byte _n;
    public required int N
    {
        get => _n;
        init => _n = (byte)value;
    }
}

public readonly record struct LocationKey
{
    public static readonly LocationKey Nonexistent = new() { N = ushort.MaxValue };

    private readonly ushort _n;
    public required int N
    {
        get => _n;
        init => _n = (ushort)value;
    }
}

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
}

public readonly record struct ItemKey
{
    private readonly ushort _n;
    public required int N
    {
        get => _n;
        init => _n = (ushort)value;
    }
}
