using System.Collections;
using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
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

    private static readonly FrozenDictionary<ItemKey, int> s_progressionItemSortOrder = ProgressionItemSortOrder();

    private readonly CompositeDisposable _disposables = [];

    [Reactive] private string _slotName = "";

    [Reactive] private string _ratThought = s_ratThoughts[0];

    [Reactive] private bool _playerIsActivated;

    [Reactive] private bool _paused;

    [Reactive(SetModifier = AccessModifier.Private)] private int _ratCount;

    [Reactive(SetModifier = AccessModifier.Private)] private LocationDefinitionModel _currentLocation = GameDefinitions.Instance[GameDefinitions.Instance.StartLocation];

    [Reactive(SetModifier = AccessModifier.Private)] private Point _currentPoint;

    [Reactive(SetModifier = AccessModifier.Private)] private LocationDefinitionModel _targetLocation = GameDefinitions.Instance[GameDefinitions.Instance.StartLocation];

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

        PlayerToken = PlayerTokens.For(provider.Settings.PlayerToken, new(provider.Settings.PlayerTokenColor.ToUInt32()))
            .DisposeWith(_disposables);

        this.ObservableForProperty(x => x.Paused)
            .Subscribe(paused => provider.SetPaused(paused.Value))
            .DisposeWith(_disposables);

        LandmarkRegions =
        [
            .. GameDefinitions.Instance.AllRegions
                .OfType<LandmarkRegionDefinitionModel>()
                .Select(r => new LandmarkRegionViewModel(r.Key)),
        ];

        CollectableItemViewModel?[] progressionItemInPanelLookup = new CollectableItemViewModel?[GameDefinitions.Instance.AllItems.Length];
        foreach (CollectableItemViewModel item in ProgressionItemsInPanel)
        {
            progressionItemInPanelLookup[item.Model.Key.N] = item;
        }

        FrozenDictionary<ItemKey, ImmutableArray<GameRequirementToolTipViewModel>> toolTipsByItem = (
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

        FillerLocations =
        [
            .. GameDefinitions.Instance.AllRegions
                .Skip(LandmarkRegions.Length)
                .Cast<FillerRegionDefinitionModel>()
                .SelectMany(r => new FillerRegionViewModel(r).Locations),
        ];

        ImmutableArray<Point> locationPointLookup =
        [
            .. GameDefinitions.Instance.AllLocations
                .Select(l => l.Key.N < LandmarkRegions.Length
                    ? LandmarkRegions[l.Key.N].CanvasLocation
                    : FillerLocations[l.Key.N - LandmarkRegions.Length].Point),
        ];

        _disposables.Add(provider.Paused
            .Subscribe(paused => Paused = paused));

        int prevRatCount = 0;
        int prevReceivedItemsCount = 0;
        int prevCheckedLocationsCount = 0;
        bool wasCompleted = false;
        provider.CurrentGameState
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
                    MovingToSmart = g.SpoilerData[ArchipelagoItemFlags.LogicalAdvancement][g.TargetLocation.N];
                    MovingToConspiratorial = g.SpoilerData[ArchipelagoItemFlags.Trap][g.TargetLocation.N];
                }
                else
                {
                    MovingToSmart = false;
                    MovingToConspiratorial = false;
                }

                CurrentLocation = GameDefinitions.Instance[g.CurrentLocation];
                TargetLocation = GameDefinitions.Instance[g.TargetLocation];
                if (g.RatCount != prevRatCount)
                {
                    prevRatCount = g.RatCount;
                    foreach ((int ratCountThreshold, GameRequirementToolTipViewModel toolTip) in ratCountToolTips)
                    {
                        toolTip.Satisfied = prevRatCount >= ratCountThreshold;
                    }
                }

                foreach (ItemKey item in g.ReceivedItems.Skip(prevReceivedItemsCount))
                {
                    if (progressionItemInPanelLookup[item.N] is not CollectableItemViewModel viewModel)
                    {
                        continue;
                    }

                    viewModel.Collected = true;
                    if (!toolTipsByItem.TryGetValue(viewModel.Model.Key, out ImmutableArray<GameRequirementToolTipViewModel> tooltips))
                    {
                        continue;
                    }

                    foreach (GameRequirementToolTipViewModel tooltip in tooltips)
                    {
                        tooltip.Satisfied = true;
                    }
                }

                prevReceivedItemsCount = g.ReceivedItems.Count;

                foreach (LocationKey location in g.CheckedLocations.Skip(prevCheckedLocationsCount))
                {
                    if (location.N < LandmarkRegions.Length)
                    {
                        LandmarkRegions[location.N].Checked = true;
                    }
                    else
                    {
                        FillerLocations[location.N - LandmarkRegions.Length].Checked = true;
                    }
                }

                prevCheckedLocationsCount = g.CheckedLocations.Count;

                if (g.IsCompleted && !wasCompleted)
                {
                    LandmarkRegions[GameDefinitions.Instance.GoalRegion.N].Checked = true;
                }

                wasCompleted = g.IsCompleted;
                TargetPoint = locationPointLookup[g.TargetLocation.N] + FillerRegionViewModel.ToCenter;
            })
            .DisposeWith(_disposables);

        provider.CurrentGameState
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
                        .Select(l => locationPointLookup[l.N] + FillerRegionViewModel.ToCenter),
                ];
                if (!CurrentPathPoints.SequenceEqual(pathPoints))
                {
                    CurrentPathPoints.Clear();
                    CurrentPathPoints.AddRange(pathPoints);
                }
            })
            .DisposeWith(_disposables);

        provider.CurrentGameState
            .Select(SpaceOut)
            .Switch()
            .Subscribe(v =>
            {
                Point previousPoint = locationPointLookup[v.PreviousLocation.N];
                CurrentPoint = locationPointLookup[v.CurrentLocation.N];
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
            })
            .DisposeWith(_disposables);
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

    public Bitmap PlayerToken { get; }

    public Points CurrentPathPoints { get; } = [];

    public required ReactiveCommand<Unit, Unit> BackToMainMenuCommand { get; init; }

    public ImmutableArray<FillerLocationViewModel> FillerLocations { get; }

    public ImmutableArray<CollectableItemViewModel> ProgressionItemsInPanel { get; } =
    [
        .. GameDefinitions.Instance.ProgressionItemsByYamlKey
            .Where(kvp => !s_hiddenProgressionItems.Contains(kvp.Key))
            .OrderBy(kvp => s_progressionItemSortOrder[kvp.Value])
            .Select(kvp => new CollectableItemViewModel(kvp.Key)),
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

    private static FrozenDictionary<ItemKey, int> ProgressionItemSortOrder()
    {
        Dictionary<ItemKey, int> result = new(GameDefinitions.Instance.ProgressionItemsByYamlKey.Count);

        BitArray visitedRegions = new(GameDefinitions.Instance.AllRegions.Length);
        Queue<RegionKey> regions = [];
        regions.Enqueue(GameDefinitions.Instance.StartRegion);
        while (regions.TryDequeue(out RegionKey region))
        {
            ref readonly RegionDefinitionModel regionDefinition = ref GameDefinitions.Instance[region];
            if (regionDefinition is LandmarkRegionDefinitionModel landmark)
            {
                VisitItemKeys(landmark.Requirement);
            }

            foreach (RegionKey exit in regionDefinition.Connected.Forward)
            {
                if (!visitedRegions[exit.N])
                {
                    visitedRegions[exit.N] = true;
                    regions.Enqueue(exit);
                }
            }
        }

        return result.ToFrozenDictionary();

        void VisitItemKeys(GameRequirement req)
        {
            switch (req)
            {
                case AllChildrenGameRequirement all:
                    foreach (GameRequirement child in all.Children)
                    {
                        VisitItemKeys(child);
                    }

                    break;

                case AnyChildGameRequirement any:
                    foreach (GameRequirement child in any.Children)
                    {
                        VisitItemKeys(child);
                    }

                    break;

                case AnyTwoChildrenGameRequirement any2:
                    foreach (GameRequirement child in any2.Children)
                    {
                        VisitItemKeys(child);
                    }

                    break;

                case ReceivedItemRequirement item:
                    result.Add(item.ItemKey, result.Count);
                    break;
            }
        }
    }
}
