using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Elogio.Services;
using Elogio.Views.Pages;
using Serilog;
using Wpf.Ui.Controls;

namespace Elogio.ViewModels;

/// <summary>
/// Status of the update check operation.
/// </summary>
public enum UpdateCheckStatus
{
    Idle,
    Checking,
    NoUpdates,
    UpdateAvailable,
    Error
}

public partial class MainViewModel : ObservableObject
{
    private readonly INavigationService _navigationService;
    private readonly IUpdateService _updateService;

    [ObservableProperty]
    private string _title;

    [ObservableProperty]
    private UpdateCheckStatus _updateStatus = UpdateCheckStatus.Idle;

    [ObservableProperty]
    private string _updateStatusText = string.Empty;

    [ObservableProperty]
    private SolidColorBrush _updateStatusBrush = new(Colors.Gray);

    [ObservableProperty]
    private SymbolRegular _updateStatusIcon = SymbolRegular.Info24;

    [ObservableProperty]
    private bool _isUpdateStatusVisible;

    public MainViewModel(IUpdateService updateService, INavigationService navigationService)
    {
        _navigationService = navigationService;
        _updateService = updateService;
        _title = $"Elogio v{updateService.CurrentVersion}";

        // Subscribe to update available event
        _updateService.UpdateAvailable += OnUpdateAvailable;
    }

    /// <summary>
    /// Check for updates and update the status display.
    /// </summary>
    public async Task CheckForUpdatesAsync()
    {
        SetUpdateStatus(UpdateCheckStatus.Checking);

        try
        {
            await _updateService.CheckForUpdatesAsync();

            // If no update was found (event not fired), set NoUpdates status
            if (UpdateStatus == UpdateCheckStatus.Checking)
            {
                SetUpdateStatus(UpdateCheckStatus.NoUpdates);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Update check failed");
            SetUpdateStatus(UpdateCheckStatus.Error);
        }
    }

    private void OnUpdateAvailable(object? sender, UpdateInfo updateInfo)
    {
        // Ensure UI update happens on the dispatcher thread
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            SetUpdateStatus(UpdateCheckStatus.UpdateAvailable, updateInfo.Version);
        });
    }

    private void SetUpdateStatus(UpdateCheckStatus status, string? version = null)
    {
        UpdateStatus = status;
        IsUpdateStatusVisible = status != UpdateCheckStatus.Idle;

        switch (status)
        {
            case UpdateCheckStatus.Checking:
                UpdateStatusText = "Checking for updates...";
                UpdateStatusBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xA5, 0x00)); // Orange
                UpdateStatusIcon = SymbolRegular.ArrowSync24;
                break;

            case UpdateCheckStatus.NoUpdates:
                UpdateStatusText = "Up to date";
                UpdateStatusBrush = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50)); // Green
                UpdateStatusIcon = SymbolRegular.CheckmarkCircle24;
                break;

            case UpdateCheckStatus.UpdateAvailable:
                UpdateStatusText = $"Update {version} available";
                UpdateStatusBrush = new SolidColorBrush(Color.FromRgb(0x21, 0x96, 0xF3)); // Blue
                UpdateStatusIcon = SymbolRegular.ArrowDownload24;
                break;

            case UpdateCheckStatus.Error:
                UpdateStatusText = "Update check failed";
                UpdateStatusBrush = new SolidColorBrush(Color.FromRgb(0xF4, 0x43, 0x36)); // Red
                UpdateStatusIcon = SymbolRegular.ErrorCircle24;
                break;

            default:
                UpdateStatusText = string.Empty;
                UpdateStatusBrush = new SolidColorBrush(Colors.Gray);
                UpdateStatusIcon = SymbolRegular.Info24;
                break;
        }
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
