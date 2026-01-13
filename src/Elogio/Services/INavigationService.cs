using System.Windows.Controls;

namespace Elogio.Services;

/// <summary>
/// Interface for page navigation service.
/// </summary>
public interface INavigationService
{
    /// <summary>
    /// Navigate to a page by type.
    /// </summary>
    bool Navigate(Type pageType);

    /// <summary>
    /// Navigate to a page by type with generic syntax.
    /// </summary>
    bool Navigate<TPage>() where TPage : Page;

    /// <summary>
    /// Set the frame used for navigation.
    /// </summary>
    void SetFrame(Frame frame);

    /// <summary>
    /// Event raised when navigation occurs.
    /// </summary>
    event EventHandler<Type>? Navigated;
}
