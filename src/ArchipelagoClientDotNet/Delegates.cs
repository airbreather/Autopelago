namespace ArchipelagoClientDotNet;

public delegate ValueTask AsyncEventHandler<in T>(object? sender, T args, CancellationToken cancellationToken);
