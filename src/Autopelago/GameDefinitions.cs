using System.Collections.Frozen;

public static class GameDefinitions
{
    private static readonly GameDefinitionsModel s_model = GameDefinitionsModel.LoadFromEmbeddedResource();

    public static FrozenDictionary<string, ItemDefinitionModel> ItemsByName { get; } = s_model.Items.AllItems.ToFrozenDictionary(i => i.Name);

    public static FrozenDictionary<string, LocationDefinitionModel> LocationsByName { get; } = (
        from region in s_model.Regions.AllRegions.Values
        from location in region.Locations
        select KeyValuePair.Create(location.Name, location)
    ).ToFrozenDictionary();

    public static FrozenDictionary<string, LocationDefinitionModel> LocationsByKey { get; } = (
        from region in s_model.Regions.AllRegions.Values
        from location in region.Locations
        select KeyValuePair.Create(location.Key, location)
    ).ToFrozenDictionary();
}
