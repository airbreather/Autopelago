using System.Buffers;
using System.Collections.Immutable;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Channels;

namespace ArchipelagoClientDotNet;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(ImmutableArray<ArchipelagoPacketModel>))]
[JsonSerializable(typeof(ArchipelagoPacketModel[]))]
internal sealed partial class SourceGenerationContext : JsonSerializerContext
{
}

public sealed partial class WebSocketPacketChannel : Channel<ImmutableArray<ArchipelagoPacketModel>, ArchipelagoPacketModel>, IDisposable
{
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

    private CancellationTokenSource _cts = new();

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
            await _socket.ConnectAsync(new($"{_server}:{_port}"), cancellationToken);
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
                while (true)
                {
                    try
                    {
                        foreach (ArchipelagoPacketModel packet in await ReceiveNextMessageAsync(socketClosing.Token))
                        {
                            await _receiveChannel.Writer.WriteAsync(packet, socketClosing.Token);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
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
            while (await _sendChannel.Reader.WaitToReadAsync(cancellationToken))
            {
                while (_sendChannel.Reader.TryRead(out ImmutableArray<ArchipelagoPacketModel> packetGroup))
                {
                    await SendNextMessageAsync(packetGroup, cancellationToken);
                }
            }

            using (socketClosing)
            {
                await socketClosing.CancelAsync();
                await _receiveProducerTask;
            }

            await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
        }, cancellationToken);
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

    private static ArchipelagoPacketModel[] Deserialize(ReadOnlySequence<byte> buf)
    {
        Utf8JsonReader jsonReader = new(buf);
        return JsonSerializer.Deserialize<ArchipelagoPacketModel[]>(ref jsonReader, s_jsonSerializerOptions)!;
    }

    private async ValueTask<ImmutableArray<ArchipelagoPacketModel>> ReceiveNextMessageAsync(CancellationToken cancellationToken)
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

        return ImmutableCollectionsMarshal.AsImmutableArray(responsePacket);
    }

    private async ValueTask SendNextMessageAsync(ImmutableArray<ArchipelagoPacketModel> packetGroup, CancellationToken cancellationToken)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(packetGroup, s_jsonSerializerOptions);
        await _socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken);
    }
}
