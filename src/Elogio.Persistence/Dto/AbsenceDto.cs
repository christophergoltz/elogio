namespace Elogio.Persistence.Dto;

/// <summary>
/// Type of absence, determined by color in the Kelio response.
/// </summary>
public enum AbsenceType
{
    /// <summary>No absence - regular work day.</summary>
    None,

    /// <summary>Public holiday (Feiertag) - detected via isHoliday flag.</summary>
    PublicHoliday,

    /// <summary>Half-day public holiday (Halber Feiertag) - Yellow color (-256).</summary>
    HalfHoliday,

    /// <summary>Vacation/Leave (Urlaub) - Blue color (-16776961).</summary>
    Vacation,

    /// <summary>Sick leave (Krankheit) - Red color (-65536).</summary>
    SickLeave,

    /// <summary>Private appointment (Privattermin) - Green color (-16711808).</summary>
    PrivateAppointment,

    /// <summary>Rest day (Ruhezeit) - Gray color (-3355444).</summary>
    RestDay,

    /// <summary>Weekend day.</summary>
    Weekend,

    /// <summary>Unknown absence type.</summary>
    Unknown
}

/// <summary>
/// Represents a single day in the absence calendar.
/// </summary>
public record AbsenceDayDto
{
    /// <summary>The date of this day.</summary>
    public required DateOnly Date { get; init; }

    /// <summary>The primary absence type for this day.</summary>
    public required AbsenceType Type { get; init; }

    /// <summary>Whether this day is a public holiday.</summary>
    public required bool IsPublicHoliday { get; init; }

    /// <summary>Whether this day is a weekend.</summary>
    public required bool IsWeekend { get; init; }

    /// <summary>Whether this day is a rest day.</summary>
    public required bool IsRestDay { get; init; }

    /// <summary>Whether this day is also a half holiday (in addition to primary type).</summary>
    public bool IsHalfHoliday { get; init; }

    /// <summary>The color value from Kelio (-65536 = Red, -16776961 = Blue, etc.).</summary>
    public int? ColorValue { get; init; }

    /// <summary>The motif/request ID from Kelio (e.g., "URL", "KRK2", "364").</summary>
    public string? MotifId { get; init; }

    /// <summary>Display label for the absence type (from legend).</summary>
    public string? Label { get; init; }
}

/// <summary>
/// Represents the absence calendar response for a date range.
/// </summary>
public record AbsenceCalendarDto
{
    /// <summary>Employee ID.</summary>
    public required int EmployeeId { get; init; }

    /// <summary>Start date of the calendar range.</summary>
    public required DateOnly StartDate { get; init; }

    /// <summary>End date of the calendar range.</summary>
    public required DateOnly EndDate { get; init; }

    /// <summary>All days in the calendar range.</summary>
    public required List<AbsenceDayDto> Days { get; init; }

    /// <summary>Legend entries mapping colors to labels.</summary>
    public required List<AbsenceLegendEntryDto> Legend { get; init; }

    /// <summary>Get all vacation days.</summary>
    public IEnumerable<AbsenceDayDto> VacationDays =>
        Days.Where(d => d.Type == AbsenceType.Vacation);

    /// <summary>Get all sick leave days.</summary>
    public IEnumerable<AbsenceDayDto> SickLeaveDays =>
        Days.Where(d => d.Type == AbsenceType.SickLeave);

    /// <summary>Get all public holidays.</summary>
    public IEnumerable<AbsenceDayDto> PublicHolidays =>
        Days.Where(d => d.Type == AbsenceType.PublicHoliday || d.IsPublicHoliday);

    /// <summary>Get all private appointment days.</summary>
    public IEnumerable<AbsenceDayDto> PrivateAppointments =>
        Days.Where(d => d.Type == AbsenceType.PrivateAppointment);
}

/// <summary>
/// Legend entry mapping a color to an absence type label.
/// </summary>
public record AbsenceLegendEntryDto
{
    /// <summary>Color value (Java integer format).</summary>
    public required int ColorValue { get; init; }

    /// <summary>Display label (e.g., "Urlaub", "Krank mit AU").</summary>
    public required string Label { get; init; }

    /// <summary>Derived absence type from color.</summary>
    public required AbsenceType Type { get; init; }
}

/// <summary>
/// Helper methods for absence type detection.
/// </summary>
public static class AbsenceTypeHelper
{
    /// <summary>Color value for sick leave (Red).</summary>
    public const int ColorSickLeave = -65536;

    /// <summary>Color value for vacation (Blue).</summary>
    public const int ColorVacation = -16776961;

    /// <summary>Color value for private appointment (Green).</summary>
    public const int ColorPrivate = -16711808;

    /// <summary>Color value for half holiday (Yellow).</summary>
    public const int ColorHalfHoliday = -256;

    /// <summary>Color value for rest day (Gray).</summary>
    public const int ColorRestDay = -3355444;

    /// <summary>
    /// Determine the absence type from a Kelio color value.
    /// </summary>
    public static AbsenceType FromColor(int colorValue)
    {
        return colorValue switch
        {
            ColorSickLeave => AbsenceType.SickLeave,
            ColorVacation => AbsenceType.Vacation,
            ColorPrivate => AbsenceType.PrivateAppointment,
            ColorHalfHoliday => AbsenceType.HalfHoliday,
            ColorRestDay => AbsenceType.RestDay,
            _ => AbsenceType.Unknown
        };
    }

    /// <summary>
    /// Get the display color (hex) for an absence type.
    /// </summary>
    public static string GetDisplayColor(AbsenceType type)
    {
        return type switch
        {
            AbsenceType.SickLeave => "#FF0000",       // Red
            AbsenceType.Vacation => "#0000FF",        // Blue
            AbsenceType.PrivateAppointment => "#00FF80", // Green
            AbsenceType.HalfHoliday => "#FFFF00",     // Yellow
            AbsenceType.PublicHoliday => "#FF0000",   // Red
            AbsenceType.RestDay => "#CCCCCC",         // Gray
            AbsenceType.Weekend => "#CCCCCC",         // Gray
            _ => "#FFFFFF"                            // White
        };
    }

    /// <summary>
    /// Get the German label for an absence type.
    /// </summary>
    public static string GetLabel(AbsenceType type)
    {
        return type switch
        {
            AbsenceType.SickLeave => "Krankheit",
            AbsenceType.Vacation => "Urlaub",
            AbsenceType.PrivateAppointment => "Privat",
            AbsenceType.HalfHoliday => "Halber Feiertag",
            AbsenceType.PublicHoliday => "Feiertag",
            AbsenceType.RestDay => "Ruhezeit",
            AbsenceType.Weekend => "Wochenende",
            AbsenceType.None => "",
            _ => "Unbekannt"
        };
    }
}
