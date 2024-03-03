using System.Text.Json;
using System.Text.Json.Serialization;

using ArchipelagoClientDotNet;

namespace Autopelago;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(Game.State.Proxy))]
internal sealed partial class GameStateProxySerializerContext : JsonSerializerContext
{
}

public abstract class GameStateStorage
{
    protected static readonly JsonSerializerOptions s_jsonSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        TypeInfoResolver = GameStateProxySerializerContext.Default,
    };

    public virtual async ValueTask<Game.State?> LoadAsync(CancellationToken cancellationToken)
    {
        await Helper.ConfigureAwaitFalse();
        return (await LoadProxyAsync(cancellationToken))?.ToState();
    }

    public virtual ValueTask SaveAsync(Game.State state, CancellationToken cancellationToken)
    {
        return SaveProxyAsync(state.ToProxy(), cancellationToken);
    }

    protected abstract ValueTask<Game.State.Proxy?> LoadProxyAsync(CancellationToken cancellationToken);

    protected abstract ValueTask SaveProxyAsync(Game.State.Proxy proxy, CancellationToken cancellationToken);
}
