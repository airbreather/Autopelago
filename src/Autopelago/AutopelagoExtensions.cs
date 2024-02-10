using YamlDotNet.Serialization;

public static class AutopelagoExtensions
{
    public static T Get<T>(this ObjectDeserializer @this)
    {
        return (T)@this(typeof(T))!;
    }
}
