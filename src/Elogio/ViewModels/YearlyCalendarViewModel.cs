using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Elogio.Persistence.Dto;
using Elogio.Services;
using Serilog;

namespace Elogio.ViewModels;

public partial class YearlyCalendarViewModel : ObservableObject
{
    private readonly IKelioService _kelioService;

    [ObservableProperty]
    private int _selectedYear;

    [ObservableProperty]
    private string _yearDisplay = string.Empty;

    [ObservableProperty]
    private ObservableCollection<MonthAbsenceCard> _months = [];

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    // Yearly totals
    [ObservableProperty]
    private int _totalVacationDays;

    [ObservableProperty]
    private int _totalSickDays;

    [ObservableProperty]
    private int _totalHolidays;

    public YearlyCalendarViewModel(IKelioService kelioService)
    {
        _kelioService = kelioService;
        _selectedYear = DateTime.Today.Year;
        UpdateYearDisplay();
    }

    public async Task InitializeAsync()
    {
        await LoadYearDataAsync();
    }

    [RelayCommand]
    private async Task LoadYearDataAsync()
    {
        Log.Information("LoadYearDataAsync started for year {Year}", SelectedYear);
        IsLoading = true;
        ErrorMessage = null;

        try
        {
            Months.Clear();
            TotalVacationDays = 0;
            TotalSickDays = 0;
            TotalHolidays = 0;

            var today = DateOnly.FromDateTime(DateTime.Today);
            var cultureInfo = new CultureInfo("de-DE");

            // Load all 12 months
            for (int month = 1; month <= 12; month++)
            {
                var monthCard = new MonthAbsenceCard
                {
                    Month = month,
                    Year = SelectedYear,
                    MonthName = new DateTime(SelectedYear, month, 1).ToString("MMMM", cultureInfo)
                };

                // Try to load absences for this month
                try
                {
                    var absences = await _kelioService.GetMonthAbsencesAsync(SelectedYear, month);
                    BuildMonthGrid(monthCard, absences, today);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to load absences for {Year}-{Month}", SelectedYear, month);
                    BuildMonthGrid(monthCard, null, today);
                }

                // Accumulate totals
                TotalVacationDays += monthCard.VacationDayCount;
                TotalSickDays += monthCard.SickDayCount;
                TotalHolidays += monthCard.HolidayCount;

                Months.Add(monthCard);
            }

            Log.Information("Loaded {MonthCount} months for year {Year}", Months.Count, SelectedYear);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load year data");
            ErrorMessage = $"Failed to load data: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void BuildMonthGrid(MonthAbsenceCard monthCard, AbsenceCalendarDto? absences, DateOnly today)
    {
        var firstDay = new DateOnly(monthCard.Year, monthCard.Month, 1);
        var daysInMonth = DateTime.DaysInMonth(monthCard.Year, monthCard.Month);

        // Create absence lookup
        var absenceLookup = absences?.Days.ToDictionary(d => d.Date, d => d)
                            ?? new Dictionary<DateOnly, AbsenceDayDto>();

        int vacationCount = 0;
        int sickCount = 0;
        int holidayCount = 0;

        // Calculate first day offset (Monday = 0)
        var firstDayOffset = ((int)firstDay.DayOfWeek + 6) % 7;

        // Add empty cells for days before first of month
        for (int i = 0; i < firstDayOffset; i++)
        {
            monthCard.Days.Add(new YearlyDayItem { IsCurrentMonth = false });
        }

        // Add days of the month
        for (int day = 1; day <= daysInMonth; day++)
        {
            var date = new DateOnly(monthCard.Year, monthCard.Month, day);
            var isWeekend = date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

            var dayItem = new YearlyDayItem
            {
                Date = date,
                DayNumber = day,
                IsCurrentMonth = true,
                IsWeekend = isWeekend,
                IsToday = date == today
            };

            // Check for absence
            if (absenceLookup.TryGetValue(date, out var absenceDay))
            {
                dayItem.AbsenceType = absenceDay.Type;

                // Count absences
                switch (absenceDay.Type)
                {
                    case AbsenceType.Vacation:
                        vacationCount++;
                        break;
                    case AbsenceType.SickLeave:
                        sickCount++;
                        break;
                    case AbsenceType.PublicHoliday:
                    case AbsenceType.HalfHoliday:
                        holidayCount++;
                        break;
                }
            }

            monthCard.Days.Add(dayItem);
        }

        // Fill remaining cells to complete the grid (6 rows x 7 = 42)
        while (monthCard.Days.Count < 42)
        {
            monthCard.Days.Add(new YearlyDayItem { IsCurrentMonth = false });
        }

        monthCard.VacationDayCount = vacationCount;
        monthCard.SickDayCount = sickCount;
        monthCard.HolidayCount = holidayCount;
    }

    [RelayCommand]
    private async Task PreviousYearAsync()
    {
        SelectedYear--;
        UpdateYearDisplay();
        await LoadYearDataAsync();
    }

    [RelayCommand]
    private async Task NextYearAsync()
    {
        SelectedYear++;
        UpdateYearDisplay();
        await LoadYearDataAsync();
    }

    [RelayCommand]
    private async Task GoToCurrentYearAsync()
    {
        SelectedYear = DateTime.Today.Year;
        UpdateYearDisplay();
        await LoadYearDataAsync();
    }

    private void UpdateYearDisplay()
    {
        YearDisplay = SelectedYear.ToString();
    }
}
