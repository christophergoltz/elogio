using System.Windows.Controls;
using Elogio.ViewModels;

namespace Elogio.Views.Pages;

/// <summary>
/// Settings page for application preferences.
/// </summary>
public partial class SettingsPage : Page
{
    public SettingsPage(SettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
