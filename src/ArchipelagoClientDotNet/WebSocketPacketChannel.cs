using System.Buffers;
using System.Collections.Immutable;
using System.Net.WebSockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;

namespace ArchipelagoClientDotNet;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(ImmutableArray<ArchipelagoPacketModel>))]
[JsonSerializable(typeof(ArchipelagoPacketModel[]))]
internal sealed partial class SourceGenerationContext : JsonSerializerContext
{
}

public sealed partial class WebSocketPacketChannel : Channel<ImmutableArray<ArchipelagoPacketModel>, ArchipelagoPacketModel>, IAsyncDisposable
{
    private static readonly JsonReaderOptions s_jsonReaderOptions = new()
    {
        MaxDepth = 1000,
    };

    private static readonly JsonSerializerOptions s_jsonSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        TypeInfoResolver = SourceGenerationContext.Default,
    };

    private static readonly UnboundedChannelOptions s_receiveChannelOptions = new()
    {
        ////SingleWriter = true, // our send channel can "write" a completion message, so... no?
        AllowSynchronousContinuations = false,
    };

    private static readonly UnboundedChannelOptions s_sendChannelOptions = new()
    {
        SingleReader = true,
        AllowSynchronousContinuations = false,
    };

    private readonly string _server;

    private readonly ushort _port;

    private readonly Channel<ArchipelagoPacketModel> _receiveChannel = Channel.CreateUnbounded<ArchipelagoPacketModel>(s_receiveChannelOptions);

    private readonly Channel<ImmutableArray<ArchipelagoPacketModel>> _sendChannel = Channel.CreateUnbounded<ImmutableArray<ArchipelagoPacketModel>>(s_sendChannelOptions);

    private ClientWebSocket _socket = new() { Options = { DangerousDeflateOptions = new() } };

    private Task? _receiveProducerTask;

    private Task? _sendConsumerTask;

    private bool _disposed;

    public WebSocketPacketChannel(string server, ushort port)
    {
        _server = server;
        _port = port;

        Reader = _receiveChannel.Reader;
        Writer = _sendChannel.Writer;
    }

    public async ValueTask ConnectAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await Helper.ConfigureAwaitFalse();

        if (Uri.TryCreate(_server, UriKind.Absolute, out Uri? uri) && uri.Scheme is ['w', 's', ..])
        {
            await _socket.ConnectAsync(uri, cancellationToken);
        }
        else
        {
            try
            {
                await _socket.ConnectAsync(new($"wss://{_server}:{_port}"), cancellationToken);
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
                    await _socket.ConnectAsync(new($"ws://{_server}:{_port}"), cancellationToken);
                }
                catch (Exception ex2)
                {
                    throw new AggregateException(ex, ex2);
                }
            }
        }

        CancellationTokenSource socketClosing = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _receiveProducerTask = Task.Run(async () =>
        {
            await Helper.ConfigureAwaitFalse();
            try
            {
                using IMemoryOwner<byte> firstBufOwner = MemoryPool<byte>.Shared.Rent(65536);
                Memory<byte> fullFirstBuf = firstBufOwner.Memory;
                Queue<IDisposable?> extraDisposables = [];
                while (true)
                {
                    ValueWebSocketReceiveResult prevReceiveResult = await _socket.ReceiveAsync(fullFirstBuf, socketClosing.Token);
                    ReadOnlyMemory<byte> firstBuf = fullFirstBuf[..prevReceiveResult.Count];
                    if (firstBuf.IsEmpty)
                    {
                        continue;
                    }

                    // we're going to stream the objects in the array one-by-one.
                    int startIndex = 0;
                    JsonReaderState readerState = new(s_jsonReaderOptions);
                    while (TryGetNextPacket(new(firstBuf[startIndex..]), prevReceiveResult.EndOfMessage, ref readerState) is (ArchipelagoPacketModel packet, long bytesConsumed))
                    {
                        startIndex = checked((int)(startIndex + bytesConsumed));
                        await _receiveChannel.Writer.WriteAsync(packet, socketClosing.Token);
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
                            prevReceiveResult = await _socket.ReceiveAsync(fullNextBuf, socketClosing.Token);
                            endSegment = endSegment.Append(fullNextBuf[..prevReceiveResult.Count]);
                            while (TryGetNextPacket(new(startSegment, startIndex, endSegment, endSegment.Memory.Length), prevReceiveResult.EndOfMessage, ref readerState) is (ArchipelagoPacketModel packet, long bytesConsumed))
                            {
                                long totalBytesConsumed = startIndex + bytesConsumed;
                                while (totalBytesConsumed > startSegment.Memory.Length)
                                {
                                    totalBytesConsumed -= startSegment.Memory.Length;
                                    startSegment = (BasicSequenceSegment)startSegment.Next!;
                                    extraDisposables.Dequeue()?.Dispose();
                                }

                                startIndex = checked((int)totalBytesConsumed);
                                await _receiveChannel.Writer.WriteAsync(packet, socketClosing.Token);
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
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _receiveChannel.Writer.TryComplete(ex);
            }

            _receiveChannel.Writer.TryComplete();
        }, socketClosing.Token);

        _sendConsumerTask = Task.Run(async () =>
        {
            await Helper.ConfigureAwaitFalse();
            using (socketClosing)
            {
                try
                {
                    while (await _sendChannel.Reader.WaitToReadAsync(socketClosing.Token))
                    {
                        while (_sendChannel.Reader.TryRead(out ImmutableArray<ArchipelagoPacketModel> packetGroup))
                        {
                            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(packetGroup, s_jsonSerializerOptions);
                            await _socket.SendAsync(bytes, WebSocketMessageType.Text, true, socketClosing.Token);
                        }
                    }

                    await socketClosing.CancelAsync();
                }
                catch (OperationCanceledException)
                {
                }

                try
                {
                    await _receiveProducerTask;
                }
                catch (OperationCanceledException)
                {
                }
            }

            try
            {
                await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
            }
            catch (WebSocketException)
            {
            }
        }, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await (_sendConsumerTask ?? Task.CompletedTask);
        _socket.Dispose();
        _disposed = true;
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

        ArchipelagoPacketModel packet = JsonSerializer.Deserialize<ArchipelagoPacketModel>(ref reader, s_jsonSerializerOptions)!;
        readerState = reader.CurrentState;
        return (packet, reader.BytesConsumed);
    }
}
