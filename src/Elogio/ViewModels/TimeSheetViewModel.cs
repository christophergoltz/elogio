using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Elogio.Persistence.Api;
using Elogio.Persistence.Dto;

namespace Elogio.ViewModels;

public partial class TimeSheetViewModel : ObservableObject
{
    private readonly KelioClient _client;

    [ObservableProperty]
    private DateTime _selectedDate = DateTime.Today;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string _weekInfo = "";

    [ObservableProperty]
    private string _employeeName = "";

    [ObservableProperty]
    private string _totalWorked = "";

    [ObservableProperty]
    private string _totalExpected = "";

    [ObservableProperty]
    private string _balance = "";

    [ObservableProperty]
    private ObservableCollection<DayPresenceDtoViewModel> _days = [];

    public TimeSheetViewModel(KelioClient client)
    {
        _client = client;
        _ = LoadWeekAsync();
    }

    [RelayCommand]
    private async Task LoadWeekAsync()
    {
        IsLoading = true;
        ErrorMessage = null;

        try
        {
            WeekInfo = $"Week of {SelectedDate:dd.MM.yyyy}";

            var weekPresence = await _client.GetWeekPresenceAsync(DateOnly.FromDateTime(SelectedDate));

            if (weekPresence != null)
            {
                EmployeeName = weekPresence.EmployeeName;
                TotalWorked = KelioTimeHelpers.FormatTimeSpan(weekPresence.TotalWorked);
                TotalExpected = KelioTimeHelpers.FormatTimeSpan(weekPresence.TotalExpected);
                Balance = KelioTimeHelpers.FormatTimeSpan(weekPresence.Balance);

                Days.Clear();
                foreach (var day in weekPresence.Days)
                {
                    Days.Add(new DayPresenceDtoViewModel(day));
                }
            }
            else
            {
                ErrorMessage = "No data available for this week.";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task PreviousWeekAsync()
    {
        SelectedDate = SelectedDate.AddDays(-7);
        await LoadWeekAsync();
    }

    [RelayCommand]
    private async Task NextWeekAsync()
    {
        SelectedDate = SelectedDate.AddDays(7);
        await LoadWeekAsync();
    }

    [RelayCommand]
    private async Task GoToTodayAsync()
    {
        SelectedDate = DateTime.Today;
        await LoadWeekAsync();
    }
}

/// <summary>
/// ViewModel for a single day's presence data.
/// </summary>
public partial class DayPresenceDtoViewModel : ObservableObject
{
    public DayPresenceDtoViewModel(DayPresenceDto day)
    {
        Date = day.Date.ToString("dd.MM");
        DayOfWeek = day.DayOfWeek;
        WorkedTime = KelioTimeHelpers.FormatTimeSpan(day.WorkedTime);
        ExpectedTime = KelioTimeHelpers.FormatTimeSpan(day.ExpectedTime);
        IsWeekend = day.IsWeekend;
        ScheduleInfo = day.ScheduleInfo ?? "";
    }

    [ObservableProperty]
    private string _date;

    [ObservableProperty]
    private string _dayOfWeek;

    [ObservableProperty]
    private string _workedTime;

    [ObservableProperty]
    private string _expectedTime;

    [ObservableProperty]
    private bool _isWeekend;

    [ObservableProperty]
    private string _scheduleInfo;
}
