using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Reactive.Disposables;
using System.Reactive.Linq;

using Avalonia.Controls;

using DynamicData.Binding;

using ReactiveUI.Fody.Helpers;

namespace Autopelago.ViewModels;

public sealed class GameStateViewModel : ViewModelBase, IDisposable
{
    private readonly CompositeDisposable _disposables = new();

    public GameStateViewModel()
    {
        if (!Design.IsDesignMode)
        {
            return;
        }

        FrozenDictionary<string, CollectableItemViewModel> progressionItemsLookup = ProgressionItems.ToFrozenDictionary(i => i.ItemKey);
        FrozenDictionary<string, CheckableLocationViewModel> checkableLocationsLookup = CheckableLocations.ToFrozenDictionary(l => l.LocationKey);

        _disposables.Add(ProgressionItemsCollected.ObserveCollectionChanges()
            .Select(c => c.EventArgs)
            .Where(args => args.Action == NotifyCollectionChangedAction.Add)
            .SelectMany(args => args.NewItems!.Cast<string>()
                .Where(progressionItemsLookup.ContainsKey)
                .Select(added => progressionItemsLookup[added]))
            .Subscribe(item => item.Collected = true));

        _disposables.Add(LocationsAvailable.ObserveCollectionChanges()
            .Select(c => c.EventArgs)
            .Where(args => args.Action == NotifyCollectionChangedAction.Add)
            .SelectMany(args => args.NewItems!.Cast<string>()
                .Where(checkableLocationsLookup.ContainsKey)
                .Select(added => checkableLocationsLookup[added]))
            .Subscribe(location => location.Available = true));

        _disposables.Add(LocationsChecked.ObserveCollectionChanges()
            .Select(c => c.EventArgs)
            .Where(args => args.Action == NotifyCollectionChangedAction.Add)
            .SelectMany(args => args.NewItems!.Cast<string>()
                .Where(checkableLocationsLookup.ContainsKey)
                .Select(added => checkableLocationsLookup[added]))
            .Subscribe(location => location.Checked = true));
    }

    [Reactive]
    public string SlotName { get; set; } = "";

    [Reactive]
    public int RatCount { get; set; }

    [Reactive]
    public int FoodFactor { get; set; }

    [Reactive]
    public int LuckFactor { get; set; }

    [Reactive]
    public int EnergyFactor { get; set; }

    [Reactive]
    public int StyleFactor { get; set; }

    [Reactive]
    public int DistractionCounter { get; set; }

    [Reactive]
    public bool HasConfidence { get; set; }

    public ImmutableArray<CollectableItemViewModel> ProgressionItems { get; } = [
        .. new[]
        {
            "red_matador_cape", "premium_can_of_prawn_food",
            "a_cookie", "bribe",
            "masterful_longsword",
        }.Select(key => new CollectableItemViewModel(key)),
    ];

    public ImmutableArray<CheckableLocationViewModel> CheckableLocations { get; } = [
        .. new[]
        {
            "basketball",
            "prawn_stars", "minotaur",
            "pirate_bake_sale", "restaurant",
            "bowling_ball_door",
            "captured_goldfish",
        }.Select(key => new CheckableLocationViewModel(key)),
    ];

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Content)]
    public ObservableCollectionExtended<string> ProgressionItemsCollected { get; } = [];

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Content)]
    public ObservableCollectionExtended<string> LocationsChecked { get; } = [];

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Content)]
    public ObservableCollectionExtended<string> LocationsAvailable { get; } = [];

    public void Dispose()
    {
        _disposables.Dispose();
    }
}
