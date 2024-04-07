using System.Collections.Immutable;

using Avalonia.Controls;

using ReactiveUI.Fody.Helpers;

namespace Autopelago.ViewModels;

public sealed class GameStateViewModel : ViewModelBase
{
    public GameStateViewModel()
    {
        if (!Design.IsDesignMode)
        {
            return;
        }

        foreach (CollectableItemViewModel item in ProgressionItems.OrderBy(_ => Random.Shared.NextDouble()).Take(ProgressionItems.Length / 2))
        {
            item.Collected = true;
        }

        foreach (CheckableLocationViewModel location in CheckableLocations.OrderBy(_ => Random.Shared.NextDouble()).Take(CheckableLocations.Length / 2))
        {
            location.Checked = true;
        }
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
}
