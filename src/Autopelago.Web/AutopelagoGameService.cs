using System.Collections.Frozen;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Runtime.ExceptionServices;

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

        using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        ExceptionDispatchInfo? edi = null;
        BackgroundTaskRunner.BackgroundException += (sender, args) =>
        {
            edi = args.BackgroundException;
            cts.Cancel();
        };

        return settings.Slots.Select(slot =>
        {
            _logger.LogInformation("Starting for slot {Slot}", slot.Name);

            ArchipelagoConnection conn = new(settings.Server, settings.Port);
            conn.IncomingPackets.Subscribe(packet =>
            {
                if (packet is PrintJSONPacketModel printJSON)
                {
                    foreach (JSONMessagePartModel part in printJSON.Data)
                    {
                        Console.Write(part.Text);
                    }

                    Console.WriteLine();
                }
            });
            RealAutopelagoClient client = new(conn);
            IConnectableObservable<Game.State> gameStates = Game.Run(Game.State.Start(), client, _timeProvider).Publish();
            gameStates.Connect();

            ConnectResponsePacketModel connectedResponse = null!;
            BackgroundTaskRunner.Run(async () =>
            {
                await Helper.ConfigureAwaitFalse();
                connectedResponse = await conn.HandshakeAsync(new()
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
            }, stoppingToken).GetAwaiter().GetResult();

            return KeyValuePair.Create(slot.Name, (IObservable<Game.State>)gameStates);
        }).ToFrozenDictionary();
    }
}
