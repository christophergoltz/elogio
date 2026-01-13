using System.IO;
using System.Windows;
using Elogio.Services;
using Elogio.ViewModels;
using Elogio.Views.Pages;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Velopack;

namespace Elogio;

public partial class App : Application
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

        // Velopack update handling
        VelopackApp.Build().Run();

        // Build service provider
        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();

        // Attempt auto-login if credentials are saved
        var settingsService = Services.GetRequiredService<ISettingsService>();
        var settings = settingsService.Load();

        MainWindow mainWindow;

        // Create and show MainWindow immediately
        mainWindow = Services.GetRequiredService<MainWindow>();
        MainWindow = mainWindow;
        mainWindow.Show();

        if (settings.RememberCredentials && !string.IsNullOrEmpty(settings.Password))
        {
            Log.Information("Attempting auto-login for user {Username}", settings.Username);

            // Show loading overlay in main window (faster than separate window)
            mainWindow.ShowLoading();

            try
            {
                var kelioService = Services.GetRequiredService<IKelioService>();
                var success = await kelioService.LoginAsync(settings.ServerUrl, settings.Username, settings.Password);

                mainWindow.HideLoading();

                if (success)
                {
                    Log.Information("Auto-login successful");
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
        services.AddSingleton<INavigationService, NavigationService>();

        // ViewModels (Singleton for shell, Transient for pages)
        services.AddSingleton<MainViewModel>();
        services.AddTransient<LoginViewModel>();
        services.AddTransient<MonthlyCalendarViewModel>();

        // Pages (Transient)
        services.AddTransient<LoginPage>();
        services.AddTransient<MonthlyCalendarPage>();

        // Main Window (Singleton)
        services.AddSingleton<MainWindow>();
    }
}
