using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Elogio.Desktop.Services;

/// <summary>
/// Simple settings service for storing user preferences.
/// Credentials are encrypted using DPAPI (Windows Data Protection).
/// </summary>
public class SettingsService : ISettingsService
{
    private static readonly string SettingsFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Elogio");

    private static readonly string SettingsFile = Path.Combine(SettingsFolder, "settings.json");

    public UserSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsFile))
            {
                return new UserSettings();
            }

            var json = File.ReadAllText(SettingsFile);
            var settings = JsonSerializer.Deserialize<UserSettings>(json) ?? new UserSettings();

            // Decrypt password if present
            if (!string.IsNullOrEmpty(settings.EncryptedPassword))
            {
                settings.Password = DecryptString(settings.EncryptedPassword);
            }

            return settings;
        }
        catch
        {
            return new UserSettings();
        }
    }

    public void Save(UserSettings settings)
    {
        try
        {
            if (!Directory.Exists(SettingsFolder))
            {
                Directory.CreateDirectory(SettingsFolder);
            }

            // Encrypt password before saving
            if (!string.IsNullOrEmpty(settings.Password))
            {
                settings.EncryptedPassword = EncryptString(settings.Password);
            }

            // Don't save plain password to file
            var settingsToSave = new UserSettings
            {
                ServerUrl = settings.ServerUrl,
                Username = settings.Username,
                EncryptedPassword = settings.EncryptedPassword,
                RememberCredentials = settings.RememberCredentials
            };

            var json = JsonSerializer.Serialize(settingsToSave, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsFile, json);
        }
        catch
        {
            // Ignore save errors
        }
    }

    private static string EncryptString(string plainText)
    {
        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var encryptedBytes = ProtectedData.Protect(plainBytes, null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(encryptedBytes);
    }

    private static string DecryptString(string encryptedText)
    {
        var encryptedBytes = Convert.FromBase64String(encryptedText);
        var plainBytes = ProtectedData.Unprotect(encryptedBytes, null, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(plainBytes);
    }
}

public class UserSettings
{
    public string ServerUrl { get; set; } = "";
    public string Username { get; set; } = "";
    public string? EncryptedPassword { get; set; }
    public bool RememberCredentials { get; set; } = true;

    // Not serialized - only used in memory
    public string? Password { get; set; }
}
