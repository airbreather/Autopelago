using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Data;
using ArchipelagoClientDotNet;

using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;
using YamlDotNet.Serialization;

public sealed record GameDefinitionsModel : IYamlConvertible
{
    private ItemDefinitionsModel _items = null!;

    public required ItemDefinitionsModel Items
    {
        get => _items;
        init => _items = value;
    }

    private LocationDefinitionsModel _locations = null!;

    public required LocationDefinitionsModel Locations
    {
        get => _locations;
        init => _locations = value;
    }

    // public required RelativeTravelDistancesModel RelativeTravelDistances { get; init; }

    public void Read(IParser parser, Type expectedType, ObjectDeserializer nestedObjectDeserializer)
    {
        YamlMappingNode map = nestedObjectDeserializer.Get<YamlMappingNode>();

        YamlMappingNode itemsMap = (YamlMappingNode)map["items"];
        YamlMappingNode locationsMap = (YamlMappingNode)map["locations"];
        YamlSequenceNode relativeTravelDistancesSeq = (YamlSequenceNode)map["relative_travel_distances"];

        _items = ItemDefinitionsModel.DeserializeFrom(itemsMap, locationsMap);
        _locations = LocationDefinitionsModel.DeserializeFrom(locationsMap);
    }

    public void Write(IEmitter emitter, ObjectSerializer nestedObjectSerializer)
    {
        throw new NotImplementedException();
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

    public required FrozenDictionary<string, ItemDefinitionModel> ProgressionItems { get; init; } = null!;

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

public sealed record LocationDefinitionsModel
{
    public required FrozenDictionary<string, LocationDefinitionModel> DefiningLocations { get; init; }

    public required ImmutableArray<LocationFillerGroupModel> FillerGroups { get; init; }

    public static LocationDefinitionsModel DeserializeFrom(YamlMappingNode map)
    {
        Dictionary<string, LocationDefinitionModel> definingLocations = [];
        ImmutableArray<LocationFillerGroupModel> fillerGroups = default;
        foreach ((YamlNode keyNode, YamlNode valueNode) in map)
        {
            string key = ((YamlScalarNode)keyNode).Value!;
            if (key == "filler_groups")
            {
                fillerGroups = [..((YamlSequenceNode)valueNode).Cast<YamlMappingNode>().Select(LocationFillerGroupModel.DeserializeFrom)];
                continue;
            }

            definingLocations.Add(key, LocationDefinitionModel.DeserializeFrom((YamlMappingNode)valueNode));
        }

        if (fillerGroups.IsDefault)
        {
            throw new InvalidDataException("Must have filler_groups");
        }

        return new()
        {
            DefiningLocations = definingLocations.ToFrozenDictionary(),
            FillerGroups = fillerGroups,
        };
    }
}

public sealed record LocationDefinitionModel
{
    public required string Name { get; init; }

    public required LocationRequirement Requires { get; init; }

    public static LocationDefinitionModel DeserializeFrom(YamlMappingNode map)
    {
        return new()
        {
            Name = ((YamlScalarNode)map["name"]).Value!,
            Requires = LocationRequirement.DeserializeFrom(map["requires"]),
        };
    }
}

public enum BeforeOrAfter
{
    Before,
    After,
}

public sealed record LocationFillerGroupModel
{
    public required BeforeOrAfter DirectionFromDefiningLocation { get; init; }

    public required string DefiningLocationKey { get; init; }

    public required int LocationCount { get; init; }

    public required LocationRequirement EachRequires { get; init; }

    public static LocationFillerGroupModel DeserializeFrom(YamlMappingNode map)
    {
        BeforeOrAfter direction = map.Children.ContainsKey("before") ? BeforeOrAfter.Before : BeforeOrAfter.After;
        return new()
        {
            DirectionFromDefiningLocation = direction,
            DefiningLocationKey = ((YamlScalarNode)map[direction.ToString().ToLowerInvariant()]).Value!,
            LocationCount = int.Parse(((YamlScalarNode)map["count"]).Value!),
            EachRequires = LocationRequirement.DeserializeFrom(map["each_requires"]),
        };
    }
}

public abstract record LocationRequirement
{
    public abstract bool Satisfied(Game game);

    public static LocationRequirement DeserializeFrom(YamlNode node)
    {
        // special case: there's an implicit "all:" at the top, which means that we might be coming
        // in partway through that. it's easy to test for this.
        if (node is YamlSequenceNode seq)
        {
            return new AllLocationRequirement
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
            "any" => new AnyLocationRequirement { Requirements = [..((YamlSequenceNode)valueNode).Select(DeserializeFrom)] },
            "all" => new AllLocationRequirement { Requirements = [..((YamlSequenceNode)valueNode).Select(DeserializeFrom)] },
            _ => throw new InvalidDataException($"Unrecognized requirement: {keyNode.Value}"),
        };
    }
}

public sealed record AllLocationRequirement : LocationRequirement
{
    public required ImmutableArray<LocationRequirement> Requirements { get; init; }

    public override bool Satisfied(Game game)
    {
        return Requirements.All(r => r.Satisfied(game));
    }
}

public sealed record AnyLocationRequirement : LocationRequirement
{
    public required ImmutableArray<LocationRequirement> Requirements { get; init; }

    public override bool Satisfied(Game game)
    {
        return Requirements.Any(r => r.Satisfied(game));
    }
}

public sealed record AbilityCheckRequirement : LocationRequirement
{
    public required int DifficultyClass { get; init; }

    public override bool Satisfied(Game game)
    {
        return false;
    }
}

public sealed record RatCountRequirement : LocationRequirement
{
    public required int RatCount { get; init; }

    public override bool Satisfied(Game game)
    {
        return game.RatCount >= RatCount;
    }
}

public sealed record CheckedLocationRequirement : LocationRequirement
{
    public required string LocationKey { get; init; }

    public override bool Satisfied(Game game)
    {
        return false;
    }
}

public sealed record ReceivedItemRequirement : LocationRequirement
{
    public required string ItemKey { get; init; }

    public override bool Satisfied(Game game)
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
