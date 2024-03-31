using System.Text.Json.Serialization;

namespace Autopelago;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "t")]
[JsonDerivedType(typeof(LuckyEffect), "lucky")]
[JsonDerivedType(typeof(UnluckyEffect), "unlucky")]
public abstract record AuraEffect
{
    public ulong? Deadline { get; init; }

    public static AuraEffect Parse(string name)
    {
        return name switch
        {
            "lucky" => LuckyEffect.Instance,
            "unlucky" => UnluckyEffect.Instance,
        };
    }
}

public sealed record LuckyEffect : AuraEffect
{
    public static readonly LuckyEffect Instance = new();

    private LuckyEffect() { }
}

public sealed record UnluckyEffect : AuraEffect
{
    public static readonly UnluckyEffect Instance = new();

    private UnluckyEffect() { }
}
