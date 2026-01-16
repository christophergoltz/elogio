using System.Diagnostics;
using Elogio.Persistence.Api;
using Elogio.Persistence.Dto;
using Serilog;

namespace Elogio.Services;

/// <summary>
/// Kelio API service implementation wrapping KelioClient.
/// </summary>
public class KelioService : IKelioService, IDisposable
{
    private KelioClient? _client;
    private string? _employeeName;

    // Session-only cache for month data to speed up navigation
    private readonly Dictionary<(int year, int month), MonthData> _monthCache = new();

    // Session-only cache for absence data
    private readonly Dictionary<(int year, int month), AbsenceCalendarDto> _absenceCache = new();

    public bool IsAuthenticated => _client?.SessionId != null;
    public string? EmployeeName => _employeeName;
    public int? EmployeeId => _client?.EmployeeId;

    /// <summary>
    /// Pre-initialize the Kelio client for faster login.
    /// Call this early (e.g., when login page is shown) to start curl_proxy server.
    /// </summary>
    public async Task PreInitializeAsync(string serverUrl)
    {
        // Dispose previous client if exists
        _client?.Dispose();
        _client = new KelioClient(serverUrl);

        var sw = Stopwatch.StartNew();
        await _client.PreInitializeAsync();
        Log.Information("[PERF] KelioService.PreInitialize: took {ElapsedMs}ms", sw.ElapsedMilliseconds);
    }

    public async Task<bool> LoginAsync(string serverUrl, string username, string password)
    {
        var totalSw = Stopwatch.StartNew();

        // Create client if not pre-initialized or different server
        if (_client == null)
        {
            _client = new KelioClient(serverUrl);
        }

        var sw = Stopwatch.StartNew();
        var success = await _client.LoginAsync(username, password);
        Log.Information("[PERF] KelioService.LoginAsync: KelioClient.LoginAsync took {ElapsedMs}ms", sw.ElapsedMilliseconds);

        // NOTE: Employee name is fetched lazily when month data is loaded (saves ~2.2s on login)

        Log.Information("[PERF] KelioService.LoginAsync: TOTAL {ElapsedMs}ms", totalSw.ElapsedMilliseconds);
        return success;
    }

    public async Task<WeekPresenceDto?> GetWeekPresenceAsync(DateOnly date)
    {
        if (_client == null || !IsAuthenticated)
            return null;

        return await _client.GetWeekPresenceAsync(date);
    }

    public async Task<MonthData> GetMonthDataAsync(int year, int month)
    {
        var key = (year, month);

        // Check cache first
        if (_monthCache.TryGetValue(key, out var cached))
        {
            Log.Information("GetMonthDataAsync: Returning cached data for {Year}-{Month}", year, month);
            return cached;
        }

        Log.Information("GetMonthDataAsync called for {Year}-{Month}, IsAuthenticated={IsAuth}",
            year, month, IsAuthenticated);

        if (_client == null || !IsAuthenticated)
        {
            Log.Warning("GetMonthDataAsync: Client not authenticated, returning empty data");
            return new MonthData { Year = year, Month = month };
        }

        var data = await FetchMonthDataInternalAsync(year, month);

        // Store in cache
        _monthCache[key] = data;

        return data;
    }

    public void PrefetchAdjacentMonths(int year, int month)
    {
        if (_client == null || !IsAuthenticated)
            return;

        // Prefetch previous month in background
        var (prevYear, prevMonth) = GetPreviousMonth(year, month);
        var prevKey = (prevYear, prevMonth);

        if (!_monthCache.ContainsKey(prevKey))
        {
            Log.Information("Prefetching previous month {Year}-{Month}", prevYear, prevMonth);
            _ = Task.Run(async () =>
            {
                try
                {
                    var data = await FetchMonthDataInternalAsync(prevYear, prevMonth);
                    _monthCache[prevKey] = data;
                    Log.Information("Prefetch complete for {Year}-{Month}", prevYear, prevMonth);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Prefetch failed for {Year}-{Month}", prevYear, prevMonth);
                }
            });
        }
    }

    public void ClearCache()
    {
        _monthCache.Clear();
        _absenceCache.Clear();
        Log.Information("Month and absence cache cleared");
    }

    public void Logout()
    {
        _client?.Dispose();
        _client = null;
        _employeeName = null;
        ClearCache();
    }

    public async Task<PunchResultDto?> PunchAsync()
    {
        if (_client == null || !IsAuthenticated)
        {
            Log.Warning("PunchAsync: Client not authenticated");
            return null;
        }

        return await _client.PunchAsync();
    }

    public async Task<AbsenceCalendarDto?> GetMonthAbsencesAsync(int year, int month)
    {
        var key = (year, month);

        // Check cache first
        if (_absenceCache.TryGetValue(key, out var cached))
        {
            Log.Information("GetMonthAbsencesAsync: Returning cached data for {Year}-{Month}", year, month);
            return cached;
        }

        if (_client == null || !IsAuthenticated)
        {
            Log.Warning("GetMonthAbsencesAsync: Client not authenticated");
            return null;
        }

        try
        {
            // Extend date range to include visible days from adjacent months in the calendar grid
            // Add 7 days before and after to cover leading/trailing days in the grid
            var startDate = new DateOnly(year, month, 1).AddDays(-7);
            var endDate = new DateOnly(year, month, 1).AddMonths(1).AddDays(13);

            Log.Information("GetMonthAbsencesAsync: Fetching absences for {StartDate} to {EndDate} (month {Year}-{Month})",
                startDate, endDate, year, month);

            var data = await _client.GetAbsencesAsync(startDate, endDate);

            if (data != null)
            {
                _absenceCache[key] = data;
                Log.Information("GetMonthAbsencesAsync: Got {DayCount} days, {VacationCount} vacation, {SickCount} sick leave",
                    data.Days.Count, data.VacationDays.Count(), data.SickLeaveDays.Count());
            }

            return data;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "GetMonthAbsencesAsync: Failed to fetch absences for {Year}-{Month}", year, month);
            return null;
        }
    }

    public void PrefetchAdjacentMonthAbsences(int year, int month)
    {
        if (_client == null || !IsAuthenticated)
            return;

        // Prefetch previous month in background
        var (prevYear, prevMonth) = GetPreviousMonth(year, month);
        var prevKey = (prevYear, prevMonth);

        if (!_absenceCache.ContainsKey(prevKey))
        {
            Log.Information("Prefetching absences for previous month {Year}-{Month}", prevYear, prevMonth);
            _ = Task.Run(async () =>
            {
                try
                {
                    // Use same extended date range logic as GetMonthAbsencesAsync
                    var startDate = new DateOnly(prevYear, prevMonth, 1).AddDays(-7);
                    var endDate = new DateOnly(prevYear, prevMonth, 1).AddMonths(1).AddDays(13);

                    var data = await _client.GetAbsencesAsync(startDate, endDate);
                    if (data != null)
                    {
                        _absenceCache[prevKey] = data;
                        Log.Information("Absence prefetch complete for {Year}-{Month}", prevYear, prevMonth);
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Absence prefetch failed for {Year}-{Month}", prevYear, prevMonth);
                }
            });
        }
    }

    private async Task<MonthData> FetchMonthDataInternalAsync(int year, int month)
    {
        var totalSw = Stopwatch.StartNew();
        var weeks = GetWeeksInMonth(year, month);
        Log.Information("FetchMonthDataInternalAsync: Fetching {WeekCount} weeks for {Year}-{Month}: {WeekStarts}",
            weeks.Count, year, month, string.Join(", ", weeks.Select(w => w.ToString("yyyy-MM-dd"))));

        var allDays = new List<DayPresenceDto>();
        var seenDates = new HashSet<DateOnly>();
        var successfulWeeks = 0;
        var failedWeeks = 0;

        // Fetch weeks with limited parallelism to avoid overwhelming the API
        // Increased from 2 to 4 based on performance testing
        const int maxParallel = 4;
        var semaphore = new SemaphoreSlim(maxParallel);
        Log.Information("[PERF] FetchMonthData: Starting {WeekCount} weeks with parallelism={MaxParallel}", weeks.Count, maxParallel);

        var weekTasks = weeks.Select(async weekStart =>
        {
            var waitSw = Stopwatch.StartNew();
            await semaphore.WaitAsync();
            var waitMs = waitSw.ElapsedMilliseconds;

            try
            {
                var fetchSw = Stopwatch.StartNew();
                Log.Debug("Fetching week starting {WeekStart}", weekStart);
                var result = await _client!.GetWeekPresenceAsync(weekStart);
                var fetchMs = fetchSw.ElapsedMilliseconds;

                if (result != null)
                {
                    Log.Information("[PERF] FetchMonthData: Week {WeekStart} took {FetchMs}ms (waited {WaitMs}ms for semaphore)",
                        weekStart.ToString("yyyy-MM-dd"), fetchMs, waitMs);
                    Log.Information("Fetched week {WeekStart}: {DayCount} days, dates: {Dates}",
                        weekStart, result.Days.Count,
                        string.Join(", ", result.Days.Select(d => d.Date.ToString("MM-dd"))));
                }
                else
                {
                    Log.Warning("Fetched week {WeekStart}: returned NULL after {FetchMs}ms", weekStart, fetchMs);
                }
                return (weekStart, result);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to fetch week {WeekStart}", weekStart);
                return (weekStart, (WeekPresenceDto?)null);
            }
            finally
            {
                semaphore.Release();
            }
        }).ToList();

        var weekResults = await Task.WhenAll(weekTasks);
        Log.Information("[PERF] FetchMonthData: All {WeekCount} weeks completed in {ElapsedMs}ms", weeks.Count, totalSw.ElapsedMilliseconds);
        Log.Information("FetchMonthDataInternalAsync: Got {ResultCount} week results", weekResults.Length);

        foreach (var (weekStart, weekData) in weekResults)
        {
            if (weekData == null)
            {
                Log.Warning("FetchMonthDataInternalAsync: Week {WeekStart} returned null", weekStart);
                failedWeeks++;
                continue;
            }

            successfulWeeks++;

            // Update employee name if we got it
            if (string.IsNullOrEmpty(_employeeName) && !string.IsNullOrEmpty(weekData.EmployeeName))
            {
                _employeeName = weekData.EmployeeName;
            }

            // Filter to only days in the target month, avoiding duplicates
            var daysInMonth = weekData.Days.Where(d => d.Date.Month == month && d.Date.Year == year).ToList();
            foreach (var day in daysInMonth)
            {
                if (seenDates.Add(day.Date))
                {
                    allDays.Add(day);
                }
            }
            Log.Debug("Week {WeekStart}: Added {Count} days for {Year}-{Month}", weekStart, daysInMonth.Count, year, month);
        }

        Log.Information("FetchMonthDataInternalAsync: {SuccessCount} weeks succeeded, {FailedCount} weeks failed",
            successfulWeeks, failedWeeks);

        // Sort by date
        allDays = allDays.OrderBy(d => d.Date).ToList();

        var monthData = new MonthData
        {
            Year = year,
            Month = month,
            Days = allDays
        };

        Log.Information("FetchMonthDataInternalAsync: Returning {DayCount} days for {Year}-{Month}. " +
            "Dates: {Dates}. TotalWorked: {TotalWorked}, TotalExpected: {TotalExpected}, Balance: {Balance}",
            allDays.Count, year, month,
            string.Join(", ", allDays.Select(d => d.Date.ToString("MM-dd"))),
            monthData.TotalWorked, monthData.TotalExpected, monthData.Balance);

        return monthData;
    }

    private static (int year, int month) GetPreviousMonth(int year, int month)
    {
        if (month == 1)
            return (year - 1, 12);
        return (year, month - 1);
    }

    public void Dispose()
    {
        _client?.Dispose();
        _client = null;
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Get all week start dates that overlap with the given month.
    /// </summary>
    private static List<DateOnly> GetWeeksInMonth(int year, int month)
    {
        var weeks = new List<DateOnly>();
        var firstDayOfMonth = new DateOnly(year, month, 1);
        var lastDayOfMonth = firstDayOfMonth.AddMonths(1).AddDays(-1);

        // Find the Monday of the week containing the first day
        var currentWeekStart = firstDayOfMonth.AddDays(-(int)firstDayOfMonth.DayOfWeek + (int)DayOfWeek.Monday);
        if (currentWeekStart > firstDayOfMonth)
        {
            currentWeekStart = currentWeekStart.AddDays(-7);
        }

        // Add all weeks that overlap with the month
        while (currentWeekStart <= lastDayOfMonth)
        {
            weeks.Add(currentWeekStart);
            currentWeekStart = currentWeekStart.AddDays(7);
        }

        return weeks;
    }
}
