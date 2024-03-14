using System.Runtime.ExceptionServices;

using ArchipelagoClientDotNet;

using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Autopelago.Web;

public sealed class AutopelagoGameService : BackgroundService
{
    private readonly CurrentGameStates _currentGameStates;

    private readonly TimeProvider _timeProvider;

    private readonly ILogger<AutopelagoGameService> _logger;

    public AutopelagoGameService(CurrentGameStates currentGameStates, TimeProvider timeProvider, ILogger<AutopelagoGameService> logger)
    {
        _currentGameStates = currentGameStates;
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
        await Task.WhenAll(settings.Slots.Select(slot => Task.Run(async () =>
        {
            await Helper.ConfigureAwaitFalse();

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

            if (connectResponse is not ConnectedPacketModel { Team: int team, Slot: int slotNumber })
            {
                throw new InvalidDataException("Connection refused.");
            }

            ArchipelagoGameStateStorage gameStateStorage = new(archipelagoClient, $"autopelago_state_{team}_{slotNumber}");

            Game game = new(client, _timeProvider, gameStateStorage);
            game.StepFinished += async (sender, args, cancellationToken) =>
            {
                if (args.StateBeforeAdvance.Epoch != args.StateAfterAdvance.Epoch)
                {
                    await _currentGameStates.SetAsync(slot.Name, args.StateAfterAdvance, cancellationToken);
                }
            };
            try
            {
                await Task.WhenAll(
                    Task.Run(async () => await game.RunUntilCanceledOrCompletedAsync(cts.Token).ConfigureAwait(false)),
                    Task.Run(async () => await archipelagoClient.RunUntilCanceledAsync(cts.Token).ConfigureAwait(false)));
            }
            catch (OperationCanceledException)
            {
            }
        }, cts.Token)));

        edi?.Throw();
        _logger.LogInformation("Done");
    }
}
