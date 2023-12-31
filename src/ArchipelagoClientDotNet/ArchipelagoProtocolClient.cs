using System.Buffers;
using System.Text;
using System.Text.Json;

using Microsoft.IO;

using Nerdbank.Streams;

namespace ArchipelagoClientDotNet;

public sealed class ArchipelagoProtocolClient
{
    private static readonly RecyclableMemoryStreamManager s_recyclableMemoryStreamManager = new();

    private static readonly Version s_archipelagoVersion = new("0.4.3");

    private static readonly JsonSerializerOptions s_jsonSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private static readonly JsonSerializerOptions s_prettyJsonSerializerOptions = new(s_jsonSerializerOptions)
    {
        WriteIndented = true,
    };

    private readonly ArchipelagoRawClient _rawClient;

    private readonly AsyncEvent<RoomInfoPacketModel> _roomInfoMessageReceivedEvent = new();

    private readonly TaskCompletionSource<RoomInfoPacketModel> _roomInfoMessageTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public event AsyncEventHandler<RoomInfoPacketModel> RoomInfoMessageReceived
    {
        add => _roomInfoMessageReceivedEvent.Add(value);
        remove => _roomInfoMessageReceivedEvent.Remove(value);
    }

    public ArchipelagoProtocolClient(ArchipelagoRawClient rawClient)
    {
        _rawClient = rawClient;

        rawClient.RawMessageReceived += OnRawMessageReceivedAsync;
        RoomInfoMessageReceived += OnRoomInfoMessageReceivedAsync;
    }

    public async ValueTask ConnectAsync(string game, string slot, string? password = null, CancellationToken cancellationToken = default)
    {
        await Helper.ConfigureAwaitFalse();

        ConnectPacketModel[] connectPacketModel =
        [
            new()
            {
                Game = game,
                Name = slot,
                Password = password,
                Uuid = Guid.NewGuid(),
                ItemsHandling = ArchipelagoItemsHandlingFlags.All,
                SlotData = true,
                Version = new()
                {
                    Major = s_archipelagoVersion.Major,
                    Minor = s_archipelagoVersion.Minor,
                    Build = s_archipelagoVersion.Build,
                },
            },
        ];

        using RecyclableMemoryStream ms = s_recyclableMemoryStreamManager.GetStream();
        JsonSerializer.Serialize(ms, connectPacketModel, s_jsonSerializerOptions);

        RoomInfoPacketModel roomInfo = await _roomInfoMessageTcs.Task;
        await _rawClient.SendAsync(ms.GetReadOnlySequence(), cancellationToken);
    }

    private async ValueTask OnRawMessageReceivedAsync(object? sender, ReadOnlySequence<byte> rawMessage, CancellationToken cancellationToken)
    {
        await Helper.ConfigureAwaitFalse();
        await foreach (ArchipelagoPacketModel? packet in JsonSerializer.DeserializeAsyncEnumerable<ArchipelagoPacketModel>(rawMessage.AsStream(), s_jsonSerializerOptions, cancellationToken))
        {
            switch (packet)
            {
                case null:
                    throw new InvalidDataException("Received null packet.");

                case RoomInfoPacketModel roomInfo:
                    await _roomInfoMessageReceivedEvent.InvokeAsync(this, roomInfo, cancellationToken);
                    break;

                default:
                    Console.WriteLine($"Received packet that we don't handle just yet: {Encoding.UTF8.GetString(JsonSerializer.SerializeToUtf8Bytes(packet, s_prettyJsonSerializerOptions))}");
                    break;
            }
        }
    }

    private ValueTask OnRoomInfoMessageReceivedAsync(object? sender, RoomInfoPacketModel roomInfo, CancellationToken cancellationToken)
    {
        if (!_roomInfoMessageTcs.TrySetResult(roomInfo))
        {
            throw new InvalidDataException("RoomInfo packet should only ever show up once.");
        }

        return ValueTask.CompletedTask;
    }
}
