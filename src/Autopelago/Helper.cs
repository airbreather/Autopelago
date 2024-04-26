using System.Runtime.CompilerServices;

namespace Autopelago;

public static class Helper
{
    public static void Throw(this IEnumerable<Exception> exceptions) => throw new AggregateException(exceptions);
    public static void Throw(this IEnumerable<Exception> exceptions, string? message) => throw new AggregateException(message, exceptions);
    public static void Throw(this Exception[] exceptions) => throw new AggregateException(exceptions);
    public static void Throw(this Exception[] exceptions, string? message) => throw new AggregateException(message, exceptions);

    public static bool DisposeAsExceptionFilter<T>(this T @this)
        where T : IDisposable
    {
        @this.Dispose();
        return false;
    }

    public static string FormatMyWay(this TimeSpan @this)
    {
        if (@this.TotalDays >= 1)
        {
            return $"{@this:d\\:hh\\:mm\\:ss}";
        }

        if (@this.TotalHours >= 1)
        {
            return $"{@this:h\\:mm\\:ss}";
        }

        if (@this.TotalSeconds >= 2)
        {
            return $"{@this:m\\:ss}";
        }

        return $"{@this:s\\.FFF\\s}";
    }

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
