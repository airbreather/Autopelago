using System.Text.Json;

using ArchipelagoClientDotNet;

public sealed class ArchipelagoGameStateStorage : GameStateStorage
{
    private readonly IArchipelagoClient _client;

    private readonly string _key;

    public ArchipelagoGameStateStorage(IArchipelagoClient client, string key)
    {
        _client = client;
        _key = key;
    }

    protected override async ValueTask<Game.State.Proxy?> LoadProxyAsync(CancellationToken cancellationToken)
    {
        await Helper.ConfigureAwaitFalse();
        GetPacketModel retrieveGameState = new() { Keys = [_key] };
        RetrievedPacketModel retrievedGameState = await _client.GetAsync(retrieveGameState, cancellationToken);
        return retrievedGameState.Keys.TryGetValue(_key, out JsonElement stateElement)
            ? JsonSerializer.Deserialize<Game.State.Proxy>(stateElement, s_jsonSerializerOptions)
            : null;
    }

    protected override async ValueTask SaveProxyAsync(Game.State.Proxy proxy, CancellationToken cancellationToken)
    {
        await Helper.ConfigureAwaitFalse();
        DataStorageOperationModel op = new()
        {
            Operation = ArchipelagoDataStorageOperationType.Replace,
            Value = JsonSerializer.SerializeToNode(proxy, s_jsonSerializerOptions)!,
        };
        await _client.SetAsync(new() { Key = _key, Operations = [op] }, cancellationToken);
    }
}
