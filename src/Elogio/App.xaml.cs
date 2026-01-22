using System.IO;
using System.Windows;
using Elogio.Services;
using Elogio.ViewModels;
using Elogio.Views.Pages;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Wpf.Ui.Appearance;

namespace Elogio;

public partial class App
{
    private static IServiceProvider? _serviceProvider;

    /// <summary>
    /// Global service provider for dependency injection.
    /// </summary>
    public static IServiceProvider Services => _serviceProvider
        ?? throw new InvalidOperationException("Services not initialized");

    protected override async void OnStartup(StartupEventArgs e)
    {
        // Configure logging
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Elogio",
                    "logs",
                    "elogio-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7)
            .CreateLogger();

        Log.Information("Elogio starting up...");

        // Build service provider
        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();

        // Load settings and apply saved theme
        var settingsService = Services.GetRequiredService<ISettingsService>();
        var settings = settingsService.Load();
        ApplicationThemeManager.Apply(settings.IsDarkMode ? ApplicationTheme.Dark : ApplicationTheme.Light);

        MainWindow mainWindow;

        // Create and show MainWindow immediately
        mainWindow = Services.GetRequiredService<MainWindow>();
        MainWindow = mainWindow;
        mainWindow.Show();

        if (settings.RememberCredentials && !string.IsNullOrEmpty(settings.Password))
        {
            Log.Information("Attempting auto-login for user {Username}", settings.Username);

            // Show loading overlay in main window (faster than separate window)
            mainWindow.ShowLoading("Starting...");

            try
            {
                var kelioService = Services.GetRequiredService<IKelioService>();

                // Pre-initialize server + prefetch login page while showing loading screen
                // This saves ~4.6s of perceived login time
                mainWindow.UpdateLoadingStatus("Initializing connection...");
                await kelioService.PreInitializeAsync(settings.ServerUrl);

                mainWindow.UpdateLoadingStatus("Logging in...");
                var success = await kelioService.LoginAsync(settings.ServerUrl, settings.Username, settings.Password);

                mainWindow.HideLoading();

                if (success)
                {
                    Log.Information("Auto-login successful");

                    // Start background prefetch of calendar and absence data
                    kelioService.StartPostLoginPrefetch();

                    mainWindow.NavigateToMain();
                }
                else
                {
                    Log.Warning("Auto-login failed");
                    mainWindow.NavigateToLogin(showError: true, prefillSettings: settings);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Auto-login error");
                mainWindow.HideLoading();
                mainWindow.NavigateToLogin(showError: true, prefillSettings: settings);
            }
        }
        else
        {
            Log.Information("No saved credentials, showing login");
            mainWindow.NavigateToLogin(showError: false, prefillSettings: settings);
        }

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Information("Elogio shutting down");
        Log.CloseAndFlush();

        if (_serviceProvider is IDisposable disposable)
        {
            disposable.Dispose();
        }

        base.OnExit(e);
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // Services (Singleton)
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IKelioService, KelioService>();
        services.AddSingleton<IPunchService, PunchService>();
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<IUpdateService, UpdateService>();
        services.AddSingleton<IToastService, ToastService>();

        // ViewModels (Singleton for shell and dashboard, Transient for other pages)
        services.AddSingleton<MainViewModel>();
        services.AddTransient<LoginViewModel>();
        services.AddSingleton<DashboardViewModel>(); // Singleton to preserve state across navigation
        services.AddTransient<MonthlyCalendarViewModel>();
        services.AddTransient<YearlyCalendarViewModel>();
        services.AddTransient<SettingsViewModel>();

        // Pages (Transient)
        services.AddTransient<LoginPage>();
        services.AddTransient<DashboardPage>();
        services.AddTransient<MonthlyCalendarPage>();
        services.AddTransient<YearlyCalendarPage>();
        services.AddTransient<SettingsPage>();

        // Main Window (Singleton)
        services.AddSingleton<MainWindow>();
    }
}
