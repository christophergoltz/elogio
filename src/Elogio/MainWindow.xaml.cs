using System.Windows;
using Elogio.Services;
using Elogio.ViewModels;
using Elogio.Views.Pages;
using Microsoft.Extensions.DependencyInjection;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace Elogio;

/// <summary>
/// Main application window with navigation.
/// </summary>
public partial class MainWindow : FluentWindow
{
    private readonly MainViewModel _viewModel;
    private readonly INavigationService _navigationService;
    private readonly IKelioService _kelioService;

    public MainWindow(
        MainViewModel viewModel,
        INavigationService navigationService,
        IKelioService kelioService)
    {
        InitializeComponent();

        _viewModel = viewModel;
        _navigationService = navigationService;
        _kelioService = kelioService;

        DataContext = _viewModel;

        // Set up navigation service with the content frame
        _navigationService.SetFrame(ContentFrame);
        
        // Initialize theme toggle state
        ThemeToggle.IsChecked = ApplicationThemeManager.GetAppTheme() == ApplicationTheme.Dark;
    }
    
    /// <summary>
    /// Show the loading overlay.
    /// </summary>
    public void ShowLoading()
    {
        LoadingOverlay.Visibility = Visibility.Visible;
        LoginFrame.Visibility = Visibility.Collapsed;
        MainLayout.Visibility = Visibility.Collapsed;
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

        // Load today's balance
        _ = UpdateTodayBalanceAsync();
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
        }
    }

    /// <summary>
    /// Update the time entries display with badge in/out pairs.
    /// </summary>
    private void UpdateTimeEntriesDisplay(List<Elogio.Persistence.Dto.TimeEntryDto> entries)
    {
        TimeEntriesPanel.Children.Clear();

        if (entries == null || entries.Count == 0)
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

    private void MonthlyCalendarButton_Click(object sender, RoutedEventArgs e)
    {
        _navigationService.Navigate<MonthlyCalendarPage>();
    }

    private void LogoutButton_Click(object sender, RoutedEventArgs e)
    {
        _kelioService.Logout();
        NavigateToLogin();
    }

    private void ClockInButton_Click(object sender, RoutedEventArgs e)
    {
        // TODO: Implement clock in when API supports it
    }

    private void ClockOutButton_Click(object sender, RoutedEventArgs e)
    {
        // TODO: Implement clock out when API supports it
    }
    
    private void ThemeToggle_Checked(object sender, RoutedEventArgs e)
    {
        // Dark mode enabled
        ApplicationThemeManager.Apply(ApplicationTheme.Dark);
        ThemeIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.WeatherMoon24;
        ThemeLabel.Text = "Dark Mode";
    }
    
    private void ThemeToggle_Unchecked(object sender, RoutedEventArgs e)
    {
        // Light mode enabled
        ApplicationThemeManager.Apply(ApplicationTheme.Light);
        ThemeIcon.Symbol = Wpf.Ui.Controls.SymbolRegular.WeatherSunny24;
        ThemeLabel.Text = "Light Mode";
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_kelioService is IDisposable disposable)
        {
            disposable.Dispose();
        }
        base.OnClosed(e);
    }
}
