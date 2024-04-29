using System.Runtime.CompilerServices;

using Avalonia;
using Avalonia.Controls;

namespace Autopelago;

public static class Helper
{
    public static GetOffSyncContextAwaitableAndAwaiter ConfigureAwaitFalse() => default;

    public static IEnumerable<PixelRect> Intersecting(this Screens screens, PixelRect bounds)
    {
        return screens.All.Select(s => s.Bounds).Where(b => b.Intersects(bounds));
    }

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
