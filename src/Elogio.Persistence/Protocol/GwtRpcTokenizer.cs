using System.Text;

namespace Elogio.Persistence.Protocol;

/// <summary>
/// Tokenizer for GWT-RPC serialized data.
///
/// GWT-RPC Format:
/// - First value: string table count
/// - Next N values: quoted strings (the string table)
/// - Remaining values: numeric data and string table references
/// </summary>
public class GwtRpcTokenizer
{
    /// <summary>
    /// Tokenize a GWT-RPC formatted string into individual tokens.
    /// </summary>
    public GwtRpcMessage Tokenize(string data)
    {
        if (string.IsNullOrEmpty(data))
        {
            return new GwtRpcMessage([], [], data ?? "");
        }

        var tokens = new List<GwtRpcToken>();
        var position = 0;

        while (position < data.Length)
        {
            // Skip whitespace
            while (position < data.Length && char.IsWhiteSpace(data[position]))
            {
                position++;
            }

            if (position >= data.Length)
                break;

            var token = ReadToken(data, ref position);
            if (token != null)
            {
                tokens.Add(token);
            }

            // Skip comma separator
            while (position < data.Length && (data[position] == ',' || char.IsWhiteSpace(data[position])))
            {
                position++;
            }
        }

        return BuildMessage(tokens, data);
    }

    private static GwtRpcToken? ReadToken(string data, ref int position)
    {
        if (position >= data.Length)
            return null;

        var startPos = position;
        var ch = data[position];

        // String token (quoted)
        if (ch == '"')
        {
            var value = ReadQuotedString(data, ref position);
            return new GwtRpcToken(GwtRpcTokenType.String, value, startPos);
        }

        // Numeric token or identifier
        return ReadNumericOrIdentifier(data, ref position);
    }

    private static string ReadQuotedString(string data, ref int position)
    {
        var sb = new StringBuilder();
        position++; // Skip opening quote

        while (position < data.Length)
        {
            var ch = data[position];

            if (ch == '"')
            {
                position++; // Skip closing quote
                break;
            }

            if (ch == '\\' && position + 1 < data.Length)
            {
                position++;
                ch = data[position] switch
                {
                    'n' => '\n',
                    'r' => '\r',
                    't' => '\t',
                    '"' => '"',
                    '\\' => '\\',
                    _ => data[position]
                };
            }

            sb.Append(ch);
            position++;
        }

        return sb.ToString();
    }

    private static GwtRpcToken ReadNumericOrIdentifier(string data, ref int position)
    {
        var startPos = position;
        var sb = new StringBuilder();

        // Handle negative numbers
        if (data[position] == '-')
        {
            sb.Append('-');
            position++;
        }

        while (position < data.Length)
        {
            var ch = data[position];
            if (ch == ',' || char.IsWhiteSpace(ch))
                break;

            sb.Append(ch);
            position++;
        }

        var value = sb.ToString();

        // Determine token type
        if (long.TryParse(value, out _))
        {
            return new GwtRpcToken(GwtRpcTokenType.Integer, value, startPos);
        }

        if (double.TryParse(value, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out _))
        {
            return new GwtRpcToken(GwtRpcTokenType.Float, value, startPos);
        }

        return new GwtRpcToken(GwtRpcTokenType.Identifier, value, startPos);
    }

    private static GwtRpcMessage BuildMessage(List<GwtRpcToken> tokens, string raw)
    {
        if (tokens.Count == 0)
        {
            return new GwtRpcMessage([], [], raw);
        }

        // First token should be string table count
        var stringTableCount = 0;
        if (tokens[0].Type == GwtRpcTokenType.Integer && int.TryParse(tokens[0].Value, out var count))
        {
            stringTableCount = count;
        }

        // Extract string table
        var stringTable = new List<string>();
        var dataStartIndex = 1;

        for (int i = 1; i <= stringTableCount && i < tokens.Count; i++)
        {
            if (tokens[i].Type == GwtRpcTokenType.String)
            {
                stringTable.Add(tokens[i].Value);
                dataStartIndex = i + 1;
            }
            else
            {
                // Not a string, stop building string table
                break;
            }
        }

        // Remaining tokens are data
        var dataTokens = tokens.Skip(dataStartIndex).ToList();

        return new GwtRpcMessage([.. stringTable], dataTokens, raw);
    }
}

public enum GwtRpcTokenType
{
    String,
    Integer,
    Float,
    Identifier
}

public record GwtRpcToken(GwtRpcTokenType Type, string Value, int Position)
{
    public int AsInt() => int.Parse(Value);
    public long AsLong() => long.Parse(Value);
    public double AsDouble() => double.Parse(Value, System.Globalization.CultureInfo.InvariantCulture);
    public bool TryAsInt(out int result) => int.TryParse(Value, out result);
}

public class GwtRpcMessage
{
    public string[] StringTable { get; }
    public List<GwtRpcToken> DataTokens { get; }
    public string Raw { get; }

    public GwtRpcMessage(string[] stringTable, List<GwtRpcToken> dataTokens, string raw)
    {
        StringTable = stringTable;
        DataTokens = dataTokens;
        Raw = raw;
    }

    /// <summary>
    /// Get a string from the string table by index (1-based as used in GWT-RPC).
    /// </summary>
    public string? GetString(int index)
    {
        if (index < 1 || index > StringTable.Length)
            return null;
        return StringTable[index - 1];
    }

    /// <summary>
    /// Check if this is a response message.
    /// </summary>
    public bool IsResponse => StringTable.Length > 0 &&
        StringTable[0].Contains("BWPResponse", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Check if this is a request message.
    /// </summary>
    public bool IsRequest => StringTable.Length > 0 &&
        StringTable[0].Contains("BWPRequest", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Get the main class type from the response (usually at index 3).
    /// </summary>
    public string? ResponseType => StringTable.Length > 2 ? StringTable[2] : null;
}
