using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Reactive.Disposables;
using System.Reactive.Linq;

using Avalonia.ReactiveUI;

using DynamicData.Binding;

using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace Autopelago.ViewModels;

public sealed class GameStateViewModel : ViewModelBase, IDisposable
{
    private static readonly FrozenSet<string> s_hiddenProgressionItems = new[]
    {
        // these are the items marked as progression that aren't ever **individually** required.
        "rat_pack", "pack_rat", "computer_rat", "soc_rat_es",
    }.ToFrozenSet();

    private static readonly FrozenDictionary<string, int> s_progressionItemSortOrder = ProgressionItemSortOrder();

    private readonly CompositeDisposable _disposables = new();

    public GameStateViewModel()
    {
        _disposables.Add(Observable.Interval(TimeSpan.FromMilliseconds(500), AvaloniaScheduler.Instance)
            .Subscribe(_ =>
            {
                foreach (CheckableLocationViewModel loc in CheckableLocations)
                {
                    loc.NextFrame();
                }
            }));

        FrozenDictionary<string, CollectableItemViewModel> progressionItemsLookup = ProgressionItems.ToFrozenDictionary(i => i.ItemKey);
        FrozenDictionary<string, CheckableLocationViewModel> checkableLocationsLookup = CheckableLocations.ToFrozenDictionary(l => l.LocationKey);
        FrozenDictionary<string, ImmutableArray<GameRequirementToolTipViewModel>> toolTipsByItem = (
            from loc in CheckableLocations
            from tt in loc.GameRequirementToolTipSource
            from tt2 in tt.DescendantsAndSelf()
            where tt2.Model is ReceivedItemRequirement
            group tt2 by ((ReceivedItemRequirement)tt2.Model).ItemKey
        ).ToFrozenDictionary(grp => grp.Key, grp => grp.ToImmutableArray());

        ImmutableArray<(int RatCount, GameRequirementToolTipViewModel ToolTip)> ratCountToolTips =
        [
            .. from loc in CheckableLocations
               from tt in loc.GameRequirementToolTipSource
               from tt2 in tt.DescendantsAndSelf()
               where tt2.Model is RatCountRequirement
               select (((RatCountRequirement)tt2.Model).RatCount, tt2),
        ];

        _disposables.Add(this
            .WhenAnyValue(x => x.RatCount)
            .Subscribe(ratCount =>
            {
                foreach ((int ratCountThreshold, GameRequirementToolTipViewModel toolTip) in ratCountToolTips)
                {
                    toolTip.Satisfied = ratCount >= ratCountThreshold;
                }
            }));

        foreach (CollectableItemViewModel item in ProgressionItems)
        {
            if (!toolTipsByItem.TryGetValue(item.ItemKey, out ImmutableArray<GameRequirementToolTipViewModel> tooltips))
            {
                continue;
            }

            _disposables.Add(item
                .WhenAnyValue(x => x.Collected)
                .Subscribe(collected =>
                {
                    foreach (GameRequirementToolTipViewModel tooltip in tooltips)
                    {
                        tooltip.Satisfied = collected;
                    }
                }));
        }

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

    public ImmutableArray<CollectableItemViewModel> ProgressionItems { get; } =
    [
        .. GameDefinitions.Instance.ProgressionItems.Keys
            .Where(itemKey => !s_hiddenProgressionItems.Contains(itemKey))
            .OrderBy(itemKey => s_progressionItemSortOrder[itemKey])
            .Select(key => new CollectableItemViewModel(key)),
    ];

    public ImmutableArray<CheckableLocationViewModel> CheckableLocations { get; } =
    [
        .. GameDefinitions.Instance.LandmarkRegions.Keys
            .Select(key => new CheckableLocationViewModel(key)),
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

    private static FrozenDictionary<string, int> ProgressionItemSortOrder()
    {
        Dictionary<string, int> result = [];

        HashSet<RegionDefinitionModel> seenRegions = [];
        Queue<RegionDefinitionModel> regions = [];
        regions.Enqueue(GameDefinitions.Instance.StartRegion);
        while (regions.TryDequeue(out RegionDefinitionModel? region))
        {
            if (region is LandmarkRegionDefinitionModel)
            {
                region.Locations[0].Requirement.VisitItemKeys(itemKey => result.Add(itemKey, result.Count));
            }

            foreach (RegionExitDefinitionModel exit in region.Exits)
            {
                if (seenRegions.Add(exit.Region))
                {
                    regions.Enqueue(exit.Region);
                }
            }
        }

        return result.ToFrozenDictionary();
    }
}
