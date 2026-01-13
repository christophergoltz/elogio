using System.Windows;
using System.Windows.Controls;
using Elogio.Desktop.ViewModels;

namespace Elogio.Desktop.Views;

/// <summary>
/// Legacy LoginView - kept for backwards compatibility.
/// New code should use LoginPage instead.
/// </summary>
public partial class LoginView : UserControl
{
    public event EventHandler? LoginSuccessful;
    private bool _isLoadingPassword;

    public LoginView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is LoginViewModel vm)
        {
            vm.LoginSuccessful += OnViewModelLoginSuccessful;

            // Load saved password into PasswordBox
            if (!string.IsNullOrEmpty(vm.Password))
            {
                _isLoadingPassword = true;
                PasswordBox.Password = vm.Password;
                _isLoadingPassword = false;
            }
        }
    }

    private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        // Avoid feedback loop when loading saved password
        if (_isLoadingPassword) return;

        if (DataContext is LoginViewModel vm)
        {
            vm.Password = PasswordBox.Password;
        }
    }

    private void OnViewModelLoginSuccessful(object? sender, EventArgs e)
    {
        LoginSuccessful?.Invoke(this, EventArgs.Empty);
    }
}
