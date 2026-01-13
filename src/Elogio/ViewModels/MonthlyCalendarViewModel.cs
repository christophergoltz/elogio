using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Elogio.Core.Models;
using Elogio.Desktop.Services;
using Serilog;

namespace Elogio.Desktop.ViewModels;

public partial class MonthlyCalendarViewModel : ObservableObject
{
    private readonly IKelioService _kelioService;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanNavigateNext))]
    private int _selectedYear;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanNavigateNext))]
    private int _selectedMonth;

    [ObservableProperty]
    private string _monthYearDisplay = "";

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalWorkedDisplay))]
    private TimeSpan _totalWorked;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalExpectedDisplay))]
    private TimeSpan _totalExpected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BalanceDisplay))]
    private TimeSpan _balance;

    /// <summary>
    /// Formatted total worked time (HH:MM).
    /// </summary>
    public string TotalWorkedDisplay => FormatTimeSpan(TotalWorked);

    /// <summary>
    /// Formatted total expected time (HH:MM).
    /// </summary>
    public string TotalExpectedDisplay => FormatTimeSpan(TotalExpected);

    /// <summary>
    /// Formatted balance with sign (e.g., "-5:30" or "+2:15").
    /// </summary>
    public string BalanceDisplay => FormatTimeSpanWithSign(Balance);

    /// <summary>
    /// Whether the user can navigate to the next month (disabled for current/future months).
    /// </summary>
    public bool CanNavigateNext =>
        SelectedYear < DateTime.Today.Year ||
        (SelectedYear == DateTime.Today.Year && SelectedMonth < DateTime.Today.Month);

    public ObservableCollection<DayCellViewModel> DayCells { get; } = [];
    public ObservableCollection<string> DayHeaders { get; } = ["Mo", "Tu", "We", "Th", "Fr", "Sa", "Su"];

    public MonthlyCalendarViewModel(IKelioService kelioService)
    {
        _kelioService = kelioService;

        // Start with current month
        var today = DateTime.Today;
        _selectedYear = today.Year;
        _selectedMonth = today.Month;
        UpdateMonthYearDisplay();
    }

    public async Task InitializeAsync()
    {
        await LoadMonthDataAsync();
    }

    [RelayCommand]
    private async Task LoadMonthDataAsync()
    {
        Log.Information("LoadMonthDataAsync started for {Year}-{Month}", SelectedYear, SelectedMonth);
        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var monthData = await _kelioService.GetMonthDataAsync(SelectedYear, SelectedMonth);
            Log.Information("Got month data with {DayCount} days", monthData.Days.Count);

            // Update totals - include projected expected hours for remaining working days
            TotalWorked = monthData.TotalWorked;

            // Calculate projected expected: actual expected + remaining working days × 7 hours
            var projectedExpected = CalculateProjectedExpected(monthData);
            TotalExpected = projectedExpected;
            Balance = TotalWorked - TotalExpected;

            Log.Information("Totals: Worked={Worked}, Expected={Expected} (projected), Balance={Balance}",
                TotalWorked, TotalExpected, Balance);

            // Build calendar grid
            BuildCalendarGrid(monthData);
            Log.Information("Calendar grid built with {CellCount} cells", DayCells.Count);

            // Prefetch adjacent months for faster navigation
            _kelioService.PrefetchAdjacentMonths(SelectedYear, SelectedMonth);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load month data");
            ErrorMessage = $"Failed to load data: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
            Log.Information("LoadMonthDataAsync completed, IsLoading={IsLoading}", IsLoading);
        }
    }

    [RelayCommand]
    private async Task PreviousMonthAsync()
    {
        if (SelectedMonth == 1)
        {
            SelectedMonth = 12;
            SelectedYear--;
        }
        else
        {
            SelectedMonth--;
        }

        UpdateMonthYearDisplay();
        await LoadMonthDataAsync();
    }

    [RelayCommand]
    private async Task NextMonthAsync()
    {
        if (SelectedMonth == 12)
        {
            SelectedMonth = 1;
            SelectedYear++;
        }
        else
        {
            SelectedMonth++;
        }

        UpdateMonthYearDisplay();
        await LoadMonthDataAsync();
    }

    [RelayCommand]
    private async Task GoToTodayAsync()
    {
        var today = DateTime.Today;
        SelectedYear = today.Year;
        SelectedMonth = today.Month;
        UpdateMonthYearDisplay();
        await LoadMonthDataAsync();
    }

    private void UpdateMonthYearDisplay()
    {
        var date = new DateTime(SelectedYear, SelectedMonth, 1);
        MonthYearDisplay = date.ToString("MMMM yyyy", CultureInfo.CurrentCulture);
    }

    /// <summary>
    /// Calculate projected expected hours for the month.
    /// Kelio API returns data up to the end of the current week, so we project
    /// from next Monday until the end of the month.
    /// </summary>
    private TimeSpan CalculateProjectedExpected(MonthData monthData)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var daysInMonth = DateTime.DaysInMonth(SelectedYear, SelectedMonth);
        var lastDayOfMonth = new DateOnly(SelectedYear, SelectedMonth, daysInMonth);

        // Start with actual expected from API
        var totalExpected = monthData.TotalExpected;

        // Calculate next Monday (start of next week)
        // DayOfWeek: Sunday=0, Monday=1, ..., Saturday=6
        var daysUntilNextMonday = ((int)DayOfWeek.Monday - (int)today.DayOfWeek + 7) % 7;
        if (daysUntilNextMonday == 0) daysUntilNextMonday = 7; // If today is Monday, next Monday is in 7 days
        var nextMonday = today.AddDays(daysUntilNextMonday);

        // Get dates that already have data
        var datesWithData = monthData.Days.Select(d => d.Date).ToHashSet();

        // Add 7 hours for each working day from next Monday until end of month
        for (var date = nextMonday; date <= lastDayOfMonth; date = date.AddDays(1))
        {
            // Skip if we already have data for this day (shouldn't happen, but just in case)
            if (datesWithData.Contains(date))
                continue;

            // Skip weekends
            if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                continue;

            // Add 7 hours expected for this working day
            totalExpected += TimeSpan.FromHours(7);
        }

        Log.Information("CalculateProjectedExpected: API expected={ApiExpected}, NextMonday={NextMonday}, Projected={Projected}",
            monthData.TotalExpected, nextMonday, totalExpected);

        return totalExpected;
    }

    private void BuildCalendarGrid(MonthData monthData)
    {
        DayCells.Clear();

        var firstDayOfMonth = new DateOnly(SelectedYear, SelectedMonth, 1);
        var daysInMonth = DateTime.DaysInMonth(SelectedYear, SelectedMonth);

        // Calculate offset for first day (Monday = 0, Sunday = 6)
        var firstDayOffset = ((int)firstDayOfMonth.DayOfWeek + 6) % 7;

        // Create lookup for day data
        var dayDataLookup = monthData.Days.ToDictionary(d => d.Date, d => d);
        var today = DateOnly.FromDateTime(DateTime.Today);

        // Add empty cells for days before the first of the month
        for (var i = 0; i < firstDayOffset; i++)
        {
            DayCells.Add(new DayCellViewModel { IsCurrentMonth = false });
        }

        // Add cells for each day of the month
        for (var day = 1; day <= daysInMonth; day++)
        {
            var date = new DateOnly(SelectedYear, SelectedMonth, day);
            var isWeekend = date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
            var isToday = date == today;
            var isFuture = date > today;

            var cell = new DayCellViewModel
            {
                DayNumber = day,
                Date = date,
                IsCurrentMonth = true,
                IsToday = isToday,
                IsWeekend = isWeekend,
                IsFuture = isFuture
            };

            // Fill in data if available
            if (dayDataLookup.TryGetValue(date, out var dayData))
            {
                cell.WorkedTime = dayData.WorkedTime;
                cell.ExpectedTime = dayData.ExpectedTime;
                cell.UpdateState();
            }

            DayCells.Add(cell);
        }

        // Add trailing empty cells to complete the grid (6 rows x 7 columns = 42)
        while (DayCells.Count < 42)
        {
            DayCells.Add(new DayCellViewModel { IsCurrentMonth = false });
        }
    }

    /// <summary>
    /// Format a TimeSpan as total hours:minutes (e.g., "50:30" for 50h 30m).
    /// </summary>
    private static string FormatTimeSpan(TimeSpan time)
    {
        var totalHours = (int)Math.Abs(time.TotalHours);
        var minutes = Math.Abs(time.Minutes);
        return $"{totalHours}:{minutes:D2}";
    }

    /// <summary>
    /// Format a TimeSpan with sign (e.g., "-5:30" or "+2:15").
    /// </summary>
    private static string FormatTimeSpanWithSign(TimeSpan time)
    {
        var totalHours = (int)Math.Abs(time.TotalHours);
        var minutes = Math.Abs(time.Minutes);
        var sign = time < TimeSpan.Zero ? "-" : "+";
        // Don't show + for zero
        if (time == TimeSpan.Zero) sign = "";
        return $"{sign}{totalHours}:{minutes:D2}";
    }
}

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

    /// <summary>
    /// Simple worked time display (e.g., "7:30" or "--").
    /// </summary>
    public string WorkedTimeDisplay => WorkedTime == TimeSpan.Zero ? "--" : FormatTime(WorkedTime);

    /// <summary>
    /// Worked time display (e.g., "6:15" or "--").
    /// </summary>
    public string WorkedDisplay
    {
        get
        {
            if (!IsCurrentMonth || IsFuture) return "";
            if (IsWeekend || ExpectedTime == TimeSpan.Zero) return "--";
            if (WorkedTime == TimeSpan.Zero) return "--";
            return FormatTime(WorkedTime);
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
            if (IsWeekend || ExpectedTime == TimeSpan.Zero) return "";
            return $"/ {FormatTime(ExpectedTime)}";
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
            if (IsWeekend || ExpectedTime == TimeSpan.Zero) return "--";
            if (WorkedTime == TimeSpan.Zero && ExpectedTime > TimeSpan.Zero)
                return $"-- / {FormatTime(ExpectedTime)}";

            return $"{FormatTime(WorkedTime)} / {FormatTime(ExpectedTime)}";
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

    private static string FormatTime(TimeSpan t) => $"{(int)t.TotalHours}:{Math.Abs(t.Minutes):D2}";

    private static string FormatDiff(TimeSpan t)
    {
        var totalMinutes = (int)t.TotalMinutes;
        if (Math.Abs(totalMinutes) < 60)
            return $"{totalMinutes} min";
        return FormatTime(t);
    }
}

public enum DayCellState
{
    Empty,
    Normal,
    OverHours,
    UnderHours,
    MissingEntry,  // Expected > 0 but no time booked
    Weekend,
    Future,
    NoWork
}
