using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Elogio.Persistence.Dto;

namespace Elogio.ViewModels;

/// <summary>
/// Represents a day in the week overview on the dashboard.
/// </summary>
public partial class DayOverviewItem : ObservableObject
{
    [ObservableProperty]
    private DateOnly _date;

    [ObservableProperty]
    private string _dayName = string.Empty;

    [ObservableProperty]
    private string _dayNumber = string.Empty;

    [ObservableProperty]
    private TimeSpan _workedTime;

    [ObservableProperty]
    private TimeSpan _expectedTime;

    // Notify computed properties when underlying data changes
    partial void OnWorkedTimeChanged(TimeSpan value)
    {
        OnPropertyChanged(nameof(WorkedDisplay));
        OnPropertyChanged(nameof(DifferenceDisplay));
        OnPropertyChanged(nameof(DifferenceBrush));
    }

    partial void OnExpectedTimeChanged(TimeSpan value)
    {
        OnPropertyChanged(nameof(ExpectedDisplay));
        OnPropertyChanged(nameof(DifferenceDisplay));
        OnPropertyChanged(nameof(DifferenceBrush));
    }

    [ObservableProperty]
    private DayOverviewState _state = DayOverviewState.Normal;

    [ObservableProperty]
    private AbsenceType _absenceType = AbsenceType.None;

    [ObservableProperty]
    private string? _absenceLabel;

    /// <summary>
    /// Formatted worked time (e.g., "7:30").
    /// </summary>
    public string WorkedDisplay => WorkedTime == TimeSpan.Zero ? "--" : FormatTime(WorkedTime);

    /// <summary>
    /// Formatted expected time (e.g., "/ 7:00").
    /// </summary>
    public string ExpectedDisplay => ExpectedTime == TimeSpan.Zero ? "" : $"/ {FormatTime(ExpectedTime)}";

    /// <summary>
    /// Formatted difference with sign (e.g., "+0:30" or "-1:00").
    /// </summary>
    public string DifferenceDisplay
    {
        get
        {
            if (ExpectedTime == TimeSpan.Zero) return "";
            var diff = WorkedTime - ExpectedTime;
            if (diff == TimeSpan.Zero) return "";
            var sign = diff >= TimeSpan.Zero ? "+" : "-";
            var absHours = (int)Math.Abs(diff.TotalHours);
            var absMinutes = Math.Abs(diff.Minutes);
            return $"{sign}{absHours}:{absMinutes:D2}";
        }
    }

    /// <summary>
    /// Color for the difference display.
    /// </summary>
    public SolidColorBrush DifferenceBrush
    {
        get
        {
            var diff = WorkedTime - ExpectedTime;
            return diff < TimeSpan.Zero
                ? new SolidColorBrush(Color.FromRgb(0xF4, 0x43, 0x36)) // Red
                : diff > TimeSpan.Zero
                    ? new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)) // Green
                    : new SolidColorBrush(Colors.Gray);
        }
    }

    /// <summary>
    /// Whether this day has an absence.
    /// </summary>
    public bool HasAbsence => AbsenceType != AbsenceType.None && AbsenceType != AbsenceType.Weekend;

    /// <summary>
    /// Whether this is today.
    /// </summary>
    public bool IsToday => Date == DateOnly.FromDateTime(DateTime.Today);

    private static string FormatTime(TimeSpan t) => $"{(int)t.TotalHours}:{Math.Abs(t.Minutes):D2}";
}
