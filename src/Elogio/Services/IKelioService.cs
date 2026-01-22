using Elogio.Persistence.Dto;

namespace Elogio.Services;

/// <summary>
/// Interface for Kelio API operations.
/// Abstracts the KelioClient for dependency injection and testability.
/// </summary>
public interface IKelioService
{
    /// <summary>
    /// Whether the user is currently authenticated.
    /// </summary>
    bool IsAuthenticated { get; }

    /// <summary>
    /// The authenticated employee's name.
    /// </summary>
    string? EmployeeName { get; }

    /// <summary>
    /// The authenticated employee's ID.
    /// </summary>
    int? EmployeeId { get; }

    /// <summary>
    /// Pre-initialize the Kelio client for faster login.
    /// Call this early (e.g., when login page is shown) to start curl_proxy server.
    /// </summary>
    Task PreInitializeAsync(string serverUrl);

    /// <summary>
    /// Authenticate with Kelio server.
    /// </summary>
    Task<bool> LoginAsync(string serverUrl, string username, string password);

    /// <summary>
    /// Get weekly presence data for a specific date.
    /// </summary>
    Task<WeekPresenceDto?> GetWeekPresenceAsync(DateOnly date);

    /// <summary>
    /// Get monthly presence data (aggregates multiple weeks).
    /// </summary>
    Task<MonthData> GetMonthDataAsync(int year, int month);

    /// <summary>
    /// Prefetch adjacent months (previous month) in the background for faster navigation.
    /// Fire-and-forget operation.
    /// </summary>
    void PrefetchAdjacentMonths(int year, int month);

    /// <summary>
    /// Clear the month data cache.
    /// </summary>
    void ClearCache();

    /// <summary>
    /// Disconnect and clear authentication state.
    /// </summary>
    void Logout();

    /// <summary>
    /// Perform a punch operation (clock-in or clock-out).
    /// The server determines whether it's a clock-in or clock-out based on current state.
    /// </summary>
    Task<PunchResultDto?> PunchAsync();

    /// <summary>
    /// Get absence calendar data for a specific month.
    /// </summary>
    Task<AbsenceCalendarDto?> GetMonthAbsencesAsync(int year, int month);

    /// <summary>
    /// Prefetch absence data for adjacent months in background.
    /// Fire-and-forget operation.
    /// </summary>
    void PrefetchAdjacentMonthAbsences(int year, int month);

    /// <summary>
    /// Initialize the absence cache with 19 months of data (today -6 months to +12 months).
    /// Should be called once when the calendar is first opened.
    /// </summary>
    Task InitializeAbsenceCacheAsync();

    /// <summary>
    /// Check if absence data for a specific month is in the cache.
    /// </summary>
    bool IsAbsenceMonthCached(int year, int month);

    /// <summary>
    /// Get the current cached absence date range.
    /// Returns null if cache is empty.
    /// </summary>
    (DateOnly start, DateOnly end)? GetAbsenceCacheRange();

    /// <summary>
    /// Ensure at least MIN_BUFFER_MONTHS (2) months of absence data are cached
    /// in both directions from the specified month. Triggers background prefetch if needed.
    /// </summary>
    void EnsureAbsenceBuffer(int year, int month);

    /// <summary>
    /// Start background prefetch of absence and calendar data after successful login.
    /// This is a fire-and-forget operation to improve perceived performance.
    /// </summary>
    void StartPostLoginPrefetch();
}

/// <summary>
/// Aggregated monthly data from multiple weekly API calls.
/// </summary>
public class MonthData
{
    public int Year { get; init; }
    public int Month { get; init; }
    public List<DayPresenceDto> Days { get; init; } = [];
    public TimeSpan TotalWorked => TimeSpan.FromTicks(Days.Sum(d => d.WorkedTime.Ticks));
    public TimeSpan TotalExpected => TimeSpan.FromTicks(Days.Sum(d => d.ExpectedTime.Ticks));
    public TimeSpan Balance => TotalWorked - TotalExpected;
}
