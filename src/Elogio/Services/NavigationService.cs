using System.Windows.Controls;

namespace Elogio.Desktop.Services;

/// <summary>
/// Navigation service implementation for WPF-UI.
/// </summary>
public class NavigationService : INavigationService
{
    private readonly IServiceProvider _serviceProvider;
    private Frame? _frame;

    public event EventHandler<Type>? Navigated;

    public NavigationService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public void SetFrame(Frame frame)
    {
        _frame = frame;
    }

    public bool Navigate(Type pageType)
    {
        if (_frame == null)
            return false;

        var page = _serviceProvider.GetService(pageType);
        if (page == null)
            return false;

        _frame.Navigate(page);
        Navigated?.Invoke(this, pageType);
        return true;
    }

    public bool Navigate<TPage>() where TPage : Page
    {
        return Navigate(typeof(TPage));
    }
}
