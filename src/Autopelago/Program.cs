using Autopelago;
using Autopelago.ArchipelagoClient;

using Console = Colorful.Console;

await Helper.ConfigureAwaitFalse();

ArchipelagoClient game = new();
await game.ConnectAsync(args[0], ushort.Parse(args[1]), args[2], args[3], args.ElementAtOrDefault(4));

Console.WriteLine("Game started.");

TaskCompletionSource tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
Console.CancelKeyPress += (_, args) =>
{
    args.Cancel = true;
    tcs.TrySetResult();
};

await tcs.Task;
Console.WriteLine("Exiting gracefully.");
