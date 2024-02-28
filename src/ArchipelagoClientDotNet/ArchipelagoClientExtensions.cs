namespace ArchipelagoClientDotNet;

public static class ArchipelagoClientExtensions
{
    public static async ValueTask<RetrievedPacketModel> GetAsync(this IArchipelagoClient @this, GetPacketModel getPacket, CancellationToken cancellationToken)
    {
        await Helper.ConfigureAwaitFalse();
        TaskCompletionSource<RetrievedPacketModel> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        @this.PacketReceived += OnClientPacketReceived;
        await @this.WriteNextPacketAsync(getPacket, cancellationToken);
        return await tcs.Task.ConfigureAwait(false);
        ValueTask OnClientPacketReceived(object? sender, PacketReceivedEventArgs args, CancellationToken cancellationToken)
        {
            if (args.Packet is RetrievedPacketModel retrieved)
            {
                @this.PacketReceived -= OnClientPacketReceived;
                tcs.TrySetResult(retrieved);
            }

            return ValueTask.CompletedTask;
        }
    }

    public static async ValueTask<SetReplyPacketModel?> SetAsync(this IArchipelagoClient @this, SetPacketModel setPacket, CancellationToken cancellationToken)
    {
        await Helper.ConfigureAwaitFalse();
        if (setPacket.WantReply)
        {
            await @this.WriteNextPacketAsync(setPacket, cancellationToken);
            return null;
        }

        TaskCompletionSource<SetReplyPacketModel> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        @this.PacketReceived += OnClientPacketReceived;
        await @this.WriteNextPacketAsync(setPacket, cancellationToken);
        return await tcs.Task.ConfigureAwait(false);
        ValueTask OnClientPacketReceived(object? sender, PacketReceivedEventArgs args, CancellationToken cancellationToken)
        {
            if (args.Packet is SetReplyPacketModel retrieved)
            {
                tcs.TrySetResult(retrieved);
            }

            return ValueTask.CompletedTask;
        }
    }
}
