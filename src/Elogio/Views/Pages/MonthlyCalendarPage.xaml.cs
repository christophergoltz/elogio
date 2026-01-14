using System.Windows;
using Elogio.ViewModels;

namespace Elogio.Views.Pages;

/// <summary>
/// Monthly calendar page showing time tracking data.
/// </summary>
public partial class MonthlyCalendarPage
{
    private readonly MonthlyCalendarViewModel _viewModel;
    private bool _isInitialized;

    public MonthlyCalendarPage(MonthlyCalendarViewModel viewModel)
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
