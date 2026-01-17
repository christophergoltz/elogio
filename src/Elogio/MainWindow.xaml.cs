using System.Windows;
using Elogio.Persistence.Dto;
using Elogio.Services;
using Elogio.ViewModels;
using Elogio.Views.Pages;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Wpf.Ui.Controls;

namespace Elogio;

/// <summary>
/// Main application window with navigation.
/// </summary>
public partial class MainWindow
{
    private readonly INavigationService _navigationService;
    private readonly IKelioService _kelioService;
    private readonly IUpdateService _updateService;
    private readonly MainViewModel _viewModel;
    private readonly Snackbar _snackbar;

    // Track current punch state: null = unknown, true = clocked in, false = clocked out
    private bool? _punchState;

    public MainWindow(
        MainViewModel viewModel,
        INavigationService navigationService,
        IKelioService kelioService,
        IUpdateService updateService)
    {
        InitializeComponent();

        _navigationService = navigationService;
        _kelioService = kelioService;
        _updateService = updateService;
        _viewModel = viewModel;

        DataContext = viewModel;

        // Set up navigation service with the content frame
        _navigationService.SetFrame(ContentFrame);

        // Initialize Snackbar
        _snackbar = new Snackbar(SnackbarPresenter);

        // Subscribe to update available event for the banner display
        _updateService.UpdateAvailable += OnUpdateAvailable;

        // Start update checks when window is ready
        ContentRendered += OnContentRendered;
    }

    private async void OnContentRendered(object? sender, EventArgs e)
    {
        // Unsubscribe to only run once
        ContentRendered -= OnContentRendered;

        // Start initial and periodic update checks
        await _viewModel.StartUpdateChecksAsync();
    }
    
    /// <summary>
    /// Show the loading overlay with optional status text.
    /// </summary>
    public void ShowLoading(string status = "Connecting...")
    {
        LoadingStatusText.Text = status;
        LoadingOverlay.Visibility = Visibility.Visible;
        LoginFrame.Visibility = Visibility.Collapsed;
        MainLayout.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Update the loading status text.
    /// </summary>
    public void UpdateLoadingStatus(string status)
    {
        LoadingStatusText.Text = status;
    }

    /// <summary>
    /// Hide the loading overlay.
    /// </summary>
    public void HideLoading()
    {
        LoadingOverlay.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Navigate to the login page.
    /// </summary>
    public void NavigateToLogin(bool showError = false, UserSettings? prefillSettings = null)
    {
        // Hide main layout, show login frame
        MainLayout.Visibility = Visibility.Collapsed;
        LoginFrame.Visibility = Visibility.Visible;

        // Create and navigate to login page
        var loginPage = App.Services.GetRequiredService<LoginPage>();
        loginPage.LoginSuccessful += OnLoginSuccessful;

        if (prefillSettings != null)
        {
            loginPage.PrefillCredentials(prefillSettings, showError);
        }

        LoginFrame.Navigate(loginPage);
    }

    /// <summary>
    /// Navigate to the main content area (after successful login).
    /// </summary>
    public void NavigateToMain()
    {
        // Update employee name
        EmployeeNameText.Text = _kelioService.EmployeeName ?? "Employee";

        // Hide login, show main layout
        LoginFrame.Visibility = Visibility.Collapsed;
        MainLayout.Visibility = Visibility.Visible;

        // Navigate to monthly calendar as default page
        _navigationService.Navigate<MonthlyCalendarPage>();

        // Load today's balance and determine initial punch state
        _ = UpdateTodayBalanceAsync();
    }

    /// <summary>
    /// Handle update available event from UpdateService.
    /// </summary>
    private void OnUpdateAvailable(object? sender, UpdateInfo updateInfo)
    {
        Dispatcher.Invoke(() =>
        {
            UpdateVersionText.Text = $"Version {updateInfo.Version} is available";
            UpdateBanner.Visibility = Visibility.Visible;

            // Add margin to MainLayout so banner doesn't overlap content
            MainLayout.Margin = new Thickness(0, 54, 0, 0);
        });
    }

    /// <summary>
    /// Install the update and restart the application.
    /// </summary>
    private async void InstallUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            InstallUpdateButton.IsEnabled = false;
            UpdateDetailsText.Text = "Downloading update...";

            await _updateService.ApplyUpdateAndRestartAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to install update");
            ShowErrorToast("Update Failed", $"Could not install update: {ex.Message}");
            InstallUpdateButton.IsEnabled = true;
            UpdateDetailsText.Text = "Click 'Install Now' to try again";
        }
    }

    /// <summary>
    /// Dismiss the update banner.
    /// </summary>
    private void DismissUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        UpdateBanner.Visibility = Visibility.Collapsed;

        // Remove margin from MainLayout
        MainLayout.Margin = new Thickness(0);
    }

    /// <summary>
    /// Update today's balance display in the sidebar.
    /// </summary>
    private async Task UpdateTodayBalanceAsync()
    {
        try
        {
            var today = DateTime.Today;
            var weekData = await _kelioService.GetWeekPresenceAsync(DateOnly.FromDateTime(today));

            if (weekData != null)
            {
                // Update employee name if now available (loaded lazily after login)
                var employeeName = _kelioService.EmployeeName;
                if (!string.IsNullOrEmpty(employeeName) && EmployeeNameText.Text == "Employee")
                {
                    EmployeeNameText.Text = employeeName;
                }

                // Find today's data
                var todayData = weekData.Days.FirstOrDefault(d => d.Date == DateOnly.FromDateTime(today));
                if (todayData != null)
                {
                    // Worked time
                    var workedHours = (int)todayData.WorkedTime.TotalHours;
                    var workedMinutes = todayData.WorkedTime.Minutes;
                    TodayWorkedText.Text = $"{workedHours}:{workedMinutes:D2}";

                    // Expected time
                    var expectedHours = (int)todayData.ExpectedTime.TotalHours;
                    var expectedMinutes = todayData.ExpectedTime.Minutes;
                    TodayExpectedText.Text = $"/ {expectedHours}:{expectedMinutes:D2}";

                    // Difference
                    var balance = todayData.WorkedTime - todayData.ExpectedTime;
                    var diffHours = (int)Math.Abs(balance.TotalHours);
                    var diffMinutes = Math.Abs(balance.Minutes);
                    var displaySign = balance < TimeSpan.Zero ? "-" : "+";
                    TodayDifferenceText.Text = $"({displaySign}{diffHours}:{diffMinutes:D2})";

                    // Color for difference
                    TodayDifferenceText.Foreground = balance < TimeSpan.Zero
                        ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF4, 0x43, 0x36))
                        : balance > TimeSpan.Zero
                            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x4C, 0xAF, 0x50))
                            : (System.Windows.Media.Brush)FindResource("TextFillColorSecondaryBrush");

                    // Update time entries display
                    UpdateTimeEntriesDisplay(todayData.Entries);

                    // Determine current punch state based on number of entries
                    // Odd number of entries = clocked in, Even = clocked out
                    _punchState = todayData.Entries.Count % 2 == 1;
                    UpdatePunchButtonState();
                }
            }
        }
        catch
        {
            // Silently fail - balance display is not critical
            TodayWorkedText.Text = "--:--";
            TodayExpectedText.Text = "";
            TodayDifferenceText.Text = "";
            TimeEntriesPanel.Children.Clear();
            TimeEntriesSeparator.Visibility = Visibility.Collapsed;

            // Keep unknown state on error - button stays disabled
            _punchState = null;
            UpdatePunchButtonState();
        }
    }

    /// <summary>
    /// Update the time entries display with badge in/out pairs.
    /// </summary>
    private void UpdateTimeEntriesDisplay(List<TimeEntryDto> entries)
    {
        TimeEntriesPanel.Children.Clear();

        if (entries.Count == 0)
        {
            TimeEntriesSeparator.Visibility = Visibility.Collapsed;
            return;
        }

        TimeEntriesSeparator.Visibility = Visibility.Visible;

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

            var textBlock = new System.Windows.Controls.TextBlock
            {
                Text = displayText,
                FontSize = 10,
                Margin = new Thickness(0, 0, 0, 1)
            };
            // Use dynamic resource binding for theme support
            textBlock.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");

            TimeEntriesPanel.Children.Add(textBlock);
        }
    }

    private void OnLoginSuccessful(object? sender, EventArgs e)
    {
        // Update employee name and switch to main view
        NavigateToMain();
    }

    private void LogoutButton_Click(object sender, RoutedEventArgs e)
    {
        _kelioService.Logout();
        NavigateToLogin();
    }

    private async void PunchButton_Click(object sender, RoutedEventArgs e)
    {
        await ExecutePunchAsync();
    }

    /// <summary>
    /// Execute a punch operation and show toast notification with result.
    /// </summary>
    private async Task ExecutePunchAsync()
    {
        // Disable button during operation
        PunchButton.IsEnabled = false;

        try
        {
            var result = await _kelioService.PunchAsync();

            if (result == null)
            {
                ShowErrorToast("Fehler", "Stempeln fehlgeschlagen. Bitte erneut versuchen.");
                return;
            }

            if (result.Success)
            {
                var typeText = result.Type == PunchType.ClockIn ? "Kommen" : "Gehen";
                var timeText = result.Timestamp?.ToString("HH:mm") ?? "--:--";
                var message = !string.IsNullOrEmpty(result.Message)
                    ? result.Message
                    : $"{typeText} um {timeText}";

                ShowSuccessToast(result.Label ?? "Buchung erfolgreich", message);

                // Update button state based on punch result
                _punchState = result.Type == PunchType.ClockIn;
                UpdatePunchButtonState();

                // Refresh today's balance display
                await UpdateTodayBalanceAsync();
            }
            else
            {
                ShowErrorToast("Fehler", result.Message ?? "Stempeln fehlgeschlagen.");
            }
        }
        catch (Exception ex)
        {
            ShowErrorToast("Fehler", $"Fehler: {ex.Message}");
        }
        finally
        {
            // Re-enable button
            PunchButton.IsEnabled = true;
        }
    }

    /// <summary>
    /// Update the punch button appearance based on current state.
    /// </summary>
    private void UpdatePunchButtonState()
    {
        switch (_punchState)
        {
            case true:
                // Currently clocked in -> show GEHEN button (red/danger)
                PunchButton.Content = "GEHEN";
                PunchButton.Appearance = ControlAppearance.Danger;
                PunchButtonIcon.Symbol = SymbolRegular.ArrowExit20;
                PunchButton.IsEnabled = true;
                break;

            case false:
                // Currently clocked out -> show KOMMEN button (green/success)
                PunchButton.Content = "KOMMEN";
                PunchButton.Appearance = ControlAppearance.Success;
                PunchButtonIcon.Symbol = SymbolRegular.ArrowEnter20;
                PunchButton.IsEnabled = true;
                break;

            default:
                // Unknown state -> show neutral disabled button
                PunchButton.Content = "STEMPELN";
                PunchButton.Appearance = ControlAppearance.Secondary;
                PunchButtonIcon.Symbol = SymbolRegular.Clock24;
                PunchButton.IsEnabled = false;
                break;
        }
    }

    /// <summary>
    /// Show a success toast notification (green).
    /// </summary>
    private void ShowSuccessToast(string title, string message)
    {
        _snackbar.Title = title;
        _snackbar.Content = message;
        _snackbar.Appearance = ControlAppearance.Success;
        _snackbar.Icon = new SymbolIcon(SymbolRegular.CheckmarkCircle24);
        _snackbar.Timeout = TimeSpan.FromSeconds(4);
        _snackbar.Show();
    }

    /// <summary>
    /// Show an error toast notification (red).
    /// </summary>
    private void ShowErrorToast(string title, string message)
    {
        _snackbar.Title = title;
        _snackbar.Content = message;
        _snackbar.Appearance = ControlAppearance.Danger;
        _snackbar.Icon = new SymbolIcon(SymbolRegular.ErrorCircle24);
        _snackbar.Timeout = TimeSpan.FromSeconds(5);
        _snackbar.Show();
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.Dispose();

        if (_kelioService is IDisposable disposable)
        {
            disposable.Dispose();
        }
        base.OnClosed(e);
    }
}
