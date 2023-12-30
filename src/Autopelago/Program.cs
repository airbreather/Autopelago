using Autopelago;

using Console = Colorful.Console;

await Helper.ConfigureAwaitFalse();

AutopelagoGame game = new();
await game.StartAsync(args[0], ushort.Parse(args[1]), args[2], args.ElementAtOrDefault(3));

Console.WriteLine("Game started.");

TaskCompletionSource tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
Console.CancelKeyPress += (_, args) =>
{
    args.Cancel = true;
    tcs.TrySetResult();
};

await tcs.Task;
Console.WriteLine("Exiting gracefully.");
