using Serilog;

namespace Elogio.Persistence.Services;

/// <summary>
/// Manages background tasks that should run independently of the main flow.
/// Singleton - shared across the application lifetime.
/// Used for non-blocking prefetch operations during login.
/// </summary>
public class BackgroundTaskManager
{
    private static BackgroundTaskManager? _instance;
    private static readonly object _lock = new();

    public static BackgroundTaskManager Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    _instance ??= new BackgroundTaskManager();
                }
            }
            return _instance;
        }
    }

    private Task? _calendarPrefetchTask;
    private CancellationTokenSource? _cts;

    private BackgroundTaskManager() { }

    /// <summary>
    /// Start calendar navigation prefetch in background.
    /// Fire-and-forget - doesn't block caller.
    /// Safe to call multiple times - only runs once per session.
    /// </summary>
    public void StartCalendarPrefetch(Func<Task> prefetchAction)
    {
        if (_calendarPrefetchTask != null)
        {
            Log.Debug("BackgroundTaskManager: Calendar prefetch already running/completed");
            return;
        }

        _cts = new CancellationTokenSource();
        _calendarPrefetchTask = Task.Run(async () =>
        {
            try
            {
                Log.Information("[PERF] BackgroundTaskManager: Starting calendar prefetch");
                await prefetchAction();
                Log.Information("[PERF] BackgroundTaskManager: Calendar prefetch completed");
            }
            catch (OperationCanceledException)
            {
                Log.Debug("BackgroundTaskManager: Calendar prefetch cancelled");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "BackgroundTaskManager: Calendar prefetch failed");
            }
        }, _cts.Token);
    }

    /// <summary>
    /// Wait for calendar prefetch to complete (with timeout).
    /// Returns true if prefetch is done, false if timeout or not started.
    /// </summary>
    public async Task<bool> WaitForCalendarPrefetchAsync(int timeoutMs = 5000)
    {
        if (_calendarPrefetchTask == null)
            return false;

        try
        {
            await Task.WhenAny(_calendarPrefetchTask, Task.Delay(timeoutMs));
            return _calendarPrefetchTask.IsCompletedSuccessfully;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Cancel all background tasks and reset state.
    /// Call on logout/dispose.
    /// </summary>
    public void Reset()
    {
        _cts?.Cancel();
        _calendarPrefetchTask = null;
        _cts?.Dispose();
        _cts = null;
        Log.Debug("BackgroundTaskManager: Reset completed");
    }
}
