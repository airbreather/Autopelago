using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Autopelago.BespokeMultiworld;

[DebuggerDisplay("Slot = {Slot}, Location = {GameDefinitions.Instance[Location].Name}")]
[StructLayout(LayoutKind.Auto, Pack = 1)]
public readonly record struct WorldLocation
{
    public required int Slot { get; init; }

    public required LocationKey Location { get; init; }
}
