using ArchipelagoClientDotNet;

using Console = Colorful.Console;

await Helper.ConfigureAwaitFalse();

using ArchipelagoRawClient rawClient = new(args[0], ushort.Parse(args[1]));
ArchipelagoProtocolClient protocolClient = new(rawClient);

await rawClient.StartAsync();
await protocolClient.ConnectAsync(args[2], args[3], args.ElementAtOrDefault(4));

TaskCompletionSource tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
Console.CancelKeyPress += (_, args) =>
{
    args.Cancel = true;
    tcs.TrySetResult();
};

await tcs.Task;
Console.WriteLine("Exiting gracefully.");
