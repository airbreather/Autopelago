using ArchipelagoClientDotNet;

namespace Autopelago;

public sealed class UnrandomizedAutopelagoClient : IAutopelagoClient
{
    private readonly List<ItemDefinitionModel> _allReceivedItems = [];

    private readonly HashSet<LocationDefinitionModel> _allCheckedLocations = [];

    private readonly AsyncEvent<ReceivedItemsEventArgs> _receivedItemsEvent = new();

    public event AsyncEventHandler<ReceivedItemsEventArgs> ReceivedItems
    {
        add => _receivedItemsEvent.Add(value);
        remove => _receivedItemsEvent.Remove(value);
    }

    public async ValueTask SendLocationChecksAsync(IEnumerable<LocationDefinitionModel> locations, CancellationToken cancellationToken)
    {
        List<LocationDefinitionModel> newLocations = [];
        foreach (LocationDefinitionModel location in locations)
        {
            if (_allCheckedLocations.Add(location))
            {
                newLocations.Add(location);
            }
        }

        if (newLocations.Count == 0)
        {
            return;
        }

        await Helper.ConfigureAwaitFalse();
        ReceivedItemsEventArgs args = new()
        {
            Index = _allReceivedItems.Count,
            Items = [.. newLocations.Select(l => l.UnrandomizedItem)],
        };
        _allReceivedItems.AddRange(args.Items);
        await _receivedItemsEvent.InvokeAsync(this, args, cancellationToken);
    }
}
