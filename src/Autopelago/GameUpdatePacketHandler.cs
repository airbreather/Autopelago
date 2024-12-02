using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

using Serilog;
using Serilog.Context;

namespace Autopelago;

using static Constants;

public sealed class GameUpdatePacketHandler : ArchipelagoPacketHandler, IDisposable
{
    private static readonly ImmutableArray<string> s_newTargetPhrases =
    [
        "Oh, hey, what's that thing over there at '{LOCATION}'?",
        "There's something at '{LOCATION}', I'm sure of it!",
        "Something at '{LOCATION}' smells good!",
        "There's a rumor that something's going on at '{LOCATION}'!",
    ];

    private readonly CompositeDisposable _disposables = [];

    private readonly Settings _settings;

    private readonly BehaviorSubject<Game> _gameUpdates;

    private readonly BehaviorSubject<MultiworldInfo> _contextUpdates;

    public GameUpdatePacketHandler(Settings settings, Game game, MultiworldInfo initialContext)
    {
        _settings = settings;
        _disposables.Add(_gameUpdates = new(game));
        GameUpdates = _gameUpdates.AsObservable();
        _disposables.Add(_contextUpdates = new(initialContext));
        ContextUpdates = _contextUpdates.AsObservable();
    }

    public void Dispose()
    {
        _disposables.Dispose();
    }

    public IObservable<Game> GameUpdates { get; }

    public IObservable<MultiworldInfo> ContextUpdates { get; }

    public override async ValueTask HandleAsync(ArchipelagoPacketModel nextPacket, ArchipelagoPacketProvider sender, CancellationToken cancellationToken)
    {
        switch (nextPacket)
        {
            case RoomUpdatePacketModel roomUpdate:
                Handle(roomUpdate);
                break;

            case ReceivedItemsPacketModel receivedItems:
                await HandleAsync(receivedItems, sender);
                break;

            case PrintJSONPacketModel printJSON:
                await HandleAsync(printJSON, sender);
                break;
        }
    }

    private void Handle(RoomUpdatePacketModel roomUpdate)
    {
        if (roomUpdate.SlotInfo is { } slotInfo)
        {
            _contextUpdates.OnNext(_contextUpdates.Value with { SlotInfo = slotInfo.ToFrozenDictionary() });
        }

        if (roomUpdate.Players is { } players)
        {
            FrozenDictionary<string, int> slotByPlayerAlias = players.ToFrozenDictionary(p => p.Alias, p => p.Slot);
            _contextUpdates.OnNext(_contextUpdates.Value with { SlotByPlayerAlias = slotByPlayerAlias });
        }

        if (roomUpdate.CheckedLocations is { } checkedLocations)
        {
            Game game = _gameUpdates.Value;
            game.CheckLocations(ImmutableArray.CreateRange(checkedLocations, (location, locationsReverseMapping) => locationsReverseMapping[location], _contextUpdates.Value.LocationsById).AsSpan());
            _gameUpdates.OnNext(game);
        }
    }

    private async ValueTask HandleAsync(ReceivedItemsPacketModel receivedItems, ArchipelagoPacketProvider sender)
    {
        Game game = _gameUpdates.Value;
        ImmutableArray<ItemDefinitionModel> convertedItems = ImmutableArray.CreateRange(receivedItems.Items, (item, itemsReverseMapping) => itemsReverseMapping[item.Item], _contextUpdates.Value.ItemsById);
        for (int i = receivedItems.Index; i < game.ReceivedItems.Count; i++)
        {
            if (convertedItems[i - receivedItems.Index] != game.ReceivedItems[i])
            {
                throw new InvalidOperationException("Need to resync. Try connecting again.");
            }
        }

        ImmutableArray<ItemDefinitionModel> newItems = convertedItems[(game.ReceivedItems.Count - receivedItems.Index)..];
        int priorityPriorityLocationCountBefore = game.PriorityPriorityLocations.Count;
        game.ReceiveItems(newItems.AsSpan());
        _gameUpdates.OnNext(game);

        List<ArchipelagoPacketModel> newPackets = [];
        if (priorityPriorityLocationCountBefore != game.PriorityPriorityLocations.Count)
        {
            foreach (LocationDefinitionModel newPriorityLocation in game.PriorityPriorityLocations.Skip(priorityPriorityLocationCountBefore))
            {
                newPackets.Add(new SayPacketModel { Text = s_newTargetPhrases[Random.Shared.Next(s_newTargetPhrases.Length)].Replace("{LOCATION}", newPriorityLocation.Name), });
            }
        }

        newPackets.Add(new SetPacketModel
        {
            Key = AurasKey,
            Operations =
            [
                new()
                {
                    Operation = ArchipelagoDataStorageOperationType.Replace,
                    Value = JsonSerializer.SerializeToNode(
                        game.AuraData,
                        AuraDataSerializationContext.Default.AuraData
                    )!,
                },
            ],
        });

        await sender.SendPacketsAsync([.. newPackets]);
    }

    private async ValueTask HandleAsync(PrintJSONPacketModel printJSON, ArchipelagoPacketProvider sender)
    {
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
                        ctxStack.Push(LogContext.PushProperty(playerPlaceholder, _contextUpdates.Value.SlotInfo[int.Parse(playerId.Text)].Name));
                        messageTemplateBuilder.Append($"{{{playerPlaceholder}}}");
                        break;

                    case ItemIdJSONMessagePartModel itemId:
                        string gameForItem = _contextUpdates.Value.SlotInfo[itemId.Player].Game;
                        string itemPlaceholder = $"Item{nextItemPlaceholder++}";
                        ctxStack.Push(LogContext.PushProperty(itemPlaceholder, _contextUpdates.Value.GeneralItemNameMapping[gameForItem][long.Parse(itemId.Text)]));
                        messageTemplateBuilder.Append($"{{{itemPlaceholder}}}");
                        break;

                    case LocationIdJSONMessagePartModel locationId:
                        string gameForLocation = _contextUpdates.Value.SlotInfo[locationId.Player].Game;
                        string locationPlaceholder = $"Location{nextLocationPlaceholder++}";
                        ctxStack.Push(LogContext.PushProperty(locationPlaceholder, _contextUpdates.Value.GeneralLocationNameMapping[gameForLocation][long.Parse(locationId.Text)]));
                        messageTemplateBuilder.Append($"{{{locationPlaceholder}}}");
                        break;

                    default:
                        messageTemplateBuilder.Append(part.Text);
                        break;
                }
            }

            string message = $"{messageTemplateBuilder}";
            Log.Information(message);
            int tagIndex = message.IndexOf($"@{_settings.Slot}", StringComparison.InvariantCultureIgnoreCase);
            if (tagIndex >= 0)
            {
                await ProcessChatCommand(message, tagIndex, sender);
            }
        }
        finally
        {
            while (ctxStack.TryPop(out IDisposable? ctx))
            {
                ctx.Dispose();
            }
        }
    }

    private async ValueTask ProcessChatCommand(string cmd, int tagIndex, ArchipelagoPacketProvider sender)
    {
        // chat message format is "{UserAlias}: {Message}", so it needs to be at least this long.
        if (tagIndex <= ": ".Length)
        {
            Log.Error("Unexpected message format, aborting: {Command}", cmd);
            return;
        }

        string probablyPlayerAlias = cmd[..(tagIndex - ": ".Length)];
        if (!_contextUpdates.Value.SlotByPlayerAlias.ContainsKey(probablyPlayerAlias))
        {
            // this isn't necessarily an error or a mistaken assumption. it could just be that the
            // "@{SlotName}" happened partway through their message. don't test every single user's
            // alias against every single chat message that contains "@{SlotName}", just require it
            // to be at the start of the message. done.
            return;
        }

        // if we got here, then the entire rest of the message after "@{SlotName}" is the command.
        cmd = Regex.Replace(cmd[tagIndex..], $"^@{_settings.Slot} ", "", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
        if (cmd.StartsWith("go ", StringComparison.OrdinalIgnoreCase))
        {
            string loc = cmd["go ".Length..].Trim('"');
            if (GameDefinitions.Instance.LocationsByNameCaseInsensitive.TryGetValue(loc, out LocationDefinitionModel? toPrioritize))
            {
                string message = _gameUpdates.Value.AddPriorityLocation(toPrioritize) switch
                {
                    AddPriorityLocationResult.AlreadyPrioritized => $"Hey, {probablyPlayerAlias}, just so you know, I already had '{toPrioritize.Name}' on my radar. I'll get there when I can, no worries!",
                    AddPriorityLocationResult.AddedUnreachable => $"I'll keep it in mind that '{toPrioritize.Name}' is important to you, '{probablyPlayerAlias}'. I can't get there just yet, though, so please be patient with me...",
                    _ => $"All right, I'll get right over to '{toPrioritize.Name}', {probablyPlayerAlias}!",
                };
                SayPacketModel say = new() { Text = message };
                await sender.SendPacketsAsync([say]);
            }
            else
            {
                SayPacketModel say = new()
                {
                    Text = $"Um... excuse me, but... I don't know what a '{loc}' is...",
                };
                await sender.SendPacketsAsync([say]);
            }
        }
        else if (cmd.StartsWith("stop ", StringComparison.OrdinalIgnoreCase))
        {
            string loc = cmd["stop ".Length..].Trim('"');
            LocationDefinitionModel? removed = _gameUpdates.Value.RemovePriorityLocation(loc);
            SayPacketModel say = new()
            {
                Text = removed is null
                    ? $"Um... excuse me, but... I don't see a '{loc}' to remove..."
                    : $"Oh, OK. I'll stop trying to get to '{removed.Name}', {probablyPlayerAlias}.",
            };
            await sender.SendPacketsAsync([say]);
        }
        else if (cmd.StartsWith("help", StringComparison.OrdinalIgnoreCase))
        {
            ImmutableArray<SayPacketModel> packets =
            [
                new() { Text = "Commands you can use are:" },
                new() { Text = $"1. @{_settings.Slot} go LOCATION_NAME" },
                new() { Text = $"2. @{_settings.Slot} stop LOCATION_NAME" },
                new() { Text = "LOCATION_NAME refers to whatever text you got in your hint, like \"Basketball\" or \"Before Prawn Stars #12\"." },
            ];
            await sender.SendPacketsAsync(packets.CastArray<ArchipelagoPacketModel>());
        }
        else
        {
            SayPacketModel say = new()
            {
                Text = $"Say \"@{_settings.Slot} help\" (without the quotes) for a list of commands.",
            };
            await sender.SendPacketsAsync([say]);
        }
    }
}
