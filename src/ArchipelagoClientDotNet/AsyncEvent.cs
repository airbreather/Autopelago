using System.Buffers;

namespace ArchipelagoClientDotNet;

public sealed class AsyncEvent<T>
{
    private readonly List<AsyncEventHandler<T>> _handlers = [];

    public void Add(AsyncEventHandler<T> handler)
    {
        lock (_handlers)
        {
            _handlers.Add(handler);
        }
    }

    public bool Remove(AsyncEventHandler<T> handler)
    {
        lock (_handlers)
        {
            return _handlers.Remove(handler);
        }
    }

    public ValueTask InvokeAsync(object? sender, T args, CancellationToken cancellationToken)
    {
        AsyncEventHandler<T>? singleHandler = null;
        AsyncEventHandler<T>[] handlers = [];
        int handlerCount;
        lock (_handlers)
        {
            switch (handlerCount = _handlers.Count)
            {
                case 0:
                    return ValueTask.CompletedTask;

                case 1:
                    // don't allocate or even rent for a single handler
                    // don't execute it inside the lock, either
                    singleHandler = _handlers[0];
                    break;

                default:
                    handlers = ArrayPool<AsyncEventHandler<T>>.Shared.Rent(handlerCount);
                    _handlers.CopyTo(handlers.AsSpan(..handlerCount));
                    break;
            }
        }

        return singleHandler is null
            ? Multi(handlers, sender, args, cancellationToken)
            : singleHandler(sender, args, cancellationToken);
        static async ValueTask Multi(AsyncEventHandler<T>[] handlers, object? sender, T args, CancellationToken cancellationToken)
        {
            foreach (AsyncEventHandler<T> handler in handlers)
            {
                await handler(sender, args, cancellationToken);
            }
        }
    }
}
