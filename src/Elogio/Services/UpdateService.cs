using Serilog;
using Velopack;
using Velopack.Sources;

namespace Elogio.Services;

/// <summary>
/// Velopack-based update service for automatic application updates.
/// Uses GitHub Releases as the update source.
/// </summary>
public sealed class UpdateService : IUpdateService
{
    private const string GitHubRepoUrl = "https://github.com/Chr1st0pher-Goltz/elogio";

    private readonly UpdateManager _updateManager;
    private Velopack.UpdateInfo? _velopackUpdateInfo;
    private UpdateInfo? _updateInfo;

    public bool IsUpdateAvailable => _updateInfo != null;
    public string? AvailableVersion => _updateInfo?.Version;
    public string CurrentVersion => _updateManager.CurrentVersion?.ToString() ?? "0.0.0";

    public event EventHandler<UpdateInfo>? UpdateAvailable;

    public UpdateService()
    {
        var source = new GithubSource(GitHubRepoUrl, null, prerelease: false);
        _updateManager = new UpdateManager(source);

        Log.Information("UpdateService initialized. Current version: {Version}, IsInstalled: {IsInstalled}",
            CurrentVersion, _updateManager.IsInstalled);
    }

    public async Task CheckForUpdatesAsync()
    {
        try
        {
            Log.Information("Checking for updates...");

            // Skip update check if not installed via Velopack (development mode)
            if (!_updateManager.IsInstalled)
            {
                Log.Information("Application not installed via Velopack, skipping update check");
                return;
            }

            _velopackUpdateInfo = await _updateManager.CheckForUpdatesAsync();

            if (_velopackUpdateInfo == null)
            {
                Log.Information("No updates available");
                return;
            }

            Log.Information("Update available: {Version}", _velopackUpdateInfo.TargetFullRelease.Version);

            _updateInfo = new UpdateInfo
            {
                Version = _velopackUpdateInfo.TargetFullRelease.Version.ToString(),
                ReleaseNotes = null
            };

            UpdateAvailable?.Invoke(this, _updateInfo);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to check for updates");
            // Don't rethrow - update checks should never crash the app
        }
    }

    public async Task ApplyUpdateAndRestartAsync()
    {
        try
        {
            if (!_updateManager.IsInstalled)
            {
                Log.Warning("Cannot apply update - not installed via Velopack");
                return;
            }

            if (_velopackUpdateInfo == null)
            {
                Log.Warning("No update information available");
                return;
            }

            Log.Information("Downloading update {Version}...", _velopackUpdateInfo.TargetFullRelease.Version);

            await _updateManager.DownloadUpdatesAsync(_velopackUpdateInfo);

            Log.Information("Update downloaded. Restarting application...");

            _updateManager.ApplyUpdatesAndRestart(_velopackUpdateInfo);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to apply update");
            throw;
        }
    }
}
