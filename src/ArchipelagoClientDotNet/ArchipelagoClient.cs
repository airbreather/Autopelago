using System.Collections.Immutable;
using System.Threading.Channels;

namespace ArchipelagoClientDotNet;

public sealed class ArchipelagoClient : IArchipelagoClient
{
    private readonly Channel<ImmutableArray<ArchipelagoPacketModel>, ArchipelagoPacketModel> _channel;

    private readonly Queue<ArchipelagoPacketModel> _buffered = [];

    public ArchipelagoClient(Channel<ImmutableArray<ArchipelagoPacketModel>, ArchipelagoPacketModel> channel)
    {
        _channel = channel;
    }

    public ValueTask<ArchipelagoPacketModel> ReadNextPacketAsync(CancellationToken cancellationToken)
    {
        return _buffered.TryDequeue(out ArchipelagoPacketModel? next)
            ? ValueTask.FromResult(next)
            : _channel.Reader.ReadAsync(cancellationToken);
    }

    public ValueTask WriteNextPacketAsync(ArchipelagoPacketModel nextPacket, CancellationToken cancellationToken)
    {
        return _channel.Writer.WriteAsync([nextPacket], cancellationToken);
    }

    public ValueTask WriteNextPacketsAsync(ImmutableArray<ArchipelagoPacketModel> nextPackets, CancellationToken cancellationToken)
    {
        return _channel.Writer.WriteAsync(nextPackets, cancellationToken);
    }

    public ValueTask<RetrievedPacketModel> GetAsync(GetPacketModel getPacket, CancellationToken cancellationToken)
    {
        return RequestAsync<RetrievedPacketModel>(getPacket, cancellationToken);
    }

    public async ValueTask<SetReplyPacketModel?> SetAsync(SetPacketModel setPacket, CancellationToken cancellationToken)
    {
        await Helper.ConfigureAwaitFalse();
        if (setPacket.WantReply)
        {
            return await RequestAsync<SetReplyPacketModel>(setPacket, cancellationToken);
        }

        await _channel.Writer.WriteAsync([setPacket], cancellationToken);
        return null;
    }

    private async ValueTask<TResponse> RequestAsync<TResponse>(ArchipelagoPacketModel request, CancellationToken cancellationToken)
        where TResponse : ArchipelagoPacketModel
    {
        await Helper.ConfigureAwaitFalse();
        await WriteNextPacketAsync(request, cancellationToken);
        while (await _channel.Reader.WaitToReadAsync(cancellationToken))
        {
            while (_channel.Reader.TryRead(out ArchipelagoPacketModel? next))
            {
                if (next is TResponse response)
                {
                    return response;
                }

                _buffered.Enqueue(next);
            }
        }

        throw new ChannelClosedException();
    }
}
