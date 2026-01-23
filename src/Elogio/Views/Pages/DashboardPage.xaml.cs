using System.Windows;
using Elogio.ViewModels;

namespace Elogio.Views.Pages;

/// <summary>
/// Dashboard page showing week overview, today's balance, and punch buttons.
/// </summary>
public partial class DashboardPage
{
    private readonly DashboardViewModel _viewModel;
    private bool _isInitialized;

    public DashboardPage(DashboardViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
        DataContext = _viewModel;
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        if (!_isInitialized)
        {
            _isInitialized = true;
            await _viewModel.InitializeAsync();
        }
    }
}
