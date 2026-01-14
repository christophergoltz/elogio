namespace Elogio.Persistence.Dto;

/// <summary>
/// Type of punch operation.
/// </summary>
public enum PunchType
{
    /// <summary>Clock in (Kommen)</summary>
    ClockIn,

    /// <summary>Clock out (Gehen)</summary>
    ClockOut,

    /// <summary>Unknown punch type</summary>
    Unknown
}

/// <summary>
/// Result of a punch (clock-in/clock-out) operation from badgerSignaler API.
/// </summary>
public record PunchResultDto
{
    /// <summary>Whether the punch was successful</summary>
    public required bool Success { get; init; }

    /// <summary>The type of punch that was registered</summary>
    public required PunchType Type { get; init; }

    /// <summary>The timestamp of the punch</summary>
    public TimeOnly? Timestamp { get; init; }

    /// <summary>The date of the punch</summary>
    public DateOnly? Date { get; init; }

    /// <summary>Server message describing the punch (e.g., "Letzte Buchung um 09:26 (Kommen)")</summary>
    public string? Message { get; init; }

    /// <summary>UI label (e.g., "Beginnen/beenden")</summary>
    public string? Label { get; init; }

    /// <summary>Error details (if failed)</summary>
    public string? Error { get; init; }

    /// <summary>
    /// Parse the punch type from a German message string.
    /// </summary>
    /// <param name="message">Message like "Letzte Buchung um 09:26 (Kommen)" or "(Gehen)"</param>
    public static PunchType ParsePunchType(string? message)
    {
        if (string.IsNullOrEmpty(message))
            return PunchType.Unknown;

        if (message.Contains("(Kommen)") || message.Contains("Kommen"))
            return PunchType.ClockIn;

        if (message.Contains("(Gehen)") || message.Contains("Gehen"))
            return PunchType.ClockOut;

        return PunchType.Unknown;
    }

    /// <summary>
    /// Parse the time from a German message string.
    /// </summary>
    /// <param name="message">Message like "Letzte Buchung um 09:26 (Kommen)"</param>
    public static TimeOnly? ParseTimeFromMessage(string? message)
    {
        if (string.IsNullOrEmpty(message))
            return null;

        // Pattern: "um HH:MM"
        var match = System.Text.RegularExpressions.Regex.Match(message, @"um\s+(\d{1,2}):(\d{2})");
        if (match.Success &&
            int.TryParse(match.Groups[1].Value, out var hour) &&
            int.TryParse(match.Groups[2].Value, out var minute))
        {
            return new TimeOnly(hour, minute);
        }

        return null;
    }
}
