using Serilog;

namespace Elogio.Services.Caching;

/// <summary>
/// In-memory cache for monthly presence data.
/// Thread-safe for concurrent reads, but writes should be synchronized externally if needed.
/// </summary>
public class MonthDataCache
{
    private readonly Dictionary<(int year, int month), MonthData> _cache = new();

    /// <summary>
    /// Try to get cached month data.
    /// </summary>
    /// <param name="year">Year</param>
    /// <param name="month">Month (1-12)</param>
    /// <param name="data">The cached data if found</param>
    /// <returns>True if data was found in cache</returns>
    public bool TryGet(int year, int month, out MonthData? data)
    {
        var key = (year, month);
        if (_cache.TryGetValue(key, out data))
        {
            Log.Debug("MonthDataCache: Cache hit for {Year}-{Month}", year, month);
            return true;
        }

        data = null;
        return false;
    }

    /// <summary>
    /// Store month data in cache.
    /// </summary>
    public void Set(int year, int month, MonthData data)
    {
        var key = (year, month);
        _cache[key] = data;
        Log.Debug("MonthDataCache: Cached data for {Year}-{Month}", year, month);
    }

    /// <summary>
    /// Check if month data is cached.
    /// </summary>
    public bool Contains(int year, int month)
    {
        return _cache.ContainsKey((year, month));
    }

    /// <summary>
    /// Clear all cached data.
    /// </summary>
    public void Clear()
    {
        _cache.Clear();
        Log.Debug("MonthDataCache: Cache cleared");
    }

    /// <summary>
    /// Number of months currently cached.
    /// </summary>
    public int Count => _cache.Count;
}
