using System.Collections.Immutable;

namespace Autopelago;

public sealed class ConnectionRefusedException : Exception
{
    public ConnectionRefusedException(ImmutableArray<string> errors)
        : base(string.Join(Environment.NewLine, errors.Prepend("Connection refused:")))
    {
        Errors = errors;
    }

    public ImmutableArray<string> Errors { get; }
}

public sealed class HandshakeException : Exception
{
    public HandshakeException()
        : base("The Archipelago handshake protocol never completed as expected.")
    {
    }
}
