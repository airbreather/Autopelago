using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;

using Avalonia;
using Avalonia.Controls;
using Avalonia.ReactiveUI;

using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace Autopelago.ViewModels;

public sealed partial class GameStateViewModel : ViewModelBase, IDisposable
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
        "'click me to see where I want to go'? what does that mean?",
    ];

    private static readonly FrozenDictionary<string, int> s_progressionItemSortOrder = ProgressionItemSortOrder();

    private readonly CompositeDisposable _disposables = [];

    [Reactive] private string _slotName = "";

    [Reactive] private string _ratThought = s_ratThoughts[0];

    [Reactive] private bool _playerIsActivated;

    [Reactive] private bool _paused;

    [Reactive(SetModifier = AccessModifier.Private)] private int _ratCount;

    [Reactive(SetModifier = AccessModifier.Private)] private LocationDefinitionModel _currentLocation = GameDefinitions.Instance.StartLocation;

    [Reactive(SetModifier = AccessModifier.Private)] private Point _currentPoint;

    [Reactive(SetModifier = AccessModifier.Private)] private LocationDefinitionModel _targetLocation = GameDefinitions.Instance.StartLocation;

    [Reactive(SetModifier = AccessModifier.Private)] private Point _targetPoint;

    [Reactive(SetModifier = AccessModifier.Private)] private double _relativeAngle;

    [Reactive(SetModifier = AccessModifier.Private)] private double _scaleX;

    [Reactive(SetModifier = AccessModifier.Private)] private int _foodFactor;

    [Reactive(SetModifier = AccessModifier.Private)] private int _energyFactor;

    [Reactive(SetModifier = AccessModifier.Private)] private int _luckFactor;

    [Reactive(SetModifier = AccessModifier.Private)] private int _styleFactor;

    [Reactive(SetModifier = AccessModifier.Private)] private int _distractionCounter;

    [Reactive(SetModifier = AccessModifier.Private)] private int _startledCounter;

    [Reactive(SetModifier = AccessModifier.Private)] private bool _hasConfidence;

    [Reactive(SetModifier = AccessModifier.Private)] private bool _movingToSmart;

    [Reactive(SetModifier = AccessModifier.Private)] private bool _movingToConspiratorial;

    public GameStateViewModel()
        : this(new(Settings.ForDesigner))
    {
    }

    public GameStateViewModel(GameStateObservableProvider provider)
    {
        if (Design.IsDesignMode)
        {
            // ain't nobody got time for dat
            _paused = true;
            provider.SetPaused(true);
        }

        _disposables.Add(this.ObservableForProperty(x => x.Paused)
            .Subscribe(paused => provider.SetPaused(paused.Value)));

        (BitmapPair yellowQuestImage, BitmapPair grayQuestImage) = LandmarkRegionViewModel.CreateQuestImages();
        _disposables.Add(yellowQuestImage);
        _disposables.Add(grayQuestImage);
        LandmarkRegions =
        [
            .. GameDefinitions.Instance.LandmarkRegions.Keys
                .Select(key => new LandmarkRegionViewModel(key, yellowQuestImage, grayQuestImage)),
        ];

        TimeSpan frameTime = Design.IsDesignMode
            ? TimeSpan.FromHours(1)
            : TimeSpan.FromMilliseconds(500);
        _disposables.Add(provider.Paused
            .Select(paused => paused
                ? Observable.Never<long>()
                : Observable.Interval(frameTime, AvaloniaScheduler.Instance)
            ).Switch()
            .StartWith(0)
            .Subscribe(_ =>
            {
                yellowQuestImage.NextFrame();
                grayQuestImage.NextFrame();
                foreach (LandmarkRegionViewModel landmark in LandmarkRegions)
                {
                    landmark.NextFrame();
                }
            }));

        FrozenDictionary<string, CollectableItemViewModel> progressionItemInPanelLookup = ProgressionItemsInPanel.ToFrozenDictionary(i => i.Model.Name);
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

        FrozenDictionary<LocationKey, Point> locationPointLookup = GameDefinitions.Instance.LocationsByKey
            .ToFrozenDictionary(kvp => kvp.Key, kvp => landmarkRegionsLookup.TryGetValue(kvp.Key.RegionKey, out LandmarkRegionViewModel? landmark)
                ? landmark.CanvasLocation
                : fillerLocationLookup[kvp.Key].Point);

        FillerLocations = [.. fillerLocationLookup.Values];

        _disposables.Add(provider.Paused
            .Subscribe(paused => Paused = paused));

        int prevRatCount = 0;
        int prevReceivedItemsCount = 0;
        int prevCheckedLocationsCount = 0;
        bool wasCompleted = false;
        _disposables.Add(provider.CurrentGameState
            .ObserveOn(AvaloniaScheduler.Instance)
            .Subscribe(g =>
            {
                RatCount = g.RatCount;
                FoodFactor = g.FoodFactor;
                EnergyFactor = g.EnergyFactor;
                LuckFactor = g.LuckFactor;
                StyleFactor = g.StyleFactor;
                DistractionCounter = g.DistractionCounter;
                StartledCounter = g.StartledCounter;
                HasConfidence = g.HasConfidence;
                if (g.TargetLocationReason == TargetLocationReason.PriorityPriority)
                {
                    MovingToSmart = g.SpoilerData[ArchipelagoItemFlags.LogicalAdvancement].Contains(g.TargetLocation.Key);
                    MovingToConspiratorial = g.SpoilerData[ArchipelagoItemFlags.Trap].Contains(g.TargetLocation.Key);
                }
                else
                {
                    MovingToSmart = false;
                    MovingToConspiratorial = false;
                }

                CurrentLocation = g.CurrentLocation;
                TargetLocation = g.TargetLocation;
                if (g.RatCount != prevRatCount)
                {
                    prevRatCount = g.RatCount;
                    foreach ((int ratCountThreshold, GameRequirementToolTipViewModel toolTip) in ratCountToolTips)
                    {
                        toolTip.Satisfied = prevRatCount >= ratCountThreshold;
                    }
                }

                foreach (ItemDefinitionModel item in g.ReceivedItems.Skip(prevReceivedItemsCount))
                {
                    if (!progressionItemInPanelLookup.TryGetValue(item.Name, out CollectableItemViewModel? viewModel))
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

                prevReceivedItemsCount = g.ReceivedItems.Count;

                foreach (LocationDefinitionModel location in g.CheckedLocations.Order.Skip(prevCheckedLocationsCount))
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

                prevCheckedLocationsCount = g.CheckedLocations.Count;

                if (g.IsCompleted && !wasCompleted && landmarkRegionsLookup.TryGetValue(GameDefinitions.Instance.GoalRegion.Key, out LandmarkRegionViewModel? goalViewModel))
                {
                    goalViewModel.Checked = true;
                }

                wasCompleted = g.IsCompleted;
                TargetPoint = locationPointLookup[g.TargetLocation.Key] + FillerRegionViewModel.ToCenter;
            }));

        _disposables.Add(provider.CurrentGameState
            .CombineLatest(this.ObservableForProperty(x => x.PlayerIsActivated))
            .Subscribe(chg =>
            {
                if (!chg.Second.Value)
                {
                    CurrentPathPoints.Clear();
                    return;
                }

                Game g = chg.First;
                Point[] pathPoints =
                [
                    .. g.CalculateRoute(g.CurrentLocation, g.TargetLocation)
                        .Select(l => locationPointLookup[l.Key] + FillerRegionViewModel.ToCenter),
                ];
                if (!CurrentPathPoints.SequenceEqual(pathPoints))
                {
                    CurrentPathPoints.Clear();
                    CurrentPathPoints.AddRange(pathPoints);
                }
            }));

        _disposables.Add(provider.CurrentGameState
            .Select(SpaceOut)
            .Switch()
            .Subscribe(v =>
            {
                Point previousPoint = locationPointLookup[v.PreviousLocation.Key];
                CurrentPoint = locationPointLookup[v.CurrentLocation.Key];
                double trueAngle = previousPoint == CurrentPoint
                    ? 0
                    : Math.Atan2(CurrentPoint.Y - previousPoint.Y, CurrentPoint.X - previousPoint.X) * 180 / Math.PI;

                if (Math.Abs(trueAngle) < 90)
                {
                    RelativeAngle = trueAngle;
                    ScaleX = 1;
                }
                else
                {
                    RelativeAngle = trueAngle - 180;
                    ScaleX = -1;
                }
            }));
        IObservable<LocationVector> SpaceOut(Game gameState)
        {
            ReadOnlyCollection<LocationVector> locations = gameState.PreviousStepMovementLog;
            if (locations.Count < 2)
            {
                return locations.ToObservable();
            }

            return Observable.Create<LocationVector>(async (obs, cancellationToken) =>
            {
                obs.OnNext(locations[0]);
                for (int i = 1; i < locations.Count; i++)
                {
                    await Task.Delay(MovementAnimationTime, cancellationToken);
                    obs.OnNext(locations[i]);
                }
            });
        }
    }

    public void Dispose()
    {
        _disposables.Dispose();
    }

    public static TimeSpan MovementAnimationTime { get; } = TimeSpan.FromSeconds(0.1);

    public Points CurrentPathPoints { get; } = [];

    public required ReactiveCommand<Unit, Unit> BackToMainMenuCommand { get; init; }

    public ImmutableArray<FillerLocationViewModel> FillerLocations { get; }

    public ImmutableArray<CollectableItemViewModel> ProgressionItemsInPanel { get; } =
    [
        .. GameDefinitions.Instance.ProgressionItems.Keys
            .Where(itemKey => !s_hiddenProgressionItems.Contains(itemKey))
            .OrderBy(itemKey => s_progressionItemSortOrder[itemKey])
            .Select(key => new CollectableItemViewModel(key)),
    ];

    public ImmutableArray<LandmarkRegionViewModel> LandmarkRegions { get; }

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
