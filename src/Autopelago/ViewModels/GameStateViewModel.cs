using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;

using Avalonia;
using Avalonia.ReactiveUI;

using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace Autopelago.ViewModels;

public sealed class GameStateViewModel : ViewModelBase, IDisposable
{
    private static readonly FrozenSet<string> s_hiddenProgressionItems = new[]
    {
        // these are the items marked as progression that aren't ever **individually** required.
        "pack_rat", "rat_pack",

        // this one is fixed and doesn't really get sent by others.
        "blackbird",
    }.ToFrozenSet();

    private static readonly ImmutableArray<string> s_ratThoughts =
    [
        "the moon looks cheesy today",
        "i could really go for some cheddar",
        "squeak squeak",
        "wait, is there even air in space?",
        "i sure hope the moon hasn't gotten moldy",
        "wait... you can read my mind?",
        "did you know, real rats don't even like cheese all that much!",
        "don't you DARE call me a mouse!",
        "rat rat rat rat rat rat rat rat rat rat rat rat",
        "i may live in a sewer, but i'm squeaky clean!",
        "ahem, a little privacy please?",
        "you're not a cat, are you? just checking...",
    ];

    private static readonly FrozenDictionary<string, int> s_progressionItemSortOrder = ProgressionItemSortOrder();

    private readonly CompositeDisposable _subscriptions = [];

    public GameStateViewModel()
        : this(new(Settings.ForDesigner))
    {
    }

    public GameStateViewModel(GameStateObservableProvider provider)
    {
        PlayPauseCommand = ReactiveCommand.Create(provider.TogglePause);

        _subscriptions.Add(provider.Paused
            .Select(paused => paused
                ? Observable.Never<long>()
                : Observable.Interval(TimeSpan.FromMilliseconds(500), AvaloniaScheduler.Instance)
            ).Switch()
            .Subscribe(_ =>
            {
                foreach (LandmarkRegionViewModel landmark in LandmarkRegions)
                {
                    landmark.NextFrame();
                }
            }));

        FrozenDictionary<string, LandmarkRegionViewModel> landmarkRegionsLookup = LandmarkRegions.ToFrozenDictionary(l => l.RegionKey);
        FrozenDictionary<string, ImmutableArray<GameRequirementToolTipViewModel>> toolTipsByItem = (
            from loc in LandmarkRegions
            from tt in loc.GameRequirementToolTipSource.DescendantsAndSelf()
            where tt.Model is ReceivedItemRequirement
            group tt by ((ReceivedItemRequirement)tt.Model).ItemKey
        ).ToFrozenDictionary(grp => grp.Key, grp => grp.ToImmutableArray());

        ImmutableArray<(int RatCount, GameRequirementToolTipViewModel ToolTip)> ratCountToolTips = [
            .. from loc in LandmarkRegions
               from tt in loc.GameRequirementToolTipSource.DescendantsAndSelf()
               where tt.Model is RatCountRequirement
               select (((RatCountRequirement)tt.Model).RatCount, tt),
        ];

        FrozenDictionary<LocationKey, FillerLocationViewModel> fillerLocationLookup = GameDefinitions.Instance.FillerRegions.Values
            .SelectMany(r => new FillerRegionViewModel(r).Locations)
            .ToFrozenDictionary(l => l.Model.Key);
        FillerLocations = [.. fillerLocationLookup.Values];

        Paused = provider.Paused;

        RatCount = provider.CurrentGameState
            .Select(g => g.RatCount);
        FoodFactor = provider.CurrentGameState
            .Select(g => g.FoodFactor);
        EnergyFactor = provider.CurrentGameState
            .Select(g => g.EnergyFactor);
        LuckFactor = provider.CurrentGameState
            .Select(g => g.LuckFactor);
        StyleFactor = provider.CurrentGameState
            .Select(g => g.StyleFactor);
        DistractionCounter = provider.CurrentGameState
            .Select(g => g.DistractionCounter);
        StartledCounter = provider.CurrentGameState
            .Select(g => g.StartledCounter);
        HasConfidence = provider.CurrentGameState
            .Select(g => g.HasConfidence);
        MovingToSmart = provider.CurrentGameState
            .Select(g => g.TargetLocationReason == TargetLocationReason.PriorityPriority && g.SpoilerData[g.TargetLocation] == ArchipelagoItemFlags.LogicalAdvancement);
        MovingToConspiratorial = provider.CurrentGameState
            .Select(g => g.TargetLocationReason == TargetLocationReason.PriorityPriority && g.SpoilerData[g.TargetLocation] == ArchipelagoItemFlags.Trap);

        CurrentLocation = provider.CurrentGameState
            .Select(v => v.CurrentLocation);

        TargetLocation = provider.CurrentGameState
            .Select(g => g.TargetLocation);

        IConnectableObservable<LocationVector> movementLogs0 = provider.CurrentGameState
            .Select(SpaceOut)
            .Switch()
            .Publish();
        _subscriptions.Add(movementLogs0.Connect());
        IObservable<LocationVector> SpaceOut(Game gameState)
        {
            ImmutableArray<LocationVector> locations = gameState.PreviousStepMovementLog;
            if (locations.Length < 2)
            {
                return locations.ToObservable();
            }

            return Observable.Create<LocationVector>(async (obs, cancellationToken) =>
            {
                obs.OnNext(locations[0]);
                for (int i = 1; i < locations.Length; i++)
                {
                    await Task.Delay(MovementAnimationTime, cancellationToken);
                    obs.OnNext(locations[i]);
                }
            });
        }

        IConnectableObservable<LocationVector> movementLogs = movementLogs0
            .Replay(1);
        _subscriptions.Add(movementLogs.Connect());

        CurrentPoint = movementLogs
            .Select(v => GetPoint(v.CurrentLocation));

        IObservable<double> trueAngle = movementLogs
            .Select(v => GetTrueAngle(GetPoint(v.PreviousLocation), GetPoint(v.CurrentLocation)));

        RelativeAngle = trueAngle
            .Select(angle => Math.Abs(angle) < 90 ? angle : angle - 180);

        ScaleX = trueAngle
            .Select(angle => Math.Abs(angle) < 90 ? (double)1 : -1);

        _subscriptions.Add(provider.CurrentGameState
            .DistinctUntilChanged(g => g.RatCount)
            .Subscribe(g =>
            {
                foreach ((int ratCountThreshold, GameRequirementToolTipViewModel toolTip) in ratCountToolTips)
                {
                    toolTip.Satisfied = g.RatCount >= ratCountThreshold;
                }
            }));

        FrozenDictionary<ItemDefinitionModel, CollectableItemViewModel> collectableItemsByModel = ProgressionItems.ToFrozenDictionary(i => i.Model);
        int lastReceivedItemsCount = 0;
        _subscriptions.Add(provider.CurrentGameState
            .Where(g => g.ReceivedItems.Count > lastReceivedItemsCount)
            .Subscribe(g =>
            {
                foreach (ItemDefinitionModel item in g.ReceivedItems.Skip(lastReceivedItemsCount))
                {
                    if (!collectableItemsByModel.TryGetValue(item, out CollectableItemViewModel? viewModel))
                    {
                        continue;
                    }

                    viewModel.Collected = true;
                    if (!toolTipsByItem.TryGetValue(viewModel.ItemKey, out ImmutableArray<GameRequirementToolTipViewModel> tooltips))
                    {
                        continue;
                    }

                    foreach (GameRequirementToolTipViewModel tooltip in tooltips)
                    {
                        tooltip.Satisfied = true;
                    }
                }

                lastReceivedItemsCount = g.ReceivedItems.Count;
            }));

        int lastCheckedLocationsCount = 0;
        bool wasCompleted = false;
        _subscriptions.Add(provider.CurrentGameState
            .Where(g => g.CheckedLocations.Count > lastCheckedLocationsCount || (g.IsCompleted && !wasCompleted))
            .Subscribe(g =>
            {
                foreach (LocationDefinitionModel location in g.CheckedLocations.Skip(lastCheckedLocationsCount))
                {
                    if (landmarkRegionsLookup.TryGetValue(location.Key.RegionKey, out LandmarkRegionViewModel? landmarkViewModel))
                    {
                        landmarkViewModel.Checked = true;
                    }
                    else if (fillerLocationLookup.TryGetValue(location.Key, out FillerLocationViewModel? fillerViewModel))
                    {
                        fillerViewModel.Checked = true;
                    }
                }

                if (g.IsCompleted && !wasCompleted && landmarkRegionsLookup.TryGetValue(GameDefinitions.Instance.GoalRegion.Key, out LandmarkRegionViewModel? goalViewModel))
                {
                    goalViewModel.Checked = true;
                }

                lastCheckedLocationsCount = g.CheckedLocations.Count;
                wasCompleted = g.IsCompleted;
            }));

        Point GetPoint(LocationDefinitionModel location)
        {
            return landmarkRegionsLookup.TryGetValue(location.Region.Key, out LandmarkRegionViewModel? landmark)
                ? landmark.CanvasLocation
                : fillerLocationLookup[location.Key].Point;
        }

        double GetTrueAngle(Point prev, Point curr)
        {
            if (prev == curr)
            {
                return 0;
            }

            return Math.Atan2(curr.Y - prev.Y, curr.X - prev.X) * 180 / Math.PI;
        }
    }

    public void Dispose()
    {
        _subscriptions.Dispose();
    }

    public static TimeSpan MovementAnimationTime { get; } = TimeSpan.FromSeconds(0.1);

    [Reactive]
    public string SlotName { get; set; } = "";

    [Reactive]
    public string RatThought { get; set; } = s_ratThoughts[0];

    public IObservable<bool> Paused { get; }

    public IObservable<int> RatCount { get; }

    public IObservable<LocationDefinitionModel> CurrentLocation { get; }

    public IObservable<Point> CurrentPoint { get; }

    public IObservable<LocationDefinitionModel> TargetLocation { get; }

    public IObservable<double> RelativeAngle { get; }

    public IObservable<double> ScaleX { get; }

    public IObservable<int> FoodFactor { get; }

    public IObservable<int> LuckFactor { get; }

    public IObservable<int> EnergyFactor { get; }

    public IObservable<int> StyleFactor { get; }

    public IObservable<int> DistractionCounter { get; }

    public IObservable<int> StartledCounter { get; }

    public IObservable<bool> HasConfidence { get; }

    public IObservable<bool> MovingToSmart { get; }

    public IObservable<bool> MovingToConspiratorial { get; }

    public required ReactiveCommand<Unit, Unit> BackToMainMenuCommand { get; init; }

    public ReactiveCommand<Unit, Unit> PlayPauseCommand { get; }

    public ImmutableArray<FillerLocationViewModel> FillerLocations { get; }

    public ImmutableArray<CollectableItemViewModel> ProgressionItems { get; } =
    [
        .. GameDefinitions.Instance.ProgressionItems.Keys
            .Where(itemKey => !s_hiddenProgressionItems.Contains(itemKey))
            .OrderBy(itemKey => s_progressionItemSortOrder[itemKey])
            .Select(key => new CollectableItemViewModel(key)),
    ];

    public ImmutableArray<LandmarkRegionViewModel> LandmarkRegions { get; } =
    [
        .. GameDefinitions.Instance.LandmarkRegions.Keys
            .Select(key => new LandmarkRegionViewModel(key)),
    ];

    public void NextRatThought()
    {
        string prevRatThought = RatThought;
        string nextRatThought = RatThought;
        while (nextRatThought == prevRatThought)
        {
            nextRatThought = s_ratThoughts[Random.Shared.Next(s_ratThoughts.Length)];
        }

        RatThought = nextRatThought;
    }

    private static FrozenDictionary<string, int> ProgressionItemSortOrder()
    {
        Dictionary<string, int> result = [];

        HashSet<RegionDefinitionModel> seenRegions = [];
        Queue<RegionDefinitionModel> regions = [];
        regions.Enqueue(GameDefinitions.Instance.StartRegion);
        while (regions.TryDequeue(out RegionDefinitionModel? region))
        {
            if (region is LandmarkRegionDefinitionModel landmark)
            {
                landmark.Requirement.VisitItemKeys(itemKey => result.Add(itemKey, result.Count));
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
