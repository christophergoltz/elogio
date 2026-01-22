using CommunityToolkit.Mvvm.ComponentModel;
using Elogio.Persistence.Dto;

namespace Elogio.ViewModels.Models;

/// <summary>
/// Represents a single day in the yearly calendar mini-month view.
/// </summary>
public partial class YearlyDayItem : ObservableObject
{
    [ObservableProperty]
    private DateOnly _date;

    [ObservableProperty]
    private int _dayNumber;

    [ObservableProperty]
    private bool _isCurrentMonth;

    [ObservableProperty]
    private bool _isWeekend;

    [ObservableProperty]
    private bool _isToday;

    [ObservableProperty]
    private AbsenceType _absenceType = AbsenceType.None;

    /// <summary>
    /// Whether this day has a displayable absence.
    /// </summary>
    public bool HasAbsence => AbsenceType != AbsenceType.None &&
                              AbsenceType != AbsenceType.Weekend &&
                              AbsenceType != AbsenceType.Unknown;

    /// <summary>
    /// Get the color for this absence type.
    /// </summary>
    public string AbsenceColor => AbsenceType switch
    {
        AbsenceType.Vacation => "#00BCD4",      // Cyan
        AbsenceType.SickLeave => "#F44336",     // Red
        AbsenceType.PublicHoliday => "#9E9E9E", // Gray
        AbsenceType.HalfHoliday => "#FFEB3B",   // Yellow
        _ => "Transparent"
    };
}
