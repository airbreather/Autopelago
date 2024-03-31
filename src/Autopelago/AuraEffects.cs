using System.Text.Json.Serialization;

namespace Autopelago;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "t")]
[JsonDerivedType(typeof(LuckyEffect), "lucky")]
[JsonDerivedType(typeof(UnluckyEffect), "unlucky")]
public abstract record AuraEffect
{
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
