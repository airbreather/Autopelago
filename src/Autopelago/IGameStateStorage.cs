using System.Text.Json.Serialization;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(Game.State), TypeInfoPropertyName = "Game")]
internal sealed partial class GameStateSerializerContext : JsonSerializerContext
{
}

public interface IGameStateStorage
{
    ValueTask<Game.State?> LoadAsync(CancellationToken cancellationToken);
    ValueTask SaveAsync(Game.State state, CancellationToken cancellationToken);
}
