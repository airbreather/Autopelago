using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.IO.Pipelines;
using System.Net.WebSockets;
using System.Text.Json;

namespace ArchipelagoClientDotNet;

public sealed class ArchipelagoClient(string server, ushort port) : IDisposable
{
    private static readonly Version s_archipelagoVersion = new("0.4.3");

    private static readonly JsonSerializerOptions s_jsonSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private static readonly JsonSerializerOptions s_prettyJsonSerializerOptions = new(s_jsonSerializerOptions)
    {
        WriteIndented = true,
    };

    private readonly AsyncEvent<ReadOnlyMemory<ArchipelagoPacketModel>> _packetGroupReceivedEvent = new();

    private readonly AsyncEvent<ArchipelagoPacketModel> _anyPacketReceivedEvent = new();

    private readonly AsyncEvent<RoomInfoPacketModel> _roomInfoPacketReceivedEvent = new();

    private readonly AsyncEvent<DataPackagePacketModel> _dataPackagePacketReceivedEvent = new();

    private readonly AsyncEvent<ConnectedPacketModel> _connectedPacketReceivedEvent = new();

    private readonly AsyncEvent<ConnectionRefusedPacketModel> _connectionRefusedPacketReceivedEvent = new();

    private readonly AsyncEvent<ReceivedItemsPacketModel> _receivedItemsPacketReceivedEvent = new();

    private readonly AsyncEvent<PrintJSONPacketModel> _printJSONPacketReceivedEvent = new();

    private readonly ClientWebSocket _socket = new() { Options = { DangerousDeflateOptions = new() } };

    private CancellationTokenSource _cts = new();

    private bool _disposed;

    public event AsyncEventHandler<ReadOnlyMemory<ArchipelagoPacketModel>> PacketGroupReceived
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

    public PipeReader? MessageReader { get; private set; }

    public PipeWriter? MessageWriter { get; private set; }

    public async ValueTask<bool> TryConnectAsync(string game, string slot, string? password = null, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (MessageReader is not null || MessageWriter is not null)
        {
            throw new InvalidOperationException("TryConnectAsync is a one-shot in this client.");
        }

        await Helper.ConfigureAwaitFalse();

        await _socket.ConnectAsync(new Uri($"ws://{server}:{port}"), cancellationToken);
        MessageReader = _socket.UsePipeReader(cancellationToken: _cts.Token);
        MessageWriter = _socket.UsePipeWriter(cancellationToken: _cts.Token);

        if (await ProcessNextMessageAsync(cancellationToken) is not [RoomInfoPacketModel roomInfo])
        {
            throw new InvalidDataException("Protocol error: expected 'RoomInfo' right away.");
        }

        // TODO: cache result on disk by checksum so we don't always need to do this.
        GetDataPackagePacketModel[] getDataPackage = [new() { Games = roomInfo.Games }];
        await WriteNextAsync(getDataPackage, MessageWriter, cancellationToken);
        _ = await ProcessNextMessageAsync(cancellationToken);

        ConnectPacketModel[] connectPacket =
        [
            new()
            {
                Game = game,
                Name = slot,
                Password = password,
                Uuid = Guid.NewGuid(),
                ItemsHandling = ArchipelagoItemsHandlingFlags.All,
                SlotData = true,
                Tags = [],
                Version = new(s_archipelagoVersion),
            },
        ];

        await WriteNextAsync(connectPacket, MessageWriter, cancellationToken);
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
        });

        return result;
    }

    public async ValueTask SendAsync(ReadOnlyMemory<ArchipelagoPacketModel> packets, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (MessageWriter is not PipeWriter writer)
        {
            throw new InvalidOperationException("Not connected.");
        }

        await Helper.ConfigureAwaitFalse();

        await WriteNextAsync(packets, writer, cancellationToken);
    }

    public async ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        await Helper.ConfigureAwaitFalse();

        if (_socket.State == WebSocketState.Open)
        {
            MessageReader = null;
            MessageWriter = null;

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
        _disposed = true;
    }

    private static bool TryDeserialize(PipeReader pipeReader, in ReadResult pipeReadResult, [NotNullWhen(true)] out ArchipelagoPacketModel[]? result)
    {
        ReadOnlySequence<byte> buffer = pipeReadResult.Buffer;
        Utf8JsonReader jsonReader = new(buffer);
        try
        {
            result = JsonSerializer.Deserialize<ArchipelagoPacketModel[]>(ref jsonReader, s_jsonSerializerOptions);
        }
        catch (JsonException ex)
        {
            if (pipeReadResult.IsCompleted)
            {
                throw new EndOfStreamException("Stream ended before a full JSON object", ex);
            }

            if (pipeReadResult.IsCanceled)
            {
                throw new OperationCanceledException("Stream was canceled before reading a full JSON object", ex);
            }

            pipeReader.AdvanceTo(buffer.Start, buffer.End);
            result = default;
            return false;
        }

        pipeReader.AdvanceTo(buffer.GetPosition(jsonReader.BytesConsumed));
        if (result is null)
        {
            throw new InvalidDataException("JSON object was somehow null.");
        }

        return true;
    }

    private static async ValueTask WriteNextAsync(ReadOnlyMemory<ArchipelagoPacketModel> values, PipeWriter writer, CancellationToken cancellationToken)
    {
        await JsonSerializer.SerializeAsync(writer.AsStream(true), values, s_jsonSerializerOptions, cancellationToken);
    }

    private async ValueTask<ArchipelagoPacketModel[]> ProcessNextMessageAsync(CancellationToken cancellationToken)
    {
        if (MessageReader is not PipeReader reader)
        {
            throw new InvalidOperationException("Must be running.");
        }

        ArchipelagoPacketModel[]? responsePacket;
        while (true)
        {
            ReadResult readResult = await reader.ReadAsync(cancellationToken);
            if (TryDeserialize(reader, readResult, out responsePacket))
            {
                break;
            }
        }

        for (int i = 0; i < responsePacket.Length; i++)
        {
            if (responsePacket[i] is PrintJSONPacketModel printJSON)
            {
                responsePacket[i] = printJSON.ToBestDerivedType(s_jsonSerializerOptions);
            }
        }

        await _packetGroupReceivedEvent.InvokeAsync(this, responsePacket, cancellationToken);
        foreach (ArchipelagoPacketModel next in responsePacket)
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
                _ => new(Task.Run(() => Console.WriteLine("UNRECOGNIZED"))),
            });
        }

        return responsePacket;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(ArchipelagoClient));
        }
    }
}
