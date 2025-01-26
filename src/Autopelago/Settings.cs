using System.Text.Json.Serialization;

using Avalonia.Media;

namespace Autopelago;

public sealed record Settings
{
    public static readonly Settings Default = new()
    {
        Host = "archipelago.gg",
        Port = 38281,
        Slot = "",
        Password = "",
        MinStepSeconds = 20,
        MaxStepSeconds = 30,
        RatChat = true,
        TileAnimations = true,
        PlayerAnimations = true,
    };

    public static readonly Settings ForDesigner = new()
    {
        Host = "UI DESIGNER",
        Port = 38281,
        Slot = "Ratthew",
        Password = "",
        MinStepSeconds = 1,
        MaxStepSeconds = 1,
        RatChat = true,
        TileAnimations = true,
        PlayerAnimations = true,
    };

    public required string Host { get; init; }

    public required ushort Port { get; init; }

    public required string Slot { get; init; }

    [JsonIgnore]
    public string Password { get; init; } = "";

    public required decimal MinStepSeconds { get; init; }

    public required decimal MaxStepSeconds { get; init; }

    public required bool RatChat { get; init; } = true;

    public bool RatChatForTargetChanges { get; set; } = true;

    public bool RatChatForFirstBlocked { get; set; } = true;

    public bool RatChatForStillBlocked { get; set; } = true;

    public bool RatChatForUnblocked { get; set; } = true;

    public bool RatChatForOneTimeEvents { get; set; } = true;

    public bool TileAnimations { get; set; } = true;

    public bool PlayerAnimations { get; set; } = true;

    public PlayerTokenKind PlayerToken { get; init; }

    [JsonIgnore]
    public Color PlayerTokenColor { get; set; } = Color.Parse("#382E26");

    public uint PlayerTokenColorNum
    {
        get => PlayerTokenColor.ToUInt32();

        // set, not init, to work around some variation of:
        // https://github.com/dotnet/runtime/issues/84484
        set => PlayerTokenColor = Color.FromUInt32(value);
    }
}
