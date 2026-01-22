using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Elogio.Resources;
using Elogio.Services;
using Elogio.Views.Pages;
using Serilog;
using Wpf.Ui.Controls;

namespace Elogio.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private const int UpdateCheckIntervalMinutes = 30;

    private readonly INavigationService _navigationService;
    private readonly IKelioService _kelioService;
    private readonly IUpdateService _updateService;
    private readonly DispatcherTimer _updateCheckTimer;
    private bool _disposed;

    #region Observable Properties

    [ObservableProperty]
    private string _title;

    // Update status properties
    [ObservableProperty]
    private UpdateCheckStatus _updateStatus = UpdateCheckStatus.Idle;

    [ObservableProperty]
    private string _updateStatusText = string.Empty;

    [ObservableProperty]
    private SolidColorBrush _updateStatusBrush = AppColors.NeutralBrush;

    [ObservableProperty]
    private SymbolRegular _updateStatusIcon = SymbolRegular.Info24;

    [ObservableProperty]
    private bool _isUpdateStatusVisible;

    // Update banner properties
    [ObservableProperty]
    private bool _isUpdateBannerVisible;

    [ObservableProperty]
    private string _updateBannerVersionText = string.Empty;

    [ObservableProperty]
    private string _updateBannerDetailsText = "Click 'Install Now' to update and restart";

    [ObservableProperty]
    private bool _isInstallUpdateEnabled = true;

    // Employee properties
    [ObservableProperty]
    private string _employeeName = "Employee";

    // Current page type for navigation highlighting
    [ObservableProperty]
    private Type? _currentPageType;

    // View state properties
    [ObservableProperty]
    private bool _isMainLayoutVisible;

    [ObservableProperty]
    private bool _isLoginVisible = true;

    [ObservableProperty]
    private bool _isLoadingVisible;

    [ObservableProperty]
    private string _loadingStatusText = "Connecting...";

    // Toast notification (for code-behind to display)
    public event EventHandler<ToastNotificationEventArgs>? ToastRequested;

    #endregion

    public MainViewModel(
        IUpdateService updateService,
        INavigationService navigationService,
        IKelioService kelioService)
    {
        _navigationService = navigationService;
        _updateService = updateService;
        _kelioService = kelioService;
        _title = $"Elogio {updateService.CurrentVersion}";

        // Subscribe to update available event
        _updateService.UpdateAvailable += OnUpdateAvailable;

        // Setup periodic update check timer
        _updateCheckTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(UpdateCheckIntervalMinutes)
        };
        _updateCheckTimer.Tick += async (_, _) => await CheckForUpdatesAsync();
    }

    #region Update Check Methods

    /// <summary>
    /// Start initial update check and periodic timer.
    /// Call this when the window is ready.
    /// </summary>
    public async Task StartUpdateChecksAsync()
    {
        // Initial check
        await CheckForUpdatesAsync();

        // Start periodic checks (only if not already found an update)
        if (UpdateStatus != UpdateCheckStatus.UpdateAvailable)
        {
            _updateCheckTimer.Start();
            Log.Information("Periodic update check started (every {Minutes} minutes)", UpdateCheckIntervalMinutes);
        }
    }

    /// <summary>
    /// Stop periodic update checks.
    /// </summary>
    public void StopUpdateChecks()
    {
        _updateCheckTimer.Stop();
        Log.Information("Periodic update check stopped");
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
        Application.Current.Dispatcher.Invoke(() =>
        {
            SetUpdateStatus(UpdateCheckStatus.UpdateAvailable, updateInfo.Version);

            // Show update banner
            UpdateBannerVersionText = $"Version {updateInfo.Version} is available";
            IsUpdateBannerVisible = true;
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
                UpdateStatusBrush = AppColors.WarningBrush;
                UpdateStatusIcon = SymbolRegular.ArrowSync24;
                break;

            case UpdateCheckStatus.NoUpdates:
                UpdateStatusText = "Up to date";
                UpdateStatusBrush = AppColors.SuccessBrush;
                UpdateStatusIcon = SymbolRegular.CheckmarkCircle24;
                break;

            case UpdateCheckStatus.UpdateAvailable:
                UpdateStatusText = $"Update {version} available";
                UpdateStatusBrush = AppColors.InfoBrush;
                UpdateStatusIcon = SymbolRegular.ArrowDownload24;
                // Stop periodic checks once update is found
                _updateCheckTimer.Stop();
                break;

            case UpdateCheckStatus.Error:
                UpdateStatusText = "Update check failed";
                UpdateStatusBrush = AppColors.ErrorBrush;
                UpdateStatusIcon = SymbolRegular.ErrorCircle24;
                break;

            default:
                UpdateStatusText = string.Empty;
                UpdateStatusBrush = AppColors.NeutralBrush;
                UpdateStatusIcon = SymbolRegular.Info24;
                break;
        }
    }

    [RelayCommand]
    private async Task InstallUpdateAsync()
    {
        try
        {
            IsInstallUpdateEnabled = false;
            UpdateBannerDetailsText = "Downloading update...";

            await _updateService.ApplyUpdateAndRestartAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to install update");
            ShowToast("Update Failed", $"Could not install update: {ex.Message}", ToastType.Error);
            IsInstallUpdateEnabled = true;
            UpdateBannerDetailsText = "Click 'Install Now' to try again";
        }
    }

    [RelayCommand]
    private void DismissUpdateBanner()
    {
        IsUpdateBannerVisible = false;
    }

    #endregion

    #region Navigation Methods

    [RelayCommand]
    private void NavigateToDashboard()
    {
        _navigationService.Navigate<DashboardPage>();
        CurrentPageType = typeof(DashboardPage);
    }

    [RelayCommand]
    private void NavigateToCalendar()
    {
        _navigationService.Navigate<MonthlyCalendarPage>();
        CurrentPageType = typeof(MonthlyCalendarPage);
    }

    [RelayCommand]
    private void NavigateToYearlyCalendar()
    {
        _navigationService.Navigate<YearlyCalendarPage>();
        CurrentPageType = typeof(YearlyCalendarPage);
    }

    [RelayCommand]
    private void NavigateToSettings()
    {
        _navigationService.Navigate<SettingsPage>();
        CurrentPageType = typeof(SettingsPage);
    }

    /// <summary>
    /// Navigate to main content after successful login.
    /// </summary>
    public void NavigateToMain()
    {
        // Update employee name
        EmployeeName = _kelioService.EmployeeName ?? "Employee";

        // Switch views
        IsLoginVisible = false;
        IsMainLayoutVisible = true;

        // Navigate to dashboard as default page
        _navigationService.Navigate<DashboardPage>();
        CurrentPageType = typeof(DashboardPage);
    }

    /// <summary>
    /// Navigate to login page.
    /// </summary>
    public void NavigateToLogin()
    {
        IsMainLayoutVisible = false;
        IsLoginVisible = true;
    }

    [RelayCommand]
    private void Logout()
    {
        _kelioService.Logout();
        NavigateToLogin();
    }

    #endregion

    #region Loading Overlay Methods

    public void ShowLoading(string status = "Connecting...")
    {
        LoadingStatusText = status;
        IsLoadingVisible = true;
        IsLoginVisible = false;
        IsMainLayoutVisible = false;
    }

    public void UpdateLoadingStatus(string status)
    {
        LoadingStatusText = status;
    }

    public void HideLoading()
    {
        IsLoadingVisible = false;
    }

    #endregion

    #region Toast Notification

    private void ShowToast(string title, string message, ToastType type)
    {
        ToastRequested?.Invoke(this, new ToastNotificationEventArgs(title, message, type));
    }

    #endregion

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _updateCheckTimer.Stop();
        _updateService.UpdateAvailable -= OnUpdateAvailable;
    }
}
