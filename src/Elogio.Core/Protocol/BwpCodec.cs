namespace Elogio.Core.Protocol;

/// <summary>
/// BWP (Bodet Web Protocol) Encoder/Decoder.
/// Reverse-engineered from Kelio GWT JavaScript.
///
/// BWP Format:
///     [MARKER][KEY_COUNT][KEYS...][ENCODED_BODY]
///
/// Where:
///     - MARKER: 0xA4 (¤ character)
///     - KEY_COUNT: chr(48 + N) where N is number of keys
///     - KEYS: chr(48 + key[i] + (i % 11)) for each key
///     - ENCODED_BODY: Each char encoded as chr(charCode + key[i % N] - (i % 17))
/// </summary>
public class BwpCodec
{
    private const char Marker = '\u00A4'; // ¤
    private const int Mask = 0xFFFF; // 16-bit mask

    /// <summary>
    /// Check if data is BWP-encoded.
    /// </summary>
    public bool IsBwp(string data)
    {
        return !string.IsNullOrEmpty(data) && data[0] == Marker;
    }

    /// <summary>
    /// Decode BWP-encoded data.
    /// </summary>
    /// <param name="data">The BWP-encoded string</param>
    /// <returns>A BwpMessage containing the decoded data</returns>
    public BwpMessage Decode(string data)
    {
        if (string.IsNullOrEmpty(data) || data[0] != Marker)
        {
            return new BwpMessage(data ?? "", false, [], data ?? "", 0);
        }

        // Read key count from byte 1
        int keyCount = data[1] - 48;

        // Read keys from bytes 2 to 2+keyCount
        var keys = new int[keyCount];
        for (int i = 0; i < keyCount; i++)
        {
            keys[i] = data[2 + i] - 48 - (i % 11);
        }

        // Decode body starting at position 2 + keyCount
        int headerLength = 2 + keyCount;
        var decoded = new char[data.Length - headerLength];

        for (int i = 0; i < decoded.Length; i++)
        {
            int charCode = data[headerLength + i];
            int key = keys[i % keys.Length];
            decoded[i] = (char)((charCode - key + (i % 17)) & Mask);
        }

        return new BwpMessage(data, true, keys, new string(decoded), headerLength);
    }

    /// <summary>
    /// Encode data to BWP format.
    /// </summary>
    /// <param name="data">The plain text to encode</param>
    /// <param name="keys">Optional keys to use; random keys generated if null</param>
    /// <returns>BWP-encoded string</returns>
    public string Encode(string data, int[]? keys = null)
    {
        if (string.IsNullOrEmpty(data))
            return data ?? "";

        // Generate keys if not provided
        keys ??= GenerateKeys();

        var result = new char[2 + keys.Length + data.Length];
        int pos = 0;

        // Marker
        result[pos++] = Marker;

        // Key count
        result[pos++] = (char)((48 + keys.Length) & Mask);

        // Encoded keys
        for (int i = 0; i < keys.Length; i++)
        {
            result[pos++] = (char)((48 + keys[i] + (i % 11)) & Mask);
        }

        // Encode body
        for (int i = 0; i < data.Length; i++)
        {
            int charCode = data[i];
            int key = keys[i % keys.Length];
            result[pos++] = (char)((charCode + key - (i % 17)) & Mask);
        }

        return new string(result);
    }

    private static int[] GenerateKeys()
    {
        var random = new Random();
        int keyCount = random.Next(4, 38); // 4-37 keys
        var keys = new int[keyCount];
        for (int i = 0; i < keyCount; i++)
        {
            keys[i] = random.Next(0, 15); // 0-14
        }
        return keys;
    }
}
