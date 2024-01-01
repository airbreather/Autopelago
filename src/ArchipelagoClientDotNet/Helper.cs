using System.Runtime.CompilerServices;

namespace ArchipelagoClientDotNet;

public static class Helper
{
    public static void Throw(this IEnumerable<Exception> exceptions) => throw new AggregateException(exceptions);
    public static void Throw(this IEnumerable<Exception> exceptions, string? message) => throw new AggregateException(message, exceptions);
    public static void Throw(this Exception[] exceptions) => throw new AggregateException(exceptions);
    public static void Throw(this Exception[] exceptions, string? message) => throw new AggregateException(message, exceptions);

    public static GetOffSyncContextAwaitableAndAwaiter ConfigureAwaitFalse() => default;

    public readonly struct GetOffSyncContextAwaitableAndAwaiter : INotifyCompletion
    {
        public GetOffSyncContextAwaitableAndAwaiter GetAwaiter() => default;

        public bool IsCompleted => SynchronizationContext.Current is null;

        public void GetResult() { }

        public void OnCompleted(Action continuation)
        {
            SynchronizationContext? suppressed = SynchronizationContext.Current;
            try
            {
                SynchronizationContext.SetSynchronizationContext(null);
                continuation();
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(suppressed);
            }
        }
    }
}
