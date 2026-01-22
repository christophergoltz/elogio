namespace Elogio.Utilities;

/// <summary>
/// Utility class for consistent TimeSpan formatting across the application.
/// </summary>
public static class TimeSpanFormatter
{
    /// <summary>
    /// Formats a TimeSpan as "H:mm" (e.g., "7:30", "12:05").
    /// </summary>
    public static string Format(TimeSpan time)
    {
        var totalHours = (int)Math.Abs(time.TotalHours);
        var minutes = Math.Abs(time.Minutes);
        return $"{totalHours}:{minutes:D2}";
    }

    /// <summary>
    /// Formats a TimeSpan with sign as "+H:mm" or "-H:mm" (e.g., "+1:30", "-0:45").
    /// Returns empty string for zero.
    /// </summary>
    public static string FormatWithSign(TimeSpan time)
    {
        if (time == TimeSpan.Zero)
            return "";

        var sign = time < TimeSpan.Zero ? "-" : "+";
        var totalHours = (int)Math.Abs(time.TotalHours);
        var minutes = Math.Abs(time.Minutes);
        return $"{sign}{totalHours}:{minutes:D2}";
    }

    /// <summary>
    /// Formats a TimeSpan with sign in parentheses as "(+H:mm)" or "(-H:mm)".
    /// Returns empty string for zero.
    /// </summary>
    public static string FormatWithSignInParentheses(TimeSpan time)
    {
        if (time == TimeSpan.Zero)
            return "";

        return $"({FormatWithSign(time)})";
    }
}
