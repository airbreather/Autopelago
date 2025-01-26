using System.Text.Json.Serialization;

namespace Autopelago;

[Flags]
[JsonConverter(typeof(JsonStringEnumConverter<BuffTokens>))]
public enum BuffTokens
{
    None = 0,

    [JsonStringEnumMemberName("well_fed")]
    WellFed = 1 << 0,

    [JsonStringEnumMemberName("lucky")]
    Lucky = 1 << 1,

    [JsonStringEnumMemberName("energized")]
    Energized = 1 << 2,

    [JsonStringEnumMemberName("stylish")]
    Stylish = 1 << 3,

    [JsonStringEnumMemberName("confident")]
    Confident = 1 << 4,

    [JsonStringEnumMemberName("smart")]
    Smart = 1 << 5,

    All = WellFed | Lucky | Energized | Stylish | Confident | Smart,
}

[Flags]
[JsonConverter(typeof(JsonStringEnumConverter<TrapTokens>))]
public enum TrapTokens
{
    None = 0,

    [JsonStringEnumMemberName("upset_tummy")]
    UpsetTummy = 1 << 0,

    [JsonStringEnumMemberName("unlucky")]
    Unlucky = 1 << 1,

    [JsonStringEnumMemberName("sluggish")]
    Sluggish = 1 << 2,

    [JsonStringEnumMemberName("distracted")]
    Distracted = 1 << 3,

    [JsonStringEnumMemberName("startled")]
    Startled = 1 << 4,

    [JsonStringEnumMemberName("conspiratorial")]
    Conspiratorial = 1 << 5,

    All = UpsetTummy | Unlucky | Sluggish | Distracted | Startled | Conspiratorial,
}
