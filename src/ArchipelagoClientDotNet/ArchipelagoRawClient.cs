using System.Buffers;
using System.Net.WebSockets;

namespace ArchipelagoClientDotNet;

public sealed class ArchipelagoRawClient(string server, ushort port) : IDisposable
{
    private readonly ClientWebSocket _socket = new();

    private readonly AsyncEvent<ReadOnlySequence<byte>> _rawMessageReceivedEvent = new();

    private readonly CancellationTokenSource _cts = new();

    private bool _disposed;

    public bool Running { get; private set; }

    public event AsyncEventHandler<ReadOnlySequence<byte>> RawMessageReceived
    {
        add => _rawMessageReceivedEvent.Add(value);
        remove => _rawMessageReceivedEvent.Remove(value);
    }

    public async ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (Running)
        {
            throw new InvalidOperationException("Already running.");
        }

        await Helper.ConfigureAwaitFalse();

        await _socket.ConnectAsync(new Uri($"ws://{server}:{port}"), cancellationToken);
        Running = true;
        _ = Task.Run(async () => await ReadFullMessagesAsync(_cts.Token), _cts.Token);
    }

    public async ValueTask SendAsync(ReadOnlyMemory<byte> rawMessage, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (!Running)
        {
            throw new InvalidOperationException("Not running.");
        }

        await Helper.ConfigureAwaitFalse();

        using CancellationTokenSource? cts = cancellationToken.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cts.Token)
            : null;
        cancellationToken = (cts ?? _cts).Token;

        await _socket.SendAsync(rawMessage, WebSocketMessageType.Text, WebSocketMessageFlags.EndOfMessage, cancellationToken);
    }

    public ValueTask SendAsync(ReadOnlySequence<byte> rawMessage, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (!Running)
        {
            throw new InvalidOperationException("Not running.");
        }

        return rawMessage.IsSingleSegment
            ? SendAsync(rawMessage.First, cancellationToken)
            : Inner(rawMessage, cancellationToken);

        async ValueTask Inner(ReadOnlySequence<byte> rawMessage, CancellationToken cancellationToken)
        {
            await Helper.ConfigureAwaitFalse();
            using CancellationTokenSource? cts = cancellationToken.CanBeCanceled
                ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cts.Token)
                : null;
            cancellationToken = (cts ?? _cts).Token;

            ReadOnlySequence<byte>.Enumerator enumerator = rawMessage.GetEnumerator();
            ReadOnlyMemory<byte> prev = Memory<byte>.Empty;
            if (enumerator.MoveNext())
            {
                prev = enumerator.Current;
                while (enumerator.MoveNext())
                {
                    await _socket.SendAsync(prev, WebSocketMessageType.Text, WebSocketMessageFlags.None, cancellationToken);
                    prev = enumerator.Current;
                }
            }

            await _socket.SendAsync(prev, WebSocketMessageType.Text, WebSocketMessageFlags.EndOfMessage, cancellationToken);
        }
    }

    public async ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (!Running)
        {
            throw new InvalidOperationException("Not running.");
        }

        await Helper.ConfigureAwaitFalse();

        await _cts.CancelAsync();
        await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", cancellationToken);
        Running = false;
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
            throw new ObjectDisposedException(nameof(ArchipelagoRawClient));
        }
    }
}
