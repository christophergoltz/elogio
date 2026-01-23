using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Elogio.Services;

namespace Elogio.ViewModels;

public partial class LoginViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly IKelioService _kelioService;

    [ObservableProperty]
    private string _serverUrl = "";

    [ObservableProperty]
    private string _username = "";

    [ObservableProperty]
    private string _password = "";

    [ObservableProperty]
    private bool _rememberCredentials = true;

    [ObservableProperty]
    private bool _isLoggingIn;

    [ObservableProperty]
    private string? _errorMessage;

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public event EventHandler? LoginSuccessful;

    public LoginViewModel(ISettingsService settingsService, IKelioService kelioService)
    {
        _settingsService = settingsService;
        _kelioService = kelioService;

        LoadSettings();
    }

    partial void OnErrorMessageChanged(string? value)
    {
        _ = value; // Unused but required by partial method signature
        OnPropertyChanged(nameof(HasError));
    }

    private void LoadSettings()
    {
        var settings = _settingsService.Load();
        ServerUrl = settings.ServerUrl;
        Username = settings.Username;
        Password = settings.Password ?? "";
        RememberCredentials = settings.RememberCredentials;
    }

    private void SaveSettings()
    {
        if (RememberCredentials)
        {
            _settingsService.Save(new UserSettings
            {
                ServerUrl = ServerUrl,
                Username = Username,
                Password = Password,
                RememberCredentials = RememberCredentials
            });
        }
        else
        {
            // Clear saved credentials
            _settingsService.Save(new UserSettings
            {
                ServerUrl = ServerUrl,
                Username = "",
                Password = null,
                RememberCredentials = false
            });
        }
    }

    [RelayCommand]
    private async Task LoginAsync()
    {
        if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password))
        {
            ErrorMessage = "Please enter username and password";
            return;
        }

        if (string.IsNullOrWhiteSpace(ServerUrl))
        {
            ErrorMessage = "Please enter server URL";
            return;
        }

        IsLoggingIn = true;
        ErrorMessage = null;

        try
        {
            var success = await _kelioService.LoginAsync(ServerUrl, Username, Password);

            if (success)
            {
                SaveSettings();

                // Start background prefetch of calendar and absence data
                _kelioService.StartPostLoginPrefetch();

                LoginSuccessful?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                ErrorMessage = "Login failed. Please check your credentials.";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Login failed: {ex.Message}";
        }
        finally
        {
            IsLoggingIn = false;
        }
    }
}
