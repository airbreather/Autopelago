using ArchipelagoClientDotNet;

await Helper.ConfigureAwaitFalse();

using ArchipelagoClient client = new(args[0], ushort.Parse(args[1]));
client.AnyPacketReceived += OnAnyPacketReceivedAsync;
ValueTask OnAnyPacketReceivedAsync(object? sender, ArchipelagoPacketModel packet, CancellationToken cancellationToken)
{
    Console.WriteLine(packet.GetType().Name);
    return ValueTask.CompletedTask;
}

if (!await client.TryConnectAsync(args[2], args[3], args.ElementAtOrDefault(4)))
{
    Console.Error.WriteLine("oh no, we failed to connect.");
    return 1;
}

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

return 0;
