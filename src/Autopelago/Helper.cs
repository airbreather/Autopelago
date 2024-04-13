using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Autopelago;

public static partial class Helper
{
    private static readonly Regex YamlTrueRegex = MakeYamlTrueRegex();

    private static readonly Regex YamlFalseRegex = MakeYamlFalseRegex();

    public static void Throw(this IEnumerable<Exception> exceptions) => throw new AggregateException(exceptions);
    public static void Throw(this IEnumerable<Exception> exceptions, string? message) => throw new AggregateException(message, exceptions);
    public static void Throw(this Exception[] exceptions) => throw new AggregateException(exceptions);
    public static void Throw(this Exception[] exceptions, string? message) => throw new AggregateException(message, exceptions);

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

    public static async ValueTask<TResult> NextAsync<TSource, TResult>(Action<AsyncEventHandler<TSource>> subscribe, Action<AsyncEventHandler<TSource>> unsubscribe, Func<TSource, bool> predicate, Func<TSource, TResult> selector, CancellationToken cancellationToken)
        where TResult: notnull
    {
        TaskCompletionSource<TResult> box = new(TaskCreationOptions.RunContinuationsAsynchronously);
        subscribe(OnEventAsync);
        await using CancellationTokenRegistration reg = cancellationToken.Register(() =>
        {
            unsubscribe(OnEventAsync);
            box.TrySetCanceled(cancellationToken);
        });
        return await box.Task.ConfigureAwait(false);

        ValueTask OnEventAsync(object? sender, TSource source, CancellationToken cancellationToken)
        {
            if (predicate(source))
            {
                unsubscribe(OnEventAsync);
                box.TrySetResult(selector(source));
            }

            return ValueTask.CompletedTask;
        }
    }

    public static bool ToBoolean(this YamlScalarNode node)
    {
        if (node.Value is string s)
        {
            if (YamlTrueRegex.IsMatch(s))
            {
                return true;
            }

            if (YamlFalseRegex.IsMatch(s))
            {
                return false;
            }
        }

        throw new YamlException($"'{node.Value}' is not a standard YAML boolean value.");
    }

    [GeneratedRegex("^(true|y|yes|on)$", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    private static partial Regex MakeYamlTrueRegex();

    [GeneratedRegex("^(false|n|no|off)$", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    private static partial Regex MakeYamlFalseRegex();

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
