using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Elogio.Persistence.Dto;
using Elogio.Services;
using Serilog;

namespace Elogio.ViewModels;

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
    /// Whether the user can navigate to the next month (always enabled).
    /// </summary>
    public bool CanNavigateNext => true;

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
            // Load month data and absences in parallel for better performance
            var monthDataTask = _kelioService.GetMonthDataAsync(SelectedYear, SelectedMonth);
            var absencesTask = _kelioService.GetMonthAbsencesAsync(SelectedYear, SelectedMonth);

            await Task.WhenAll(monthDataTask, absencesTask);

            var monthData = await monthDataTask;
            AbsenceCalendarDto? absences = null;
            try
            {
                absences = await absencesTask;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to load absences for {Year}-{Month}, continuing without absence data",
                    SelectedYear, SelectedMonth);
            }

            Log.Information("Got month data with {DayCount} days, absences: {HasAbsences}",
                monthData.Days.Count, absences != null);

            // Update totals - include projected expected hours for remaining working days
            TotalWorked = monthData.TotalWorked;

            // Calculate projected expected: actual expected + remaining working days × 7 hours
            // Pass absences to exclude vacation, sick leave, holidays from projection
            var projectedExpected = CalculateProjectedExpected(monthData, absences);
            TotalExpected = projectedExpected;
            Balance = TotalWorked - TotalExpected;

            Log.Information("Totals: Worked={Worked}, Expected={Expected} (projected), Balance={Balance}",
                TotalWorked, TotalExpected, Balance);

            // Build calendar grid with absence data
            BuildCalendarGrid(monthData, absences);
            Log.Information("Calendar grid built with {CellCount} cells", DayCells.Count);

            // Prefetch adjacent months for faster navigation
            _kelioService.PrefetchAdjacentMonths(SelectedYear, SelectedMonth);
            _kelioService.PrefetchAdjacentMonthAbsences(SelectedYear, SelectedMonth);
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
    /// Calculate adjusted expected hours for the month.
    /// The Kelio API returns expected hours WITHOUT subtracting absences,
    /// so we need to subtract hours for vacation, sick leave, and holidays.
    /// Also projects remaining working days for future periods.
    /// </summary>
    private TimeSpan CalculateProjectedExpected(MonthData monthData, AbsenceCalendarDto? absences)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var firstDayOfMonth = new DateOnly(SelectedYear, SelectedMonth, 1);
        var daysInMonth = DateTime.DaysInMonth(SelectedYear, SelectedMonth);
        var lastDayOfMonth = new DateOnly(SelectedYear, SelectedMonth, daysInMonth);

        // Start with actual expected from API
        var totalExpected = monthData.TotalExpected;
        var absenceDeduction = TimeSpan.Zero;

        // Build absence lookup for the month
        var absenceLookup = absences?.Days.ToDictionary(d => d.Date, d => d)
                            ?? new Dictionary<DateOnly, AbsenceDayDto>();

        // Get dates that already have data from API
        var datesWithData = monthData.Days.Select(d => d.Date).ToHashSet();

        // STEP 1: Subtract absence hours from past/current days
        // The API includes 7h expected for each working day, even if there was an absence
        foreach (var date in datesWithData)
        {
            if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                continue;

            if (absenceLookup.TryGetValue(date, out var absenceDay))
            {
                // Full-day absences: subtract the full 7 hours
                if (absenceDay.Type is AbsenceType.Vacation or AbsenceType.SickLeave or AbsenceType.PublicHoliday)
                {
                    absenceDeduction += TimeSpan.FromHours(7);
                    Log.Debug("CalculateProjectedExpected: Subtracting 7h for {Date} due to {Type}", date, absenceDay.Type);
                }
                // Half holiday (standalone): subtract 3.5 hours (expect only 3.5h instead of 7h)
                else if (absenceDay.Type == AbsenceType.HalfHoliday)
                {
                    absenceDeduction += TimeSpan.FromHours(3.5);
                    Log.Debug("CalculateProjectedExpected: Subtracting 3.5h for {Date} (half holiday)", date);
                }
                // Combined half holiday (e.g., Vacation + HalfHoliday on Dec 24)
                // The main absence (Vacation) already deducts 7h, no additional deduction needed
            }
        }

        totalExpected -= absenceDeduction;

        // STEP 2: Project future days that don't have API data yet
        // Calculate next Monday (start of next week)
        var daysUntilNextMonday = ((int)DayOfWeek.Monday - (int)today.DayOfWeek + 7) % 7;
        if (daysUntilNextMonday == 0) daysUntilNextMonday = 7;
        var nextMonday = today.AddDays(daysUntilNextMonday);

        // Add expected hours for each future day from next Monday until end of month
        for (var date = nextMonday; date <= lastDayOfMonth; date = date.AddDays(1))
        {
            // Skip if we already have data for this day
            if (datesWithData.Contains(date))
                continue;

            // Skip weekends
            if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                continue;

            // Check for absences
            if (absenceLookup.TryGetValue(date, out var absenceDay))
            {
                // Full-day absences: no expected hours
                if (absenceDay.Type is AbsenceType.Vacation or AbsenceType.SickLeave or AbsenceType.PublicHoliday)
                {
                    Log.Debug("CalculateProjectedExpected: Skipping future {Date} due to {Type}", date, absenceDay.Type);
                    continue;
                }

                // Half holiday: only 3.5 hours expected
                if (absenceDay.Type == AbsenceType.HalfHoliday || absenceDay.IsHalfHoliday)
                {
                    totalExpected += TimeSpan.FromHours(3.5);
                    Log.Debug("CalculateProjectedExpected: Future {Date} is half holiday, adding 3.5h", date);
                    continue;
                }
            }

            // Regular working day: 7 hours expected
            totalExpected += TimeSpan.FromHours(7);
        }

        Log.Information("CalculateProjectedExpected: API={ApiExpected}, Deduction={Deduction}, Projected={Projected}",
            monthData.TotalExpected, absenceDeduction, totalExpected);

        return totalExpected;
    }

    private void BuildCalendarGrid(MonthData monthData, AbsenceCalendarDto? absences)
    {
        DayCells.Clear();

        var firstDayOfMonth = new DateOnly(SelectedYear, SelectedMonth, 1);
        var daysInMonth = DateTime.DaysInMonth(SelectedYear, SelectedMonth);

        // Calculate offset for first day (Monday = 0, Sunday = 6)
        var firstDayOffset = ((int)firstDayOfMonth.DayOfWeek + 6) % 7;

        // Create lookups for day data and absences
        var dayDataLookup = monthData.Days.ToDictionary(d => d.Date, d => d);
        var absenceLookup = absences?.Days.ToDictionary(d => d.Date, d => d)
                            ?? new Dictionary<DateOnly, AbsenceDayDto>();
        var today = DateOnly.FromDateTime(DateTime.Today);

        Log.Information("BuildCalendarGrid: {AbsenceCount} absences in lookup for {Year}-{Month}",
            absenceLookup.Count, SelectedYear, SelectedMonth);

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

            // Fill in work time data if available
            if (dayDataLookup.TryGetValue(date, out var dayData))
            {
                cell.WorkedTime = dayData.WorkedTime;
                cell.ExpectedTime = dayData.ExpectedTime;
            }

            // Fill in absence data if available
            if (absenceLookup.TryGetValue(date, out var absenceDay))
            {
                cell.AbsenceType = absenceDay.Type;

                // Track if this is a combined half-holiday situation for visual display
                cell.IsHalfHolidayCombined = absenceDay.IsHalfHoliday &&
                    absenceDay.Type is AbsenceType.Vacation or AbsenceType.SickLeave;

                // Combine labels when day has vacation/sick AND is also a half holiday
                var baseLabel = AbsenceTypeHelper.GetLabel(absenceDay.Type);
                if (cell.IsHalfHolidayCombined)
                {
                    // Show combined label: "Urlaub + H.Feiertag"
                    cell.AbsenceLabel = $"{baseLabel} + H.Feiertag";
                }
                else if (absenceDay.IsPublicHoliday &&
                    absenceDay.Type is AbsenceType.Vacation or AbsenceType.SickLeave)
                {
                    // Full public holiday with vacation
                    cell.AbsenceLabel = $"{baseLabel} + Feiertag";
                }
                else
                {
                    cell.AbsenceLabel = baseLabel;
                }

                cell.AbsenceBorderColor = AbsenceTypeHelper.GetDisplayColor(absenceDay.Type);
                Log.Debug("Day {Date}: AbsenceType={Type}, Label={Label}, IsHalfHoliday={IsHalfHoliday}, IsHalfHolidayCombined={IsCombined}",
                    date, absenceDay.Type, cell.AbsenceLabel, absenceDay.IsHalfHoliday, cell.IsHalfHolidayCombined);
            }

            // Update state AFTER both work time and absence data are set
            cell.UpdateState();

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
    public string WorkedTimeDisplay => WorkedTime == TimeSpan.Zero ? "--" : FormatTime(WorkedTime);

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
            if (IsWeekend || IsFullDayAbsence || ExpectedTime == TimeSpan.Zero) return "";
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
            if (IsWeekend || IsFullDayAbsence || ExpectedTime == TimeSpan.Zero) return "--";
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
