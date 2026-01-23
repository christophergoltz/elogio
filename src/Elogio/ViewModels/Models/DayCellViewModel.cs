using CommunityToolkit.Mvvm.ComponentModel;
using Elogio.Persistence.Dto;
using Elogio.Utilities;

namespace Elogio.ViewModels.Models;

public partial class DayCellViewModel : ObservableObject
{
    [ObservableProperty]
    private int _dayNumber;

    [ObservableProperty]
    private DateOnly _date;

    [ObservableProperty]
    private bool _isCurrentMonth;

    [ObservableProperty]
    private bool _isToday;

    [ObservableProperty]
    private bool _isWeekend;

    [ObservableProperty]
    private bool _isFuture;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private TimeSpan _workedTime;

    [ObservableProperty]
    private TimeSpan _expectedTime;

    [ObservableProperty]
    private DayCellState _state = DayCellState.Empty;

    // Absence properties
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDisplayableAbsence))]
    private AbsenceType _absenceType = AbsenceType.None;

    [ObservableProperty]
    private string? _absenceLabel;

    [ObservableProperty]
    private string? _absenceBorderColor;

    /// <summary>
    /// Whether this day is a half holiday in combination with another absence type (e.g., Vacation + HalfHoliday).
    /// Used to show an additional visual indicator.
    /// </summary>
    [ObservableProperty]
    private bool _isHalfHolidayCombined;

    /// <summary>
    /// Whether this day has an absence that should display a label.
    /// Excludes None, weekends, and unknown types.
    /// </summary>
    public bool HasDisplayableAbsence =>
        AbsenceType != AbsenceType.None &&
        AbsenceType != AbsenceType.Weekend &&
        AbsenceType != AbsenceType.Unknown;

    /// <summary>
    /// Whether this is a full-day absence where no work is expected.
    /// </summary>
    public bool IsFullDayAbsence =>
        AbsenceType is AbsenceType.Vacation or AbsenceType.SickLeave or AbsenceType.PublicHoliday;

    /// <summary>
    /// Simple worked time display (e.g., "7:30" or "--").
    /// </summary>
    public string WorkedTimeDisplay => WorkedTime == TimeSpan.Zero ? "--" : TimeSpanFormatter.Format(WorkedTime);

    /// <summary>
    /// Worked time display (e.g., "6:15" or "--").
    /// </summary>
    public string WorkedDisplay
    {
        get
        {
            if (!IsCurrentMonth || IsFuture) return "";
            if (IsWeekend || IsFullDayAbsence || ExpectedTime == TimeSpan.Zero) return "--";
            if (WorkedTime == TimeSpan.Zero) return "--";
            return TimeSpanFormatter.Format(WorkedTime);
        }
    }

    /// <summary>
    /// Expected time display (e.g., "/ 7:00").
    /// </summary>
    public string ExpectedDisplay
    {
        get
        {
            if (!IsCurrentMonth || IsFuture) return "";
            if (IsWeekend || IsFullDayAbsence || ExpectedTime == TimeSpan.Zero) return "";
            return $"/ {TimeSpanFormatter.Format(ExpectedTime)}";
        }
    }

    /// <summary>
    /// Legacy TimeDisplay for compatibility.
    /// </summary>
    public string TimeDisplay
    {
        get
        {
            if (!IsCurrentMonth || IsFuture) return "";
            if (IsWeekend || IsFullDayAbsence || ExpectedTime == TimeSpan.Zero) return "--";
            if (WorkedTime == TimeSpan.Zero && ExpectedTime > TimeSpan.Zero)
                return $"-- / {TimeSpanFormatter.Format(ExpectedTime)}";

            return $"{TimeSpanFormatter.Format(WorkedTime)} / {TimeSpanFormatter.Format(ExpectedTime)}";
        }
    }

    /// <summary>
    /// Difference display on separate line (e.g., "(+0:17)" or "(-30 min)").
    /// </summary>
    public string DifferenceDisplay
    {
        get
        {
            if (!IsCurrentMonth || IsFuture || IsWeekend || ExpectedTime == TimeSpan.Zero) return "";
            // No difference display for missing entries - shown as "!" indicator instead
            if (WorkedTime == TimeSpan.Zero && ExpectedTime > TimeSpan.Zero) return "";

            var diff = WorkedTime - ExpectedTime;
            if (diff == TimeSpan.Zero) return "";
            var sign = diff >= TimeSpan.Zero ? "+" : "";
            return $"({sign}{FormatDiff(diff)})";
        }
    }

    public void UpdateState()
    {
        if (!IsCurrentMonth)
        {
            State = DayCellState.Empty;
            return;
        }
        if (IsWeekend)
        {
            State = DayCellState.Weekend;
            return;
        }
        if (IsFuture)
        {
            State = DayCellState.Future;
            return;
        }
        if (ExpectedTime == TimeSpan.Zero)
        {
            State = DayCellState.NoWork;
            return;
        }

        // Full-day absences (vacation, sick leave) - treat like NoWork, don't show as missing entry
        if (AbsenceType is AbsenceType.Vacation or AbsenceType.SickLeave or AbsenceType.PublicHoliday)
        {
            State = DayCellState.NoWork;
            return;
        }

        // Missing entry: expected time but nothing booked
        if (WorkedTime == TimeSpan.Zero && ExpectedTime > TimeSpan.Zero)
        {
            State = DayCellState.MissingEntry;
            return;
        }

        if (WorkedTime >= ExpectedTime)
        {
            State = WorkedTime > ExpectedTime ? DayCellState.OverHours : DayCellState.Normal;
        }
        else
        {
            State = DayCellState.UnderHours;
        }
    }

    private static string FormatDiff(TimeSpan t)
    {
        var totalMinutes = (int)t.TotalMinutes;
        if (Math.Abs(totalMinutes) < 60)
            return $"{totalMinutes} min";
        return TimeSpanFormatter.Format(t);
    }
}
