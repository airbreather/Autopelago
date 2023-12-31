using System.Buffers;
using System.Diagnostics;
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

    private readonly AsyncEvent<ConnectedPacketModel> _connectedPacketReceivedEvent = new();

    private readonly AsyncEvent<ConnectionRefusedPacketModel> _connectionRefusedPacketReceivedEvent = new();

    private readonly ClientWebSocket _socket = new();

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

    public PipeReader? MessageReader { get; private set; }

    public PipeWriter? MessageWriter { get; private set; }

    public bool Running { get; private set; }

    public async ValueTask<bool> TryConnectAsync(string game, string slot, string? password = null, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        await Helper.ConfigureAwaitFalse();

        if (MessageReader is null || MessageWriter is null)
        {
            await _socket.ConnectAsync(new Uri($"ws://{server}:{port}"), cancellationToken);
            MessageReader = _socket.UsePipeReader(cancellationToken: _cts.Token);
            MessageWriter = _socket.UsePipeWriter(cancellationToken: _cts.Token);

            await ProcessNextMessageAsync(cancellationToken);
            CancellationToken fullStreamReadToken = _cts.Token;
            _ = Task.Run(async () =>
            {
                while (true)
                {
                    await ProcessNextMessageAsync(fullStreamReadToken);
                }
            }, fullStreamReadToken);
        }

        ConnectPacketModel connectPacket = new()
        {
            Game = game,
            Name = slot,
            Password = password,
            Uuid = Guid.NewGuid(),
            ItemsHandling = ArchipelagoItemsHandlingFlags.All,
            SlotData = true,
            Tags = [],
            Version = new(s_archipelagoVersion),
        };

        TaskCompletionSource<bool> resultTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        AnyPacketReceived += OnAnyPacketReceivedAsync;
        await WriteNextAsync([connectPacket], MessageWriter, cancellationToken);
        ValueTask OnAnyPacketReceivedAsync(object? sender, ArchipelagoPacketModel packet, CancellationToken cancellationToken)
        {
            AnyPacketReceived -= OnAnyPacketReceivedAsync;
            switch (packet)
            {
                case ConnectedPacketModel:
                    resultTcs.SetResult(true);
                    break;

                case ConnectionRefusedPacketModel:
                    resultTcs.SetResult(false);
                    break;

                default:
                    resultTcs.SetException(new InvalidDataException("Protocol error: received something other than 'Connected' or 'ConnectionRefused' immediately after we sent a 'Connect'..."));
                    break;
            }

            return ValueTask.CompletedTask;
        }

        return await resultTcs.Task;
    }

    public async ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        await Helper.ConfigureAwaitFalse();

        if (Running)
        {
            Running = false;
            MessageReader = null;
            MessageWriter = null;

            await _cts.CancelAsync();
            await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", cancellationToken);
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

            pipeReader.AdvanceTo(buffer.Start, buffer.GetPosition(jsonReader.BytesConsumed));
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

    private static async ValueTask WriteNextAsync(ArchipelagoPacketModel[] values, PipeWriter writer, CancellationToken cancellationToken)
    {
        await JsonSerializer.SerializeAsync(writer.AsStream(true), values, s_jsonSerializerOptions, cancellationToken);
    }

    private async ValueTask ProcessNextMessageAsync(CancellationToken cancellationToken)
    {
        if (MessageReader is not PipeReader reader)
        {
            throw new InvalidOperationException("Must be running.");
        }

        ArchipelagoPacketModel[]? responsePacket;
        while (true)
        {
            if (TryDeserialize(reader, await reader.ReadAsync(cancellationToken), out responsePacket))
            {
                break;
            }
        }

        await _packetGroupReceivedEvent.InvokeAsync(this, responsePacket, cancellationToken);
        foreach (ArchipelagoPacketModel next in responsePacket)
        {
            await _anyPacketReceivedEvent.InvokeAsync(this, next, cancellationToken).ConfigureAwait(false);
            await (next switch
            {
                RoomInfoPacketModel roomInfo => _roomInfoPacketReceivedEvent.InvokeAsync(this, roomInfo, cancellationToken),
                ConnectedPacketModel connected => _connectedPacketReceivedEvent.InvokeAsync(this, connected, cancellationToken),
                ConnectionRefusedPacketModel connectionRefused => _connectionRefusedPacketReceivedEvent.InvokeAsync(this, connectionRefused, cancellationToken),
                _ => ValueTask.CompletedTask,
            });
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(ArchipelagoClient));
        }
    }
}
