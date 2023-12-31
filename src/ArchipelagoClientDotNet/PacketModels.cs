using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace ArchipelagoClientDotNet;

[Flags]
public enum ArchipelagoItemFlags
{
    None = 0b000,

    LogicalAdvancement = 0b001,
    ImportantNonAdvancement = 0b010,
    Trap = 0b100,

    All = 0b111,
}

[Flags]
public enum ArchipelagoItemsHandlingFlags
{
    None = 0b000,

    FromOtherWorlds = 0b001,

    // protocol doc says that these next two flags "Require[] 0b001 to be set"...
    _FromMyWorldOnly = 0b010,
    _StartingInventoryOnly = 0b100,

    FromMyWorld = _FromMyWorldOnly | FromOtherWorlds,
    StartingInventory = _StartingInventoryOnly | FromOtherWorlds,

    All = FromOtherWorlds | _FromMyWorldOnly | _StartingInventoryOnly,
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "cmd", IgnoreUnrecognizedTypeDiscriminators = true, UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FallBackToNearestAncestor)]
[JsonDerivedType(typeof(RoomInfoPacketModel), "RoomInfo")]
[JsonDerivedType(typeof(ConnectPacketModel), "Connect")]
[JsonDerivedType(typeof(ConnectedPacketModel), "Connected")]
[JsonDerivedType(typeof(ConnectionRefusedPacketModel), "ConnectionRefused")]
[JsonDerivedType(typeof(ReceivedItemsPacketModel), "ReceivedItems")]
[JsonDerivedType(typeof(PrintJSONPacketModel), "PrintJSON")]
[JsonDerivedType(typeof(SayPacketModel), "Say")]
public record ArchipelagoPacketModel
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement> ExtensionData { get; init; } = [];
}

public sealed record RoomInfoPacketModel : ArchipelagoPacketModel
{
    public required VersionModel Version { get; init; }

    public required VersionModel GeneratorVersion { get; init; }

    public required string[] Tags { get; init; }

    public required bool Password { get; init; }

    public required int HintCost { get; init; }

    public required int LocationCheckPoints { get; init; }

    public required string[] Games { get; init; }

    public required string SeedName { get; init; }

    public required double Time { get; init; }
}

public sealed record ConnectPacketModel : ArchipelagoPacketModel
{
    public string? Password { get; init; }

    public required string Game { get; init; }

    public required string Name { get; init; }

    public required Guid Uuid { get; init; }

    public required VersionModel Version { get; init; }

    public required ArchipelagoItemsHandlingFlags ItemsHandling { get; init; }

    public ImmutableArray<string> Tags { get; init; } = [];

    public required bool SlotData { get; init; }
}

public sealed record ConnectedPacketModel : ArchipelagoPacketModel
{
    public required int Team { get; init; }

    public required int Slot { get; init; }

    public required ImmutableArray<PlayerModel> Players { get; init; }

    public required ImmutableArray<long> MissingLocations { get; init; }

    public required ImmutableArray<long> CheckedLocations { get; init; }

    public Dictionary<string, JsonElement> SlotData { get; } = [];

    public Dictionary<int, SlotModel> SlotInfo { get; } = [];

    public required int HintPoints { get; init; }
}

public sealed record ConnectionRefusedPacketModel : ArchipelagoPacketModel
{
    public string[] Errors { get; init; } = [];
}

public sealed record ReceivedItemsPacketModel : ArchipelagoPacketModel
{
    public required int Index { get; init; }

    public required ImmutableArray<ItemModel> Items { get; init; }
}

public record PrintJSONPacketModel : ArchipelagoPacketModel
{
    private static readonly Dictionary<string, Type> s_recognizedTypes = new()
    {
        ["ItemSend"] = typeof(ItemSendPrintJSONPacketModel),
        ["ItemCheat"] = typeof(ItemCheatPrintJSONPacketModel),
        ["Hint"] = typeof(HintPrintJSONPacketModel),
        ["Join"] = typeof(JoinPrintJSONPacketModel),
        ["Part"] = typeof(PartPrintJSONPacketModel),
        ["Chat"] = typeof(ChatPrintJSONPacketModel),
        ["ServerChat"] = typeof(ServerChatPrintJSONPacketModel),
        ["Tutorial"] = typeof(TutorialPrintJSONPacketModel),
        ["TagsChanged"] = typeof(TagsChangedPrintJSONPacketModel),
        ["CommandResult"] = typeof(CommandResultPrintJSONPacketModel),
        ["AdminCommandResult"] = typeof(AdminCommandResultPrintJSONPacketModel),
        ["Goal"] = typeof(GoalPrintJSONPacketModel),
        ["Release"] = typeof(ReleasePrintJSONPacketModel),
        ["Collect"] = typeof(CollectPrintJSONPacketModel),
        ["Countdown"] = typeof(CountdownPrintJSONPacketModel),
    };

    public required string Type { get; init; } = "";

    public required ImmutableArray<JSONMessagePartModel> Data { get; init; }

    public PrintJSONPacketModel ToBestDerivedType(JsonSerializerOptions options)
    {
        if (!s_recognizedTypes.TryGetValue(Type, out Type? bestDerivedType))
        {
            return this;
        }

        JsonObject obj = new(
            ExtensionData.Select(kvp => KeyValuePair.Create(kvp.Key, JsonSerializer.SerializeToNode(kvp.Value, options)))
                .Prepend(KeyValuePair.Create("data", JsonSerializer.SerializeToNode(Data, options)))
                .Prepend(KeyValuePair.Create("type", JsonSerializer.SerializeToNode(Type, options)))
        );

        return (PrintJSONPacketModel)obj.Deserialize(bestDerivedType, options)!;
    }
}

public sealed record ItemSendPrintJSONPacketModel : PrintJSONPacketModel
{
    public required int Receiving { get; init; }
    public required ItemModel Item { get; init; }
}

public sealed record ItemCheatPrintJSONPacketModel : PrintJSONPacketModel
{
    public required int Receiving { get; init; }
    public required ItemModel Item { get; init; }
    public required int Team { get; init; }
}

public sealed record HintPrintJSONPacketModel : PrintJSONPacketModel
{
    public required int Receiving { get; init; }
    public required ItemModel Item { get; init; }
    public required bool Found { get; init; }
}

public sealed record JoinPrintJSONPacketModel : PrintJSONPacketModel
{
    public required int Team { get; init; }
    public required int Slot { get; init; }
    public required ImmutableArray<string> Tags { get; init; }
}

public sealed record PartPrintJSONPacketModel : PrintJSONPacketModel
{
    public required int Team { get; init; }
    public required int Slot { get; init; }
}

public sealed record ChatPrintJSONPacketModel : PrintJSONPacketModel
{
    public required int Team { get; init; }
    public required int Slot { get; init; }
    public required string Message { get; init; }
}

public sealed record ServerChatPrintJSONPacketModel : PrintJSONPacketModel
{
    public required string Message { get; init; }
}

public sealed record TutorialPrintJSONPacketModel : PrintJSONPacketModel { }

public sealed record TagsChangedPrintJSONPacketModel : PrintJSONPacketModel
{
    public required int Team { get; init; }
    public required int Slot { get; init; }
    public required ImmutableArray<string> Tags { get; init; }
}

public sealed record CommandResultPrintJSONPacketModel : PrintJSONPacketModel { }
public sealed record AdminCommandResultPrintJSONPacketModel : PrintJSONPacketModel { }

public sealed record GoalPrintJSONPacketModel : PrintJSONPacketModel
{
    public required int Team { get; init; }
    public required int Slot { get; init; }
}

public sealed record ReleasePrintJSONPacketModel : PrintJSONPacketModel
{
    public required int Team { get; init; }
    public required int Slot { get; init; }
}

public sealed record CollectPrintJSONPacketModel : PrintJSONPacketModel
{
    public required int Team { get; init; }
    public required int Slot { get; init; }
}

public sealed record CountdownPrintJSONPacketModel : PrintJSONPacketModel
{
    public required int Countdown { get; init; }
}

public sealed record SayPacketModel : ArchipelagoPacketModel
{
    public required string Text { get; init; }
}

public sealed record PlayerModel
{
    public string Class => "Player";

    public required int Team { get; init; }

    public required int Slot { get; init; }

    public required string Alias { get; init; }

    public required string Name { get; init; }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(SpectatorSlotModel), 0b00)]
[JsonDerivedType(typeof(PlayerSlotModel), 0b01)]
[JsonDerivedType(typeof(GroupSlotModel), 0b10)]
public abstract record SlotModel
{
    public string Class => "Slot";

    public required string Name { get; init; }

    public required string Game { get; init; }
}

public sealed record SpectatorSlotModel : SlotModel { }
public sealed record PlayerSlotModel : SlotModel { }
public sealed record GroupSlotModel : SlotModel
{
    public required FrozenSet<int> GroupMembers { get; init; }
}

public sealed record VersionModel
{
    public string Class => "Version";

    public VersionModel()
    {
    }

    [SetsRequiredMembers]
    public VersionModel(Version version)
    {
        Major = version.Major;
        Minor = version.Minor;
        Build = version.Build;
    }

    public required int Major { get; init; }

    public required int Minor { get; init; }

    public required int Build { get; init; }
}

public sealed record ItemModel
{
    public string Class => "Item";

    public required long Item { get; init; }

    public required long Location { get; init; }

    public required int Player { get; init; }

    public required ArchipelagoItemFlags Flags { get; init; }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(PlayerIdJSONMessagePartModel), "player_id")]
[JsonDerivedType(typeof(PlayerNameJSONMessagePartModel), "player_name")]
[JsonDerivedType(typeof(ItemIdJSONMessagePartModel), "item_id")]
[JsonDerivedType(typeof(ItemNameJSONMessagePartModel), "item_name")]
[JsonDerivedType(typeof(LocationIdJSONMessagePartModel), "location_id")]
[JsonDerivedType(typeof(LocationNameJSONMessagePartModel), "location_name")]
[JsonDerivedType(typeof(EntranceNameJSONMessagePartModel), "entrance_name")]
[JsonDerivedType(typeof(ColorJSONMessagePartModel), "color")]
public record JSONMessagePartModel
{
    public string Text { get; init; } = "";
}

public sealed record PlayerIdJSONMessagePartModel : JSONMessagePartModel { }
public sealed record PlayerNameJSONMessagePartModel : JSONMessagePartModel { }
public sealed record ItemIdJSONMessagePartModel : JSONMessagePartModel
{
    public required int Player { get; init; }
    public required ArchipelagoItemFlags Flags { get; init; }
}

public sealed record ItemNameJSONMessagePartModel : JSONMessagePartModel
{
    public required int Player { get; init; }
    public required ArchipelagoItemFlags Flags { get; init; }
}

public sealed record LocationIdJSONMessagePartModel : JSONMessagePartModel
{
    public required int Player { get; init; }
}

public sealed record LocationNameJSONMessagePartModel : JSONMessagePartModel
{
    public required int Player { get; init; }
}

public sealed record EntranceNameJSONMessagePartModel : JSONMessagePartModel { }

public sealed record ColorJSONMessagePartModel : JSONMessagePartModel
{
    public required string Color { get; init; } = "";
}
