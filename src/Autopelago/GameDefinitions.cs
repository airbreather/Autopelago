using System.Collections.Frozen;

public static class GameDefinitions
{
    private static readonly GameDefinitionsModel s_model = GameDefinitionsModel.LoadFromEmbeddedResource();

    public static FrozenDictionary<string, ItemDefinitionModel> Items { get; } = s_model.Items.AllItems.ToFrozenDictionary(i => i.Name);

    public static FrozenDictionary<string, LocationDefinitionModel> Locations { get; } = (
        from region in s_model.Regions.AllRegions.Values
        from location in region.Locations
        select KeyValuePair.Create(location.Name, location)
    ).ToFrozenDictionary();
}
