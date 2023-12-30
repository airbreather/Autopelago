using System.Buffers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace Autopelago.ArchipelagoClient;

public sealed class ArchipelagoClient : IDisposable
{
    private static readonly JsonSerializerOptions s_jsonSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private readonly ClientWebSocket _socket = new();

    private readonly AsyncEvent<ReadOnlySequence<byte>> _rawMessageReceivedEvent = new();

    private bool _disposed;

    public event AsyncEventHandler<ReadOnlySequence<byte>> RawMessageReceived
    {
        add => _rawMessageReceivedEvent.Add(value);
        remove => _rawMessageReceivedEvent.Remove(value);
    }

    public async ValueTask ConnectAsync(string server, ushort port, string game, string slot, string? password = null, CancellationToken cancellationToken = default)
    {
        await Helper.ConfigureAwaitFalse();

        RawMessageReceived += OnFirstMessageReceived;

        await _socket.ConnectAsync(new Uri($"ws://{server}:{port}"), cancellationToken);
        _ = Task.Run(async () => await ReadFullMessagesAsync(cancellationToken), cancellationToken);

        async ValueTask OnFirstMessageReceived(object? sender, ReadOnlySequence<byte> rawBytes, CancellationToken cancellationToken)
        {
            RawMessageReceived -= OnFirstMessageReceived;
            if (Deserialize<RoomInfoPacketModel[]>(rawBytes) is not [ RoomInfoPacketModel roomInfo ])
            {
                throw new InvalidDataException("No room info.");
            }

            Console.WriteLine(Encoding.UTF8.GetString(JsonSerializer.SerializeToUtf8Bytes(roomInfo, new JsonSerializerOptions() { WriteIndented = true })));
            throw new NotImplementedException("I haven't gotten any further than this yet.");
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

    private static T? Deserialize<T>(ReadOnlySequence<byte> rawBytes)
    {
        Utf8JsonReader reader = new(rawBytes);
        return JsonSerializer.Deserialize<T>(ref reader, s_jsonSerializerOptions);
    }

    private async ValueTask ReadFullMessagesAsync(CancellationToken cancellationToken)
    {
        await Helper.ConfigureAwaitFalse();

        using IMemoryOwner<byte> firstBufOwner = MemoryPool<byte>.Shared.Rent(4096);
        List<IMemoryOwner<byte>> moreBufOwners = [];
        while (true)
        {
            Memory<byte> fullFirstBuf = firstBufOwner.Memory;
            ValueWebSocketReceiveResult result = await _socket.ReceiveAsync(fullFirstBuf, cancellationToken);
            ReadOnlyMemory<byte> firstBuf = fullFirstBuf[..result.Count];
            if (result.EndOfMessage)
            {
                await _rawMessageReceivedEvent.InvokeAsync(this, new(firstBuf), cancellationToken);
                continue;
            }

            MySequenceSegment firstSegment = new(firstBuf);
            MySequenceSegment lastSegmentSoFar = firstSegment;

            try
            {
                while (!result.EndOfMessage)
                {
                    IMemoryOwner<byte> nextBufOwner = MemoryPool<byte>.Shared.Rent(65536);
                    moreBufOwners.Add(nextBufOwner);

                    Memory<byte> fullNextBuf = nextBufOwner.Memory;
                    result = await _socket.ReceiveAsync(fullNextBuf, cancellationToken);
                    lastSegmentSoFar = lastSegmentSoFar.Append(fullNextBuf[..result.Count]);
                }

                await _rawMessageReceivedEvent.InvokeAsync(this, new(firstSegment, 0, lastSegmentSoFar, result.Count), cancellationToken);
            }
            finally
            {
                foreach (IMemoryOwner<byte> owner in moreBufOwners)
                {
                    owner.Dispose();
                }

                moreBufOwners.Clear();
            }
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(ArchipelagoClient));
        }
    }

    private sealed record RoomInfoPacketModel
    {
        public required VersionModel Version { get; init; }

        public required VersionModel GeneratorVersion { get; init; }

        public required string[] Tags { get; init; }

        public required bool Password { get; init; }

        public required int HintCost { get; init; }

        public required int LocationCheckPoints { get; init; }

        public required string[] Games { get; init; }

        public required string SeedName { get; init; }

        public required double Time { get; init; }
    }

    private sealed record VersionModel
    {
        public required int Major { get; init; }

        public required int Minor { get; init; }

        public required int Build { get; init; }
    }
}
