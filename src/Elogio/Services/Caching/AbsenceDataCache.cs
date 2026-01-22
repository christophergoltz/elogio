using Elogio.Persistence.Dto;
using Serilog;

namespace Elogio.Services.Caching;

/// <summary>
/// In-memory cache for absence calendar data with range tracking.
/// Supports smart prefetching by tracking the cached date range.
/// </summary>
public class AbsenceDataCache
{
    private readonly Dictionary<(int year, int month), AbsenceCalendarDto> _cache = new();
    private readonly object _prefetchLock = new();

    private DateOnly? _cacheStart;
    private DateOnly? _cacheEnd;
    private bool _isInitialized;

    /// <summary>
    /// Whether the cache has been initialized with bulk data.
    /// </summary>
    public bool IsInitialized => _isInitialized;

    /// <summary>
    /// Try to get cached absence data for a month.
    /// </summary>
    public bool TryGet(int year, int month, out AbsenceCalendarDto? data)
    {
        var key = (year, month);
        if (_cache.TryGetValue(key, out data))
        {
            Log.Debug("AbsenceDataCache: Cache hit for {Year}-{Month}", year, month);
            return true;
        }

        data = null;
        return false;
    }

    /// <summary>
    /// Store absence data for a month.
    /// </summary>
    public void Set(int year, int month, AbsenceCalendarDto data)
    {
        var key = (year, month);
        _cache[key] = data;
        Log.Debug("AbsenceDataCache: Cached data for {Year}-{Month}", year, month);
    }

    /// <summary>
    /// Check if absence data for a month is cached.
    /// </summary>
    public bool Contains(int year, int month)
    {
        return _cache.ContainsKey((year, month));
    }

    /// <summary>
    /// Get the current cached date range.
    /// </summary>
    /// <returns>Start and end dates, or null if cache is empty</returns>
    public (DateOnly start, DateOnly end)? GetCacheRange()
    {
        if (_cacheStart == null || _cacheEnd == null)
            return null;

        return (_cacheStart.Value, _cacheEnd.Value);
    }

    /// <summary>
    /// Update the cached date range after bulk loading or prefetching.
    /// </summary>
    public void SetCacheRange(DateOnly start, DateOnly end)
    {
        _cacheStart = start;
        _cacheEnd = end;
    }

    /// <summary>
    /// Mark the cache as initialized after bulk loading.
    /// </summary>
    public void MarkInitialized()
    {
        _isInitialized = true;
    }

    /// <summary>
    /// Calculate buffer months in each direction from a given month.
    /// </summary>
    /// <param name="year">Current year</param>
    /// <param name="month">Current month</param>
    /// <returns>Tuple of (backward buffer months, forward buffer months), or null if cache range not set</returns>
    public (int backward, int forward)? GetBufferMonths(int year, int month)
    {
        if (_cacheStart == null || _cacheEnd == null)
            return null;

        var currentMonthDate = new DateOnly(year, month, 1);
        var bufferBackward = MonthDifference(_cacheStart.Value, currentMonthDate);
        var bufferForward = MonthDifference(currentMonthDate, _cacheEnd.Value);

        return (bufferBackward, bufferForward);
    }

    /// <summary>
    /// Try to acquire prefetch lock and extend cache range.
    /// Returns false if prefetch is already in progress for the requested direction.
    /// </summary>
    /// <param name="newStart">New start date (for backward prefetch)</param>
    /// <param name="newEnd">New end date (for forward prefetch)</param>
    /// <param name="isBackward">True for backward prefetch, false for forward</param>
    /// <returns>True if lock acquired and range updated</returns>
    public bool TryStartPrefetch(DateOnly newStart, DateOnly newEnd, bool isBackward)
    {
        lock (_prefetchLock)
        {
            if (isBackward)
            {
                if (_cacheStart != null && newStart >= _cacheStart.Value)
                {
                    Log.Debug("AbsenceDataCache: Backward prefetch already in progress or not needed");
                    return false;
                }
                _cacheStart = newStart;
            }
            else
            {
                if (_cacheEnd != null && newEnd <= _cacheEnd.Value)
                {
                    Log.Debug("AbsenceDataCache: Forward prefetch already in progress or not needed");
                    return false;
                }
                _cacheEnd = newEnd;
            }
            return true;
        }
    }

    /// <summary>
    /// Revert cache range on prefetch failure.
    /// </summary>
    /// <param name="monthsToRevert">Number of months to revert</param>
    /// <param name="isBackward">True to revert backward extension, false for forward</param>
    public void RevertPrefetch(int monthsToRevert, bool isBackward)
    {
        lock (_prefetchLock)
        {
            if (isBackward)
                _cacheStart = _cacheStart?.AddMonths(monthsToRevert);
            else
                _cacheEnd = _cacheEnd?.AddMonths(-monthsToRevert);
        }
    }

    /// <summary>
    /// Clear all cached data and reset range tracking.
    /// </summary>
    public void Clear()
    {
        _cache.Clear();
        _cacheStart = null;
        _cacheEnd = null;
        _isInitialized = false;
        Log.Debug("AbsenceDataCache: Cache cleared");
    }

    /// <summary>
    /// Number of months currently cached.
    /// </summary>
    public int Count => _cache.Count;

    private static int MonthDifference(DateOnly from, DateOnly to)
    {
        return (to.Year - from.Year) * 12 + (to.Month - from.Month);
    }
}
