using Elogio.Persistence.Dto;

namespace Elogio.Services;

/// <summary>
/// Service for managing punch (clock-in/clock-out) operations and state.
/// </summary>
public interface IPunchService
{
    /// <summary>
    /// Current punch state: true = clocked in, false = clocked out, null = unknown.
    /// </summary>
    bool? PunchState { get; }

    /// <summary>
    /// Whether the "Kommen" (clock-in) button should be enabled.
    /// </summary>
    bool IsKommenEnabled { get; }

    /// <summary>
    /// Whether the "Gehen" (clock-out) button should be enabled.
    /// </summary>
    bool IsGehenEnabled { get; }

    /// <summary>
    /// Whether a punch operation is currently in progress.
    /// </summary>
    bool IsPunchInProgress { get; }

    /// <summary>
    /// Raised when any punch-related state changes.
    /// </summary>
    event EventHandler? StateChanged;

    /// <summary>
    /// Execute a punch operation. The server determines clock-in vs clock-out.
    /// </summary>
    /// <returns>The punch result, or null on failure</returns>
    Task<PunchResultDto?> PunchAsync();

    /// <summary>
    /// Update the punch state based on today's time entries.
    /// Odd number of entries = clocked in, even = clocked out.
    /// </summary>
    /// <param name="entryCount">Number of time entries for today</param>
    void UpdateStateFromEntryCount(int entryCount);

    /// <summary>
    /// Reset to unknown state (e.g., on logout or error).
    /// </summary>
    void Reset();
}
