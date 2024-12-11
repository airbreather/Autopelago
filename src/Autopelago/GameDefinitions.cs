using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

using YamlDotNet.RepresentationModel;
using YamlDotNet.Serialization;

namespace Autopelago;

public sealed class GameDefinitions
{
    public static readonly GameDefinitions Instance = LoadFromEmbeddedResource();

    private GameDefinitions()
    {
    }

    // EVERYTHING must be derived from these first few All* arrays. this is important to minimize
    // how much work #52 will be!
    public required ImmutableArray<ItemDefinitionModel> AllItems { get; init; }

    public required ImmutableArray<LocationDefinitionModel> AllLocations { get; init; }

    public required ImmutableArray<RegionDefinitionModel> AllRegions { get; init; }

    // what remains are STRICTLY pre-computed indexes into the above arrays, solely for the sake of
    // speeding up computations. especially, PLEASE don't put anything here that's derived using
    // magic strings! also, please do not add anything like a RegionLocationKey --> LocationKey
    // (or reversed) hash mapping that can be calculated by following pointers through these arrays:
    // the same ergonomic benefits can come from making wrappers for those arrays.
    public required LocationKey StartLocation { get; init; }

    public required LocationKey GoalLocation { get; init; }

    public required ImmutableArray<ItemKey> ItemsWithNonzeroRatCounts { get; init; }

    public required FrozenDictionary<string, LocationKey> LocationsByName { get; init; }

    public required FrozenDictionary<string, LocationKey> LocationsByNameCaseInsensitive { get; init; }

    public required FrozenDictionary<ArchipelagoItemFlags, FrozenSet<LocationKey>> UnrandomizedSpoilerData { get; init; }

    public ref readonly ItemDefinitionModel this[ItemKey key] => ref AllItems.AsSpan()[key.N];

    public ref readonly LocationDefinitionModel this[LocationKey key] => ref AllLocations.AsSpan()[key.N];

    public ref readonly RegionDefinitionModel this[RegionKey key] => ref AllRegions.AsSpan()[key.N];

    public ref readonly LocationDefinitionModel this[RegionLocationKey key] => ref AllLocations.AsSpan()[AllRegions[key.Region.N].Locations[key.N].N];

    public RegionKeyHelper RegionKey => new(AllLocations);

    public RegionLocationKey StartRegionLocation => this[StartLocation].RegionLocationKey;

    public RegionLocationKey GoalRegionLocation => this[GoalLocation].RegionLocationKey;

    public RegionKey StartRegion => StartRegionLocation.Region;

    public RegionKey GoalRegion => GoalRegionLocation.Region;

    private static GameDefinitions LoadFromEmbeddedResource()
    {
        using Stream yamlStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("AutopelagoDefinitions.yml")!;
        using StreamReader yamlReader = new(yamlStream, Encoding.UTF8);
        YamlMappingNode map = new DeserializerBuilder().Build().Deserialize<YamlMappingNode>(yamlReader);

        YamlMappingNode itemsMap = (YamlMappingNode)map["items"];
        YamlMappingNode regionsMap = (YamlMappingNode)map["regions"];

        ItemDefinitionsModel items = ItemDefinitionsModel.DeserializeFrom(itemsMap);
        RegionDefinitionsModel regions = RegionDefinitionsModel.DeserializeFrom(regionsMap, items);
        return new()
        {
            AllItems = items.AllItems,
            AllLocations = regions.AllLocations,
            AllRegions = regions.AllRegions,

            StartLocation = regions.AllLocations.First(l => l.Connected.Backward.IsEmpty).Key,
            GoalLocation = regions.AllLocations
                .First(l =>
                    l.Connected.Forward.IsEmpty &&
                    l.Connected.Backward.All(pl =>
                        regions.AllLocations[pl.N].Connected.Forward.Length == 1)
                ).Key,
            ItemsWithNonzeroRatCounts = items.ItemsWithNonzeroRatCounts,
            LocationsByName = regions.AllLocations.ToFrozenDictionary(l => l.Name, l => l.Key),
            LocationsByNameCaseInsensitive = regions.AllLocations.ToFrozenDictionary(l => l.Name, l => l.Key, StringComparer.InvariantCultureIgnoreCase),
            UnrandomizedSpoilerData = regions.AllLocations
                .GroupBy(l => items.AllItems[l.UnrandomizedItem.N].ArchipelagoFlags, l => l.Key)
                .ToFrozenDictionary(grp => grp.Key, grp => grp.ToFrozenSet()),
        };
    }

    public readonly struct RegionKeyHelper
    {
        private readonly ImmutableArray<LocationDefinitionModel> _allLocations;

        internal RegionKeyHelper(ImmutableArray<LocationDefinitionModel> allLocations)
        {
            _allLocations = allLocations;
        }

        public RegionKey this[LocationKey key] => _allLocations[key.N].RegionLocationKey.Region;
    }
}

public sealed class ItemDefinitionsModel
{
    private static readonly FrozenDictionary<string, ArchipelagoItemFlags> s_bulkItemFlagsLookup = new Dictionary<string, ArchipelagoItemFlags>
    {
        ["useful_nonprogression"] = ArchipelagoItemFlags.ImportantNonAdvancement,
        ["trap"] = ArchipelagoItemFlags.Trap,
        ["filler"] = ArchipelagoItemFlags.None,
    }.ToFrozenDictionary();

    public required ImmutableArray<ItemDefinitionModel> AllItems { get; init; }

    public required FrozenDictionary<string, ItemKey> ProgressionItemsByYamlKey { get; init; }

    public required ImmutableArray<int> RatCounts { get; init; }

    public required ImmutableArray<ItemKey> ItemsWithNonzeroRatCounts { get; init; }

    public static ItemDefinitionsModel DeserializeFrom(YamlMappingNode itemsMap)
    {
        List<ItemDefinitionModel> allItems = [];
        Dictionary<string, ItemKey> keyedItems = [];

        foreach ((YamlNode keyNode, YamlNode valueNode) in itemsMap)
        {
            string key = keyNode.To<string>();
            switch (key)
            {
                case "rats":
                    foreach ((string ratKey, ItemDefinitionModel ratItem) in DeserializeRatsFrom((ushort)allItems.Count, (YamlMappingNode)valueNode))
                    {
                        allItems.Add(ratItem);
                        keyedItems.Add(ratKey, ratItem.Key);
                    }

                    break;

                case string when s_bulkItemFlagsLookup.TryGetValue(key, out ArchipelagoItemFlags flags):
                    foreach (ItemDefinitionModel item in DeserializeBulkFrom((ushort)allItems.Count, (YamlSequenceNode)valueNode, flags))
                    {
                        allItems.Add(item);
                    }

                    break;

                default:
                    // for simplicity's sake (and because this is getting a bit old), we expect only
                    // progression items to be given keys in the YAML file.
                    allItems.Add(ItemDefinitionModel.DeserializeFrom((ushort)allItems.Count, valueNode, ArchipelagoItemFlags.LogicalAdvancement));
                    keyedItems.Add(key, allItems[^1].Key);
                    break;
            }
        }

        _ = checked((ushort)allItems.Count);
        return new()
        {
            AllItems = [.. allItems],
            ProgressionItemsByYamlKey = keyedItems.ToFrozenDictionary(),
            RatCounts = [.. allItems.Select(i => i.RatCount)],
            ItemsWithNonzeroRatCounts = [.. allItems.Where(i => i.RatCount != 0).Select(i => i.Key)],
        };
    }

    private static IEnumerable<KeyValuePair<string, ItemDefinitionModel>> DeserializeRatsFrom(ushort nextItemNum, YamlMappingNode map)
    {
        // at the time of writing, all rats (in this mapping) are considered logical advancements.
        ArchipelagoItemFlags archipelagoFlags = ArchipelagoItemFlags.LogicalAdvancement;
        foreach ((YamlNode keyNode, YamlNode valueNode) in map)
        {
            string key = keyNode.To<string>();
            ItemDefinitionModel value = ItemDefinitionModel.DeserializeFrom(nextItemNum, valueNode, archipelagoFlags, defaultRatCount: 1);
            yield return KeyValuePair.Create(key, value);
            nextItemNum++;
        }
    }

    private static IEnumerable<ItemDefinitionModel> DeserializeBulkFrom(ushort nextItemNum, YamlSequenceNode seq, ArchipelagoItemFlags flags, string? associatedGame = null)
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
                    foreach (ItemDefinitionModel gameSpecificItem in DeserializeBulkFrom(nextItemNum, gameValue, flags, gameKey))
                    {
                        yield return gameSpecificItem;
                        ++nextItemNum;
                    }
                }

                continue;
            }

            yield return ItemDefinitionModel.DeserializeFrom(nextItemNum, valueNode, flags, associatedGame: associatedGame);
            ++nextItemNum;
        }
    }
}

[StructLayout(LayoutKind.Auto)]
public readonly record struct ItemDefinitionModel
{
    public required ItemKey Key { get; init; }

    public required string? AssociatedGame { get; init; }

    public required string Name { get; init; }

    public required ArchipelagoItemFlags ArchipelagoFlags { get; init; }

    public ImmutableArray<string> AurasGranted { get; init; }

    public string? FlavorText { get; init; }

    public int RatCount { get; init; }

    public static ItemDefinitionModel DeserializeFrom(ushort itemNum, YamlNode node, ArchipelagoItemFlags archipelagoFlags, string? associatedGame = null, int defaultRatCount = 0)
    {
        return node switch
        {
            YamlScalarNode scalar => DeserializeFrom(itemNum, scalar, archipelagoFlags, associatedGame, defaultRatCount),
            YamlSequenceNode sequence => DeserializeFrom(itemNum, sequence, archipelagoFlags, associatedGame, defaultRatCount),
            YamlMappingNode map => DeserializeFrom(itemNum, map, archipelagoFlags, associatedGame, defaultRatCount),
            _ => throw new InvalidDataException("Bad node type"),
        };
    }

    private static ItemDefinitionModel DeserializeFrom(ushort itemNum, YamlScalarNode scalar, ArchipelagoItemFlags archipelagoFlags, string? associatedGame = null, int defaultRatCount = 0)
    {
        return new()
        {
            Key = new() { N = itemNum },
            AssociatedGame = associatedGame,
            Name = scalar.Value!,
            ArchipelagoFlags = archipelagoFlags,
            RatCount = defaultRatCount,
        };
    }

    private static ItemDefinitionModel DeserializeFrom(ushort itemNum, YamlSequenceNode sequence, ArchipelagoItemFlags archipelagoFlags, string? associatedGame = null, int defaultRatCount = 0)
    {
        if (sequence.Children is not [YamlScalarNode { Value: string itemName }, YamlSequenceNode aurasGranted])
        {
            throw new InvalidDataException("Bad format.");
        }

        return new()
        {
            Key = new() { N = itemNum },
            AssociatedGame = associatedGame,
            Name = itemName,
            ArchipelagoFlags = archipelagoFlags,
            AurasGranted = [.. aurasGranted.Select(a => ((YamlScalarNode)a).Value!)],
            RatCount = defaultRatCount,
        };
    }

    private static ItemDefinitionModel DeserializeFrom(ushort itemNum, YamlMappingNode map, ArchipelagoItemFlags archipelagoFlags, string? associatedGame = null, int defaultRatCount = 0)
    {
        string? name = null;
        int ratCount = defaultRatCount;
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
            Key = new() { N = itemNum },
            AssociatedGame = associatedGame,
            Name = name ?? throw new InvalidDataException("name is required."),
            ArchipelagoFlags = archipelagoFlags,
            AurasGranted = aurasGranted,
            RatCount = ratCount,
            FlavorText = flavorText,
        };
    }

    public bool Equals(ItemDefinitionModel other)
    {
        return Key == other.Key;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(
            Key
        );
    }
}

public readonly struct Connected<T>
{
    public required ImmutableArray<T> Forward { get; init; }

    public required ImmutableArray<T> Backward { get; init; }
}

public sealed class RegionDefinitionsModel
{
    public required ImmutableArray<RegionDefinitionModel> AllRegions { get; init; }

    public required ImmutableArray<LocationDefinitionModel> AllLocations { get; init; }

    public static RegionDefinitionsModel DeserializeFrom(YamlMappingNode map, ItemDefinitionsModel items)
    {
        List<RegionDefinitionModel> allRegions = [];
        List<LocationDefinitionModel> allLocations = [];
        List<string[]> exits = [];
        Dictionary<string, RegionKey> regionLookup = new();
        foreach ((YamlNode keyNode, YamlNode valueNode) in (YamlMappingNode)map["landmarks"])
        {
            string stringKey = keyNode.To<string>();
            RegionKey key = new() { N = checked((byte)allRegions.Count) };
            LocationDefinitionModel location = LandmarkRegionDefinitionModel.DeserializeFrom(key, (YamlMappingNode)valueNode, items, out LandmarkRegionDefinitionModel region, out string[] currExits);
            allRegions.Add(region);
            allLocations.Add(location);
            exits.Add(currExits);
            regionLookup.Add(stringKey, key);
        }

        foreach ((YamlNode keyNode, YamlNode valueNode) in (YamlMappingNode)map["fillers"])
        {
            string stringKey = keyNode.To<string>();
            RegionKey key = new() { N = checked((byte)allRegions.Count) };
            ImmutableArray<LocationDefinitionModel> locations = FillerRegionDefinitionModel.DeserializeFrom(key, (ushort)allLocations.Count, (YamlMappingNode)valueNode, items, out FillerRegionDefinitionModel region, out string[] currExits);
            allLocations.AddRange(locations);
            allRegions.Add(region);
            exits.Add(currExits);
            regionLookup.Add(stringKey, key);
        }

        (List<RegionKey> Forward, List<RegionKey> Backward)[] connectedRegions = new (List<RegionKey> Forward, List<RegionKey> Backward)[checked((byte)allRegions.Count)];
        for (int i = 0; i < connectedRegions.Length; i++)
        {
            connectedRegions[i] = ([], []);
        }

        (List<LocationKey> Forward, List<LocationKey> Backward)[] connectedLocations = new (List<LocationKey> Forward, List<LocationKey> Backward)[checked((ushort)allLocations.Count)];
        for (int i = 0; i < connectedLocations.Length; i++)
        {
            connectedLocations[i] = ([], []);
        }

        Queue<(LocationKey? Prev, RegionKey Curr)> regionsQueue = [];
        regionsQueue.Enqueue((null, regionLookup["Menu"]));
        while (regionsQueue.TryDequeue(out (LocationKey? Prev, RegionKey Curr) tup))
        {
            (LocationKey? prevOrNull, RegionKey curr) = tup;
            foreach (LocationKey next in allRegions[curr.N].Locations)
            {
                if (prevOrNull is LocationKey prev)
                {
                    connectedLocations[prev.N].Forward.Add(next);
                    connectedLocations[next.N].Backward.Add(prev);
                }

                prevOrNull = next;
            }

            foreach (string nextStr in exits[curr.N])
            {
                RegionKey next = regionLookup[nextStr];
                regionsQueue.Enqueue((prevOrNull, next));
                connectedRegions[curr.N].Forward.Add(next);
                connectedRegions[next.N].Backward.Add(curr);
            }
        }

        for (int i = 0; i < allRegions.Count; i++)
        {
            (List<RegionKey> forward, List<RegionKey> backward) = connectedRegions[i];
            allRegions[i] = allRegions[i] with
            {
                Connected = new()
                {
                    Forward = [.. forward.Distinct().OrderBy(k => k.N)],
                    Backward = [.. backward.Distinct().OrderBy(k => k.N)],
                },
            };
        }

        for (int i = 0; i < allLocations.Count; i++)
        {
            (List<LocationKey> forward, List<LocationKey> backward) = connectedLocations[i];
            allLocations[i] = allLocations[i] with
            {
                Connected = new()
                {
                    Forward = [.. forward.Distinct().OrderBy(k => k.N)],
                    Backward = [.. backward.Distinct().OrderBy(k => k.N)],
                },
                ConnectedAsRegionLocations = new()
                {
                    Forward = [.. forward.Distinct().Select(l => allLocations[l.N].RegionLocationKey).OrderBy(l => l.Region.N).ThenBy(l => l.N)],
                    Backward = [.. backward.Distinct().Select(l => allLocations[l.N].RegionLocationKey).OrderBy(l => l.Region.N).ThenBy(l => l.N)],
                },
            };
        }

        return new()
        {
            AllRegions = [.. allRegions],
            AllLocations = [.. allLocations],
        };
    }
}

public abstract record RegionDefinitionModel
{
    public required RegionKey Key { get; init; }

    public required int AbilityCheckDC { get; init; }

    public required Connected<RegionKey> Connected { get; init; }

    public abstract ImmutableArray<LocationKey> Locations { get; }

    public abstract bool Equals(RegionDefinitionModel? other);

    public abstract override int GetHashCode();
}

public sealed record LandmarkRegionDefinitionModel : RegionDefinitionModel
{
    public LocationKey Location => new() { N = Key.N };

    public override ImmutableArray<LocationKey> Locations => [Location];

    public required GameRequirement Requirement { get; init; }

    public static LocationDefinitionModel DeserializeFrom(RegionKey key, YamlMappingNode map, ItemDefinitionsModel items, out LandmarkRegionDefinitionModel region, out string[] exits)
    {
        exits = map.TryGetValue("exits", out string[]? exitsOrNull) ? exitsOrNull : [];
        GameRequirement requirement = GameRequirement.DeserializeFrom(map["requires"], items);
        int abilityCheckDC = map["ability_check_dc"].To<int>();
        region = new()
        {
            Key = key,
            Requirement = requirement,
            AbilityCheckDC = abilityCheckDC,
            Connected = new() { Forward = [], Backward = [] }, // we'll have to deal with this later.
        };

        return new()
        {
            Key = new() { N = key.N }, // landmarks come first, same order as locations.
            RegionLocationKey = new() { Region = key, N = 0 },
            Name = map["name"].To<string>(),
            FlavorText = map.TryGetValue("flavor_text", out string? flavorText) ? flavorText : null,
            UnrandomizedItem = items.ProgressionItemsByYamlKey.GetValueOrDefault(map["unrandomized_item"].To<string>()),
            AbilityCheckDC = abilityCheckDC,
            RewardIsFixed = map.TryGetValue("reward_is_fixed", out bool rewardIsFixed) && rewardIsFixed,
            Connected = new() { Forward = [], Backward = [] }, // we'll have to deal with this later.
            ConnectedAsRegionLocations = new() { Forward = [], Backward = [] }, // we'll have to deal with this later.
        };
    }

    public bool Equals(LandmarkRegionDefinitionModel? other)
    {
        return ReferenceEquals(this, other) || (other is not null && Key == other.Key);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(
            Key
        );
    }
}

public sealed record FillerRegionDefinitionModel : RegionDefinitionModel
{
    public override ImmutableArray<LocationKey> Locations => FillerLocations;

    public required ImmutableArray<LocationKey> FillerLocations { get; init; }

    public static ImmutableArray<LocationDefinitionModel> DeserializeFrom(RegionKey regionKey, ushort firstLocationNum, YamlMappingNode map, ItemDefinitionsModel items, out FillerRegionDefinitionModel region, out string[] exits)
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
        List<ItemKey> unrandomizedItems = [];
        foreach ((YamlNode keyNode, YamlNode valueNode) in (YamlMappingNode)map["unrandomized_items"])
        {
            switch (keyNode.To<string>())
            {
                case "key":
                    foreach (YamlNode itemRefNode in (YamlSequenceNode)valueNode)
                    {
                        ItemRefModel itemRef = ItemRefModel.DeserializeFrom(itemRefNode);
                        ItemKey itemKey = items.ProgressionItemsByYamlKey[itemRef.Key];
                        for (int i = 0; i < itemRef.ItemCount; i++)
                        {
                            unrandomizedItems.Add(itemKey);
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
                        unrandomizedItems.Add(grp.ElementAt(nextSrc++).Key);
                    }

                    break;
            }
        }

        exits = map.TryGetValue("exits", out string[]? exitsOrNull) ? exitsOrNull : [];
        int abilityCheckDC = map.Children.TryGetValue("ability_check_dc", out YamlNode? abilityCheckDCNode)
            ? abilityCheckDCNode.To<int>()
            : -1;
        LocationDefinitionModel[] locations = new LocationDefinitionModel[checked((byte)unrandomizedItems.Count)];
        for (byte i = 0; i != locations.Length; i++)
        {
            locations[i] = new()
            {
                Key = new() { N = (ushort)(firstLocationNum + i) },
                RegionLocationKey = RegionLocationKey.For(regionKey, i),
                Name = nameTemplate.Replace("{n}", $"{i + 1}"),
                AbilityCheckDC = abilityCheckDC,
                UnrandomizedItem = unrandomizedItems[i],
                RewardIsFixed = false,
                Connected = new() { Forward = [], Backward = [] }, // we'll have to deal with this later.
                ConnectedAsRegionLocations = new() { Forward = [], Backward = [] }, // we'll have to deal with this later.
            };
        }

        region = new()
        {
            Key = regionKey,
            AbilityCheckDC = abilityCheckDC,
            Connected = new() { Forward = [], Backward = [] }, // we'll have to deal with this later.
            FillerLocations = ImmutableCollectionsMarshal.AsImmutableArray(Array.ConvertAll(locations, l => l.Key)),
        };

        return ImmutableCollectionsMarshal.AsImmutableArray(locations);
    }

    public bool Equals(FillerRegionDefinitionModel? other)
    {
        return ReferenceEquals(this, other) || (other is not null && Key == other.Key);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(
            Key
        );
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

[StructLayout(LayoutKind.Auto)]
public readonly record struct LocationDefinitionModel
{
    public required LocationKey Key { get; init; }

    public required RegionLocationKey RegionLocationKey { get; init; }

    public required string Name { get; init; }

    public string? FlavorText { get; init; }

    public required int AbilityCheckDC { get; init; }

    public required ItemKey UnrandomizedItem { get; init; }

    public required bool RewardIsFixed { get; init; }

    public required Connected<LocationKey> Connected { get; init; }

    public required Connected<RegionLocationKey> ConnectedAsRegionLocations { get; init; }

    public bool Equals(LocationDefinitionModel other)
    {
        return Key == other.Key;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(
            Key
        );
    }
}

public abstract record GameRequirement
{
    public virtual bool Satisfied(ReadOnlyBitArray receivedItems)
    {
        return true;
    }

    public static GameRequirement DeserializeFrom(YamlNode node, ItemDefinitionsModel lookup)
    {
        if (node is not YamlMappingNode { Children: [(YamlScalarNode keyNode, YamlNode valueNode)] })
        {
            throw new InvalidDataException("Bad node type");
        }

        return keyNode.Value switch
        {
            "rat_count" => RatCountRequirement.DeserializeFrom(valueNode, lookup),
            "item" => ReceivedItemRequirement.DeserializeFrom(valueNode, lookup),
            "any" => AnyChildGameRequirement.DeserializeFrom(valueNode, lookup),
            "any_two" => AnyTwoChildrenGameRequirement.DeserializeFrom(valueNode, lookup),
            "all" => AllChildrenGameRequirement.DeserializeFrom(valueNode, lookup),
            _ => throw new InvalidDataException($"Unrecognized requirement: {keyNode.Value}"),
        };
    }
}

public sealed record AllChildrenGameRequirement : GameRequirement
{
    public required ImmutableArray<GameRequirement> Children { get; init; }

    public static new AllChildrenGameRequirement DeserializeFrom(YamlNode node, ItemDefinitionsModel lookup)
    {
        return new() { Children = [.. ((YamlSequenceNode)node).Select(n => GameRequirement.DeserializeFrom(n, lookup))] };
    }

    public override bool Satisfied(ReadOnlyBitArray receivedItems)
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

    public static new AnyChildGameRequirement DeserializeFrom(YamlNode node, ItemDefinitionsModel lookup)
    {
        return new() { Children = [.. ((YamlSequenceNode)node).Select(n => GameRequirement.DeserializeFrom(n, lookup))] };
    }

    public override bool Satisfied(ReadOnlyBitArray receivedItems)
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

    public static new AnyTwoChildrenGameRequirement DeserializeFrom(YamlNode node, ItemDefinitionsModel lookup)
    {
        return new() { Children = [.. ((YamlSequenceNode)node).Select(n => GameRequirement.DeserializeFrom(n, lookup))] };
    }

    public override bool Satisfied(ReadOnlyBitArray receivedItems)
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

    private ImmutableArray<int> RatCounts { get; init; }

    private ImmutableArray<ItemKey> ItemsWithNonzeroRatCounts { get; init; }

    public static new RatCountRequirement DeserializeFrom(YamlNode node, ItemDefinitionsModel lookup)
    {
        return new()
        {
            RatCount = node.To<int>(),
            RatCounts = lookup.RatCounts,
            ItemsWithNonzeroRatCounts = lookup.ItemsWithNonzeroRatCounts,
        };
    }

    public override bool Satisfied(ReadOnlyBitArray receivedItems)
    {
        int stillNeeded = RatCount;
        foreach (ItemKey item in ItemsWithNonzeroRatCounts)
        {
            if (!receivedItems[item.N])
            {
                continue;
            }

            stillNeeded -= RatCounts[item.N];
            if (stillNeeded <= 0)
            {
                return true;
            }
        }

        return false;
    }
}

public sealed record ReceivedItemRequirement : GameRequirement
{
    public required ItemKey ItemKey { get; init; }

    public static new ReceivedItemRequirement DeserializeFrom(YamlNode node, ItemDefinitionsModel lookup)
    {
        return new() { ItemKey = lookup.ProgressionItemsByYamlKey[node.To<string>()] };
    }

    public override bool Satisfied(ReadOnlyBitArray receivedItems)
    {
        return receivedItems[ItemKey.N];
    }
}
