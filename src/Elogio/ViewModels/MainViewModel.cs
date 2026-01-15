using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Elogio.Services;
using Elogio.Views.Pages;

namespace Elogio.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly INavigationService _navigationService;

    [ObservableProperty]
    private string _title;

    public MainViewModel(IUpdateService updateService, INavigationService navigationService)
    {
        _navigationService = navigationService;
        _title = $"Elogio v{updateService.CurrentVersion}";
    }

    [RelayCommand]
    private void NavigateToSettings()
    {
        _navigationService.Navigate<SettingsPage>();
    }

    [RelayCommand]
    private void NavigateToCalendar()
    {
        _navigationService.Navigate<MonthlyCalendarPage>();
    }
}
