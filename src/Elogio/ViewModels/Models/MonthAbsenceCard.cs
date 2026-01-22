using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Elogio.ViewModels;

/// <summary>
/// Represents a month card in the yearly calendar view.
/// </summary>
public partial class MonthAbsenceCard : ObservableObject
{
    [ObservableProperty]
    private int _month;

    [ObservableProperty]
    private int _year;

    [ObservableProperty]
    private string _monthName = string.Empty;

    [ObservableProperty]
    private ObservableCollection<YearlyDayItem> _days = [];

    [ObservableProperty]
    private int _vacationDayCount;

    [ObservableProperty]
    private int _sickDayCount;

    [ObservableProperty]
    private int _holidayCount;

    /// <summary>
    /// Whether this month has any absences.
    /// </summary>
    public bool HasAbsences => VacationDayCount > 0 || SickDayCount > 0 || HolidayCount > 0;

    /// <summary>
    /// Summary text for the month's absences.
    /// </summary>
    public string AbsenceSummary
    {
        get
        {
            var parts = new List<string>();
            if (VacationDayCount > 0) parts.Add($"{VacationDayCount} Urlaub");
            if (SickDayCount > 0) parts.Add($"{SickDayCount} Krank");
            if (HolidayCount > 0) parts.Add($"{HolidayCount} Feiertag");
            return parts.Count > 0 ? string.Join(", ", parts) : "Keine Abwesenheiten";
        }
    }
}
