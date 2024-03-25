namespace Autopelago;

public sealed class UnrandomizedAutopelagoClient : AutopelagoClient
{
    private readonly List<ItemDefinitionModel> _allReceivedItems = [];

    private readonly HashSet<LocationDefinitionModel> _allCheckedLocations = [];

    private readonly AsyncEvent<ReceivedItemsEventArgs> _receivedItemsEvent = new();

    public override event AsyncEventHandler<ReceivedItemsEventArgs> ReceivedItems
    {
        add { _receivedItemsEvent.Add(value); }
        remove { _receivedItemsEvent.Remove(value); }
    }

    public override ValueTask SendMessageAsync(string message, CancellationToken cancellationToken)
    {
        Console.WriteLine(message);
        return ValueTask.CompletedTask;
    }

    public override ValueTask IWonAsync(CancellationToken cancellationToken)
    {
        return ValueTask.CompletedTask;
    }

    public override async ValueTask SendLocationChecksAsync(IEnumerable<LocationDefinitionModel> locations, CancellationToken cancellationToken)
    {
        List<LocationDefinitionModel> newLocations = [];
        foreach (LocationDefinitionModel location in locations)
        {
            if (_allCheckedLocations.Add(location))
            {
                newLocations.Add(location);
            }
        }

        if (newLocations.Count > 0)
        {
            ReceivedItemsEventArgs args = new()
            {
                Index = _allReceivedItems.Count,
                Items = [.. newLocations.Select(l => l.UnrandomizedItem)],
            };
            _allReceivedItems.AddRange(args.Items);
            await Helper.ConfigureAwaitFalse();
            await _receivedItemsEvent.InvokeAsync(this, args, cancellationToken);
        }
    }
}
