using Elogio.Persistence.Dto;

namespace Elogio.Persistence.Protocol;

/// <summary>
/// Parser for BadgerSignaler (punch/clock-in/clock-out) GWT-RPC responses.
///
/// Response format:
/// 8,"BWPResponse","NULL","PortailVignetteBadgerSignalerResultatMessageBWT",
///   "java.lang.Boolean","java.lang.String","Beginnen/beenden","java.util.List",
///   "Letzte Buchung um 09:26 (Kommen)",0,1,2,3,1,3,0,3,1,4,5,6,1,4,7,1,3,0
///
/// Key strings:
/// - Index 5: Label (e.g., "Beginnen/beenden")
/// - Index 7: Message (e.g., "Letzte Buchung um 09:26 (Kommen)")
/// </summary>
public class BadgerSignalerResponseParser
{
    private readonly GwtRpcTokenizer _tokenizer = new();

    /// <summary>
    /// Parse a decoded GWT-RPC response containing BadgerSignaler result.
    /// </summary>
    public PunchResultDto? Parse(string gwtRpcData)
    {
        if (string.IsNullOrEmpty(gwtRpcData))
        {
            return null;
        }

        // Check for server error
        if (gwtRpcData.Contains("ExceptionBWT"))
        {
            return new PunchResultDto
            {
                Success = false,
                Type = PunchType.Unknown,
                Error = "Server returned ExceptionBWT"
            };
        }

        var message = _tokenizer.Tokenize(gwtRpcData);

        if (!message.IsResponse)
        {
            return null;
        }

        // Check if this is a BadgerSignaler response
        if (!IsValidBadgerSignalerResponse(message))
        {
            return null;
        }

        return ParseBadgerSignalerResult(message);
    }

    private static bool IsValidBadgerSignalerResponse(GwtRpcMessage message)
    {
        // Look for the result type in the string table
        foreach (var str in message.StringTable)
        {
            if (str.Contains("BadgerSignaler") || str.Contains("VignetteBadger"))
            {
                return true;
            }
        }
        return false;
    }

    private static PunchResultDto ParseBadgerSignalerResult(GwtRpcMessage message)
    {
        string? label = null;
        string? punchMessage = null;

        // Find the label and message in the string table
        // Label is typically short ("Beginnen/beenden")
        // Message contains time info ("Letzte Buchung um 09:26 (Kommen)")
        foreach (var str in message.StringTable)
        {
            // Skip type names
            if (str.Contains("com.bodet") || str.Contains("java.") ||
                str == "NULL" || str.Contains("BWP"))
            {
                continue;
            }

            // Message contains time info or error messages
            if (str.Contains("Buchung") || str.Contains("Kommen") || str.Contains("Gehen"))
            {
                punchMessage = str;
            }
            // Short strings without time info are likely labels
            else if (str.Length < 50 && !string.IsNullOrWhiteSpace(str))
            {
                label = str;
            }
        }

        // Check for error messages (punch rejected by server)
        var isError = IsErrorMessage(punchMessage);

        // Parse punch type from message
        var punchType = PunchResultDto.ParsePunchType(punchMessage);

        // Parse time from message
        var timestamp = PunchResultDto.ParseTimeFromMessage(punchMessage);

        return new PunchResultDto
        {
            Success = !isError,
            Type = punchType,
            Timestamp = timestamp,
            Date = DateOnly.FromDateTime(DateTime.Today),
            Message = punchMessage,
            Label = isError ? null : label,
            Error = isError ? punchMessage : null
        };
    }

    /// <summary>
    /// Check if the message indicates an error (punch rejected).
    /// </summary>
    private static bool IsErrorMessage(string? message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return false;
        }

        // Known error patterns (German)
        return message.Contains("zu nah aufeinander") ||
               message.Contains("mindestens") ||
               message.Contains("nicht möglich") ||
               message.Contains("nicht erlaubt") ||
               message.Contains("Fehler");
    }
}
