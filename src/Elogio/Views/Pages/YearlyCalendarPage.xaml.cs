using System.Windows;
using Elogio.ViewModels;

namespace Elogio.Views.Pages;

/// <summary>
/// Yearly calendar page showing absence overview for the entire year.
/// </summary>
public partial class YearlyCalendarPage
{
    private readonly YearlyCalendarViewModel _viewModel;
    private bool _isInitialized;

    public YearlyCalendarPage(YearlyCalendarViewModel viewModel)
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
