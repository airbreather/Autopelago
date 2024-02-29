using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Data;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

using ArchipelagoClientDotNet;

using YamlDotNet.RepresentationModel;
using YamlDotNet.Serialization;

public sealed record GameDefinitionsModel
{
    public required ItemDefinitionsModel Items { get; init; }

    public required RegionDefinitionsModel Regions { get; init; }

    public static GameDefinitionsModel LoadFromEmbeddedResource()
    {
        using Stream yamlStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("AutopelagoDefinitions.yml")!;
        using StreamReader yamlReader = new(yamlStream, Encoding.UTF8);
        YamlMappingNode map = new DeserializerBuilder().Build().Deserialize<YamlMappingNode>(yamlReader);

        YamlMappingNode itemsMap = (YamlMappingNode)map["items"];
        YamlMappingNode regionsMap = (YamlMappingNode)map["regions"];

        ItemDefinitionsModel items = ItemDefinitionsModel.DeserializeFrom(itemsMap, regionsMap);
        RegionDefinitionsModel regions = RegionDefinitionsModel.DeserializeFrom(regionsMap, items);
        return new()
        {
            Items = items,
            Regions = regions,
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

    public required ItemDefinitionModel Goal { get; init; }

    public required ItemDefinitionModel NormalRat { get; init; }

    public required ImmutableArray<ItemDefinitionModel> AllItems { get; init; }

    public required FrozenDictionary<string, ItemDefinitionModel> ProgressionItems { get; init; }

    public static ItemDefinitionsModel DeserializeFrom(YamlMappingNode itemsMap, YamlMappingNode locationsMap)
    {
        ItemDefinitionModel? goal = null;
        ItemDefinitionModel? normalRat = null;
        List<ItemDefinitionModel> allItems = [];
        Dictionary<string, ItemDefinitionModel> keyedItems = [];
        HashSet<string> progressionItemKeysToValidate = [];

        foreach ((YamlNode keyNode, YamlNode valueNode) in itemsMap)
        {
            string key = ((YamlScalarNode)keyNode).Value!;
            switch (key)
            {
                case "rats":
                    foreach ((string ratKey, ItemDefinitionModel ratItem) in DeserializeRatsFrom((YamlMappingNode)valueNode))
                    {
                        allItems.Add(ratItem);
                        keyedItems.Add(ratKey, ratItem);
                    }

                    break;

                case string bulk when s_bulkItemFlagsLookup.TryGetValue(bulk, out ArchipelagoItemFlags flags):
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
                    if (key == "goal")
                    {
                        // no need for validation.
                        goal = allItems[^1];
                    }
                    else if (key == "normal_rat")
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

        if ((goal, normalRat) is not ({}, { RatCount: 1 }))
        {
            throw new InvalidDataException("'goal' and 'normal_rat' are required items (and 'normal_rat' needs to have a rat_count of 1).");
        }

        AllItemKeysVisitor itemKeysVisitor = new() { NeededItemsNotYetVisited = progressionItemKeysToValidate };
        locationsMap.Accept(itemKeysVisitor);
        if (progressionItemKeysToValidate.Count > 0)
        {
            throw new NotSupportedException($"All items with keys need to be progression items for now... these weren't found in 'locations': {string.Join(", ", progressionItemKeysToValidate)}");
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
            Goal = goal,
            NormalRat = normalRat,
            AllItems = [..allItems],
            ProgressionItems = keyedItems.ToFrozenDictionary(),
        };
    }

    private static IEnumerable<KeyValuePair<string, ItemDefinitionModel>> DeserializeRatsFrom(YamlMappingNode map)
    {
        // at the time of writing, all rats are considered logical advancements.
        ArchipelagoItemFlags archipelagoFlags = ArchipelagoItemFlags.LogicalAdvancement;
        foreach ((YamlNode keyNode, YamlNode valueNode) in map)
        {
            string key = ((YamlScalarNode)keyNode).Value!;
            ItemDefinitionModel value = ItemDefinitionModel.DeserializeFrom(valueNode, archipelagoFlags, defaultRatCount: 1);
            yield return KeyValuePair.Create(key, value);
        }
    }

    private static IEnumerable<ItemDefinitionModel> DeserializeBulkFrom(YamlSequenceNode seq, ArchipelagoItemFlags flags, string? associatedGame = null)
    {
        foreach (YamlNode valueNode in seq)
        {
            if (valueNode is YamlMappingNode valueMap)
            {
                if (!(associatedGame is null && valueMap.Children.TryGetValue("game_specific", out YamlNode? gameSpecificNode)))
                {
                    throw new InvalidDataException("Bad node type");
                }

                foreach ((YamlNode gameKeyNode, YamlNode gameValueNode) in (YamlMappingNode)gameSpecificNode)
                {
                    string gameKey = ((YamlScalarNode)gameKeyNode).Value!;
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

    public string? FlavorText { get; init; }

    public int? RatCount { get; init; }

    public static ItemDefinitionModel DeserializeFrom(YamlNode node, ArchipelagoItemFlags archipelagoFlags, string? associatedGame = null, int? defaultRatCount = null)
    {
        return node switch
        {
            YamlScalarNode scalar => DeserializeFrom(scalar, archipelagoFlags, associatedGame, defaultRatCount),
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

    private static ItemDefinitionModel DeserializeFrom(YamlMappingNode map, ArchipelagoItemFlags archipelagoFlags, string? associatedGame = null, int? defaultRatCount = null)
    {
        string? name = null;
        int? ratCount = defaultRatCount;
        string? flavorText = null;
        foreach ((YamlNode keyNode, YamlNode valueNode) in map)
        {
            string key = ((YamlScalarNode)keyNode).Value!;
            string value = ((YamlScalarNode)valueNode).Value!;
            switch (key)
            {
                case "name":
                    name = value;
                    break;

                case "rat_count":
                    ratCount = int.Parse(value);
                    break;

                case "flavor_text":
                    flavorText = value;
                    break;
            }
        }

        return new()
        {
            AssociatedGame = associatedGame,
            Name = name ?? throw new InvalidDataException("name is required."),
            ArchipelagoFlags = archipelagoFlags,
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
        Dictionary<string, RegionDefinitionModel> allRegions = [];

        Dictionary<string, LandmarkRegionDefinitionModel> landmarkRegions = [];
        foreach ((YamlNode keyNode, YamlNode valueNode) in (YamlMappingNode)map["landmarks"])
        {
            string key = ((YamlScalarNode)keyNode).Value!;
            LandmarkRegionDefinitionModel value = LandmarkRegionDefinitionModel.DeserializeFrom((YamlMappingNode)valueNode, items);
            landmarkRegions.Add(key, value);
            allRegions.Add(key, value);
        }

        Dictionary<string, FillerRegionDefinitionModel> fillerRegions = [];
        foreach ((YamlNode keyNode, YamlNode valueNode) in (YamlMappingNode)map["fillers"])
        {
            string key = ((YamlScalarNode)keyNode).Value!;
            FillerRegionDefinitionModel value = FillerRegionDefinitionModel.DeserializeFrom((YamlMappingNode)valueNode, items);
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
    public required ImmutableArray<string> Exits { get; init; }

    public required ImmutableArray<LocationDefinitionModel> Locations { get; init; }
}

public sealed record LandmarkRegionDefinitionModel : RegionDefinitionModel
{
    public static LandmarkRegionDefinitionModel DeserializeFrom(YamlMappingNode map, ItemDefinitionsModel items)
    {
        return new()
        {
            Exits = [..((YamlSequenceNode)map["exits"]).Select(n => ((YamlScalarNode)n).Value!)],
            Locations =
            [
                new()
                {
                    Name = ((YamlScalarNode)map["name"]).Value!,
                    UnrandomizedItem = items.ProgressionItems[((YamlScalarNode)map["unrandomized_item"]).Value!],
                    Requires = GameRequirement.DeserializeFrom(map["requires"]),
                },
            ],
        };
    }
}

public sealed record FillerRegionDefinitionModel : RegionDefinitionModel
{
    public static FillerRegionDefinitionModel DeserializeFrom(YamlMappingNode map, ItemDefinitionsModel items)
    {
        Dictionary<ArchipelagoItemFlags, string> keyMap = new()
        {
            [ArchipelagoItemFlags.None] = "filler",
            [ArchipelagoItemFlags.ImportantNonAdvancement] = "useful_nonprogression",
        };
        ILookup<string, ItemDefinitionModel> itemsLookup = items.AllItems.Where(item => keyMap.ContainsKey(item.ArchipelagoFlags)).ToLookup(item => keyMap[item.ArchipelagoFlags]);
        Dictionary<string, int> nextInGroup = [];
        string nameTemplate = ((YamlScalarNode)map["name_template"]).Value!;
        List<ItemDefinitionModel> unrandomizedItems = [];
        foreach ((YamlNode keyNode, YamlNode valueNode) in (YamlMappingNode)map["unrandomized_items"])
        {
            switch (((YamlScalarNode)keyNode).Value)
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

                case string key:
                    IEnumerable<ItemDefinitionModel> grp = itemsLookup[key];
                    int cnt = int.Parse(((YamlScalarNode)valueNode).Value!);
                    ref int nextSrc = ref CollectionsMarshal.GetValueRefOrAddDefault(nextInGroup, key, out _);
                    for (int i = 0; i < cnt; i++)
                    {
                        unrandomizedItems.Add(grp.ElementAt(nextSrc++));
                    }

                    break;
            }
        }

        GameRequirement eachRequires = GameRequirement.DeserializeFrom(map["each_requires"]);
        return new()
        {
            Exits = [..((YamlSequenceNode)map["exits"]).Select(n => ((YamlScalarNode)n).Value!)],
            Locations = [..unrandomizedItems.Select((item, n) => new LocationDefinitionModel()
            {
                Name = nameTemplate.Replace("{n}", $"{n + 1}"),
                Requires = eachRequires,
                UnrandomizedItem = item,
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
                Key = ((YamlScalarNode)map["item"]).Value!,
                ItemCount = map.Children.TryGetValue("count", out YamlNode? countNode) ? int.Parse(((YamlScalarNode)countNode).Value!) : 1,
            };
        }
    }
}

public sealed record LocationDefinitionModel
{
    public required string Name { get; init; }

    public required GameRequirement Requires { get; init; }

    public required ItemDefinitionModel UnrandomizedItem { get; init; }
}

public abstract record GameRequirement
{
    public static readonly GameRequirement AlwaysSatisfied = new AllGameRequirement { Requirements = [] };

    public abstract bool Satisfied(Game.State state);

    public static GameRequirement DeserializeFrom(YamlNode node)
    {
        // special case: there's an implicit "all:" at the top, which means that we might be coming
        // in partway through that. it's easy to test for this.
        if (node is YamlSequenceNode seq)
        {
            return new AllGameRequirement
            {
                Requirements = [..seq.Select(DeserializeFrom)],
            };
        }

        if (node is not YamlMappingNode { Children: [(YamlScalarNode keyNode, YamlNode valueNode)] } map)
        {
            throw new InvalidDataException("Bad node type");
        }

        return keyNode.Value switch
        {
            "ability_check_with_dc" => new AbilityCheckRequirement { DifficultyClass = int.Parse(((YamlScalarNode)valueNode).Value!) },
            "rat_count" => new RatCountRequirement { RatCount = int.Parse(((YamlScalarNode)valueNode).Value!) },
            "location" => new CheckedLocationRequirement { LocationKey = ((YamlScalarNode)valueNode).Value! },
            "item" => new ReceivedItemRequirement { ItemKey = ((YamlScalarNode)valueNode).Value! },
            "any" => new AnyGameRequirement { Requirements = [..((YamlSequenceNode)valueNode).Select(DeserializeFrom)] },
            "all" => new AllGameRequirement { Requirements = [..((YamlSequenceNode)valueNode).Select(DeserializeFrom)] },
            _ => throw new InvalidDataException($"Unrecognized requirement: {keyNode.Value}"),
        };
    }
}

public sealed record AllGameRequirement : GameRequirement
{
    public required ImmutableArray<GameRequirement> Requirements { get; init; }

    public override bool Satisfied(Game.State state)
    {
        return Requirements.All(r => r.Satisfied(state));
    }
}

public sealed record AnyGameRequirement : GameRequirement
{
    public required ImmutableArray<GameRequirement> Requirements { get; init; }

    public override bool Satisfied(Game.State state)
    {
        return Requirements.Any(r => r.Satisfied(state));
    }
}

public sealed record AbilityCheckRequirement : GameRequirement
{
    public required int DifficultyClass { get; init; }

    public override bool Satisfied(Game.State state)
    {
        return false;
    }
}

public sealed record RatCountRequirement : GameRequirement
{
    public required int RatCount { get; init; }

    public override bool Satisfied(Game.State state)
    {
        return state.RatCount >= RatCount;
    }
}

public sealed record CheckedLocationRequirement : GameRequirement
{
    public required string LocationKey { get; init; }

    public override bool Satisfied(Game.State state)
    {
        return false;
    }
}

public sealed record ReceivedItemRequirement : GameRequirement
{
    public required string ItemKey { get; init; }

    public override bool Satisfied(Game.State state)
    {
        return false;
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
