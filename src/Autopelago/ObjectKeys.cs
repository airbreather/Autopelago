namespace Autopelago;

public readonly record struct RegionKey
{
    public required byte N { get; init; }
}

public readonly record struct LocationKey
{
    public required ushort N { get; init; }
}

public readonly record struct RegionLocationKey
{
    public required RegionKey Region { get; init; }

    public required byte N { get; init; }

    public static RegionLocationKey For(RegionKey region)
    {
        return For(region, 0);
    }

    public static RegionLocationKey For(RegionKey region, byte n)
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
    public required ushort N { get; init; }
}
