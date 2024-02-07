using System.Collections.Immutable;
using System.Text.Json.Nodes;

namespace ArchipelagoClientDotNet;

public interface IArchipelagoClient
{
    event AsyncEventHandler<ArchipelagoPacketModel> AnyPacketReceived;

    event AsyncEventHandler<RoomInfoPacketModel> RoomInfoPacketReceived;

    event AsyncEventHandler<DataPackagePacketModel> DataPackagePacketReceived;

    event AsyncEventHandler<ConnectedPacketModel> ConnectedPacketReceived;

    event AsyncEventHandler<ConnectionRefusedPacketModel> ConnectionRefusedPacketReceived;

    event AsyncEventHandler<ReceivedItemsPacketModel> ReceivedItemsPacketReceived;

    event AsyncEventHandler<PrintJSONPacketModel> PrintJSONPacketReceived;

    event AsyncEventHandler<RoomUpdatePacketModel> RoomUpdatePacketReceived;

    event AsyncEventHandler<RetrievedPacketModel> RetrievedPacketReceived;

    event AsyncEventHandler<SetReplyPacketModel> SetReplyPacketReceived;

    ValueTask<bool> TryConnectAsync(string game, string slot, string? password, ImmutableArray<string> tags, CancellationToken cancellationToken = default);

    ValueTask SayAsync(string text, CancellationToken cancellationToken = default);

    ValueTask LocationChecksAsync(ReadOnlyMemory<long> locations, CancellationToken cancellationToken = default);

    ValueTask StatusUpdateAsync(ArchipelagoClientStatus status, CancellationToken cancellationToken = default);

    ValueTask<RetrievedPacketModel> GetAsync(ImmutableArray<string> keys, CancellationToken cancellationToken = default);

    ValueTask<SetReplyPacketModel?> SetAsync(string key, ImmutableArray<DataStorageOperationModel> operations, JsonNode? defaultValue = null, bool wantReply = false, CancellationToken cancellationToken = default);

    ValueTask<ImmutableArray<SetReplyPacketModel>> SetAsync(ImmutableArray<SetPacketModel> packets, CancellationToken cancellationToken = default);

    ValueTask StopGracefullyAsync(CancellationToken cancellationToken = default);
}
