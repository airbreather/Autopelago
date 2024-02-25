#if false

using System.Collections.Frozen;
using System.Collections.Immutable;

public static class GameDefinitions
{
    private static readonly GameDefinitionsModel s_model = GameDefinitionsModel.LoadFromEmbeddedResource();

    public static FrozenDictionary<string, ItemDefinitionModel> Items { get; } = s_model.Items.AllItems.ToFrozenDictionary(i => i.Name);

    public static FrozenDictionary<string, ImmutableArray<LocationDefinitionModel>> Regions { get; } = BuildRegions();

    private static FrozenDictionary<string, ImmutableArray<LocationDefinitionModel>> BuildRegions()
    {
        Dictionary<string, List<LocationDefinitionModel>> result = new()
        {
            ["start"] = [],
        };
        foreach ((string key, LocationDefinitionModel location) in s_model.Locations.DefiningLocations)
        {
            result.Add(key, [location]);
            result.Add($"{key}.before", []);
            result.Add($"{key}.after", []);
        }

        foreach (LocationFillerGroupModel fillerGroup in s_model.Locations.FillerGroups)
        {
            LocationDefinitionModel defining = result[fillerGroup.DefiningLocationKey].Single();
            string baseName = $"{fillerGroup.DirectionFromDefiningLocation} {defining.Name} #";
            List<LocationDefinitionModel> toFill = result[fillerGroup.RegionKey];
            for (int i = 0; i < fillerGroup.LocationCount; i++)
            {
                toFill.Add(new()
                {
                    Name = $"{baseName} {toFill.Count + 1}",
                    Requires = fillerGroup.EachRequires,
                });
            }
        }

        return result.ToFrozenDictionary(kvp => kvp.Key, kvp => kvp.Value.ToImmutableArray());
    }
}

#endif
