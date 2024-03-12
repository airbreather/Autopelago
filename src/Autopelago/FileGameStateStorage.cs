using System.Text.Json;

using ArchipelagoClientDotNet;

namespace Autopelago;

public sealed class FileGameStateStorage : GameStateStorage
{
    private static readonly FileStreamOptions s_readOptions = new()
    {
        Mode = FileMode.Open,
        Access = FileAccess.Read,
        Share = FileShare.ReadWrite | FileShare.Delete,
        Options = FileOptions.SequentialScan | FileOptions.Asynchronous,
    };

    private static readonly FileStreamOptions s_writeOptions = new()
    {
        Mode = FileMode.Create,
        Access = FileAccess.Write,
        Share = FileShare.Read | FileShare.Delete,
        Options = FileOptions.Asynchronous,
    };

    private readonly string _path;

    private readonly string _tempPath;

    public FileGameStateStorage(string path)
    {
        _path = path;
        _tempPath = path + ".tmp";
    }

    protected override async ValueTask<Game.State.Proxy?> LoadProxyAsync(CancellationToken cancellationToken)
    {
        await Helper.ConfigureAwaitFalse();
        try
        {
            await using FileStream file = new(_path, s_readOptions);
            return await JsonSerializer.DeserializeAsync<Game.State.Proxy>(file, Game.State.Proxy.SerializerOptions, cancellationToken);
        }
        catch (IOException)
        {
            return null;
        }
    }

    protected override async ValueTask SaveProxyAsync(Game.State.Proxy proxy, CancellationToken cancellationToken)
    {
        await Helper.ConfigureAwaitFalse();
        await using (FileStream file = new(_tempPath, s_writeOptions))
        {
            await JsonSerializer.SerializeAsync(file, proxy, Game.State.Proxy.SerializerOptions, cancellationToken);
        }

        File.Move(_tempPath, _path, overwrite: true);
    }
}
