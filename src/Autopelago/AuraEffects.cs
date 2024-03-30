using System.Text.Json.Serialization;

namespace Autopelago;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "t")]
[JsonDerivedType(typeof(LuckyEffect), "lucky")]
public abstract record AuraEffect
{
    public ulong? Deadline { get; init; }

    public static AuraEffect Parse(string name)
    {
        return name switch
        {
            "lucky" => new LuckyEffect(),
        };
    }
}

public sealed record LuckyEffect : AuraEffect
{
}
