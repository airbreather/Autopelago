using System.Buffers;

namespace ArchipelagoClientDotNet;

internal sealed class BasicSequenceSegment : ReadOnlySequenceSegment<byte>
{
    public BasicSequenceSegment(ReadOnlyMemory<byte> memory)
        : this(memory, 0)
    {
    }

    private BasicSequenceSegment(ReadOnlyMemory<byte> memory, long runningIndex)
    {
        Memory = memory;
        RunningIndex = runningIndex;
    }

    public BasicSequenceSegment Append(ReadOnlyMemory<byte> memory)
    {
        if (Next is not null)
        {
            throw new InvalidOperationException("This segment already has a Next.");
        }

        BasicSequenceSegment next = new(memory, RunningIndex + Memory.Length);
        Next = next;
        return next;
    }
}
