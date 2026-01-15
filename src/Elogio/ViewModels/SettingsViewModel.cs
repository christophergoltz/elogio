using CommunityToolkit.Mvvm.ComponentModel;
using Elogio.Services;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace Elogio.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;

    [ObservableProperty]
    private bool _isDarkMode;

    public SymbolRegular ThemeIcon => IsDarkMode ? SymbolRegular.WeatherMoon24 : SymbolRegular.WeatherSunny24;
    public string ThemeLabel => IsDarkMode ? "Dark Mode" : "Light Mode";

    public SettingsViewModel(ISettingsService settingsService)
    {
        _settingsService = settingsService;

        // Load current theme setting
        var settings = _settingsService.Load();
        _isDarkMode = settings.IsDarkMode;
    }

    partial void OnIsDarkModeChanged(bool value)
    {
        // Apply theme
        ApplicationThemeManager.Apply(value ? ApplicationTheme.Dark : ApplicationTheme.Light);
        OnPropertyChanged(nameof(ThemeIcon));
        OnPropertyChanged(nameof(ThemeLabel));

        // Save setting
        var settings = _settingsService.Load();
        settings.IsDarkMode = value;
        _settingsService.Save(settings);
    }
}
