using System.Windows;
using Elogio.Services;
using Elogio.ViewModels;
using Elogio.Views.Pages;
using Microsoft.Extensions.DependencyInjection;
using Wpf.Ui.Controls;

namespace Elogio;

/// <summary>
/// Main application window with navigation.
/// Code-behind is minimal - all logic is in MainViewModel.
/// </summary>
public partial class MainWindow
{
    private readonly MainViewModel _viewModel;
    private readonly INavigationService _navigationService;
    private readonly IKelioService _kelioService;
    private readonly IToastService _toastService;
    private readonly Snackbar _snackbar;

    public MainWindow(
        MainViewModel viewModel,
        INavigationService navigationService,
        IKelioService kelioService,
        IToastService toastService)
    {
        InitializeComponent();

        _viewModel = viewModel;
        _navigationService = navigationService;
        _kelioService = kelioService;
        _toastService = toastService;

        DataContext = viewModel;

        // Set up navigation service with the content frame
        _navigationService.SetFrame(ContentFrame);

        // Initialize Snackbar for toast notifications
        _snackbar = new Snackbar(SnackbarPresenter);

        // Subscribe to toast requests from both MainViewModel and ToastService
        _viewModel.ToastRequested += OnToastRequested;
        _toastService.ToastRequested += OnToastRequested;

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
    /// Handle toast notification requests from ViewModel.
    /// </summary>
    private void OnToastRequested(object? sender, ToastNotificationEventArgs e)
    {
        _snackbar.Title = e.Title;
        _snackbar.Content = e.Message;
        _snackbar.Appearance = e.Type switch
        {
            ToastType.Success => ControlAppearance.Success,
            ToastType.Error => ControlAppearance.Danger,
            _ => ControlAppearance.Secondary
        };
        _snackbar.Icon = new SymbolIcon(e.Type switch
        {
            ToastType.Success => SymbolRegular.CheckmarkCircle24,
            ToastType.Error => SymbolRegular.ErrorCircle24,
            _ => SymbolRegular.Info24
        });
        _snackbar.Timeout = e.Type == ToastType.Error ? TimeSpan.FromSeconds(5) : TimeSpan.FromSeconds(4);
        _snackbar.Show();
    }

    /// <summary>
    /// Show the loading overlay with optional status text.
    /// Called from App.xaml.cs during auto-login.
    /// </summary>
    public void ShowLoading(string status = "Connecting...")
    {
        _viewModel.ShowLoading(status);
    }

    /// <summary>
    /// Update the loading status text.
    /// </summary>
    public void UpdateLoadingStatus(string status)
    {
        _viewModel.UpdateLoadingStatus(status);
    }

    /// <summary>
    /// Hide the loading overlay.
    /// </summary>
    public void HideLoading()
    {
        _viewModel.HideLoading();
    }

    /// <summary>
    /// Navigate to the login page.
    /// </summary>
    public void NavigateToLogin(bool showError = false, UserSettings? prefillSettings = null)
    {
        _viewModel.NavigateToLogin();

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
        _viewModel.NavigateToMain();
    }

    private void OnLoginSuccessful(object? sender, EventArgs e)
    {
        NavigateToMain();
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.ToastRequested -= OnToastRequested;
        _toastService.ToastRequested -= OnToastRequested;
        _viewModel.Dispose();

        if (_kelioService is IDisposable disposable)
        {
            disposable.Dispose();
        }

        base.OnClosed(e);
    }
}
