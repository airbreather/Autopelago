using System.Buffers;

namespace ArchipelagoClientDotNet;

internal sealed class MySequenceSegment : ReadOnlySequenceSegment<byte>
{
    public MySequenceSegment(ReadOnlyMemory<byte> buf) : this(buf, 0)
    {
    }

    private MySequenceSegment(ReadOnlyMemory<byte> buf, long runningIndex)
    {
        Memory = buf;
        RunningIndex = runningIndex;
    }

    public MySequenceSegment Append(ReadOnlyMemory<byte> buf)
    {
        if (Next is not null)
        {
            throw new InvalidOperationException("Cannot Append onto a segment that has already been appended to.");
        }

        MySequenceSegment next = new(buf, RunningIndex + Memory.Length);
        Next = next;
        return next;
    }
}
