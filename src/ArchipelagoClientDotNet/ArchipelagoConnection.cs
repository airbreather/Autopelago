using System.Buffers;
using System.Collections.Immutable;
using System.Net.WebSockets;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Reactive.Threading.Tasks;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ArchipelagoClientDotNet;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(ImmutableArray<ArchipelagoPacketModel>))]
internal sealed partial class SourceGenerationContext : JsonSerializerContext
{
}

public interface IArchipelagoConnection
{
    IObservable<ArchipelagoPacketModel> IncomingPackets { get; }

    ValueTask SendPacketsAsync(ImmutableArray<ArchipelagoPacketModel> packets, CancellationToken cancellationToken);

    ValueTask<ConnectResponsePacketModel> HandshakeAsync(ConnectPacketModel connect, CancellationToken cancellationToken);
}

public sealed partial class ArchipelagoConnection : IArchipelagoConnection, IDisposable
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

    private readonly string _server;

    private readonly ushort _port;

    private readonly IConnectableObservable<ArchipelagoPacketModel> _incomingPackets;

    private ClientWebSocket _socket = new() { Options = { DangerousDeflateOptions = new() } };

    private bool _disposed;

    public ArchipelagoConnection(string server, ushort port)
    {
        _server = server;
        _port = port;

        _incomingPackets = Observable.Create<ArchipelagoPacketModel>(async (observer, cancellationToken) =>
        {
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

            using IMemoryOwner<byte> firstBufOwner = MemoryPool<byte>.Shared.Rent(65536);
            Memory<byte> fullFirstBuf = firstBufOwner.Memory;
            Queue<IDisposable?> extraDisposables = [];
            while (true)
            {
                ValueWebSocketReceiveResult prevReceiveResult = await _socket.ReceiveAsync(fullFirstBuf, cancellationToken);
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
                    observer.OnNext(packet);
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
                        prevReceiveResult = await _socket.ReceiveAsync(fullNextBuf, cancellationToken);
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
                            observer.OnNext(packet);
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
        }).Publish();
    }

    public IObservable<ArchipelagoPacketModel> IncomingPackets => _incomingPackets;

    public async ValueTask SendPacketsAsync(ImmutableArray<ArchipelagoPacketModel> packets, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_socket.State != WebSocketState.Open)
        {
            throw new InvalidOperationException("Not yet connected.");
        }

        await Helper.ConfigureAwaitFalse();
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(packets, s_jsonSerializerOptions);
        await _socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
    }

    public async ValueTask<ConnectResponsePacketModel> HandshakeAsync(ConnectPacketModel connect, CancellationToken cancellationToken)
    {
        await Helper.ConfigureAwaitFalse();
        Task<RoomInfoPacketModel> roomInfoTask = _incomingPackets.FirstAsync().Cast<RoomInfoPacketModel>().ToTask(cancellationToken);
        _incomingPackets.Connect();
        RoomInfoPacketModel roomInfo = await roomInfoTask;
        GetDataPackagePacketModel getDataPackage = new() { Games = roomInfo.Games };
        Task<DataPackagePacketModel> dataPackageTask = _incomingPackets.FirstAsync().Cast<DataPackagePacketModel>().ToTask(cancellationToken);
        await SendPacketsAsync([getDataPackage], cancellationToken);
        await dataPackageTask;
        Task<ConnectResponsePacketModel> connectResponseTask = _incomingPackets.FirstAsync().Cast<ConnectResponsePacketModel>().ToTask(cancellationToken);
        await SendPacketsAsync([connect], cancellationToken);
        return await connectResponseTask;
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
