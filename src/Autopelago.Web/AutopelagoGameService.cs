using System.Collections.Frozen;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;

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

        Dictionary<string, IObservable<Game.State>> result = [];
        for (int i = 0; i < settings.Slots.Count; i++)
        {
            AutopelagoPlayerSettingsModel slot = settings.Slots[i];
            ArchipelagoConnection conn = new(settings.Server, settings.Port);
            RealAutopelagoClient client = new(conn);
            if (i == 0)
            {
                conn.IncomingPackets.OfType<PrintJSONPacketModel>()
                    .WithLatestFrom(client.ServerGameData)
                    .Subscribe(
                        tup =>
                        {
                            (PrintJSONPacketModel printJSON, RealAutopelagoClient.AutopelagoData data) = tup;

                            StringBuilder sb = new();
                            sb.Append($"[{DateTime.Now:G}] -> ");
                            foreach (JSONMessagePartModel part in printJSON.Data)
                            {
                                sb.Append(part switch
                                {
                                    PlayerIdJSONMessagePartModel playerId => data.SlotInfo[int.Parse(playerId.Text)].Name,
                                    ItemIdJSONMessagePartModel itemId => data.ItemsReverseMapping[long.Parse(itemId.Text)].Name,
                                    LocationIdJSONMessagePartModel locationId => data.LocationsReverseMapping[long.Parse(locationId.Text)].Name,
                                    _ => part.Text,
                                });
                            }

                            Console.WriteLine(sb);
                        });
            }

            GetDataPackagePacketModel getDataPackage = new();
            ConnectPacketModel connect = new()
            {
                Password = slot.Password,
                Game = settings.GameName,
                Name = slot.Name,
                Uuid = Guid.NewGuid(),
                Version = new(new Version("0.4.4")),
                ItemsHandling = ArchipelagoItemsHandlingFlags.All,
                Tags = ["AP"],
                SlotData = true,
            };
            Game.State state =
                await client.InitAsync(getDataPackage, connect, Random.Shared, stoppingToken) ??
                throw new InvalidOperationException("Failed to connect");

            result.Add(slot.Name, Observable.Using(() => new EventLoopScheduler(), sch =>
            {
                IConnectableObservable<Game.State> multicast = Game.Run(state, client, _timeProvider)
                    .ObserveOn(sch)
                    .Do(state =>
                    {
                        client.SaveGameStateAsync(state, stoppingToken).WaitMoreSafely();
                        if (state.IsCompleted)
                        {
                            StatusUpdatePacketModel statusUpdate = new() { Status = ArchipelagoClientStatus.Goal };
                            conn.SendPacketsAsync([statusUpdate], stoppingToken).WaitMoreSafely();
                        }
                    })
                    .Replay(1);

                multicast.Connect();
                return multicast;
            }));
        }

        return result.ToFrozenDictionary();
    }
}
