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

    public required ItemDefinitionModel PackRat { get; init; }

    public required ImmutableArray<ItemDefinitionModel> AllItems { get; init; }

    public required FrozenDictionary<string, ItemDefinitionModel> ProgressionItems { get; init; }

    public required FrozenDictionary<string, RegionDefinitionModel> AllRegions { get; init; }

    public required FrozenDictionary<string, LandmarkRegionDefinitionModel> LandmarkRegions { get; init; }

    public required FrozenDictionary<string, FillerRegionDefinitionModel> FillerRegions { get; init; }

    public required RegionDefinitionModel StartRegion { get; init; }

    public required LocationDefinitionModel StartLocation { get; init; }

    public required LandmarkRegionDefinitionModel GoalRegion { get; init; }

    public required LocationDefinitionModel GoalLocation { get; init; }

    public required FrozenDictionary<string, ItemDefinitionModel> ItemsByName { get; init; }

    public required FrozenDictionary<LocationKey, LocationDefinitionModel> LocationsByKey { get; init; }

    public required FrozenDictionary<string, LocationDefinitionModel> LocationsByName { get; init; }

    public required FrozenDictionary<string, LocationDefinitionModel> LocationsByNameCaseInsensitive { get; init; }

    public required FrozenDictionary<LocationDefinitionModel, ImmutableArray<LocationDefinitionModel>> ConnectedLocations { get; init; }

    public required FrozenDictionary<ArchipelagoItemFlags, ImmutableArray<ItemDefinitionModel>> NonGameSpecificItemsByFlags { get; init; }

    public required FrozenDictionary<ArchipelagoItemFlags, ImmutableArray<LocationDefinitionModel>> LocationsByUnrandomizedItemFlags { get; init; }

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
            PackRat = items.PackRat,
            AllItems = items.AllItems,
            ProgressionItems = items.ProgressionItems,
            ItemsByName = items.AllItems.ToFrozenDictionary(i => i.Name),

            AllRegions = regions.AllRegions,
            LandmarkRegions = regions.LandmarkRegions,
            FillerRegions = regions.FillerRegions,
            StartRegion = regions.AllRegions["Menu"],
            StartLocation = locationsByKey[LocationKey.For("Menu", 0)],
            GoalRegion = regions.LandmarkRegions["moon_comma_the"],
            GoalLocation = locationsByKey[LocationKey.For("moon_comma_the", 0)],

            LocationsByKey = locationsByKey.Values.ToFrozenDictionary(location => location.Key),
            LocationsByName = locationsByKey.Values.ToFrozenDictionary(location => location.Name),
            LocationsByNameCaseInsensitive = locationsByKey.Values.ToFrozenDictionary(location => location.Name, StringComparer.InvariantCultureIgnoreCase),
            ConnectedLocations = regions.ConnectedLocations,

            // things that should only be needed in the debugger to help populate the filler regions
            NonGameSpecificItemsByFlags = items.AllItems
                .Where(i => i.AssociatedGame is null)
                .ToLookup(i => i.ArchipelagoFlags)
                .ToFrozenDictionary(grp => grp.Key, grp => grp.ToImmutableArray()),

            LocationsByUnrandomizedItemFlags = locationsByKey.Values
                .Where(l => l.UnrandomizedItem is not null)
                .ToLookup(l => l.UnrandomizedItem!.ArchipelagoFlags)
                .ToFrozenDictionary(grp => grp.Key, grp => grp.ToImmutableArray()),
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
    }.ToFrozenDictionary();

    public required ItemDefinitionModel PackRat { get; init; }

    public required ImmutableArray<ItemDefinitionModel> AllItems { get; init; }

    public required FrozenDictionary<string, ItemDefinitionModel> ProgressionItems { get; init; }

    public static ItemDefinitionsModel DeserializeFrom(YamlMappingNode itemsMap, YamlMappingNode locationsMap)
    {
        ItemDefinitionModel? packRat = null;
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
                    // progression items to be given keys in the YAML file. we should validate this
                    // though, because it would be a major problem if this got messed up.
                    allItems.Add(ItemDefinitionModel.DeserializeFrom(valueNode, ArchipelagoItemFlags.LogicalAdvancement));
                    keyedItems.Add(key, allItems[^1]);
                    if (key == "pack_rat")
                    {
                        // no need for validation
                        packRat = allItems[^1];
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

        if (packRat is not { RatCount: 1 })
        {
            throw new InvalidDataException("'pack_rat' is required and needs to have a rat_count of 1.");
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
            if (itemKey == "Victory")
            {
                // it's something like an "event" item.
                continue;
            }

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
            PackRat = packRat,
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

public sealed record ItemDefinitionModel
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

    public bool Equals(ItemDefinitionModel? other)
    {
        return
            other is not null &&
            AssociatedGame == other.AssociatedGame &&
            Name == other.Name &&
            ArchipelagoFlags == other.ArchipelagoFlags &&
            AurasGranted.SequenceEqual(other.AurasGranted) && // order is important!
            FlavorText == other.FlavorText &&
            RatCount == other.RatCount;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(
            AssociatedGame,
            Name,
            ArchipelagoFlags,
            AurasGranted.Length,
            FlavorText,
            RatCount
        );
    }
}

public sealed record RegionDefinitionsModel
{
    public required FrozenDictionary<string, RegionDefinitionModel> AllRegions { get; init; }

    public required FrozenDictionary<string, LandmarkRegionDefinitionModel> LandmarkRegions { get; init; }

    public required FrozenDictionary<string, FillerRegionDefinitionModel> FillerRegions { get; init; }

    public required FrozenDictionary<LocationDefinitionModel, ImmutableArray<LocationDefinitionModel>> ConnectedLocations { get; init; }

    public static RegionDefinitionsModel DeserializeFrom(YamlMappingNode map, ItemDefinitionsModel items)
    {
        Dictionary<string, RegionDefinitionModel> allRegions = new();

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

        Dictionary<LocationDefinitionModel, List<LocationDefinitionModel>> connectedLocations = [];
        Queue<(LocationDefinitionModel? Prev, RegionDefinitionModel Curr)> regionsQueue = [];
        regionsQueue.Enqueue((null, allRegions["Menu"]));
        while (regionsQueue.TryDequeue(out (LocationDefinitionModel? Prev, RegionDefinitionModel Curr) tup))
        {
            (LocationDefinitionModel? prev, RegionDefinitionModel curr) = tup;
            foreach (LocationDefinitionModel next in curr.Locations)
            {
                if (prev is not null)
                {
                    (CollectionsMarshal.GetValueRefOrAddDefault(connectedLocations, prev, out _) ??= []).Add(next);
                    (CollectionsMarshal.GetValueRefOrAddDefault(connectedLocations, next, out _) ??= []).Add(prev);
                }

                prev = next;
            }

            foreach (RegionExitDefinitionModel exit in curr.Exits)
            {
                regionsQueue.Enqueue((prev, allRegions[exit.RegionKey]));
            }
        }

        return new()
        {
            AllRegions = allRegions.ToFrozenDictionary(),
            LandmarkRegions = landmarkRegions.ToFrozenDictionary(),
            FillerRegions = fillerRegions.ToFrozenDictionary(),
            ConnectedLocations = connectedLocations.ToFrozenDictionary(kvp => kvp.Key, kvp => kvp.Value.ToImmutableArray()),
        };
    }
}

public abstract record RegionDefinitionModel
{
    public required string Key { get; init; }

    public required ImmutableArray<RegionExitDefinitionModel> Exits { get; init; }

    public required ImmutableArray<LocationDefinitionModel> Locations { get; init; }

    public virtual bool Equals(RegionDefinitionModel? other)
    {
        return
            other is not null &&
            EqualityContract == other.EqualityContract &&
            Key == other.Key &&
            Exits.SequenceEqual(other.Exits) &&
            Locations.SequenceEqual(other.Locations);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(
            EqualityContract,
            Key,
            Exits.Length,
            Locations.Length
        );
    }
}

public sealed record RegionExitDefinitionModel
{
    public required string RegionKey { get; init; }

    public RegionDefinitionModel Region => GameDefinitions.Instance.AllRegions[RegionKey];

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
    public required GameRequirement Requirement { get; init; }

    public static LandmarkRegionDefinitionModel DeserializeFrom(string key, YamlMappingNode map, ItemDefinitionsModel items)
    {
        YamlNode[] exits = map.TryGetValue("exits", out YamlNode[]? exitsOrNull) ? exitsOrNull : [];
        GameRequirement requirement = GameRequirement.DeserializeFrom(map["requires"]);
        return new()
        {
            Key = key,
            Requirement = requirement,
            Exits = [.. exits.Select(RegionExitDefinitionModel.DeserializeFrom)],
            Locations =
            [
                new()
                {
                    Key = LocationKey.For(key),
                    Name = map["name"].To<string>(),
                    FlavorText = map.TryGetValue("flavor_text", out string? flavorText) ? flavorText : null,
                    UnrandomizedItem = items.ProgressionItems.GetValueOrDefault(map["unrandomized_item"].To<string>()),
                    AbilityCheckDC = map.TryGetValue("ability_check_dc", out int abilityCheckDC) ? abilityCheckDC : 1,
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

        // this mapping **intentionally** restarts counting from 0 for every filler region. trap
        // items are only added by Archipelago to take the place of filler items. to make the most
        // of it, that means we will have more locations whose unrandomized item is "filler" than we
        // have actual "filler" items. in a perfect world, the former count is equal to the latter
        // count plus the number of "trap" items in the pool.
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

    public required int AbilityCheckDC { get; init; }

    public required ItemDefinitionModel? UnrandomizedItem { get; init; }

    public required bool RewardIsFixed { get; init; }

    public RegionDefinitionModel Region => GameDefinitions.Instance.AllRegions[Key.RegionKey];

    public LocationDefinitionModel NextLocationTowards(LocationDefinitionModel target, GameState state) =>
        this.EnumerateReachableLocationsByDistance(state, true, false)
            .FirstOrDefault(tup => tup.Location == target)
            .Path
            ?.FirstOrDefault()
        ?? this;

    public LocationDefinitionModel NextOpenLocationTowards(LocationDefinitionModel target, GameState state) =>
        this.EnumerateReachableLocationsByDistance(state, false, true)
            .FirstOrDefault(tup => tup.Location == target)
            .Path
            ?.FirstOrDefault()
        ?? this;

    public IEnumerable<(LocationDefinitionModel Location, ImmutableList<LocationDefinitionModel> Path)> EnumerateReachableLocationsByDistance(GameState state)
    {
        return this.EnumerateReachableLocationsByDistance(state, false, false);
    }

    private IEnumerable<(LocationDefinitionModel Location, ImmutableList<LocationDefinitionModel> Path)> EnumerateReachableLocationsByDistance(GameState state, bool collect, bool onlyOpen)
    {
        Dictionary<string, bool> testedRegions = new() { [this.Key.RegionKey] = true };
        HashSet<LocationKey> testedLocations = [this.Key];
        FrozenSet<string>? allowedLandmarks = null;
        if (onlyOpen)
        {
            allowedLandmarks = state.CheckedLocations
                .DistinctBy(l => l.Key.RegionKey)
                .Select(l => l.Region)
                .OfType<LandmarkRegionDefinitionModel>()
                .Select(r => r.Key)
                .ToFrozenSet();
        }

        Queue<(LocationDefinitionModel Location, ImmutableList<LocationDefinitionModel> Path, ImmutableList<ItemDefinitionModel> ReceivedItems)> q = new([(this, [], state.ReceivedItems.InReceivedOrder)]);
        while (q.TryDequeue(out (LocationDefinitionModel Location, ImmutableList<LocationDefinitionModel> Path, ImmutableList<ItemDefinitionModel> ReceivedItems) curr))
        {
            yield return (curr.Location, curr.Path);
            foreach (LocationDefinitionModel nxt in GameDefinitions.Instance.ConnectedLocations[curr.Location])
            {
                if (!testedLocations.Add(nxt.Key) || !RegionIsOpen(nxt.Key.RegionKey, curr.ReceivedItems))
                {
                    continue;
                }

                ImmutableList<ItemDefinitionModel> receivedItems = curr.ReceivedItems;
                if (nxt.RewardIsFixed && collect)
                {
                    receivedItems = receivedItems.Add(nxt.UnrandomizedItem!);
                }

                q.Enqueue((nxt, curr.Path.Add(nxt), receivedItems));
            }
        }

        bool RegionIsOpen(string regionKey, ImmutableList<ItemDefinitionModel> receivedItems)
        {
            ref bool result = ref CollectionsMarshal.GetValueRefOrAddDefault(testedRegions, regionKey, out bool existed);
            if (!existed)
            {
                result = (!GameDefinitions.Instance.LandmarkRegions.TryGetValue(regionKey, out LandmarkRegionDefinitionModel? landmark)) ||
                         (allowedLandmarks?.Contains(landmark.Key) != false &&
                          landmark.Requirement.Satisfied(receivedItems));
            }

            return result;
        }
    }

    public bool TryCheck(ref GameState state)
    {
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

        if (GameState.NextD20(ref state) + state.DiceModifier + extraDiceModifier < this.AbilityCheckDC)
        {
            return false;
        }

        state = state with { CheckedLocations = state.CheckedLocations.Add(this) };
        return true;
    }
}

public sealed record ReceivedItems
{
    public GameDefinitions GameDefinitions => GameDefinitions.Instance;

    public required ImmutableList<ItemDefinitionModel> InReceivedOrder
    {
        get;
        init
        {
            field = value;
            AsFrozenSet = [.. value];
            RatCount = value.Sum(v => v.RatCount.GetValueOrDefault());
        }
    }

    public int Count => InReceivedOrder.Count;

    public FrozenSet<ItemDefinitionModel> AsFrozenSet { get; private init; } = [];

    public int RatCount { get; private init; }

    public bool Equals(ReceivedItems? other)
    {
        return
            other is not null &&
            InReceivedOrder.SequenceEqual(other.InReceivedOrder);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(
            InReceivedOrder.Count
        );
    }
}

public abstract record GameRequirement
{
    public virtual bool Satisfied(ReceivedItems receivedItems)
    {
        return true;
    }

    public virtual bool Satisfied(ImmutableList<ItemDefinitionModel> receivedItems)
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

    public override bool Satisfied(ReceivedItems receivedItems)
    {
        foreach (GameRequirement child in Children)
        {
            if (!child.Satisfied(receivedItems))
            {
                return false;
            }
        }

        return true;
    }

    public override bool Satisfied(ImmutableList<ItemDefinitionModel> receivedItems)
    {
        foreach (GameRequirement child in Children)
        {
            if (!child.Satisfied(receivedItems))
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

    public override bool Satisfied(ReceivedItems receivedItems)
    {
        foreach (GameRequirement child in Children)
        {
            if (child.Satisfied(receivedItems))
            {
                return true;
            }
        }

        return false;
    }

    public override bool Satisfied(ImmutableList<ItemDefinitionModel> receivedItems)
    {
        foreach (GameRequirement child in Children)
        {
            if (child.Satisfied(receivedItems))
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

    public override bool Satisfied(ReceivedItems receivedItems)
    {
        bool one = false;
        foreach (GameRequirement child in Children)
        {
            if (!child.Satisfied(receivedItems))
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

    public override bool Satisfied(ImmutableList<ItemDefinitionModel> receivedItems)
    {
        bool one = false;
        foreach (GameRequirement child in Children)
        {
            if (!child.Satisfied(receivedItems))
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

    public bool Equals(AnyTwoChildrenGameRequirement? other)
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

    public override bool Satisfied(ReceivedItems receivedItems)
    {
        return receivedItems.RatCount >= RatCount;
    }

    public override bool Satisfied(ImmutableList<ItemDefinitionModel> receivedItems)
    {
        return receivedItems.Sum(i => i.RatCount.GetValueOrDefault()) >= RatCount;
    }
}

public sealed record ReceivedItemRequirement : GameRequirement
{
    public required string ItemKey { get; init; }

    public static new ReceivedItemRequirement DeserializeFrom(YamlNode node)
    {
        return new() { ItemKey = node.To<string>() };
    }

    public override bool Satisfied(ReceivedItems receivedItems)
    {
        return receivedItems.AsFrozenSet.Contains(receivedItems.GameDefinitions.ProgressionItems[ItemKey]);
    }

    public override bool Satisfied(ImmutableList<ItemDefinitionModel> receivedItems)
    {
        return receivedItems.Contains(GameDefinitions.Instance.ProgressionItems[ItemKey]);
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
