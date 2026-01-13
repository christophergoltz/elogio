namespace Elogio.Persistence.Dto;

/// <summary>
/// Represents a week of presence/time tracking data.
/// </summary>
public record WeekPresenceDto
{
    public required string EmployeeName { get; init; }
    public required DateOnly WeekStartDate { get; init; }
    public required List<DayPresenceDto> Days { get; init; }
    public required TimeSpan TotalWorked { get; init; }
    public required TimeSpan TotalExpected { get; init; }
    public TimeSpan Balance => TotalWorked - TotalExpected;
}

/// <summary>
/// Represents a single day of presence data.
/// </summary>
public record DayPresenceDto
{
    public required DateOnly Date { get; init; }
    public required string DayOfWeek { get; init; }
    public required List<TimeEntryDto> Entries { get; init; }
    public required TimeSpan WorkedTime { get; init; }
    public required TimeSpan ExpectedTime { get; init; }
    public string? ScheduleInfo { get; init; }
    public bool IsWeekend => Date.DayOfWeek is System.DayOfWeek.Saturday or System.DayOfWeek.Sunday;
}

/// <summary>
/// Represents a single time entry (badge in/out).
/// </summary>
public record TimeEntryDto
{
    public required TimeOnly Time { get; init; }
    public required TimeEntryType Type { get; init; }
    public string? Location { get; init; }
    public string? Description { get; init; }
}

public enum TimeEntryType
{
    BadgeIn,
    BadgeOut,
    Declaration,
    Unknown
}

/// <summary>
/// Helper methods for parsing Kelio time formats.
/// </summary>
public static class KelioTimeHelpers
{
    /// <summary>
    /// Parse a Kelio date (YYYYMMDD format) to DateOnly.
    /// </summary>
    public static DateOnly ParseDate(int kelioDate)
    {
        var year = kelioDate / 10000;
        var month = (kelioDate / 100) % 100;
        var day = kelioDate % 100;
        return new DateOnly(year, month, day);
    }

    /// <summary>
    /// Parse a Kelio date (YYYYMMDD format) to DateOnly.
    /// </summary>
    public static DateOnly ParseDate(string kelioDate)
    {
        if (int.TryParse(kelioDate, out var date))
        {
            return ParseDate(date);
        }
        throw new ArgumentException($"Invalid Kelio date format: {kelioDate}");
    }

    /// <summary>
    /// Convert seconds since midnight to TimeSpan.
    /// </summary>
    public static TimeSpan SecondsToTimeSpan(int seconds)
    {
        return TimeSpan.FromSeconds(seconds);
    }

    /// <summary>
    /// Convert seconds since midnight to TimeOnly.
    /// </summary>
    public static TimeOnly SecondsToTimeOnly(int seconds)
    {
        var hours = seconds / 3600;
        var minutes = (seconds % 3600) / 60;
        var secs = seconds % 60;

        // Handle overflow for times > 24h
        hours %= 24;

        return new TimeOnly(hours, minutes, secs);
    }

    /// <summary>
    /// Parse a time string like "7:00" or "28:00" to TimeSpan.
    /// </summary>
    public static TimeSpan ParseTimeString(string timeStr)
    {
        if (string.IsNullOrEmpty(timeStr))
            return TimeSpan.Zero;

        var parts = timeStr.Split(':');
        if (parts.Length >= 2 &&
            int.TryParse(parts[0], out var hours) &&
            int.TryParse(parts[1], out var minutes))
        {
            var seconds = 0;
            if (parts.Length >= 3 && int.TryParse(parts[2], out var s))
            {
                seconds = s;
            }
            return new TimeSpan(hours, minutes, seconds);
        }

        return TimeSpan.Zero;
    }

    /// <summary>
    /// Format a TimeSpan as "HH:MM" string.
    /// </summary>
    public static string FormatTimeSpan(TimeSpan time)
    {
        var totalHours = (int)time.TotalHours;
        var minutes = Math.Abs(time.Minutes);
        var sign = time < TimeSpan.Zero ? "-" : "";
        return $"{sign}{Math.Abs(totalHours)}:{minutes:D2}";
    }
}
