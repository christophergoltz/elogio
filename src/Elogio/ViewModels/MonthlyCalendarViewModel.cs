using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Elogio.Persistence.Dto;
using Elogio.Services;
using Elogio.Utilities;
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
    public string TotalWorkedDisplay => TimeSpanFormatter.Format(TotalWorked);

    /// <summary>
    /// Formatted total expected time (HH:MM).
    /// </summary>
    public string TotalExpectedDisplay => TimeSpanFormatter.Format(TotalExpected);

    /// <summary>
    /// Formatted balance with sign (e.g., "-5:30" or "+2:15").
    /// </summary>
    public string BalanceDisplay => TimeSpanFormatter.FormatWithSign(Balance);

    /// <summary>
    /// Whether the user can navigate to the next month (always enabled).
    /// </summary>
    public bool CanNavigateNext => true;

    public ObservableCollection<Models.DayCellViewModel> DayCells { get; } = [];
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
        // Start absence cache initialization in background (don't block calendar loading)
        // This runs in parallel with LoadMonthDataAsync - the first month's absences
        // will be fetched via GetMonthAbsencesAsync if not yet cached
        _ = Task.Run(async () =>
        {
            try
            {
                await _kelioService.InitializeAbsenceCacheAsync();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Background absence cache initialization failed");
            }
        });

        await LoadMonthDataAsync();
    }

    [RelayCommand]
    private async Task LoadMonthDataAsync()
    {
        Log.Information("LoadMonthDataAsync started for {Year}-{Month}", SelectedYear, SelectedMonth);
        IsLoading = true;
        ErrorMessage = null;

        // Start prefetch for previous month IMMEDIATELY (fire-and-forget)
        // This runs in parallel so when user navigates back, data is likely already cached
        _kelioService.PrefetchAdjacentMonths(SelectedYear, SelectedMonth);

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

            // Ensure at least 2 months buffer in each direction for absence data
            _kelioService.EnsureAbsenceBuffer(SelectedYear, SelectedMonth);
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
            DayCells.Add(new Models.DayCellViewModel { IsCurrentMonth = false });
        }

        // Add cells for each day of the month
        for (var day = 1; day <= daysInMonth; day++)
        {
            var date = new DateOnly(SelectedYear, SelectedMonth, day);
            var isWeekend = date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
            var isToday = date == today;
            var isFuture = date > today;

            var cell = new Models.DayCellViewModel
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
            DayCells.Add(new Models.DayCellViewModel { IsCurrentMonth = false });
        }
    }
}
