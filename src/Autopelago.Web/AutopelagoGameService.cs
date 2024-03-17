using System.Collections.Frozen;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;

using ArchipelagoClientDotNet;

using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Autopelago.Web;

public sealed class AutopelagoGameService : BackgroundService
{
    private readonly SlotGameStates _slotGameStates;

    private readonly IScheduler _timeProvider;

    private readonly ILogger<AutopelagoGameService> _logger;

    public AutopelagoGameService(SlotGameStates slotGameStates, IScheduler timeProvider, ILogger<AutopelagoGameService> logger)
    {
        _slotGameStates = slotGameStates;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Helper.ConfigureAwaitFalse();
        try
        {
            _slotGameStates.GameStatesMappingBox.TrySetResult(await InnerExecuteAsync(stoppingToken));
        }
        catch (OperationCanceledException ex)
        {
            _slotGameStates.GameStatesMappingBox.TrySetCanceled(ex.CancellationToken);
        }
        catch (Exception ex)
        {
            _slotGameStates.GameStatesMappingBox.TrySetException(ex);
        }
    }

    private async ValueTask<FrozenDictionary<string, IObservable<Game.State>>> InnerExecuteAsync(CancellationToken stoppingToken)
    {
        await Helper.ConfigureAwaitFalse();

        string settingsYaml = await File.ReadAllTextAsync(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "game-config.yaml"), stoppingToken);
        AutopelagoSettingsModel settings = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build()
            .Deserialize<AutopelagoSettingsModel>(settingsYaml);

        return settings.Slots.Select((slot, i) =>
        {
            _logger.LogInformation("Starting for slot {Slot}", slot.Name);

            ArchipelagoConnection conn = new(settings.Server, settings.Port);
            RealAutopelagoClient client = new(conn, i == 0);

            ConnectedPacketModel? connected = null;
            Game.State? state = null;
            BackgroundTaskRunner.Run(async () =>
            {
                await Helper.ConfigureAwaitFalse();
                ConnectResponsePacketModel connectedResponse = await conn.HandshakeAsync(new()
                {
                    Password = slot.Password,
                    Game = settings.GameName,
                    Name = slot.Name,
                    Uuid = Guid.NewGuid(),
                    Version = new(new Version("0.4.4")),
                    ItemsHandling = ArchipelagoItemsHandlingFlags.All,
                    Tags = ["AP"],
                    SlotData = true,
                }, stoppingToken);

                if (connectedResponse is ConnectedPacketModel connected_)
                {
                    connected = connected_;
                    state = await client.InitGameStateAsync(connected, null, stoppingToken);
                }
            }, stoppingToken).GetAwaiter().GetResult();

            if (connected is null || state is null)
            {
                return KeyValuePair.Create(slot.Name, Observable.Throw<Game.State>(new InvalidOperationException("Failed to connect")));
            }

            IConnectableObservable<Game.State> gameStates = Game.Run(state, client, _timeProvider)
                .Do(state => BackgroundTaskRunner.Run(async () =>
                {
                    await Helper.ConfigureAwaitFalse();
                    await client.SaveGameStateAsync(connected, state, stoppingToken);
                    if (state.IsCompleted)
                    {
                        StatusUpdatePacketModel statusUpdate = new() { Status = ArchipelagoClientStatus.Goal };
                        await conn.SendPacketsAsync([statusUpdate], stoppingToken);
                    }
                }, stoppingToken).GetAwaiter().GetResult())
                .Replay(1);
            gameStates.Connect();

            return KeyValuePair.Create(slot.Name, gameStates.AsObservable());
        }).ToFrozenDictionary();
    }
}
