using System.Collections.Immutable;
using System.Text.Json.Nodes;
using System.Threading.Channels;

namespace ArchipelagoClientDotNet;

public sealed class ArchipelagoClient(Channel<ImmutableArray<ArchipelagoPacketModel>, ArchipelagoPacketModel> packetsChannel) : IArchipelagoClient
{
    private static readonly Version s_archipelagoVersion = new("0.4.4");

    private readonly AsyncEvent<ArchipelagoPacketModel> _anyPacketReceivedEvent = new();

    private readonly AsyncEvent<RoomInfoPacketModel> _roomInfoPacketReceivedEvent = new();

    private readonly AsyncEvent<DataPackagePacketModel> _dataPackagePacketReceivedEvent = new();

    private readonly AsyncEvent<ConnectedPacketModel> _connectedPacketReceivedEvent = new();

    private readonly AsyncEvent<ConnectionRefusedPacketModel> _connectionRefusedPacketReceivedEvent = new();

    private readonly AsyncEvent<ReceivedItemsPacketModel> _receivedItemsPacketReceivedEvent = new();

    private readonly AsyncEvent<PrintJSONPacketModel> _printJSONPacketReceivedEvent = new();

    private readonly AsyncEvent<RoomUpdatePacketModel> _roomUpdatePacketReceivedEvent = new();

    private readonly AsyncEvent<RetrievedPacketModel> _retrievedPacketReceivedEvent = new();

    private readonly AsyncEvent<SetReplyPacketModel> _setReplyPacketReceivedEvent = new();

    private bool _triedConnecting;

    private Task? _channelReadTask;

    public event AsyncEventHandler<ArchipelagoPacketModel> AnyPacketReceived
    {
        add => _anyPacketReceivedEvent.Add(value);
        remove => _anyPacketReceivedEvent.Remove(value);
    }

    public event AsyncEventHandler<RoomInfoPacketModel> RoomInfoPacketReceived
    {
        add => _roomInfoPacketReceivedEvent.Add(value);
        remove => _roomInfoPacketReceivedEvent.Remove(value);
    }

    public event AsyncEventHandler<DataPackagePacketModel> DataPackagePacketReceived
    {
        add => _dataPackagePacketReceivedEvent.Add(value);
        remove => _dataPackagePacketReceivedEvent.Remove(value);
    }

    public event AsyncEventHandler<ConnectedPacketModel> ConnectedPacketReceived
    {
        add => _connectedPacketReceivedEvent.Add(value);
        remove => _connectedPacketReceivedEvent.Remove(value);
    }

    public event AsyncEventHandler<ConnectionRefusedPacketModel> ConnectionRefusedPacketReceived
    {
        add => _connectionRefusedPacketReceivedEvent.Add(value);
        remove => _connectionRefusedPacketReceivedEvent.Remove(value);
    }

    public event AsyncEventHandler<ReceivedItemsPacketModel> ReceivedItemsPacketReceived
    {
        add => _receivedItemsPacketReceivedEvent.Add(value);
        remove => _receivedItemsPacketReceivedEvent.Remove(value);
    }

    public event AsyncEventHandler<PrintJSONPacketModel> PrintJSONPacketReceived
    {
        add => _printJSONPacketReceivedEvent.Add(value);
        remove => _printJSONPacketReceivedEvent.Remove(value);
    }

    public event AsyncEventHandler<RoomUpdatePacketModel> RoomUpdatePacketReceived
    {
        add => _roomUpdatePacketReceivedEvent.Add(value);
        remove => _roomUpdatePacketReceivedEvent.Remove(value);
    }

    public event AsyncEventHandler<RetrievedPacketModel> RetrievedPacketReceived
    {
        add => _retrievedPacketReceivedEvent.Add(value);
        remove => _retrievedPacketReceivedEvent.Remove(value);
    }

    public event AsyncEventHandler<SetReplyPacketModel> SetReplyPacketReceived
    {
        add => _setReplyPacketReceivedEvent.Add(value);
        remove => _setReplyPacketReceivedEvent.Remove(value);
    }

    public async ValueTask<bool> TryConnectAsync(string game, string slot, string? password, ImmutableArray<string> tags, CancellationToken cancellationToken = default)
    {
        if (_triedConnecting)
        {
            throw new InvalidOperationException("TryConnectAsync is a one-shot in this client.");
        }

        _triedConnecting = true;
        await Helper.ConfigureAwaitFalse();

        if (await packetsChannel.Reader.ReadAsync(cancellationToken) is not RoomInfoPacketModel roomInfo)
        {
            throw new InvalidDataException("Protocol error: expected 'RoomInfo' right away.");
        }

        await _roomInfoPacketReceivedEvent.InvokeAsync(this, roomInfo, cancellationToken);

        // TODO: cache result on disk by checksum so we don't always need to do this.
        await GetDataPackageAsync(roomInfo.Games, cancellationToken);
        if (await packetsChannel.Reader.ReadAsync(cancellationToken) is not DataPackagePacketModel dataPackage)
        {
            throw new InvalidDataException("Protocol error: expected 'DataPackage' right away.");
        }

        await _dataPackagePacketReceivedEvent.InvokeAsync(this, dataPackage, cancellationToken);

        await ConnectAsync(
            game: game,
            slot: slot,
            password: password,
            uuid: Guid.NewGuid(),
            itemsHandling: ArchipelagoItemsHandlingFlags.All,
            slotData: true,
            tags: tags,
            version: new(s_archipelagoVersion),
            cancellationToken: cancellationToken);

        switch (await packetsChannel.Reader.ReadAsync(cancellationToken))
        {
            case ConnectedPacketModel connected:
                await _connectedPacketReceivedEvent.InvokeAsync(this, connected, cancellationToken);
                break;

            case ConnectionRefusedPacketModel connectionRefused:
                await _connectionRefusedPacketReceivedEvent.InvokeAsync(this, connectionRefused, cancellationToken);
                return false;

            default:
                throw new InvalidDataException("Protocol error: received something other than 'Connected' or 'ConnectionRefused' immediately after we sent a 'Connect'...");
        };

        _channelReadTask = Task.Run(async () =>
        {
            await foreach (ArchipelagoPacketModel packet in packetsChannel.Reader.ReadAllAsync().WithCancellation(cancellationToken))
            {
                await _anyPacketReceivedEvent.InvokeAsync(this, packet, cancellationToken).ConfigureAwait(false);
                await (packet switch
                {
                    RoomInfoPacketModel roomInfo => _roomInfoPacketReceivedEvent.InvokeAsync(this, roomInfo, cancellationToken),
                    DataPackagePacketModel dataPackage => _dataPackagePacketReceivedEvent.InvokeAsync(this, dataPackage, cancellationToken),
                    ConnectedPacketModel connected => _connectedPacketReceivedEvent.InvokeAsync(this, connected, cancellationToken),
                    ConnectionRefusedPacketModel connectionRefused => _connectionRefusedPacketReceivedEvent.InvokeAsync(this, connectionRefused, cancellationToken),
                    ReceivedItemsPacketModel receivedItems => _receivedItemsPacketReceivedEvent.InvokeAsync(this, receivedItems, cancellationToken),
                    PrintJSONPacketModel printJSON => _printJSONPacketReceivedEvent.InvokeAsync(this, printJSON, cancellationToken),
                    RoomUpdatePacketModel roomUpdate => _roomUpdatePacketReceivedEvent.InvokeAsync(this, roomUpdate, cancellationToken),
                    RetrievedPacketModel retrieved => _retrievedPacketReceivedEvent.InvokeAsync(this, retrieved, cancellationToken),
                    SetReplyPacketModel setReply => _setReplyPacketReceivedEvent.InvokeAsync(this, setReply, cancellationToken),
                    _ => ValueTask.CompletedTask,
                });
            }
        }, cancellationToken);

        return true;
    }

    public async ValueTask SayAsync(string text, CancellationToken cancellationToken = default)
    {
        await Helper.ConfigureAwaitFalse();
        SayPacketModel say = new() { Text = text };
        await packetsChannel.Writer.WriteAsync([say], cancellationToken);
    }

    public async ValueTask LocationChecksAsync(ReadOnlyMemory<long> locations, CancellationToken cancellationToken = default)
    {
        await Helper.ConfigureAwaitFalse();
        LocationChecksPacketModel locationChecks = new() { Locations = locations };
        await packetsChannel.Writer.WriteAsync([locationChecks], cancellationToken);
    }

    public async ValueTask StatusUpdateAsync(ArchipelagoClientStatus status, CancellationToken cancellationToken = default)
    {
        await Helper.ConfigureAwaitFalse();
        StatusUpdatePacketModel statusUpdate = new() { Status = status };
        await packetsChannel.Writer.WriteAsync([statusUpdate], cancellationToken);
    }

    public async ValueTask<RetrievedPacketModel> GetAsync(ImmutableArray<string> keys, CancellationToken cancellationToken = default)
    {
        await Helper.ConfigureAwaitFalse();
        GetPacketModel @get = new() { Keys = keys };
        TaskCompletionSource<RetrievedPacketModel> replyTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        RetrievedPacketReceived += OnRetrievedPacketReceivedAsync;
        using (cancellationToken.Register(() => RetrievedPacketReceived -= OnRetrievedPacketReceivedAsync))
        {
            await packetsChannel.Writer.WriteAsync([@get], cancellationToken);
            return await replyTcs.Task.WaitAsync(cancellationToken);
        }

        ValueTask OnRetrievedPacketReceivedAsync(object? sender, RetrievedPacketModel retrieved, CancellationToken cancellationToken)
        {
            RetrievedPacketReceived -= OnRetrievedPacketReceivedAsync;
            replyTcs.TrySetResult(retrieved);
            return ValueTask.CompletedTask;
        }
    }

    public async ValueTask<SetReplyPacketModel?> SetAsync(string key, ImmutableArray<DataStorageOperationModel> operations, JsonNode? defaultValue = null, bool wantReply = false, CancellationToken cancellationToken = default)
    {
        await Helper.ConfigureAwaitFalse();
        SetPacketModel @set = new()
        {
            Key = key,
            Operations = operations,
            Default = defaultValue,
            WantReply = wantReply,
        };
        if (wantReply)
        {
            TaskCompletionSource<SetReplyPacketModel> replyTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
            SetReplyPacketReceived += OnSetReplyPacketReceivedAsync;
            using (cancellationToken.Register(() => SetReplyPacketReceived -= OnSetReplyPacketReceivedAsync))
            {
                await packetsChannel.Writer.WriteAsync([@set], cancellationToken);
                return await replyTcs.Task.WaitAsync(cancellationToken);
            }

            ValueTask OnSetReplyPacketReceivedAsync(object? sender, SetReplyPacketModel setReply, CancellationToken cancellationToken)
            {
                SetReplyPacketReceived -= OnSetReplyPacketReceivedAsync;
                replyTcs.TrySetResult(setReply);
                return ValueTask.CompletedTask;
            }
        }
        else
        {
            await packetsChannel.Writer.WriteAsync([@set], cancellationToken);
            return null;
        }
    }

    public async ValueTask<ImmutableArray<SetReplyPacketModel>> SetAsync(ImmutableArray<SetPacketModel> packets, CancellationToken cancellationToken = default)
    {
        await Helper.ConfigureAwaitFalse();
        int repliesWanted = 0;
        foreach (SetPacketModel packet in packets)
        {
            if (packet.WantReply)
            {
                ++repliesWanted;
            }
        }

        if (repliesWanted > 0)
        {
            ImmutableArray<SetReplyPacketModel>.Builder repliesBuilder = ImmutableArray.CreateBuilder<SetReplyPacketModel>(repliesWanted);
            TaskCompletionSource replyTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
            SetReplyPacketReceived += OnSetReplyPacketReceivedAsync;
            using (cancellationToken.Register(() => SetReplyPacketReceived -= OnSetReplyPacketReceivedAsync))
            {
                await packetsChannel.Writer.WriteAsync(ImmutableArray<ArchipelagoPacketModel>.CastUp(packets), cancellationToken);
                await replyTcs.Task.WaitAsync(cancellationToken);
                return repliesBuilder.MoveToImmutable();
            }

            ValueTask OnSetReplyPacketReceivedAsync(object? sender, SetReplyPacketModel setReply, CancellationToken cancellationToken)
            {
                repliesBuilder.Add(setReply);
                if (--repliesWanted == 0)
                {
                    SetReplyPacketReceived -= OnSetReplyPacketReceivedAsync;
                    replyTcs.TrySetResult();
                }

                return ValueTask.CompletedTask;
            }
        }
        else
        {
            await packetsChannel.Writer.WriteAsync(ImmutableArray<ArchipelagoPacketModel>.CastUp(packets), cancellationToken);
            return [];
        }
    }

    public async ValueTask StopGracefullyAsync(CancellationToken cancellationToken = default)
    {
        await Helper.ConfigureAwaitFalse();
        if (_channelReadTask is not { } channelReadTask)
        {
            throw new InvalidOperationException("Cannot until after a successful TryConnectAsync.");
        }

        packetsChannel.Writer.Complete();
        try
        {
            await channelReadTask.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async ValueTask GetDataPackageAsync(ImmutableArray<string> games, CancellationToken cancellationToken)
    {
        await Helper.ConfigureAwaitFalse();
        GetDataPackagePacketModel getDataPackage = new() { Games = games };
        await packetsChannel.Writer.WriteAsync([getDataPackage], cancellationToken);
    }

    private async ValueTask ConnectAsync(string game, string slot, string? password, Guid uuid, ArchipelagoItemsHandlingFlags itemsHandling, bool slotData, ImmutableArray<string> tags, VersionModel version, CancellationToken cancellationToken)
    {
        await Helper.ConfigureAwaitFalse();
        ConnectPacketModel connect = new()
        {
            Game = game,
            Name = slot,
            Password = password,
            Uuid = uuid,
            ItemsHandling = itemsHandling,
            SlotData = slotData,
            Tags = tags,
            Version = version,
        };
        await packetsChannel.Writer.WriteAsync([connect], cancellationToken);
    }
}
