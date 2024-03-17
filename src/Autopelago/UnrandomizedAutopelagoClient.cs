using System.Diagnostics.CodeAnalysis;
using System.Reactive.Subjects;

namespace Autopelago;

[SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "Subject<T> does not need to be disposed.")]
public sealed class UnrandomizedAutopelagoClient : AutopelagoClient
{
    private readonly List<ItemDefinitionModel> _allReceivedItems = [];

    private readonly HashSet<LocationDefinitionModel> _allCheckedLocations = [];

    private readonly Subject<ReceivedItemsEventArgs> _receivedItemsEvents = new();

    public override IObservable<ReceivedItemsEventArgs> ReceivedItemsEvents => _receivedItemsEvents;

    public override ValueTask SendLocationChecksAsync(IEnumerable<LocationDefinitionModel> locations, CancellationToken cancellationToken)
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
            _receivedItemsEvents.OnNext(args);
        }

        return ValueTask.CompletedTask;
    }
}
