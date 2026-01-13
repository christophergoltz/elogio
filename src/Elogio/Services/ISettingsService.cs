namespace Elogio.Desktop.Services;

/// <summary>
/// Interface for user settings persistence.
/// </summary>
public interface ISettingsService
{
    /// <summary>
    /// Load user settings from storage.
    /// </summary>
    UserSettings Load();

    /// <summary>
    /// Save user settings to storage.
    /// </summary>
    void Save(UserSettings settings);
}
