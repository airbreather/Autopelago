using System.Runtime.ExceptionServices;

using ArchipelagoClientDotNet;

using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Autopelago.Web;

public sealed class AutopelagoGameService : BackgroundService
{
    private readonly TimeProvider _timeProvider;

    private readonly ILogger<AutopelagoGameService> _logger;

    public AutopelagoGameService(TimeProvider timeProvider, ILogger<AutopelagoGameService> logger)
    {
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Helper.ConfigureAwaitFalse();

        string settingsYaml = await File.ReadAllTextAsync(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "game-config.yaml"), stoppingToken);
        AutopelagoSettingsModel settings = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build()
            .Deserialize<AutopelagoSettingsModel>(settingsYaml);

        using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        ExceptionDispatchInfo? edi = null;
        BackgroundTaskRunner.BackgroundException += (sender, args) =>
        {
            edi = args.BackgroundException;
            cts.Cancel();
        };
        await Task.WhenAll(settings.Slots.Select(slotName => Task.Run(async () =>
        {
            await Helper.ConfigureAwaitFalse();

            #if false

            await using WebSocketPacketChannel channel = new(settings.Server, settings.Port);
            await channel.ConnectAsync(cts.Token);
            ArchipelagoClient archipelagoClient = new(channel);
            archipelagoClient.PacketReceived += OnClientPacketReceived;
            static ValueTask OnClientPacketReceived(object? sender, PacketReceivedEventArgs args, CancellationToken cancellationToken)
            {
                if (args.Packet is PrintJSONPacketModel printJSON)
                {
                    foreach (JSONMessagePartModel part in printJSON.Data)
                    {
                        Console.Write(part.Text);
                    }

                    Console.WriteLine();
                }

                return ValueTask.CompletedTask;
            }

            RealAutopelagoClient client = new(archipelagoClient);
            RoomInfoPacketModel roomInfo = await archipelagoClient.Handshake1Async(cts.Token);
            DataPackagePacketModel dataPackage = await archipelagoClient.Handshake2Async(new() { Games = [settings.GameName] }, cts.Token);
            ConnectResponsePacketModel connectResponse = await archipelagoClient.Handshake3Async(new()
            {
                Password = settings.Slots[0].Password,
                Game = settings.GameName,
                Name = settings.Slots[0].Name,
                Uuid = Guid.NewGuid(),
                Version = new(new Version("0.4.4")),
                ItemsHandling = ArchipelagoItemsHandlingFlags.All,
                Tags = ["AP"],
                SlotData = true,
            }, cts.Token);

            if (connectResponse is not ConnectedPacketModel { Team: int team, Slot: int slot })
            {
                throw new InvalidDataException("Connection refused.");
            }

            ArchipelagoGameStateStorage gameStateStorage = new(archipelagoClient, $"autopelago_state_{team}_{slot}");

            #else
            UnrandomizedAutopelagoClient client = new();
            LocalGameStateStorage gameStateStorage = new();
            #endif

            Game game = new(client, _timeProvider, gameStateStorage);
            try
            {
                await game.RunUntilCanceledOrCompletedAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
            }
        }, cts.Token)));

        edi?.Throw();
        _logger.LogInformation("Done");
    }
}
