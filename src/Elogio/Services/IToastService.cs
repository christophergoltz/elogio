using Elogio.ViewModels;
using Elogio.ViewModels.Models;

namespace Elogio.Services;

/// <summary>
/// Service for showing toast notifications in the application.
/// </summary>
public interface IToastService
{
    /// <summary>
    /// Event raised when a toast should be shown.
    /// </summary>
    event EventHandler<ToastNotificationEventArgs>? ToastRequested;

    /// <summary>
    /// Show a toast notification.
    /// </summary>
    void ShowToast(string title, string message, ToastType type);
}

/// <summary>
/// Implementation of toast service.
/// </summary>
public class ToastService : IToastService
{
    public event EventHandler<ToastNotificationEventArgs>? ToastRequested;

    public void ShowToast(string title, string message, ToastType type)
    {
        ToastRequested?.Invoke(this, new ToastNotificationEventArgs(title, message, type));
    }
}
