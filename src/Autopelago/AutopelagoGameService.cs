using System.Text;

using Serilog.Context;

namespace Autopelago;

public sealed class AutopelagoGameService
{
    private readonly AutopelagoSettingsModel _settings;

    private readonly SlotGameLookup _slotGameStates;

    private readonly TimeProvider _timeProvider;

    private readonly Serilog.ILogger _logger;

    public AutopelagoGameService(AutopelagoSettingsModel settings, SlotGameLookup slotGameStates, TimeProvider timeProvider, Serilog.ILogger logger)
    {
        _settings = settings;
        _slotGameStates = slotGameStates;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    private async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Helper.ConfigureAwaitFalse();

        AsyncEvent<object?> keepAliveEvent = new();
        SyncOverAsync.FireAndForget(async () =>
        {
            await Helper.ConfigureAwaitFalse();
            TimeSpan keepAliveInterval = TimeSpan.FromMinutes(2);
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(keepAliveInterval, _timeProvider, stoppingToken);
                await keepAliveEvent.InvokeAsync(null, null, stoppingToken);
            }
        });

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

                    StringBuilder messageTemplateBuilder = new();
                    Stack<IDisposable> ctxStack = [];
                    try
                    {
                        int nextPlayerPlaceholder = 0;
                        int nextItemPlaceholder = 0;
                        int nextLocationPlaceholder = 0;
                        foreach (JSONMessagePartModel part in printJSON.Data)
                        {
                            switch (part)
                            {
                                case PlayerIdJSONMessagePartModel playerId:
                                    string playerPlaceholder = $"Player{nextPlayerPlaceholder++}";
                                    ctxStack.Push(LogContext.PushProperty(playerPlaceholder, latestData.SlotInfo[int.Parse(playerId.Text)].Name));
                                    messageTemplateBuilder.Append($"{{{playerPlaceholder}}}");
                                    break;

                                case ItemIdJSONMessagePartModel itemId:
                                    string itemPlaceholder = $"Item{nextItemPlaceholder++}";
                                    ctxStack.Push(LogContext.PushProperty(itemPlaceholder, latestData.GeneralItemNameMapping[long.Parse(itemId.Text)]));
                                    messageTemplateBuilder.Append($"{{{itemPlaceholder}}}");
                                    break;

                                case LocationIdJSONMessagePartModel locationId:
                                    string locationPlaceholder = $"Location{nextLocationPlaceholder++}";
                                    ctxStack.Push(LogContext.PushProperty(locationPlaceholder, latestData.GeneralLocationNameMapping[long.Parse(locationId.Text)]));
                                    messageTemplateBuilder.Append($"{{{locationPlaceholder}}}");
                                    break;

                                default:
                                    messageTemplateBuilder.Append(part.Text);
                                    break;
                            }
                        }

                        _logger.Information($"{messageTemplateBuilder}");
                    }
                    finally
                    {
                        while (ctxStack.TryPop(out IDisposable? ctx))
                        {
                            ctx.Dispose();
                        }
                    }

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
                Version = new(new("0.4.4")),
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

            keepAliveEvent.Add(async (_, _, cancellationToken) =>
            {
                await Helper.ConfigureAwaitFalse();
                StatusUpdatePacketModel statusUpdate = new() { Status = ArchipelagoClientStatus.Playing };
                await conn.SendPacketsAsync([statusUpdate], cancellationToken);
            });

            state = await game.AdvanceFirstAsync(state, cancellationToken);
            while (!state.IsCompleted)
            {
                state = await game.AdvanceOnceAsync(cancellationToken);
            }

            await client.IWonAsync(cancellationToken);
        });
    }
}
