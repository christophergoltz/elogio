using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Elogio.Persistence.Dto;
using Elogio.Resources;
using Elogio.Services;
using Elogio.Utilities;
using Elogio.ViewModels.Models;
using Serilog;

namespace Elogio.ViewModels;

public partial class DashboardViewModel : ObservableObject
{
    private readonly IKelioService _kelioService;
    private readonly IToastService _toastService;
    private readonly IPunchService _punchService;

    // Track whether initial data has been loaded (for Singleton pattern)
    private bool _dataLoaded;

    #region Week Overview Properties

    [ObservableProperty]
    private ObservableCollection<Models.DayOverviewItem>? _weekDays;

    [ObservableProperty]
    private string _weekRangeDisplay = string.Empty;

    [ObservableProperty]
    private string _calendarWeekDisplay = string.Empty;

    [ObservableProperty]
    private TimeSpan _weeklyWorked;

    [ObservableProperty]
    private TimeSpan _weeklyExpected;

    [ObservableProperty]
    private TimeSpan _weeklyBalance;

    /// <summary>
    /// Formatted weekly worked time.
    /// </summary>
    public string WeeklyWorkedDisplay => TimeSpanFormatter.Format(WeeklyWorked);

    /// <summary>
    /// Formatted weekly expected time.
    /// </summary>
    public string WeeklyExpectedDisplay => TimeSpanFormatter.Format(WeeklyExpected);

    /// <summary>
    /// Formatted weekly balance with sign.
    /// </summary>
    public string WeeklyBalanceDisplay => TimeSpanFormatter.FormatWithSign(WeeklyBalance);

    /// <summary>
    /// Color for the weekly balance.
    /// </summary>
    public SolidColorBrush WeeklyBalanceBrush => WeeklyBalance < TimeSpan.Zero
        ? AppColors.ErrorBrush
        : WeeklyBalance > TimeSpan.Zero
            ? AppColors.SuccessBrush
            : AppColors.NeutralBrush;

    #endregion

    #region Today's Balance Properties

    [ObservableProperty]
    private string _todayWorkedText = "--:--";

    [ObservableProperty]
    private string _todayExpectedText = "";

    [ObservableProperty]
    private string _todayDifferenceText = "";

    [ObservableProperty]
    private SolidColorBrush _todayDifferenceBrush = AppColors.NeutralBrush;

    [ObservableProperty]
    private bool _hasTimeEntries;

    [ObservableProperty]
    private ObservableCollection<Models.TimeEntryDisplayItem> _timeEntries = [];

    #endregion

    #region Punch Button State (delegated to PunchService)

    public bool? PunchState => _punchService.PunchState;
    public bool IsKommenEnabled => _punchService.IsKommenEnabled;
    public bool IsGehenEnabled => _punchService.IsGehenEnabled;
    public bool IsPunchInProgress => _punchService.IsPunchInProgress;

    #endregion

    #region Absent Colleagues (Placeholder)

    [ObservableProperty]
    private ObservableCollection<AbsentColleagueItem>? _absentColleagues;

    [ObservableProperty]
    private bool _hasAbsentColleagues;

    #endregion

    #region Loading State

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    #endregion

    public DashboardViewModel(IKelioService kelioService, IToastService toastService, IPunchService punchService)
    {
        _kelioService = kelioService;
        _toastService = toastService;
        _punchService = punchService;

        // Subscribe to punch state changes
        _punchService.StateChanged += OnPunchStateChanged;

        InitializePlaceholderData();
    }

    private void OnPunchStateChanged(object? sender, EventArgs e)
    {
        // Notify UI of all punch-related property changes
        OnPropertyChanged(nameof(PunchState));
        OnPropertyChanged(nameof(IsKommenEnabled));
        OnPropertyChanged(nameof(IsGehenEnabled));
        OnPropertyChanged(nameof(IsPunchInProgress));
    }

    /// <summary>
    /// Initialize data as null to indicate loading state.
    /// null = loading, non-null (even if empty) = loaded.
    /// </summary>
    private void InitializePlaceholderData()
    {
        WeekDays = null;
        AbsentColleagues = null;
        HasAbsentColleagues = false;
    }

    /// <summary>
    /// Initialize the dashboard - load all data.
    /// On subsequent calls, refreshes data in background without showing loading spinner.
    /// </summary>
    public async Task InitializeAsync()
    {
        if (_dataLoaded)
        {
            // Already loaded - refresh in background without blocking UI
            _ = RefreshDataInBackgroundAsync();
            return;
        }

        await LoadDashboardDataAsync();
        _dataLoaded = true;
    }

    /// <summary>
    /// Refresh data in background without showing loading spinner.
    /// Used when returning to dashboard from other pages.
    /// </summary>
    private async Task RefreshDataInBackgroundAsync()
    {
        try
        {
            var today = DateTime.Today;
            var weekData = await _kelioService.GetWeekPresenceAsync(DateOnly.FromDateTime(today));

            if (weekData != null)
            {
                // Update on UI thread
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    BuildWeekOverview(weekData);
                    UpdateTodayBalance(weekData, today);
                });
            }

            // Also refresh colleague absences
            await LoadColleagueAbsencesAsync(today);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Background refresh failed");
            // Don't show error to user for background refresh
        }
    }

    /// <summary>
    /// Refresh all dashboard data.
    /// </summary>
    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadDashboardDataAsync();
    }

    /// <summary>
    /// Load all dashboard data (week overview, today's balance, punch state, colleague absences).
    /// </summary>
    private async Task LoadDashboardDataAsync()
    {
        IsLoading = true;
        ErrorMessage = null;

        try
        {
            var today = DateTime.Today;
            var weekData = await _kelioService.GetWeekPresenceAsync(DateOnly.FromDateTime(today));

            if (weekData != null)
            {
                BuildWeekOverview(weekData);
                UpdateTodayBalance(weekData, today);
            }

            // Load colleague absences (fire-and-forget, non-blocking)
            _ = LoadColleagueAbsencesAsync(today);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load dashboard data");
            ErrorMessage = $"Failed to load data: {ex.Message}";
            ResetToDefaultState();
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Load colleague absence data for today.
    /// </summary>
    private async Task LoadColleagueAbsencesAsync(DateTime date)
    {
        try
        {
            // Load current month's data
            var colleagues = await _kelioService.GetColleagueAbsencesAsync(date.Year, date.Month);

            // Also load next month's data for colleagues whose absence might extend beyond current month
            var nextMonth = date.AddMonths(1);
            List<ColleagueAbsenceDto>? nextMonthColleagues = null;
            try
            {
                nextMonthColleagues = await _kelioService.GetColleagueAbsencesAsync(nextMonth.Year, nextMonth.Month);
            }
            catch
            {
                // Ignore errors loading next month - it's optional
            }

            // Filter to colleagues who are absent today and not the current user
            var currentEmployeeId = _kelioService.EmployeeId;
            var absentToday = colleagues
                .Where(c => c.IsAbsentToday && c.EmployeeId != currentEmployeeId)
                .Select(c =>
                {
                    // Find next month's data for this colleague (by employee ID or name)
                    var nextMonthData = nextMonthColleagues?.FirstOrDefault(n =>
                        n.EmployeeId == c.EmployeeId ||
                        (n.EmployeeId == null && n.Name == c.Name));

                    return new AbsentColleagueItem
                    {
                        Name = FormatColleagueName(c.Name),
                        AbsenceInfo = "Abwesend",
                        ReturnDate = CalculateReturnDate(c, nextMonthData, date)
                    };
                })
                .OrderBy(c => c.Name)
                .ToList();

            // Update on UI thread
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                AbsentColleagues = new ObservableCollection<AbsentColleagueItem>(absentToday);
                HasAbsentColleagues = AbsentColleagues.Count > 0;
            });

            Log.Information("LoadColleagueAbsencesAsync: Found {Count} absent colleagues today", absentToday.Count);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load colleague absences");
            // Don't show error to user - colleague absences are non-critical
        }
    }

    /// <summary>
    /// Format colleague name from "Lastname Firstname (ID)" to "Firstname Lastname".
    /// </summary>
    private static string FormatColleagueName(string fullName)
    {
        // Remove the ID suffix like " (14)"
        var nameWithoutId = System.Text.RegularExpressions.Regex.Replace(fullName, @"\s*\(\d+\)$", "");

        // Split by space and reverse (assumes "Lastname Firstname" format)
        var parts = nameWithoutId.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2)
        {
            // Swap first and last name
            return $"{parts[1]} {parts[0]}";
        }

        return nameWithoutId;
    }

    /// <summary>
    /// Calculate the return date based on absence days.
    /// Finds the first workday after the end of the entire absence period,
    /// including any subsequent absence blocks that follow shortly after gaps.
    /// </summary>
    private static string CalculateReturnDate(ColleagueAbsenceDto currentMonth, ColleagueAbsenceDto? nextMonth, DateTime today)
    {
        // Build a set of all absence dates across both months
        var absenceDates = new HashSet<DateOnly>();

        foreach (var day in currentMonth.AbsenceDays)
        {
            absenceDates.Add(new DateOnly(today.Year, today.Month, day));
        }

        if (nextMonth != null)
        {
            var nextMonthDate = new DateOnly(today.Year, today.Month, 1).AddMonths(1);
            foreach (var day in nextMonth.AbsenceDays)
            {
                absenceDates.Add(new DateOnly(nextMonthDate.Year, nextMonthDate.Month, day));
            }
        }

        if (absenceDates.Count == 0)
        {
            return "Unbekannt";
        }

        // Start from tomorrow and find the actual return date
        var currentDate = DateOnly.FromDateTime(today).AddDays(1);
        var maxLookAhead = 90; // Look ahead up to 90 days

        for (var i = 0; i < maxLookAhead; i++)
        {
            var checkDate = currentDate.AddDays(i);
            var isWeekend = checkDate.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
            var isAbsent = absenceDates.Contains(checkDate);

            // Skip weekends and absence days
            if (isWeekend || isAbsent)
            {
                continue;
            }

            // Found a potential return date (workday that's not absent)
            // Check if there are more absences within the next 5 workdays
            // This handles cases like: absent Mon-Thu, Friday free, absent next Mon-Fri
            if (HasMoreAbsencesSoon(checkDate, absenceDates, workdaysToCheck: 5))
            {
                // There are more absences coming soon - this isn't the real return
                continue;
            }

            // This is the actual return date
            return FormatReturnDate(checkDate.ToDateTime(TimeOnly.MinValue));
        }

        return "Unbekannt";
    }

    /// <summary>
    /// Check if there are absence days within the next N workdays from the given date.
    /// This helps detect non-consecutive absence patterns like: absent, free Friday, absent next week.
    /// </summary>
    private static bool HasMoreAbsencesSoon(DateOnly fromDate, HashSet<DateOnly> absenceDates, int workdaysToCheck)
    {
        var workdaysChecked = 0;
        var daysAhead = 1;

        while (workdaysChecked < workdaysToCheck && daysAhead <= 14)
        {
            var checkDate = fromDate.AddDays(daysAhead);
            var isWeekend = checkDate.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

            if (!isWeekend)
            {
                workdaysChecked++;
                if (absenceDates.Contains(checkDate))
                {
                    return true;
                }
            }

            daysAhead++;
        }

        return false;
    }

    /// <summary>
    /// Skip to the next workday (Monday-Friday) if the date falls on a weekend.
    /// </summary>
    private static DateTime SkipToNextWorkday(DateTime date)
    {
        while (date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday)
        {
            date = date.AddDays(1);
        }
        return date;
    }

    /// <summary>
    /// Format the return date with day name for better readability.
    /// </summary>
    private static string FormatReturnDate(DateTime date)
    {
        var dayName = date.DayOfWeek switch
        {
            DayOfWeek.Monday => "Montag",
            DayOfWeek.Tuesday => "Dienstag",
            DayOfWeek.Wednesday => "Mittwoch",
            DayOfWeek.Thursday => "Donnerstag",
            DayOfWeek.Friday => "Freitag",
            DayOfWeek.Saturday => "Samstag",
            DayOfWeek.Sunday => "Sonntag",
            _ => ""
        };

        return $"Zurück am {dayName}, {date:dd.MM.}";
    }

    /// <summary>
    /// Build the week overview from week data.
    /// </summary>
    private void BuildWeekOverview(WeekPresenceDto weekData)
    {
        var newWeekDays = new ObservableCollection<Models.DayOverviewItem>();

        var today = DateOnly.FromDateTime(DateTime.Today);
        var dayNames = new[] { "Mo", "Di", "Mi", "Do", "Fr" };

        // Calculate week totals
        var totalWorked = TimeSpan.Zero;
        var totalExpected = TimeSpan.Zero;

        foreach (var dayData in weekData.Days.OrderBy(d => d.Date))
        {
            // Skip weekends in week overview
            if (dayData.Date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                continue;

            var dayIndex = ((int)dayData.Date.DayOfWeek + 6) % 7; // Monday = 0
            if (dayIndex >= 5) continue; // Safety check

            var item = new Models.DayOverviewItem
            {
                Date = dayData.Date,
                DayName = dayNames[dayIndex],
                DayNumber = dayData.Date.Day.ToString(),
                WorkedTime = dayData.WorkedTime,
                ExpectedTime = dayData.ExpectedTime
            };

            // Determine state
            if (dayData.Date > today)
            {
                item.State = DayOverviewState.Future;
            }
            else if (dayData.ExpectedTime == TimeSpan.Zero)
            {
                item.State = DayOverviewState.Absent;
            }
            else if (dayData.WorkedTime == TimeSpan.Zero && dayData.ExpectedTime > TimeSpan.Zero)
            {
                item.State = DayOverviewState.MissingEntry;
            }
            else if (dayData.WorkedTime >= dayData.ExpectedTime)
            {
                item.State = dayData.WorkedTime > dayData.ExpectedTime
                    ? DayOverviewState.OverHours
                    : DayOverviewState.Normal;
            }
            else
            {
                item.State = DayOverviewState.UnderHours;
            }

            newWeekDays.Add(item);

            // Always accumulate expected time for the full week
            totalExpected += dayData.ExpectedTime;

            // Only accumulate worked time for past and current days
            if (dayData.Date <= today)
            {
                totalWorked += dayData.WorkedTime;
            }
        }

        // Set the new collection (signals loading complete)
        WeekDays = newWeekDays;

        // Update week totals
        WeeklyWorked = totalWorked;
        WeeklyExpected = totalExpected;
        WeeklyBalance = totalWorked - totalExpected;
        OnPropertyChanged(nameof(WeeklyWorkedDisplay));
        OnPropertyChanged(nameof(WeeklyExpectedDisplay));
        OnPropertyChanged(nameof(WeeklyBalanceDisplay));
        OnPropertyChanged(nameof(WeeklyBalanceBrush));

        // Update week range display and calendar week
        if (weekData.Days.Count > 0)
        {
            var firstDay = weekData.Days.Min(d => d.Date);
            var lastDay = weekData.Days.Max(d => d.Date);
            WeekRangeDisplay = $"{firstDay:dd.MM.} - {lastDay:dd.MM.yyyy}";

            // Calculate ISO 8601 calendar week
            var calendarWeek = ISOWeek.GetWeekOfYear(firstDay.ToDateTime(TimeOnly.MinValue));
            CalendarWeekDisplay = $"(KW {calendarWeek:D2})";
        }
    }

    /// <summary>
    /// Update today's balance display from week data.
    /// </summary>
    private void UpdateTodayBalance(WeekPresenceDto weekData, DateTime today)
    {
        var todayData = weekData.Days.FirstOrDefault(d => d.Date == DateOnly.FromDateTime(today));
        if (todayData == null)
        {
            ResetTodayBalance();
            return;
        }

        // Worked time
        var workedHours = (int)todayData.WorkedTime.TotalHours;
        var workedMinutes = todayData.WorkedTime.Minutes;
        TodayWorkedText = $"{workedHours}:{workedMinutes:D2}";

        // Expected time
        var expectedHours = (int)todayData.ExpectedTime.TotalHours;
        var expectedMinutes = todayData.ExpectedTime.Minutes;
        TodayExpectedText = $"/ {expectedHours}:{expectedMinutes:D2}";

        // Difference
        var balance = todayData.WorkedTime - todayData.ExpectedTime;
        var diffHours = (int)Math.Abs(balance.TotalHours);
        var diffMinutes = Math.Abs(balance.Minutes);
        var displaySign = balance < TimeSpan.Zero ? "-" : "+";
        TodayDifferenceText = $"({displaySign}{diffHours}:{diffMinutes:D2})";

        // Color for difference
        TodayDifferenceBrush = balance < TimeSpan.Zero
            ? AppColors.ErrorBrush
            : balance > TimeSpan.Zero
                ? AppColors.SuccessBrush
                : AppColors.NeutralBrush;
        
        // Update time entries display
        UpdateTimeEntriesDisplay(todayData.Entries);

        // Update punch state based on number of entries
        _punchService.UpdateStateFromEntryCount(todayData.Entries.Count);
    }

    /// <summary>
    /// Update the time entries display with badge in/out pairs.
    /// </summary>
    private void UpdateTimeEntriesDisplay(List<TimeEntryDto> entries)
    {
        TimeEntries.Clear();

        if (entries.Count == 0)
        {
            HasTimeEntries = false;
            return;
        }

        HasTimeEntries = true;

        // Group entries into pairs (BadgeIn - BadgeOut)
        var sortedEntries = entries.OrderBy(e => e.Time).ToList();

        for (int i = 0; i < sortedEntries.Count; i += 2)
        {
            var startEntry = sortedEntries[i];
            var endEntry = i + 1 < sortedEntries.Count ? sortedEntries[i + 1] : null;

            var startTime = startEntry.Time.ToString("HH:mm");
            var displayText = endEntry != null
                ? $"{startTime} - {endEntry.Time:HH:mm}"
                : startTime;

            TimeEntries.Add(new Models.TimeEntryDisplayItem { DisplayText = displayText });
        }
    }

    #region Punch Methods

    [RelayCommand]
    private async Task PunchKommenAsync()
    {
        await ExecutePunchAsync();
    }

    [RelayCommand]
    private async Task PunchGehenAsync()
    {
        await ExecutePunchAsync();
    }

    /// <summary>
    /// Execute a punch operation and show toast notification with result.
    /// </summary>
    private async Task ExecutePunchAsync()
    {
        var result = await _punchService.PunchAsync();

        if (result == null)
        {
            ShowToast("Fehler", "Stempeln fehlgeschlagen. Bitte erneut versuchen.", ToastType.Error);
            return;
        }

        if (result.Success)
        {
            var typeText = result.Type == PunchType.ClockIn ? "Kommen" : "Gehen";
            var timeText = result.Timestamp?.ToString("HH:mm") ?? "--:--";
            var message = !string.IsNullOrEmpty(result.Message)
                ? result.Message
                : $"{typeText} um {timeText}";

            ShowToast(result.Label ?? "Buchung erfolgreich", message, ToastType.Success);

            // Refresh dashboard data
            await LoadDashboardDataAsync();
        }
        else
        {
            ShowToast("Fehler", result.Message ?? "Stempeln fehlgeschlagen.", ToastType.Error);
        }
    }

    #endregion

    #region Helper Methods

    private void ResetToDefaultState()
    {
        WeekDays = null;
        AbsentColleagues = null;
        ResetTodayBalance();
        _punchService.Reset();
    }

    private void ResetTodayBalance()
    {
        TodayWorkedText = "--:--";
        TodayExpectedText = "";
        TodayDifferenceText = "";
        TimeEntries.Clear();
        HasTimeEntries = false;
    }

    private void ShowToast(string title, string message, ToastType type)
    {
        _toastService.ShowToast(title, message, type);
    }

    #endregion
}
