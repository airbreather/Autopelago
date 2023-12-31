using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
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
[JsonDerivedType(typeof(ConnectedPacketModel), "Connected")]
[JsonDerivedType(typeof(ConnectionRefusedPacketModel), "ConnectionRefused")]
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
