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
