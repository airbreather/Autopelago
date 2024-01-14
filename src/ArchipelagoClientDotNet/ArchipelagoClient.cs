using System.Buffers;
using System.Collections.Immutable;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ArchipelagoClientDotNet;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(ImmutableArray<ArchipelagoPacketModel>))]
[JsonSerializable(typeof(ArchipelagoPacketModel[]))]
internal sealed partial class SourceGenerationContext : JsonSerializerContext
{
}

public sealed partial class ArchipelagoClient(string server, ushort port) : IDisposable
{
    private static readonly Version s_archipelagoVersion = new("0.4.4");

    private static readonly Regex s_hasWebSocketSchemeRegex = HasWebSocketSchemeRegex();

    private static readonly JsonSerializerOptions s_jsonSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        TypeInfoResolver = SourceGenerationContext.Default,
    };

    private readonly AsyncEvent<ImmutableArray<ArchipelagoPacketModel>> _packetGroupReceivedEvent = new();

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

    private readonly SemaphoreSlim _writerLock = new(1, 1);

    private ClientWebSocket _socket = new() { Options = { DangerousDeflateOptions = new() } };

    private CancellationTokenSource _cts = new();

    private bool _triedConnecting;

    private bool _disposed;

    public bool NeedDataPackageForAllGames { get; set; }

    public event AsyncEventHandler<ImmutableArray<ArchipelagoPacketModel>> PacketGroupReceived
    {
        add => _packetGroupReceivedEvent.Add(value);
        remove => _packetGroupReceivedEvent.Remove(value);
    }

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

    public async ValueTask<bool> TryConnectAsync(string game, string slot, string? password = null, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_triedConnecting)
        {
            throw new InvalidOperationException("TryConnectAsync is a one-shot in this client.");
        }

        _triedConnecting = true;
        await Helper.ConfigureAwaitFalse();

        if (s_hasWebSocketSchemeRegex.IsMatch(server))
        {
            await _socket.ConnectAsync(new($"{server}:{port}"), cancellationToken);
        }
        else
        {
            try
            {
                await _socket.ConnectAsync(new($"wss://{server}:{port}"), cancellationToken);
            }
            catch (Exception ex)
            {
                try
                {
                    // the socket actually disposes itself after ConnectAsync fails for practically
                    // any reason (which is why we need to overwrite it with a new one here), but it
                    // still makes me feel icky not to dispose it explicitly before overwriting it,
                    // so just do it ourselves (airbreather 2024-01-12).
                    _socket.Dispose();
                    _socket = new() { Options = { DangerousDeflateOptions = new() } };
                    await _socket.ConnectAsync(new($"ws://{server}:{port}"), cancellationToken);
                }
                catch (Exception ex2)
                {
                    throw new AggregateException(ex, ex2);
                }
            }
        }

        if (await ProcessNextMessageAsync(cancellationToken) is not [RoomInfoPacketModel roomInfo])
        {
            throw new InvalidDataException("Protocol error: expected 'RoomInfo' right away.");
        }

        // TODO: cache result on disk by checksum so we don't always need to do this.
        await GetDataPackageAsync(NeedDataPackageForAllGames ? default : roomInfo.Games, cancellationToken);
        _ = await ProcessNextMessageAsync(cancellationToken);

        await ConnectAsync(
            game: game,
            slot: slot,
            password: password,
            uuid: Guid.NewGuid(),
            itemsHandling: ArchipelagoItemsHandlingFlags.All,
            slotData: true,
            tags: [],
            version: new(s_archipelagoVersion),
            cancellationToken: cancellationToken);

        bool result = await ProcessNextMessageAsync(cancellationToken) switch
        {
            [ConnectedPacketModel, ..] => true,
            [ConnectionRefusedPacketModel, ..] => false,
            _ => throw new InvalidDataException("Protocol error: received something other than 'Connected' or 'ConnectionRefused' immediately after we sent a 'Connect'..."),
        };

        _ = Task.Run(async () =>
        {
            await Helper.ConfigureAwaitFalse();
            while (!_cts.Token.IsCancellationRequested)
            {
                await ProcessNextMessageAsync(_cts.Token);
            }
        }, CancellationToken.None);

        return result;
    }

    public async ValueTask SayAsync(string text, CancellationToken cancellationToken = default)
    {
        await Helper.ConfigureAwaitFalse();
        SayPacketModel say = new() { Text = text };
        await WriteNextAsync([say], cancellationToken);
    }

    public async ValueTask LocationChecksAsync(ReadOnlyMemory<long> locations, CancellationToken cancellationToken = default)
    {
        await Helper.ConfigureAwaitFalse();
        LocationChecksPacketModel locationChecks = new() { Locations = locations };
        await WriteNextAsync([locationChecks], cancellationToken);
    }

    public async ValueTask StatusUpdateAsync(ArchipelagoClientStatus status, CancellationToken cancellationToken = default)
    {
        await Helper.ConfigureAwaitFalse();
        StatusUpdatePacketModel statusUpdate = new() { Status = status };
        await WriteNextAsync([statusUpdate], cancellationToken);
    }

    public async ValueTask<RetrievedPacketModel> GetAsync(ImmutableArray<string> keys, CancellationToken cancellationToken = default)
    {
        await Helper.ConfigureAwaitFalse();
        GetPacketModel @get = new() { Keys = keys };
        TaskCompletionSource<RetrievedPacketModel> replyTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        RetrievedPacketReceived += OnRetrievedPacketReceivedAsync;
        using (cancellationToken.Register(() => RetrievedPacketReceived -= OnRetrievedPacketReceivedAsync))
        {
            await WriteNextAsync([@get], cancellationToken);
            return await replyTcs.Task.WaitAsync(cancellationToken);
        }

        ValueTask OnRetrievedPacketReceivedAsync(object? sender, RetrievedPacketModel retrieved, CancellationToken cancellationToken)
        {
            RetrievedPacketReceived -= OnRetrievedPacketReceivedAsync;
            replyTcs.TrySetResult(retrieved);
            return ValueTask.CompletedTask;
        }
    }

    public async ValueTask<SetReplyPacketModel?> SetAsync(string key, ImmutableArray<DataStorageOperationModel> operations, JsonNode? @default = null, bool wantReply = false, CancellationToken cancellationToken = default)
    {
        await Helper.ConfigureAwaitFalse();
        SetPacketModel @set = new()
        {
            Key = key,
            Operations = operations,
            Default = @default,
            WantReply = wantReply,
        };
        if (wantReply)
        {
            TaskCompletionSource<SetReplyPacketModel> replyTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
            SetReplyPacketReceived += OnSetReplyPacketReceivedAsync;
            using (cancellationToken.Register(() => SetReplyPacketReceived -= OnSetReplyPacketReceivedAsync))
            {
                await WriteNextAsync([@set], cancellationToken);
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
            await WriteNextAsync([@set], cancellationToken);
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
                await WriteNextAsync(packets, cancellationToken);
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
            await WriteNextAsync(packets, cancellationToken);
            return [];
        }
    }

    public async ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await Helper.ConfigureAwaitFalse();

        if (_socket.State == WebSocketState.Open)
        {
            await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", cancellationToken);
            await _cts.CancelAsync();
            _cts = new();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _socket.Dispose();
        _writerLock.Dispose();
        _disposed = true;
    }

    [GeneratedRegex("^ws(?:s)?://", RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    private static partial Regex HasWebSocketSchemeRegex();

    private static ArchipelagoPacketModel[] Deserialize(ReadOnlySequence<byte> buf)
    {
        Utf8JsonReader jsonReader = new(buf);
        return JsonSerializer.Deserialize<ArchipelagoPacketModel[]>(ref jsonReader, s_jsonSerializerOptions)!;
    }

    private async ValueTask<ImmutableArray<ArchipelagoPacketModel>> ProcessNextMessageAsync(CancellationToken cancellationToken)
    {
        ArchipelagoPacketModel[] responsePacket;
        using (AFewDisposables extraDisposables = default)
        {
            using IMemoryOwner<byte> firstBufOwner = MemoryPool<byte>.Shared.Rent();
            Memory<byte> fullFirstBuf = firstBufOwner.Memory;
            ValueWebSocketReceiveResult prevReceiveResult = await _socket.ReceiveAsync(fullFirstBuf, cancellationToken);
            ReadOnlyMemory<byte> firstBuf = fullFirstBuf[..prevReceiveResult.Count];

            ReadOnlySequence<byte> buffer;
            if (prevReceiveResult.EndOfMessage)
            {
                buffer = new(firstBuf);
            }
            else
            {
                BasicSequenceSegment firstSegment = new(firstBuf);
                BasicSequenceSegment lastSegmentSoFar = firstSegment;
                while (!prevReceiveResult.EndOfMessage)
                {
                    IMemoryOwner<byte> nextBufOwner = MemoryPool<byte>.Shared.Rent();
                    extraDisposables.Add(nextBufOwner);
                    Memory<byte> fullNextBuf = nextBufOwner.Memory;
                    prevReceiveResult = await _socket.ReceiveAsync(fullNextBuf, cancellationToken);
                    lastSegmentSoFar = lastSegmentSoFar.Append(fullNextBuf[..prevReceiveResult.Count]);
                }

                buffer = new(firstSegment, 0, lastSegmentSoFar, prevReceiveResult.Count);
            }

            responsePacket = Deserialize(buffer);
        }

        for (int i = 0; i < responsePacket.Length; i++)
        {
            if (responsePacket[i] is PrintJSONPacketModel printJSON)
            {
                responsePacket[i] = printJSON.ToBestDerivedType(s_jsonSerializerOptions);
            }
        }

        ImmutableArray<ArchipelagoPacketModel> responsePacketImmutable = ImmutableCollectionsMarshal.AsImmutableArray(responsePacket);

        await _packetGroupReceivedEvent.InvokeAsync(this, responsePacketImmutable, cancellationToken);
        foreach (ArchipelagoPacketModel next in responsePacketImmutable)
        {
            await _anyPacketReceivedEvent.InvokeAsync(this, next, cancellationToken).ConfigureAwait(false);
            await (next switch
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

        return responsePacketImmutable;
    }

    private async ValueTask GetDataPackageAsync(ImmutableArray<string> games, CancellationToken cancellationToken)
    {
        await Helper.ConfigureAwaitFalse();
        GetDataPackagePacketModel getDataPackage = new() { Games = games };
        await WriteNextAsync([getDataPackage], cancellationToken);
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
        await WriteNextAsync([connect], cancellationToken);
    }

    private async ValueTask WriteNextAsync<T>(ImmutableArray<T> values, CancellationToken cancellationToken)
        where T : ArchipelagoPacketModel
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await Helper.ConfigureAwaitFalse();

        await _writerLock.WaitAsync(cancellationToken);
        try
        {
            await _socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(ImmutableArray<ArchipelagoPacketModel>.CastUp(values), s_jsonSerializerOptions), WebSocketMessageType.Text, true, cancellationToken);
        }
        finally
        {
            _writerLock.Release();
        }
    }
}
