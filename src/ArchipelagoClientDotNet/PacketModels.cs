using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ArchipelagoClientDotNet;

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
public record ArchipelagoPacketModel
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement> ExtensionData { get; init; } = [];
}

public sealed record RoomInfoPacketModel : ArchipelagoPacketModel
{
    public string Cmd => "RoomInfo";

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
    public string Cmd => "Connect";

    public string? Password { get; init; }

    public required string Game { get; init; }

    public required string Name { get; init; }

    public required Guid Uuid { get; init; }

    public required VersionModel Version { get; init; }

    public required ArchipelagoItemsHandlingFlags ItemsHandling { get; init; }

    public Collection<string> Tags { get; } = [];

    public required bool SlotData { get; init; }
}

public sealed record VersionModel
{
    public string Class => "Version";

    public required int Major { get; init; }

    public required int Minor { get; init; }

    public required int Build { get; init; }
}
