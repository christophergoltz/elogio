namespace Elogio.ViewModels.Models;

/// <summary>
/// Status of the update check operation.
/// </summary>
public enum UpdateCheckStatus
{
    Idle,
    Checking,
    NoUpdates,
    UpdateAvailable,
    Error
}
