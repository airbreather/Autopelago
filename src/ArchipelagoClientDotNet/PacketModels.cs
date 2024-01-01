using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace ArchipelagoClientDotNet;

public enum ArchipelagoSlotType
{
    Spectator,
    Player,
    Group,
}

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
[JsonDerivedType(typeof(GetDataPackagePacketModel), "GetDataPackage")]
[JsonDerivedType(typeof(DataPackagePacketModel), "DataPackage")]
[JsonDerivedType(typeof(ConnectPacketModel), "Connect")]
[JsonDerivedType(typeof(ConnectedPacketModel), "Connected")]
[JsonDerivedType(typeof(ConnectionRefusedPacketModel), "ConnectionRefused")]
[JsonDerivedType(typeof(ReceivedItemsPacketModel), "ReceivedItems")]
[JsonDerivedType(typeof(PrintJSONPacketModel), "PrintJSON")]
[JsonDerivedType(typeof(SayPacketModel), "Say")]
[JsonDerivedType(typeof(LocationChecksPacketModel), "LocationChecks")]
[JsonDerivedType(typeof(RoomUpdatePacketModel), "RoomUpdate")]
public record ArchipelagoPacketModel
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement> ExtensionData { get; init; } = [];
}

public sealed record RoomInfoPacketModel : ArchipelagoPacketModel
{
    public required VersionModel Version { get; init; }

    public required VersionModel GeneratorVersion { get; init; }

    public required ImmutableArray<string> Tags { get; init; }

    public required bool Password { get; init; }

    public required int HintCost { get; init; }

    public required int LocationCheckPoints { get; init; }

    public required ImmutableArray<string> Games { get; init; }

    public Dictionary<string, string> DatapackageChecksums { get; init; } = [];

    public required string SeedName { get; init; }

    public required double Time { get; init; }
}

public sealed record GetDataPackagePacketModel : ArchipelagoPacketModel
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public ImmutableArray<string> Games { get; init; }
}

public sealed record DataPackagePacketModel : ArchipelagoPacketModel
{
    public required DataPackageModel Data { get; init; }
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

    public Dictionary<string, JsonElement> SlotData { get; init; } = [];

    public Dictionary<int, SlotModel> SlotInfo { get; init; } = [];

    public required int HintPoints { get; init; }
}

public sealed record ConnectionRefusedPacketModel : ArchipelagoPacketModel
{
    public ImmutableArray<string> Errors { get; init; } = [];
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

public sealed record LocationChecksPacketModel : ArchipelagoPacketModel
{
    public required ReadOnlyMemory<long> Locations { get; init; }
}

public sealed record RoomUpdatePacketModel : ArchipelagoPacketModel
{
    public ImmutableArray<PlayerModel>? Players { get; init; }

    public ImmutableArray<long>? CheckedLocations { get; init; }

    public Dictionary<string, JsonElement> SlotData { get; init; } = [];

    public Dictionary<int, SlotModel> SlotInfo { get; init; } = [];

    public int? HintPoints { get; init; }
}

public sealed record PlayerModel
{
    public string Class => "Player";

    public required int Team { get; init; }

    public required int Slot { get; init; }

    public required string Alias { get; init; }

    public required string Name { get; init; }
}

public sealed record SlotModel
{
    public string Class => "Slot";

    public required string Name { get; init; }

    public required string Game { get; init; }

    public required ArchipelagoSlotType Type { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public ImmutableArray<int>? GroupMembers { get; init; }
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

public sealed record DataPackageModel
{
    public string Class => "DataPackage";

    public Dictionary<string, GameDataModel> Games { get; init; } = [];
}

public sealed record GameDataModel
{
    public string Class => "GameData";

    public Dictionary<string, long> ItemNameToId { get; init; } = [];

    public Dictionary<string, long> LocationNameToId { get; init; } = [];

    public required string Checksum { get; init; }
}

public sealed record ItemModel
{
    public string Class => "Item";

    public required long Item { get; init; }

    public required long Location { get; init; }

    public required int Player { get; init; }

    public required ArchipelagoItemFlags Flags { get; init; }
}

[JsonConverter(typeof(JSONMessagePartModelConverter))]
public record JSONMessagePartModel
{
    public required string Text { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string? Type { get; init; } = null;
}

public sealed record PlayerIdJSONMessagePartModel : JSONMessagePartModel
{
    public PlayerIdJSONMessagePartModel() { Type = "player_id"; }
}

public sealed record PlayerNameJSONMessagePartModel : JSONMessagePartModel
{
    public PlayerNameJSONMessagePartModel() { Type = "player_name"; }
}

public sealed record ItemIdJSONMessagePartModel : JSONMessagePartModel
{
    public ItemIdJSONMessagePartModel() { Type = "item_id"; }

    public required int Player { get; init; }
    public required ArchipelagoItemFlags Flags { get; init; }
}

public sealed record ItemNameJSONMessagePartModel : JSONMessagePartModel
{
    public ItemNameJSONMessagePartModel() { Type = "item_name"; }

    public required int Player { get; init; }
    public required ArchipelagoItemFlags Flags { get; init; }
}

public sealed record LocationIdJSONMessagePartModel : JSONMessagePartModel
{
    public LocationIdJSONMessagePartModel() { Type = "location_id"; }

    public required int Player { get; init; }
}

public sealed record LocationNameJSONMessagePartModel : JSONMessagePartModel
{
    public LocationNameJSONMessagePartModel() { Type = "location_name"; }

    public required int Player { get; init; }
}

public sealed record EntranceNameJSONMessagePartModel : JSONMessagePartModel
{
    public EntranceNameJSONMessagePartModel() { Type = "entrance_name"; }
}

public sealed record ColorJSONMessagePartModel : JSONMessagePartModel
{
    public ColorJSONMessagePartModel() { Type = "color"; }

    public required string Color { get; init; } = "";
}

internal sealed class JSONMessagePartModelConverter : JsonConverter<JSONMessagePartModel>
{
    public override JSONMessagePartModel? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        JsonElement element = JsonSerializer.Deserialize<JsonElement>(ref reader, options);
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!(element.TryGetProperty("text"u8, out JsonElement textElement) && textElement.GetString() is string text))
        {
            return null;
        }

        if (!element.TryGetProperty("type"u8, out JsonElement typeElement))
        {
            return new() { Text = text };
        }

        return typeElement.GetString() switch
        {
            null => new() { Text = text },
            "player_id" => new PlayerIdJSONMessagePartModel { Text = text },
            "player_name" => new PlayerNameJSONMessagePartModel { Text = text },
            "item_id" => new ItemIdJSONMessagePartModel { Text = text, Player = element.GetProperty("player"u8).GetInt32(), Flags = element.GetProperty("flags"u8).Deserialize<ArchipelagoItemFlags>(options) },
            "item_name" => new ItemNameJSONMessagePartModel { Text = text, Player = element.GetProperty("player"u8).GetInt32(), Flags = element.GetProperty("flags"u8).Deserialize<ArchipelagoItemFlags>(options) },
            "location_id" => new LocationIdJSONMessagePartModel { Text = text, Player = element.GetProperty("player"u8).GetInt32() },
            "location_name" => new LocationNameJSONMessagePartModel { Text = text, Player = element.GetProperty("player"u8).GetInt32() },
            "entrance_name" => new EntranceNameJSONMessagePartModel { Text = text },
            "color" => new ColorJSONMessagePartModel { Text = text, Color = element.GetProperty("color"u8).GetString()! },
            string type => new() { Text = text, Type = type },
        };
    }

    public override void Write(Utf8JsonWriter writer, JSONMessagePartModel value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("text"u8, value.Text);
        if (value.Type is string type)
        {
            writer.WriteString("type"u8, type);
        }

        switch (value)
        {
            case ItemIdJSONMessagePartModel itemId:
                writer.WriteNumber("player"u8, itemId.Player);
                writer.WriteNumber("flags"u8, (int)itemId.Flags);
                break;

            case ItemNameJSONMessagePartModel itemName:
                writer.WriteNumber("player"u8, itemName.Player);
                writer.WriteNumber("flags"u8, (int)itemName.Flags);
                break;

            case LocationIdJSONMessagePartModel locationId:
                writer.WriteNumber("player"u8, locationId.Player);
                break;

            case LocationNameJSONMessagePartModel locationName:
                writer.WriteNumber("player"u8, locationName.Player);
                break;

            case ColorJSONMessagePartModel color:
                writer.WriteString("color"u8, color.Color);
                break;
        }

        writer.WriteEndObject();
    }
}
