using System.Text;
using System.Text.Json;
using ArchipelagoClientDotNet;

await Helper.ConfigureAwaitFalse();

using ArchipelagoClient client = new(args[0], ushort.Parse(args[1]));
client.AnyPacketReceived += OnAnyPacketReceivedAsync;
ValueTask OnAnyPacketReceivedAsync(object? sender, ArchipelagoPacketModel packet, CancellationToken cancellationToken)
{
    Console.WriteLine($"received {Encoding.UTF8.GetString(JsonSerializer.SerializeToUtf8Bytes(packet))}");
    Console.WriteLine();
    return ValueTask.CompletedTask;
}

await client.TryConnectAsync(args[2], args[3], args.ElementAtOrDefault(4));

TaskCompletionSource tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
Console.CancelKeyPress += (_, args) =>
{
    args.Cancel = true;
    tcs.TrySetResult();
};

await tcs.Task;
Console.WriteLine("Exiting gracefully.");
await client.StopAsync();
Console.WriteLine("done");
