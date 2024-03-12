using System.Text.Json;

using ArchipelagoClientDotNet;

namespace Autopelago;

public sealed class ArchipelagoGameStateStorage : GameStateStorage
{
    private readonly ArchipelagoClient _client;

    private readonly string _key;

    public ArchipelagoGameStateStorage(ArchipelagoClient client, string key)
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
            ? JsonSerializer.Deserialize<Game.State.Proxy>(stateElement, Game.State.Proxy.SerializerOptions)
            : null;
    }

    protected override async ValueTask SaveProxyAsync(Game.State.Proxy proxy, CancellationToken cancellationToken)
    {
        await Helper.ConfigureAwaitFalse();
        DataStorageOperationModel op = new()
        {
            Operation = ArchipelagoDataStorageOperationType.Replace,
            Value = JsonSerializer.SerializeToNode(proxy, Game.State.Proxy.SerializerOptions)!,
        };
        await _client.SetAsync(new() { Key = _key, Operations = [op] }, cancellationToken);
    }
}
