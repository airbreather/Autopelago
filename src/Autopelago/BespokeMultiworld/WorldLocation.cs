using System.Runtime.InteropServices;

namespace Autopelago.BespokeMultiworld;

[StructLayout(LayoutKind.Auto, Pack = 0)]
public readonly record struct WorldLocation
{
    public required int Slot { get; init; }

    public required LocationKey Location { get; init; }
}
