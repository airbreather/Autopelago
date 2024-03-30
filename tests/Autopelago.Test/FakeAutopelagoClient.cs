using System.Collections.Immutable;

namespace Autopelago;

public sealed class FakeAutopelagoClient : AutopelagoClient
{
    private readonly AsyncEvent<ReceivedItemsEventArgs> _receivedItemsEvent = new();

    public override event AsyncEventHandler<ReceivedItemsEventArgs> ReceivedItems
    {
        add { _receivedItemsEvent.Add(value); }
        remove { _receivedItemsEvent.Remove(value); }
    }

    public List<string> MessagesSent { get; } = [];

    public bool SentIWon { get; private set; }

    public List<ImmutableArray<LocationDefinitionModel>> LocationChecksSent { get; } = [];

    public override ValueTask SendMessageAsync(string message, CancellationToken cancellationToken)
    {
        MessagesSent.Add(message);
        return ValueTask.CompletedTask;
    }

    public override ValueTask IWonAsync(CancellationToken cancellationToken)
    {
        SentIWon = true;
        return ValueTask.CompletedTask;
    }

    public override ValueTask SendLocationChecksAsync(IEnumerable<LocationDefinitionModel> locations, CancellationToken cancellationToken)
    {
        LocationChecksSent.Add([.. locations]);
        return ValueTask.CompletedTask;
    }

    public ValueTask TriggerReceivedItemsEvent(ReceivedItemsEventArgs args)
    {
        return _receivedItemsEvent.InvokeAsync(this, args, CancellationToken.None);
    }
}
