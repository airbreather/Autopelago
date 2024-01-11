using System.Runtime.CompilerServices;

namespace ArchipelagoClientDotNet;

public static class Helper
{
    public static void Throw(this IEnumerable<Exception> exceptions) => throw new AggregateException(exceptions);
    public static void Throw(this IEnumerable<Exception> exceptions, string? message) => throw new AggregateException(message, exceptions);
    public static void Throw(this Exception[] exceptions) => throw new AggregateException(exceptions);
    public static void Throw(this Exception[] exceptions, string? message) => throw new AggregateException(message, exceptions);

    public static ConfiguredTaskAwaitable ConfigureAwaitFalse() => Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
}
