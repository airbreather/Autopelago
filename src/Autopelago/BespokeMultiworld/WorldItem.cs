using System.Runtime.InteropServices;

namespace Autopelago.BespokeMultiworld;

[StructLayout(LayoutKind.Auto, Pack = 1)]
public readonly record struct WorldItem
{
    public required int Slot { get; init; }

    public required ItemKey Item { get; init; }
}
