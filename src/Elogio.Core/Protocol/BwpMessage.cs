namespace Elogio.Core.Protocol;

/// <summary>
/// Represents a decoded BWP (Bodet Web Protocol) message.
/// </summary>
public record BwpMessage(
    string Raw,
    bool IsEncoded,
    int[] Keys,
    string Decoded,
    int HeaderLength
);
