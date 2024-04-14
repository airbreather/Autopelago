using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

using YamlDotNet.RepresentationModel;
using YamlDotNet.Serialization;

namespace Autopelago;

public sealed record GameDefinitions
{
    public static readonly GameDefinitions Instance = LoadFromEmbeddedResource();

    private GameDefinitions()
    {
    }

    public required ItemDefinitionModel NormalRat { get; init; }

    public required ImmutableArray<ItemDefinitionModel> AllItems { get; init; }

    public required FrozenDictionary<string, ItemDefinitionModel> ProgressionItems { get; init; }

    public required FrozenDictionary<string, RegionDefinitionModel> AllRegions { get; init; }

    public required FrozenDictionary<string, LandmarkRegionDefinitionModel> LandmarkRegions { get; init; }

    public required FrozenDictionary<string, FillerRegionDefinitionModel> FillerRegions { get; init; }

    public required RegionDefinitionModel StartRegion { get; init; }

    public required LocationDefinitionModel StartLocation { get; init; }

    public required RegionDefinitionModel GoalRegion { get; init; }

    public required LocationDefinitionModel GoalLocation { get; init; }

    public required FrozenDictionary<string, ItemDefinitionModel> ItemsByName { get; init; }

    public required FrozenDictionary<LocationKey, LocationDefinitionModel> LocationsByKey { get; init; }

    public required FrozenDictionary<string, LocationDefinitionModel> LocationsByName { get; init; }

    public required FloydWarshall FloydWarshall { get; init; }

    private static GameDefinitions LoadFromEmbeddedResource()
    {
        using Stream yamlStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("AutopelagoDefinitions.yml")!;
        using StreamReader yamlReader = new(yamlStream, Encoding.UTF8);
        YamlMappingNode map = new DeserializerBuilder().Build().Deserialize<YamlMappingNode>(yamlReader);

        YamlMappingNode itemsMap = (YamlMappingNode)map["items"];
        YamlMappingNode regionsMap = (YamlMappingNode)map["regions"];

        ItemDefinitionsModel items = ItemDefinitionsModel.DeserializeFrom(itemsMap, regionsMap);
        RegionDefinitionsModel regions = RegionDefinitionsModel.DeserializeFrom(regionsMap, items);
        FrozenDictionary<LocationKey, LocationDefinitionModel> locationsByKey = (
            from region in regions.AllRegions.Values
            from location in region.Locations
            select KeyValuePair.Create(location.Key, location)
        ).ToFrozenDictionary();
        return new()
        {
            NormalRat = items.NormalRat,
            AllItems = items.AllItems,
            ProgressionItems = items.ProgressionItems,
            ItemsByName = items.AllItems.ToFrozenDictionary(i => i.Name),

            AllRegions = regions.AllRegions,
            LandmarkRegions = regions.LandmarkRegions,
            FillerRegions = regions.FillerRegions,
            StartRegion = regions.AllRegions["Menu"],
            StartLocation = locationsByKey[LocationKey.For("Menu", 0)],
            GoalRegion = regions.AllRegions["Victory"],
            GoalLocation = locationsByKey[LocationKey.For("Victory", 0)],

            LocationsByKey = locationsByKey.Values.ToFrozenDictionary(location => location.Key),
            LocationsByName = locationsByKey.Values.ToFrozenDictionary(location => location.Name),
            FloydWarshall = FloydWarshall.Compute(regions.AllRegions.Values),
        };
    }
}

public sealed record ItemDefinitionsModel
{
    private static readonly FrozenDictionary<string, ArchipelagoItemFlags> s_bulkItemFlagsLookup = new Dictionary<string, ArchipelagoItemFlags>
    {
        ["useful_nonprogression"] = ArchipelagoItemFlags.ImportantNonAdvancement,
        ["trap"] = ArchipelagoItemFlags.Trap,
        ["filler"] = ArchipelagoItemFlags.None,
        ["uncategorized"] = ArchipelagoItemFlags.None,
    }.ToFrozenDictionary();

    public required ItemDefinitionModel NormalRat { get; init; }

    public required ImmutableArray<ItemDefinitionModel> AllItems { get; init; }

    public required FrozenDictionary<string, ItemDefinitionModel> ProgressionItems { get; init; }

    public static ItemDefinitionsModel DeserializeFrom(YamlMappingNode itemsMap, YamlMappingNode locationsMap)
    {
        ItemDefinitionModel? normalRat = null;
        List<ItemDefinitionModel> allItems = [];
        Dictionary<string, ItemDefinitionModel> keyedItems = [];
        HashSet<string> progressionItemKeysToValidate = [];

        foreach ((YamlNode keyNode, YamlNode valueNode) in itemsMap)
        {
            string key = keyNode.To<string>();
            switch (key)
            {
                case "rats":
                    foreach ((string ratKey, ItemDefinitionModel ratItem) in DeserializeRatsFrom((YamlMappingNode)valueNode))
                    {
                        allItems.Add(ratItem);
                        keyedItems.Add(ratKey, ratItem);
                    }

                    break;

                case string when s_bulkItemFlagsLookup.TryGetValue(key, out ArchipelagoItemFlags flags):
                    foreach (ItemDefinitionModel item in DeserializeBulkFrom((YamlSequenceNode)valueNode, flags))
                    {
                        allItems.Add(item);
                    }

                    break;

                default:
                    // for simplicity's sake (and because this is getting a bit old), we expect only
                    // progression items to be given keys in the YAML file. we should validated this
                    // though, because it would be a major problem if this got messed up.
                    allItems.Add(ItemDefinitionModel.DeserializeFrom(valueNode, ArchipelagoItemFlags.LogicalAdvancement));
                    keyedItems.Add(key, allItems[^1]);
                    if (key == "normal_rat")
                    {
                        // no need for validation
                        normalRat = allItems[^1];
                    }
                    else if (allItems[^1].RatCount is > 0)
                    {
                        // anything with a rat count is defined as progression.
                    }
                    else
                    {
                        progressionItemKeysToValidate.Add(key);
                    }

                    break;
            }
        }

        if (normalRat is not { RatCount: 1 })
        {
            throw new InvalidDataException("'normal_rat' is required and needs to have a rat_count of 1.");
        }

        AllItemKeysVisitor itemKeysVisitor = new() { NeededItemsNotYetVisited = progressionItemKeysToValidate };
        locationsMap.Accept(itemKeysVisitor);
        if (progressionItemKeysToValidate.Count > 1)
        {
            throw new NotSupportedException($"All but one of the items with keys need to be progression items for now... these weren't found in 'locations': {string.Join(", ", progressionItemKeysToValidate)}");
        }

        List<string> progressionItemKeysNotMarkedAsSuch = [];
        foreach (string itemKey in itemKeysVisitor.VisitedItems)
        {
            if (!(keyedItems.TryGetValue(itemKey, out ItemDefinitionModel? item) && item.ArchipelagoFlags == ArchipelagoItemFlags.LogicalAdvancement))
            {
                progressionItemKeysNotMarkedAsSuch.Add(itemKey);
            }
        }

        if (progressionItemKeysNotMarkedAsSuch.Count > 0)
        {
            throw new InvalidDataException($"'locations' says that these should be progression items, but we didn't find them in 'items': {string.Join(", ", progressionItemKeysNotMarkedAsSuch)}");
        }

        return new()
        {
            NormalRat = normalRat,
            AllItems = [.. allItems],
            ProgressionItems = keyedItems.ToFrozenDictionary(),
        };
    }

    private static IEnumerable<KeyValuePair<string, ItemDefinitionModel>> DeserializeRatsFrom(YamlMappingNode map)
    {
        // at the time of writing, all rats are considered logical advancements.
        ArchipelagoItemFlags archipelagoFlags = ArchipelagoItemFlags.LogicalAdvancement;
        foreach ((YamlNode keyNode, YamlNode valueNode) in map)
        {
            string key = keyNode.To<string>();
            ItemDefinitionModel value = ItemDefinitionModel.DeserializeFrom(valueNode, archipelagoFlags, defaultRatCount: 1);
            yield return KeyValuePair.Create(key, value);
        }
    }

    private static IEnumerable<ItemDefinitionModel> DeserializeBulkFrom(YamlSequenceNode seq, ArchipelagoItemFlags flags, string? associatedGame = null)
    {
        foreach (YamlNode valueNode in seq)
        {
            if (valueNode is YamlMappingNode valueMap && valueMap.Children.TryGetValue("game_specific", out YamlNode? gameSpecificNode))
            {
                if (associatedGame is not null)
                {
                    throw new InvalidDataException("Bad node");
                }

                foreach ((YamlNode gameKeyNode, YamlNode gameValueNode) in (YamlMappingNode)gameSpecificNode)
                {
                    string gameKey = gameKeyNode.To<string>();
                    YamlSequenceNode gameValue = (YamlSequenceNode)gameValueNode;
                    foreach (ItemDefinitionModel gameSpecificItem in DeserializeBulkFrom(gameValue, flags, gameKey))
                    {
                        yield return gameSpecificItem;
                    }
                }

                continue;
            }

            yield return ItemDefinitionModel.DeserializeFrom(valueNode, flags, associatedGame: associatedGame);
        }
    }
}

public record ItemDefinitionModel
{
    public required string? AssociatedGame { get; init; }

    public required string Name { get; init; }

    public required ArchipelagoItemFlags ArchipelagoFlags { get; init; }

    public ImmutableArray<string> AurasGranted { get; init; } = [];

    public string? FlavorText { get; init; }

    public int? RatCount { get; init; }

    public static ItemDefinitionModel DeserializeFrom(YamlNode node, ArchipelagoItemFlags archipelagoFlags, string? associatedGame = null, int? defaultRatCount = null)
    {
        return node switch
        {
            YamlScalarNode scalar => DeserializeFrom(scalar, archipelagoFlags, associatedGame, defaultRatCount),
            YamlSequenceNode sequence => DeserializeFrom(sequence, archipelagoFlags, associatedGame, defaultRatCount),
            YamlMappingNode map => DeserializeFrom(map, archipelagoFlags, associatedGame, defaultRatCount),
            _ => throw new InvalidDataException("Bad node type"),
        };
    }

    private static ItemDefinitionModel DeserializeFrom(YamlScalarNode scalar, ArchipelagoItemFlags archipelagoFlags, string? associatedGame = null, int? defaultRatCount = null)
    {
        return new()
        {
            AssociatedGame = associatedGame,
            Name = scalar.Value!,
            ArchipelagoFlags = archipelagoFlags,
            RatCount = defaultRatCount,
        };
    }

    private static ItemDefinitionModel DeserializeFrom(YamlSequenceNode sequence, ArchipelagoItemFlags archipelagoFlags, string? associatedGame = null, int? defaultRatCount = null)
    {
        if (sequence.Children is not [YamlScalarNode { Value: string itemName }, YamlSequenceNode aurasGranted])
        {
            throw new InvalidDataException("Bad format.");
        }

        return new()
        {
            AssociatedGame = associatedGame,
            Name = itemName,
            ArchipelagoFlags = archipelagoFlags,
            AurasGranted = [.. aurasGranted.Select(a => ((YamlScalarNode)a).Value!)],
            RatCount = defaultRatCount,
        };
    }

    private static ItemDefinitionModel DeserializeFrom(YamlMappingNode map, ArchipelagoItemFlags archipelagoFlags, string? associatedGame = null, int? defaultRatCount = null)
    {
        string? name = null;
        int? ratCount = defaultRatCount;
        string? flavorText = null;
        ImmutableArray<string> aurasGranted = [];
        foreach ((YamlNode keyNode, YamlNode valueNode) in map)
        {
            string key = keyNode.To<string>();
            switch (key)
            {
                case "name":
                    name = valueNode.To<string>();
                    break;

                case "auras_granted":
                    aurasGranted = [.. valueNode.To<string[]>()];
                    break;

                case "rat_count":
                    ratCount = valueNode.To<int>();
                    break;

                case "flavor_text":
                    flavorText = valueNode.To<string>();
                    break;
            }
        }

        return new()
        {
            AssociatedGame = associatedGame,
            Name = name ?? throw new InvalidDataException("name is required."),
            ArchipelagoFlags = archipelagoFlags,
            AurasGranted = aurasGranted,
            RatCount = ratCount,
            FlavorText = flavorText,
        };
    }
}

public sealed record RegionDefinitionsModel
{
    public required FrozenDictionary<string, RegionDefinitionModel> AllRegions { get; init; }

    public required FrozenDictionary<string, LandmarkRegionDefinitionModel> LandmarkRegions { get; init; }

    public required FrozenDictionary<string, FillerRegionDefinitionModel> FillerRegions { get; init; }

    public static RegionDefinitionsModel DeserializeFrom(YamlMappingNode map, ItemDefinitionsModel items)
    {
        Dictionary<string, RegionDefinitionModel> allRegions = new()
        {
            ["Victory"] = new LandmarkRegionDefinitionModel
            {
                Key = "Victory",
                Exits = [],
                Locations =
                [
                    new()
                    {
                        Key = LocationKey.For("Victory"),
                        Name = "Victory",
                        Requirement = GameRequirement.AlwaysSatisfied,
                        AbilityCheckDC = 1,
                        UnrandomizedItem = null,
                        RewardIsFixed = true,
                    },
                ],
            },
        };

        Dictionary<string, LandmarkRegionDefinitionModel> landmarkRegions = [];
        foreach ((YamlNode keyNode, YamlNode valueNode) in (YamlMappingNode)map["landmarks"])
        {
            string key = keyNode.To<string>();
            LandmarkRegionDefinitionModel value = LandmarkRegionDefinitionModel.DeserializeFrom(key, (YamlMappingNode)valueNode, items);
            landmarkRegions.Add(key, value);
            allRegions.Add(key, value);
        }

        Dictionary<string, FillerRegionDefinitionModel> fillerRegions = [];
        foreach ((YamlNode keyNode, YamlNode valueNode) in (YamlMappingNode)map["fillers"])
        {
            string key = keyNode.To<string>();
            FillerRegionDefinitionModel value = FillerRegionDefinitionModel.DeserializeFrom(key, (YamlMappingNode)valueNode, items);
            fillerRegions.Add(key, value);
            allRegions.Add(key, value);
        }

        return new()
        {
            AllRegions = allRegions.ToFrozenDictionary(),
            LandmarkRegions = landmarkRegions.ToFrozenDictionary(),
            FillerRegions = fillerRegions.ToFrozenDictionary(),
        };
    }
}

public abstract record RegionDefinitionModel
{
    public required string Key { get; init; }

    public required ImmutableArray<RegionExitDefinitionModel> Exits { get; init; }

    public required ImmutableArray<LocationDefinitionModel> Locations { get; init; }
}

public sealed record RegionExitDefinitionModel
{
    public required string RegionKey { get; init; }

    public RegionDefinitionModel Region => GameDefinitions.Instance.AllRegions[RegionKey];

    public AllChildrenGameRequirement Requirement => Region is LandmarkRegionDefinitionModel
    {
        Locations: [ { Requirement: AllChildrenGameRequirement req } ],
    } ? req : GameRequirement.AlwaysSatisfied;

    public static RegionExitDefinitionModel DeserializeFrom(YamlNode node)
    {
        return new()
        {
            RegionKey = ((YamlScalarNode)node).Value!,
        };
    }
}

public sealed record LandmarkRegionDefinitionModel : RegionDefinitionModel
{
    public static LandmarkRegionDefinitionModel DeserializeFrom(string key, YamlMappingNode map, ItemDefinitionsModel items)
    {
        AllChildrenGameRequirement requirement = AllChildrenGameRequirement.DeserializeFrom(map["requires"]);
        return new()
        {
            Key = key,
            Exits = [.. ((YamlSequenceNode)map["exits"]).Select(RegionExitDefinitionModel.DeserializeFrom)],
            Locations =
            [
                new()
                {
                    Key = LocationKey.For(key),
                    Name = map["name"].To<string>(),
                    FlavorText = map.TryGetValue("flavor_text", out string? flavorText) ? flavorText : null,
                    UnrandomizedItem = items.ProgressionItems[map["unrandomized_item"].To<string>()],
                    AbilityCheckDC = map.TryGetValue("ability_check_dc", out int abilityCheckDC) ? abilityCheckDC : 1,
                    Requirement = requirement,
                    RewardIsFixed = map.TryGetValue("reward_is_fixed", out bool rewardIsFixed) && rewardIsFixed,
                },
            ],
        };
    }
}

public sealed record FillerRegionDefinitionModel : RegionDefinitionModel
{
    public static FillerRegionDefinitionModel DeserializeFrom(string key, YamlMappingNode map, ItemDefinitionsModel items)
    {
        Dictionary<ArchipelagoItemFlags, string> keyMap = new()
        {
            [ArchipelagoItemFlags.None] = "filler",
            [ArchipelagoItemFlags.ImportantNonAdvancement] = "useful_nonprogression",
        };
        ILookup<string, ItemDefinitionModel> itemsLookup = items.AllItems.Where(item => keyMap.ContainsKey(item.ArchipelagoFlags)).ToLookup(item => keyMap[item.ArchipelagoFlags]);
        Dictionary<string, int> nextInGroup = [];
        string nameTemplate = map["name_template"].To<string>();
        List<ItemDefinitionModel> unrandomizedItems = [];
        foreach ((YamlNode keyNode, YamlNode valueNode) in (YamlMappingNode)map["unrandomized_items"])
        {
            switch (keyNode.To<string>())
            {
                case "key":
                    foreach (YamlNode itemRefNode in (YamlSequenceNode)valueNode)
                    {
                        ItemRefModel itemRef = ItemRefModel.DeserializeFrom(itemRefNode);
                        ItemDefinitionModel item = items.ProgressionItems[itemRef.Key];
                        for (int i = 0; i < itemRef.ItemCount; i++)
                        {
                            unrandomizedItems.Add(item);
                        }
                    }

                    break;

                case string itemGroupKey:
                    IEnumerable<ItemDefinitionModel> grp = itemsLookup[itemGroupKey];
                    int cnt = int.Parse(((YamlScalarNode)valueNode).Value!);
                    ref int nextSrc = ref CollectionsMarshal.GetValueRefOrAddDefault(nextInGroup, itemGroupKey, out _);
                    for (int i = 0; i < cnt; i++)
                    {
                        // ReSharper disable once PossibleMultipleEnumeration
                        unrandomizedItems.Add(grp.ElementAt(nextSrc++));
                    }

                    break;
            }
        }

        if (!map.TryGetValue("ability_check_dc", out int abilityCheckDC))
        {
            abilityCheckDC = 1;
        }

        return new()
        {
            Key = key,
            Exits = [.. ((YamlSequenceNode)map["exits"]).Select(RegionExitDefinitionModel.DeserializeFrom)],
            Locations = [.. unrandomizedItems.Select((item, n) => new LocationDefinitionModel
            {
                Key = LocationKey.For(key, n),
                Name = nameTemplate.Replace("{n}", $"{n + 1}"),
                Requirement = GameRequirement.AlwaysSatisfied,
                AbilityCheckDC = abilityCheckDC,
                UnrandomizedItem = item,
                RewardIsFixed = false,
            })],
        };
    }

    private sealed record ItemRefModel
    {
        public required string Key { get; init; }

        public int ItemCount { get; init; } = 1;

        public static ItemRefModel DeserializeFrom(YamlNode node)
        {
            if (node is YamlScalarNode scalar)
            {
                return new() { Key = scalar.Value! };
            }

            YamlMappingNode map = (YamlMappingNode)node;
            return new()
            {
                Key = map["item"].To<string>(),
                ItemCount = map.TryGetValue("count", out int count) ? count : 1,
            };
        }
    }
}

public readonly record struct LocationKey
{
    public required string RegionKey { get; init; }

    public required int N { get; init; }

    public static LocationKey For(string regionKey)
    {
        return For(regionKey, 0);
    }

    public static LocationKey For(string regionKey, int n)
    {
        return new()
        {
            RegionKey = regionKey,
            N = n,
        };
    }
}

public sealed record LocationDefinitionModel
{
    public required LocationKey Key { get; init; }

    public required string Name { get; init; }

    public string? FlavorText { get; init; }

    public required AllChildrenGameRequirement Requirement { get; init; }

    public required int AbilityCheckDC { get; init; }

    public required ItemDefinitionModel? UnrandomizedItem { get; init; }

    public required bool RewardIsFixed { get; init; }

    public RegionDefinitionModel Region => GameDefinitions.Instance.AllRegions[Key.RegionKey];

    public int DistanceTo(LocationDefinitionModel target) => GameDefinitions.Instance.FloydWarshall.GetDistance(this, target);

    public LocationDefinitionModel NextLocationTowards(LocationDefinitionModel target) => this == target ? target : GameDefinitions.Instance.FloydWarshall.GetNextOnPath(this, target);

    public bool TryCheck(ref Game.State state)
    {
        if (!Requirement.Satisfied(state))
        {
            return false;
        }

        int extraDiceModifier = 0;
        switch (state.LuckFactor)
        {
            case < 0:
                extraDiceModifier -= 5;
                state = state with { LuckFactor = state.LuckFactor + 1 };
                break;

            case > 0:
                state = state with
                {
                    LuckFactor = state.LuckFactor - 1,
                    CheckedLocations = state.CheckedLocations.Add(this),
                };
                return true;
        }

        if (state.StyleFactor > 0)
        {
            extraDiceModifier += 5;
            state = state with { StyleFactor = state.StyleFactor - 1 };
        }

        if (Game.State.NextD20(ref state) + state.DiceModifier + extraDiceModifier < this.AbilityCheckDC)
        {
            return false;
        }

        state = state with { CheckedLocations = state.CheckedLocations.Add(this) };
        return true;
    }
}

public abstract record GameRequirement
{
    public static readonly AllChildrenGameRequirement AlwaysSatisfied = new() { Children = [] };

    public virtual bool Satisfied(Game.State state)
    {
        return true;
    }

    public virtual void VisitItemKeys(Action<string> onItemKey)
    {
    }

    public static GameRequirement DeserializeFrom(YamlNode node)
    {
        if (node is not YamlMappingNode { Children: [(YamlScalarNode keyNode, YamlNode valueNode)] })
        {
            throw new InvalidDataException("Bad node type");
        }

        return keyNode.Value switch
        {
            "rat_count" => RatCountRequirement.DeserializeFrom(valueNode),
            "location" => CheckedLocationRequirement.DeserializeFrom(valueNode),
            "item" => ReceivedItemRequirement.DeserializeFrom(valueNode),
            "any" => AnyChildGameRequirement.DeserializeFrom(valueNode),
            "any_two" => AnyTwoChildrenGameRequirement.DeserializeFrom(valueNode),
            "all" => AllChildrenGameRequirement.DeserializeFrom(valueNode),
            _ => throw new InvalidDataException($"Unrecognized requirement: {keyNode.Value}"),
        };
    }
}

public sealed record AllChildrenGameRequirement : GameRequirement
{
    public required ImmutableArray<GameRequirement> Children { get; init; }

    public static new AllChildrenGameRequirement DeserializeFrom(YamlNode node)
    {
        return new() { Children = [.. ((YamlSequenceNode)node).Select(GameRequirement.DeserializeFrom)] };
    }

    public override bool Satisfied(Game.State state)
    {
        foreach (GameRequirement child in Children)
        {
            if (!child.Satisfied(state))
            {
                return false;
            }
        }

        return true;
    }

    public override void VisitItemKeys(Action<string> onItemKey)
    {
        foreach (GameRequirement child in Children)
        {
            child.VisitItemKeys(onItemKey);
        }
    }

    public bool Equals(AllChildrenGameRequirement? other)
    {
        return
            base.Equals(other) &&
            Children.SequenceEqual(other.Children);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Children.Length);
    }
}

public sealed record AnyChildGameRequirement : GameRequirement
{
    public required ImmutableArray<GameRequirement> Children { get; init; }

    public static new AnyChildGameRequirement DeserializeFrom(YamlNode node)
    {
        return new() { Children = [.. ((YamlSequenceNode)node).Select(GameRequirement.DeserializeFrom)] };
    }

    public override bool Satisfied(Game.State state)
    {
        foreach (GameRequirement child in Children)
        {
            if (child.Satisfied(state))
            {
                return true;
            }
        }

        return false;
    }

    public override void VisitItemKeys(Action<string> onItemKey)
    {
        foreach (GameRequirement child in Children)
        {
            child.VisitItemKeys(onItemKey);
        }
    }

    public bool Equals(AnyChildGameRequirement? other)
    {
        return
            base.Equals(other) &&
            Children.SequenceEqual(other.Children);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Children.Length);
    }
}

public sealed record AnyTwoChildrenGameRequirement : GameRequirement
{
    public required ImmutableArray<GameRequirement> Children { get; init; }

    public static new AnyTwoChildrenGameRequirement DeserializeFrom(YamlNode node)
    {
        return new() { Children = [.. ((YamlSequenceNode)node).Select(GameRequirement.DeserializeFrom)] };
    }

    public override bool Satisfied(Game.State state)
    {
        bool one = false;
        foreach (GameRequirement child in Children)
        {
            if (!child.Satisfied(state))
            {
                continue;
            }

            if (one)
            {
                return true;
            }

            one = true;
        }

        return false;
    }

    public override void VisitItemKeys(Action<string> onItemKey)
    {
        foreach (GameRequirement child in Children)
        {
            child.VisitItemKeys(onItemKey);
        }
    }

    public bool Equals(AnyChildGameRequirement? other)
    {
        return
            base.Equals(other) &&
            Children.SequenceEqual(other.Children);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Children.Length);
    }
}

public sealed record RatCountRequirement : GameRequirement
{
    public required int RatCount { get; init; }

    public static new RatCountRequirement DeserializeFrom(YamlNode node)
    {
        return new() { RatCount = node.To<int>() };
    }

    public override bool Satisfied(Game.State state)
    {
        return state.RatCount >= RatCount;
    }
}

public sealed record CheckedLocationRequirement : GameRequirement
{
    public required LocationKey LocationKey { get; init; }

    public static new CheckedLocationRequirement DeserializeFrom(YamlNode node)
    {
        return new() { LocationKey = LocationKey.For(node.To<string>()) };
    }

    public override bool Satisfied(Game.State state)
    {
        return state.CheckedLocations.Any(k => k.Key == LocationKey);
    }
}

public sealed record ReceivedItemRequirement : GameRequirement
{
    public required string ItemKey { get; init; }

    public static new ReceivedItemRequirement DeserializeFrom(YamlNode node)
    {
        return new() { ItemKey = node.To<string>() };
    }

    public override bool Satisfied(Game.State state)
    {
        return state.ReceivedItems.Contains(GameDefinitions.Instance.ProgressionItems[ItemKey]);
    }

    public override void VisitItemKeys(Action<string> onItemKey)
    {
        onItemKey(ItemKey);
    }
}

file sealed class AllItemKeysVisitor : YamlVisitorBase
{
    private bool _visitingItem;

    public required HashSet<string> NeededItemsNotYetVisited { get; init; }

    public HashSet<string> VisitedItems { get; } = [];

    public override void Visit(YamlScalarNode scalar)
    {
        if (_visitingItem && scalar.Value is string val && VisitedItems.Add(val))
        {
            NeededItemsNotYetVisited.Remove(val);
        }
    }

    protected override void VisitPair(YamlNode key, YamlNode value)
    {
        key.Accept(this);
        _visitingItem = key is YamlScalarNode { Value: "item" };
        value.Accept(this);
        _visitingItem = false;
    }
}
