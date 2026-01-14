namespace Elogio.Services;

/// <summary>
/// Interface for application update operations.
/// </summary>
public interface IUpdateService
{
    /// <summary>
    /// Whether an update is currently available.
    /// </summary>
    bool IsUpdateAvailable { get; }

    /// <summary>
    /// Version string of the available update (null if no update).
    /// </summary>
    string? AvailableVersion { get; }

    /// <summary>
    /// Current application version.
    /// </summary>
    string CurrentVersion { get; }

    /// <summary>
    /// Check for updates (non-blocking).
    /// Raises UpdateAvailable event if an update is found.
    /// </summary>
    Task CheckForUpdatesAsync();

    /// <summary>
    /// Download and apply the update, then restart the application.
    /// </summary>
    Task ApplyUpdateAndRestartAsync();

    /// <summary>
    /// Event raised when an update is found.
    /// </summary>
    event EventHandler<UpdateInfo>? UpdateAvailable;
}

/// <summary>
/// Information about an available update.
/// </summary>
public sealed class UpdateInfo
{
    /// <summary>
    /// Version of the available update.
    /// </summary>
    public required string Version { get; init; }

    /// <summary>
    /// Optional release notes.
    /// </summary>
    public string? ReleaseNotes { get; init; }
}
