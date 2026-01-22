namespace Elogio.ViewModels;

/// <summary>
/// Event args for toast notification requests.
/// </summary>
public class ToastNotificationEventArgs : EventArgs
{
    public string Title { get; }
    public string Message { get; }
    public ToastType Type { get; }

    public ToastNotificationEventArgs(string title, string message, ToastType type)
    {
        Title = title;
        Message = message;
        Type = type;
    }
}

public enum ToastType
{
    Success,
    Error,
    Info
}
