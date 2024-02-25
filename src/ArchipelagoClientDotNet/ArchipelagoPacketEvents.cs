using System.Collections.Concurrent;

namespace ArchipelagoClientDotNet;

public sealed record ArchipelagoPacketEvents
{
    public Received_ Received { get; } = new();

    public sealed record Received_
    {
        private readonly ConcurrentDictionary<Type, object> _events = [];

        public event AsyncEventHandler<ArchipelagoPacketModel> AnyPacket { add => AddHandler(value); remove => RemoveHandler(value); }
        public event AsyncEventHandler<DataPackagePacketModel> DataPackage { add => AddHandler(value); remove => RemoveHandler(value); }
        public event AsyncEventHandler<ConnectResponsePacketModel> ConnectResponse { add => AddHandler(value); remove => RemoveHandler(value); }
        public event AsyncEventHandler<ConnectedPacketModel> Connected { add => AddHandler(value); remove => RemoveHandler(value); }
        public event AsyncEventHandler<ConnectionRefusedPacketModel> ConnectionRefused { add => AddHandler(value); remove => RemoveHandler(value); }

        public ValueTask NotifyAsync<T>(T payload, CancellationToken cancellationToken = default)
        {
            return _events.TryGetValue(typeof(T), out object? evt)
                ? ((AsyncEvent<T>)evt).InvokeAsync(this, payload, cancellationToken)
                : ValueTask.CompletedTask;
        }

        private void AddHandler<T>(AsyncEventHandler<T> handler)
        {
            ((AsyncEvent<T>)_events.GetOrAdd(typeof(T), _ => new AsyncEvent<T>())).Add(handler);
        }

        private bool RemoveHandler<T>(AsyncEventHandler<T> handler)
        {
            return _events.TryGetValue(typeof(T), out object? evt) && ((AsyncEvent<T>)evt).Remove(handler);
        }
    }
}
