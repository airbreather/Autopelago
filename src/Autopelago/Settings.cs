﻿using System.Text.Json.Serialization;

namespace Autopelago;

public sealed record Settings
{
    public static readonly Settings Default = new()
    {
        Host = "archipelago.gg",
        Port = 65535,
        Slot = "",
        Password = "",
        MinStepSeconds = 30,
        MaxStepSeconds = 60,
    };

    public static readonly Settings ForDesigner = new()
    {
        Host = "UI DESIGNER",
        Port = 65535,
        Slot = "Ratthew",
        Password = "",
        MinStepSeconds = 1,
        MaxStepSeconds = 1,
    };

    public required string Host { get; init; }

    public required ushort Port { get; init; }

    public required string Slot { get; init; }

    [JsonIgnore]
    public string Password { get; init; } = "";

    public required decimal MinStepSeconds { get; init; }

    public required decimal MaxStepSeconds { get; init; }
}