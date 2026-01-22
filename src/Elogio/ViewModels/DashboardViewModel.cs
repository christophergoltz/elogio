using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Elogio.Persistence.Dto;
using Elogio.Resources;
using Elogio.Services;
using Elogio.Utilities;
using Serilog;

namespace Elogio.ViewModels;

public partial class DashboardViewModel : ObservableObject
{
    private readonly IKelioService _kelioService;
    private readonly IToastService _toastService;

    // Track whether initial data has been loaded (for Singleton pattern)
    private bool _dataLoaded;

    #region Week Overview Properties

    [ObservableProperty]
    private ObservableCollection<DayOverviewItem> _weekDays = [];

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
    private ObservableCollection<TimeEntryDisplayItem> _timeEntries = [];

    #endregion

    #region Punch Button State

    // null = unknown, true = clocked in (show Gehen enabled), false = clocked out (show Kommen enabled)
    [ObservableProperty]
    private bool? _punchState;

    [ObservableProperty]
    private bool _isKommenEnabled;

    [ObservableProperty]
    private bool _isGehenEnabled;

    [ObservableProperty]
    private bool _isPunchInProgress;

    #endregion

    #region Absent Colleagues (Placeholder)

    [ObservableProperty]
    private ObservableCollection<AbsentColleagueItem> _absentColleagues = [];

    [ObservableProperty]
    private bool _hasAbsentColleagues;

    #endregion

    #region Loading State

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    #endregion

    public DashboardViewModel(IKelioService kelioService, IToastService toastService)
    {
        _kelioService = kelioService;
        _toastService = toastService;
        InitializePlaceholderData();
    }

    /// <summary>
    /// Initialize placeholder data for absent colleagues.
    /// </summary>
    private void InitializePlaceholderData()
    {
        AbsentColleagues =
        [
            new AbsentColleagueItem { Name = "Max Mustermann", AbsenceInfo = "Urlaub", ReturnDate = "Zurück am 27.01." },
            new AbsentColleagueItem { Name = "Erika Musterfrau", AbsenceInfo = "Krank", ReturnDate = "Unbekannt" }
        ];
        HasAbsentColleagues = AbsentColleagues.Count > 0;
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
    /// Load all dashboard data (week overview, today's balance, punch state).
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
    /// Build the week overview from week data.
    /// </summary>
    private void BuildWeekOverview(WeekPresenceDto weekData)
    {
        WeekDays.Clear();

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

            var item = new DayOverviewItem
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

            WeekDays.Add(item);

            // Always accumulate expected time for the full week
            totalExpected += dayData.ExpectedTime;

            // Only accumulate worked time for past and current days
            if (dayData.Date <= today)
            {
                totalWorked += dayData.WorkedTime;
            }
        }

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

        // Determine current punch state based on number of entries
        // Odd number of entries = clocked in, Even = clocked out
        PunchState = todayData.Entries.Count % 2 == 1;
        UpdatePunchButtonState();
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

            TimeEntries.Add(new TimeEntryDisplayItem { DisplayText = displayText });
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
        // Disable buttons during operation
        IsPunchInProgress = true;
        UpdatePunchButtonState();

        try
        {
            var result = await _kelioService.PunchAsync();

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

                // Update button state based on punch result
                PunchState = result.Type == PunchType.ClockIn;
                UpdatePunchButtonState();

                // Refresh dashboard data
                await LoadDashboardDataAsync();
            }
            else
            {
                ShowToast("Fehler", result.Message ?? "Stempeln fehlgeschlagen.", ToastType.Error);
            }
        }
        catch (Exception ex)
        {
            ShowToast("Fehler", $"Fehler: {ex.Message}", ToastType.Error);
        }
        finally
        {
            // Re-enable buttons
            IsPunchInProgress = false;
            UpdatePunchButtonState();
        }
    }

    /// <summary>
    /// Update the punch button enabled states based on current state.
    /// </summary>
    private void UpdatePunchButtonState()
    {
        if (IsPunchInProgress)
        {
            IsKommenEnabled = false;
            IsGehenEnabled = false;
            return;
        }

        switch (PunchState)
        {
            case true:
                // Currently clocked in -> GEHEN is the expected action
                IsKommenEnabled = false;
                IsGehenEnabled = true;
                break;

            case false:
                // Currently clocked out -> KOMMEN is the expected action
                IsKommenEnabled = true;
                IsGehenEnabled = false;
                break;

            default:
                // Unknown state -> both disabled
                IsKommenEnabled = false;
                IsGehenEnabled = false;
                break;
        }
    }

    #endregion

    #region Helper Methods

    private void ResetToDefaultState()
    {
        WeekDays.Clear();
        ResetTodayBalance();
        PunchState = null;
        UpdatePunchButtonState();
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
