using System.Windows.Controls;
using Elogio.Desktop.Services;
using Elogio.Desktop.ViewModels;

namespace Elogio.Desktop.Views.Pages;

/// <summary>
/// Login page for authentication.
/// </summary>
public partial class LoginPage : Page
{
    private readonly LoginViewModel _viewModel;

    public event EventHandler? LoginSuccessful;

    public LoginPage(LoginViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
        DataContext = _viewModel;

        _viewModel.LoginSuccessful += OnViewModelLoginSuccessful;
    }

    /// <summary>
    /// Prefill credentials from settings and optionally show an error.
    /// </summary>
    public void PrefillCredentials(UserSettings settings, bool showError)
    {
        _viewModel.ServerUrl = settings.ServerUrl;
        _viewModel.Username = settings.Username;
        _viewModel.Password = settings.Password ?? "";
        _viewModel.RememberCredentials = settings.RememberCredentials;

        if (showError)
        {
            _viewModel.ErrorMessage = "Auto-login failed. Please log in manually.";
        }
    }

    private void OnViewModelLoginSuccessful(object? sender, EventArgs e)
    {
        LoginSuccessful?.Invoke(this, EventArgs.Empty);
    }
}
