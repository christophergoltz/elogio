namespace Elogio.Persistence.Dto;

/// <summary>
/// DTO for colleague absence data from the group calendar.
/// </summary>
public class ColleagueAbsenceDto
{
    /// <summary>
    /// Full name of the colleague (e.g., "Goltz Christopher (14)").
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Employee ID extracted from name, if available.
    /// </summary>
    public int? EmployeeId { get; set; }

    /// <summary>
    /// List of days (1-31) where the colleague is absent.
    /// </summary>
    public List<int> AbsenceDays { get; set; } = [];

    /// <summary>
    /// Month (1-12) for these absences.
    /// </summary>
    public int Month { get; set; }

    /// <summary>
    /// Year for these absences.
    /// </summary>
    public int Year { get; set; }

    /// <summary>
    /// Returns true if the colleague is absent on the specified date.
    /// </summary>
    public bool IsAbsentOn(DateOnly date)
    {
        return date.Month == Month && date.Year == Year && AbsenceDays.Contains(date.Day);
    }

    /// <summary>
    /// Returns true if the colleague is absent today.
    /// </summary>
    public bool IsAbsentToday => IsAbsentOn(DateOnly.FromDateTime(DateTime.Today));
}
