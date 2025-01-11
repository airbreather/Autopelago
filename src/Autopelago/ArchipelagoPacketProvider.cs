using System.Buffers;
using System.Collections.Immutable;
using System.Net.WebSockets;
using System.Reactive.Disposables;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Autopelago;

public sealed class ArchipelagoPacketProvider
{
    private static readonly JsonReaderOptions s_jsonReaderOptions = new()
    {
        MaxDepth = 1000,
    };

    private static readonly JsonTypeInfo<ArchipelagoPacketModel> s_packetTypeInfo =
        PacketSerializerContext.Default.ArchipelagoPacketModel;

    private static readonly JsonTypeInfo<ImmutableArray<ArchipelagoPacketModel>> s_packetsTypeInfo =
        PacketSerializerContext.Default.ImmutableArrayArchipelagoPacketModel;

    private readonly Lock _lock = new();

    private readonly List<ArchipelagoPacketHandler> _handlers = [];

    private readonly SemaphoreSlim _writerMutex = new(1);

    private readonly List<ArchipelagoPacketModel> _bufferedPackets = [];

    private readonly Settings _settings;

    private ClientWebSocket? _socket;

    private CancellationToken _cancellationToken;

    public ArchipelagoPacketProvider(Settings settings)
    {
        _settings = settings;
    }

    public SetPacketModel CreateUpdateStatePacket(Game game, string serverSavedStateKey)
    {
        return new()
        {
            Key = serverSavedStateKey,
            Operations =
            [
                new()
                {
                    Operation = ArchipelagoDataStorageOperationType.Replace,
                    Value = JsonSerializer.SerializeToNode(
                        game.ServerSavedState,
                        ServerSavedStateSerializationContext.Default.ServerSavedState
                    )!,
                },
            ],
        };
    }

    public async ValueTask<IDisposable> RegisterHandlerAsync(ArchipelagoPacketHandler handler)
    {
        ImmutableArray<ArchipelagoPacketModel> bufferedPackets = [];
        lock (_lock)
        {
            _handlers.Add(handler);
            if (_bufferedPackets.Count > 0)
            {
                bufferedPackets = [.. _bufferedPackets];
                _bufferedPackets.Clear();
            }
        }

        foreach (ArchipelagoPacketModel packet in bufferedPackets)
        {
            await handler.HandleAsync(packet, this, _cancellationToken);
        }

        return Disposable.Create(() =>
        {
            lock (_lock)
            {
                _handlers.Remove(handler);
            }
        });
    }

    public async Task RunToCompletionAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref _socket, socket, null) is not null)
        {
            throw new InvalidOperationException("Already running.");
        }

        _cancellationToken = cancellationToken;
        using IMemoryOwner<byte> firstBufOwner = MemoryPool<byte>.Shared.Rent(65536);
        Memory<byte> fullFirstBuf = firstBufOwner.Memory;
        Queue<IDisposable?> extraDisposables = [];
        while (!cancellationToken.IsCancellationRequested)
        {
            ValueWebSocketReceiveResult prevReceiveResult;
            try
            {
                prevReceiveResult = await socket.ReceiveAsync(fullFirstBuf, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                continue;
            }

            ReadOnlyMemory<byte> firstBuf = fullFirstBuf[..prevReceiveResult.Count];
            if (firstBuf.IsEmpty)
            {
                continue;
            }

            // we're going to stream the objects in the array one-by-one.
            int startIndex = 0;
            JsonReaderState readerState = new(s_jsonReaderOptions);
            while (TryGetNextPacket(new(firstBuf[startIndex..]), prevReceiveResult.EndOfMessage, ref readerState) is ({ } packet, long bytesConsumed))
            {
                startIndex = checked((int)(startIndex + bytesConsumed));
                await HandleNextPacketAsync(packet, cancellationToken);
            }

            if (prevReceiveResult.EndOfMessage)
            {
                continue;
            }

            extraDisposables.Enqueue(null); // the first one lives through the entire outer loop.
            try
            {
                BasicSequenceSegment startSegment = new(firstBuf);
                BasicSequenceSegment endSegment = startSegment;
                while (!prevReceiveResult.EndOfMessage)
                {
                    IMemoryOwner<byte> nextBufOwner = MemoryPool<byte>.Shared.Rent(65536);
                    extraDisposables.Enqueue(nextBufOwner);
                    Memory<byte> fullNextBuf = nextBufOwner.Memory;
                    prevReceiveResult = await socket.ReceiveAsync(fullNextBuf, default);
                    endSegment = endSegment.Append(fullNextBuf[..prevReceiveResult.Count]);
                    while (TryGetNextPacket(new(startSegment, startIndex, endSegment, endSegment.Memory.Length), prevReceiveResult.EndOfMessage, ref readerState) is ({ } packet, long bytesConsumed))
                    {
                        long totalBytesConsumed = startIndex + bytesConsumed;
                        while (totalBytesConsumed > startSegment.Memory.Length)
                        {
                            totalBytesConsumed -= startSegment.Memory.Length;
                            startSegment = (BasicSequenceSegment)startSegment.Next!;
                            extraDisposables.Dequeue()?.Dispose();
                        }

                        startIndex = checked((int)totalBytesConsumed);
                        await HandleNextPacketAsync(packet, cancellationToken);
                    }
                }
            }
            finally
            {
                while (extraDisposables.TryDequeue(out IDisposable? disposable))
                {
                    disposable?.Dispose();
                }
            }
        }
    }

    public async ValueTask SendPacketsAsync(ImmutableArray<ArchipelagoPacketModel> packets)
    {
        if (_socket is not { } socket)
        {
            throw new InvalidOperationException("Not running!");
        }

        if (!_settings.RatChat)
        {
            packets = packets.RemoveAll(packet => packet is SayPacketModel);
        }

        if (packets.IsEmpty)
        {
            return;
        }

        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(packets, s_packetsTypeInfo);
        await _writerMutex.WaitAsync(_cancellationToken);
        try
        {
            await socket.SendAsync(bytes, WebSocketMessageType.Text, true, _cancellationToken);
        }
        finally
        {
            _writerMutex.Release();
        }
    }

    private static (ArchipelagoPacketModel Packet, long BytesConsumed)? TryGetNextPacket(ReadOnlySequence<byte> seq, bool endOfMessage, ref JsonReaderState readerState)
    {
        Utf8JsonReader reader = new(seq, endOfMessage, readerState);
        if (reader.TokenType == JsonTokenType.None && !reader.Read())
        {
            return null;
        }

        if (reader.TokenType is JsonTokenType.StartArray or JsonTokenType.EndObject && !reader.Read())
        {
            return null;
        }

        if (reader.TokenType == JsonTokenType.EndArray)
        {
            return null;
        }

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Archipelago protocol error: each message must be a JSON array of JSON objects.");
        }

        if (!endOfMessage)
        {
            Utf8JsonReader testReader = reader;
            if (!testReader.TrySkip())
            {
                return null;
            }
        }

        ArchipelagoPacketModel packet = JsonSerializer.Deserialize(ref reader, s_packetTypeInfo)!;
        if (packet is PrintJSONPacketModel printJSON)
        {
            packet = printJSON.ToBestDerivedType();
        }

        readerState = reader.CurrentState;
        return (packet, reader.BytesConsumed);
    }

    private async ValueTask HandleNextPacketAsync(ArchipelagoPacketModel packet, CancellationToken cancellationToken)
    {
        ArchipelagoPacketHandler[] handlers;
        lock (_lock)
        {
            handlers = [.. _handlers];
        }

        foreach (ArchipelagoPacketHandler handler in handlers)
        {
            await handler.HandleAsync(packet, this, cancellationToken);
        }
    }
}
