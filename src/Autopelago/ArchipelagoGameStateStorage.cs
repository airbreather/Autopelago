using System.Text.Json;
using ArchipelagoClientDotNet;

public sealed class ArchipelagoGameStateStorage : IGameStateStorage
{
    private static readonly JsonSerializerOptions s_jsonSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        TypeInfoResolver = GameStateSerializerContext.Default,
    };

    private readonly IArchipelagoClient _client;

    private readonly string _key;

    public ArchipelagoGameStateStorage(IArchipelagoClient client, string key)
    {
        _client = client;
        _key = key;
    }

    public async ValueTask<Game.State?> LoadAsync(CancellationToken cancellationToken)
    {
        await Helper.ConfigureAwaitFalse();
        GetPacketModel retrieveGameState = new() { Keys = [_key] };
        RetrievedPacketModel retrievedGameState = await _client.GetAsync(retrieveGameState, cancellationToken);
        return retrievedGameState.Keys.TryGetValue(_key, out JsonElement stateElement)
            ? JsonSerializer.Deserialize<Game.State>(stateElement, s_jsonSerializerOptions)
            : null;
    }

    public async ValueTask SaveAsync(Game.State state, CancellationToken cancellationToken)
    {
        await Helper.ConfigureAwaitFalse();
        DataStorageOperationModel op = new()
        {
            Operation = ArchipelagoDataStorageOperationType.Replace,
            Value = JsonSerializer.SerializeToNode(state, s_jsonSerializerOptions)!,
        };
        await _client.SetAsync(new() { Key = _key, Operations = [op] }, cancellationToken);
    }
}
