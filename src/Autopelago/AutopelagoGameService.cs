using System.Text;

namespace Autopelago;

public sealed class AutopelagoGameService : BackgroundService
{
    private readonly AutopelagoSettingsModel _settings;

    private readonly SlotGameLookup _slotGameStates;

    private readonly TimeProvider _timeProvider;

    private readonly IHostApplicationLifetime _lifetime;

    public AutopelagoGameService(AutopelagoSettingsModel settings, SlotGameLookup slotGameStates, TimeProvider timeProvider, IHostApplicationLifetime lifetime)
    {
        _settings = settings;
        _slotGameStates = slotGameStates;
        _timeProvider = timeProvider;
        _lifetime = lifetime;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Helper.ConfigureAwaitFalse();
        await Parallel.ForAsync(0, _settings.Slots.Count, stoppingToken, async (i, cancellationToken) =>
        {
            AutopelagoPlayerSettingsModel slot = _settings.Slots[i];
            ArchipelagoConnection conn = new(_settings.Server, _settings.Port);
            RealAutopelagoClient client = new(conn);
            TimeSpan minInterval = TimeSpan.FromSeconds(slot.OverriddenSettings?.MinSecondsPerGameStep ?? _settings.DefaultSettings.MinSecondsPerGameStep);
            TimeSpan maxInterval = TimeSpan.FromSeconds(slot.OverriddenSettings?.MaxSecondsPerGameStep ?? _settings.DefaultSettings.MaxSecondsPerGameStep);
            Game game = new(minInterval, maxInterval, client, _timeProvider);
            if (i == 0)
            {
                RealAutopelagoClient.AutopelagoData? latestData = null;
                client.ServerGameData += OnServerGameDataUpdatedAsync;
                ValueTask OnServerGameDataUpdatedAsync(object? sender, RealAutopelagoClient.AutopelagoData data, CancellationToken cancellationToken)
                {
                    latestData = data;
                    return ValueTask.CompletedTask;
                }

                conn.IncomingPacket += OnIncomingPacketAsync;
                ValueTask OnIncomingPacketAsync(object? sender, ArchipelagoPacketModel packet, CancellationToken cancellationToken)
                {
                    if (packet is not PrintJSONPacketModel printJSON || latestData is null)
                    {
                        return ValueTask.CompletedTask;
                    }

                    StringBuilder sb = new();
                    sb.Append($"[{DateTime.Now:G}] -> ");
                    foreach (JSONMessagePartModel part in printJSON.Data)
                    {
                        sb.Append(part switch
                        {
                            PlayerIdJSONMessagePartModel playerId => latestData.SlotInfo[int.Parse(playerId.Text)].Name,
                            ItemIdJSONMessagePartModel itemId => latestData.GeneralItemNameMapping[long.Parse(itemId.Text)],
                            LocationIdJSONMessagePartModel locationId => latestData.GeneralLocationNameMapping[long.Parse(locationId.Text)],
                            _ => part.Text,
                        });
                    }

                    Console.WriteLine(sb);
                    return ValueTask.CompletedTask;
                }
            }

            GetDataPackagePacketModel getDataPackage = new();
            ConnectPacketModel connect = new()
            {
                Password = slot.Password,
                Game = _settings.GameName,
                Name = slot.Name,
                Uuid = Guid.NewGuid(),
                Version = new(new Version("0.4.4")),
                ItemsHandling = ArchipelagoItemsHandlingFlags.All,
                Tags = ["AP"],
                SlotData = true,
            };
            Game.State state =
                await client.InitAsync(getDataPackage, connect, Random.Shared, cancellationToken) ??
                throw new InvalidOperationException("Failed to connect");

            if (!_slotGameStates.InitGame(slot.Name, game))
            {
                throw new InvalidOperationException("Someone else is calling InitGame improperly.");
            }

            game.StateChanged += OnGameStateChangedAsync;
            ValueTask OnGameStateChangedAsync(object? sender, GameStateEventArgs args, CancellationToken cancellationToken)
            {
                return client.SaveGameStateAsync(args.CurrentState, cancellationToken);
            }

            CancellationTokenSource cts = game.RunGameLoop(state);
            _lifetime.ApplicationStopping.Register(cts.Cancel);
        });
    }
}
