using System.Diagnostics;
using Elogio.Persistence.Api;
using Elogio.Persistence.Dto;
using Elogio.Services.Caching;
using Serilog;

namespace Elogio.Services;

/// <summary>
/// Kelio API service implementation wrapping KelioClient.
/// </summary>
public class KelioService : IKelioService, IDisposable
{
    // Prefetch configuration constants
    private const int MinBufferMonths = 2;      // Minimum months to keep cached in each direction
    private const int PrefetchMonths = 6;       // How many months to prefetch at once
    private const int InitialPastMonths = 6;    // Months to load into the past on init
    private const int InitialFutureMonths = 12; // Months to load into the future on init

    // Absence date range extension to cover calendar grid overflow
    private const int AbsenceBufferDaysBefore = 7;  // Days before month start (for leading grid days)
    private const int AbsenceBufferDaysAfter = 13;  // Days after month end (for trailing grid days)

    private KelioClient? _client;
    private string? _employeeName;

    // Session-only caches for presence and absence data
    private readonly MonthDataCache _monthCache = new();
    private readonly AbsenceDataCache _absenceCache = new();

    // Cancellation token source for background tasks - cancelled on Dispose
    private CancellationTokenSource? _backgroundTasksCts;
    private bool _disposed;

    public bool IsAuthenticated => _client?.SessionId != null;
    public string? EmployeeName => _client?.EmployeeName ?? _employeeName;
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

        var weekData = await _client.GetWeekPresenceAsync(date);

        // Update employee name if we got it (supports lazy loading after login)
        if (string.IsNullOrEmpty(_employeeName) && !string.IsNullOrEmpty(weekData?.EmployeeName))
        {
            _employeeName = weekData.EmployeeName;
            Log.Information("Employee name set from GetWeekPresenceAsync: {Name}", _employeeName);
        }

        return weekData;
    }

    public async Task<MonthData> GetMonthDataAsync(int year, int month)
    {
        // Check cache first
        if (_monthCache.TryGet(year, month, out var cached))
        {
            Log.Information("GetMonthDataAsync: Returning cached data for {Year}-{Month}", year, month);
            return cached!;
        }

        // Future months have no work time data - return empty immediately without API calls
        var today = DateOnly.FromDateTime(DateTime.Today);
        var requestedMonth = new DateOnly(year, month, 1);
        var currentMonth = new DateOnly(today.Year, today.Month, 1);

        if (requestedMonth > currentMonth)
        {
            Log.Information("GetMonthDataAsync: {Year}-{Month} is in the future, returning empty data (no API call)", year, month);
            var emptyData = new MonthData { Year = year, Month = month };
            _monthCache.Set(year, month, emptyData);
            return emptyData;
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
        _monthCache.Set(year, month, data);

        return data;
    }

    public void PrefetchAdjacentMonths(int year, int month)
    {
        if (_client == null || !IsAuthenticated)
            return;

        // Prefetch previous month in background
        var (prevYear, prevMonth) = GetPreviousMonth(year, month);

        if (!_monthCache.Contains(prevYear, prevMonth))
        {
            Log.Information("Prefetching previous month {Year}-{Month}", prevYear, prevMonth);
            var token = _backgroundTasksCts?.Token ?? CancellationToken.None;
            _ = Task.Run(async () =>
            {
                try
                {
                    token.ThrowIfCancellationRequested();
                    var data = await FetchMonthDataInternalAsync(prevYear, prevMonth);
                    _monthCache.Set(prevYear, prevMonth, data);
                    Log.Information("Prefetch complete for {Year}-{Month}", prevYear, prevMonth);
                }
                catch (OperationCanceledException)
                {
                    Log.Debug("Prefetch cancelled for {Year}-{Month}", prevYear, prevMonth);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Prefetch failed for {Year}-{Month}", prevYear, prevMonth);
                }
            }, token);
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
        // Cancel any running background tasks
        _backgroundTasksCts?.Cancel();
        _backgroundTasksCts?.Dispose();
        _backgroundTasksCts = null;

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
        // Check cache first
        if (_absenceCache.TryGet(year, month, out var cached))
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
            var (startDate, endDate) = GetAbsenceDateRange(year, month);

            Log.Information("GetMonthAbsencesAsync: Fetching absences for {StartDate} to {EndDate} (month {Year}-{Month})",
                startDate, endDate, year, month);

            var data = await _client.GetAbsencesAsync(startDate, endDate);

            if (data != null)
            {
                _absenceCache.Set(year, month, data);
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

    public async Task<List<ColleagueAbsenceDto>> GetColleagueAbsencesAsync(int year, int month)
    {
        if (_client == null || !IsAuthenticated)
        {
            Log.Warning("GetColleagueAbsencesAsync: Client not authenticated");
            return [];
        }

        try
        {
            Log.Information("GetColleagueAbsencesAsync: Fetching colleague absences for {Year}-{Month:D2}", year, month);

            var data = await _client.GetColleagueAbsencesAsync(year, month);

            Log.Information("GetColleagueAbsencesAsync: Got {ColleagueCount} colleagues", data.Count);

            return data;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "GetColleagueAbsencesAsync: Failed to fetch colleague absences for {Year}-{Month}", year, month);
            return [];
        }
    }

    public void PrefetchAdjacentMonthAbsences(int year, int month)
    {
        if (_client == null || !IsAuthenticated)
            return;

        // Prefetch previous month in background
        var (prevYear, prevMonth) = GetPreviousMonth(year, month);

        if (!_absenceCache.Contains(prevYear, prevMonth))
        {
            Log.Information("Prefetching absences for previous month {Year}-{Month}", prevYear, prevMonth);
            var token = _backgroundTasksCts?.Token ?? CancellationToken.None;
            _ = Task.Run(async () =>
            {
                try
                {
                    token.ThrowIfCancellationRequested();
                    // Use same extended date range logic as GetMonthAbsencesAsync
                    var (startDate, endDate) = GetAbsenceDateRange(prevYear, prevMonth);

                    var data = await _client.GetAbsencesAsync(startDate, endDate);
                    if (data != null)
                    {
                        _absenceCache.Set(prevYear, prevMonth, data);
                        Log.Information("Absence prefetch complete for {Year}-{Month}", prevYear, prevMonth);
                    }
                }
                catch (OperationCanceledException)
                {
                    Log.Debug("Absence prefetch cancelled for {Year}-{Month}", prevYear, prevMonth);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Absence prefetch failed for {Year}-{Month}", prevYear, prevMonth);
                }
            }, token);
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

    /// <summary>
    /// Calculate the extended date range for absence queries.
    /// Extends beyond month boundaries to cover calendar grid overflow days.
    /// </summary>
    private static (DateOnly Start, DateOnly End) GetAbsenceDateRange(int year, int month)
    {
        var firstOfMonth = new DateOnly(year, month, 1);
        return (
            firstOfMonth.AddDays(-AbsenceBufferDaysBefore),
            firstOfMonth.AddMonths(1).AddDays(AbsenceBufferDaysAfter)
        );
    }

    public async Task InitializeAbsenceCacheAsync()
    {
        if (_absenceCache.IsInitialized)
        {
            Log.Information("InitializeAbsenceCacheAsync: Cache already initialized");
            return;
        }

        if (_client == null || !IsAuthenticated)
        {
            Log.Warning("InitializeAbsenceCacheAsync: Client not authenticated");
            return;
        }

        var sw = Stopwatch.StartNew();
        var today = DateOnly.FromDateTime(DateTime.Today);

        // Calculate date range: today -6 months to +12 months = 19 months total
        var startDate = new DateOnly(today.Year, today.Month, 1).AddMonths(-InitialPastMonths);
        var endDate = new DateOnly(today.Year, today.Month, 1).AddMonths(InitialFutureMonths + 1).AddDays(-1);

        Log.Information("InitializeAbsenceCacheAsync: Loading absences from {StartDate} to {EndDate} ({MonthCount} months)",
            startDate, endDate, InitialPastMonths + InitialFutureMonths + 1);

        try
        {
            var data = await _client.GetAbsencesAsync(startDate, endDate);

            if (data != null)
            {
                // Split data into monthly chunks and cache each month
                var currentMonth = startDate;
                while (currentMonth <= endDate)
                {
                    var monthStart = new DateOnly(currentMonth.Year, currentMonth.Month, 1);
                    var monthEnd = monthStart.AddMonths(1).AddDays(-1);

                    // Extract days for this month
                    var monthDays = data.Days
                        .Where(d => d.Date >= monthStart && d.Date <= monthEnd)
                        .ToList();

                    if (monthDays.Count > 0)
                    {
                        var monthDto = new AbsenceCalendarDto
                        {
                            EmployeeId = data.EmployeeId,
                            StartDate = monthStart,
                            EndDate = monthEnd,
                            Days = monthDays,
                            Legend = data.Legend
                        };

                        _absenceCache.Set(currentMonth.Year, currentMonth.Month, monthDto);
                    }

                    currentMonth = currentMonth.AddMonths(1);
                }

                // Update cache range tracking
                _absenceCache.SetCacheRange(startDate, endDate);
                _absenceCache.MarkInitialized();

                Log.Information("[PERF] InitializeAbsenceCacheAsync: Loaded {DayCount} days across {MonthCount} months in {ElapsedMs}ms",
                    data.Days.Count, _absenceCache.Count, sw.ElapsedMilliseconds);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "InitializeAbsenceCacheAsync: Failed to load absences");
        }
    }

    public bool IsAbsenceMonthCached(int year, int month)
    {
        return _absenceCache.Contains(year, month);
    }

    public (DateOnly start, DateOnly end)? GetAbsenceCacheRange()
    {
        return _absenceCache.GetCacheRange();
    }

    public void EnsureAbsenceBuffer(int year, int month)
    {
        if (_client == null || !IsAuthenticated)
            return;

        var cacheRange = _absenceCache.GetCacheRange();
        if (cacheRange == null)
        {
            Log.Warning("EnsureAbsenceBuffer: Cache not initialized");
            return;
        }

        var (cacheStart, cacheEnd) = cacheRange.Value;
        var buffer = _absenceCache.GetBufferMonths(year, month);
        if (buffer == null)
            return;

        var (bufferBackward, bufferForward) = buffer.Value;

        Log.Debug("EnsureAbsenceBuffer: Month {Year}-{Month}, buffer backward={Back}, forward={Fwd}",
            year, month, bufferBackward, bufferForward);

        // Check backward buffer
        if (bufferBackward < MinBufferMonths)
        {
            var prefetchEnd = cacheStart.AddDays(-1);
            var prefetchStart = cacheStart.AddMonths(-PrefetchMonths);

            Log.Information("EnsureAbsenceBuffer: Buffer backward ({BufferBack}) < {MinBuffer}, prefetching {Start} to {End}",
                bufferBackward, MinBufferMonths, prefetchStart, prefetchEnd);

            PrefetchAbsenceRangeAsync(prefetchStart, prefetchEnd, isBackward: true);
        }

        // Check forward buffer
        if (bufferForward < MinBufferMonths)
        {
            var prefetchStart = cacheEnd.AddDays(1);
            var prefetchEnd = cacheEnd.AddMonths(PrefetchMonths);

            Log.Information("EnsureAbsenceBuffer: Buffer forward ({BufferFwd}) < {MinBuffer}, prefetching {Start} to {End}",
                bufferForward, MinBufferMonths, prefetchStart, prefetchEnd);

            PrefetchAbsenceRangeAsync(prefetchStart, prefetchEnd, isBackward: false);
        }
    }

    private void PrefetchAbsenceRangeAsync(DateOnly startDate, DateOnly endDate, bool isBackward)
    {
        // Try to acquire prefetch lock and update range
        if (!_absenceCache.TryStartPrefetch(startDate, endDate, isBackward))
            return;

        var token = _backgroundTasksCts?.Token ?? CancellationToken.None;
        _ = Task.Run(async () =>
        {
            var sw = Stopwatch.StartNew();
            try
            {
                token.ThrowIfCancellationRequested();
                Log.Information("PrefetchAbsenceRangeAsync: Starting prefetch for {Start} to {End}", startDate, endDate);

                var data = await _client!.GetAbsencesAsync(startDate, endDate);

                if (data != null)
                {
                    // Split into monthly chunks
                    var currentMonth = new DateOnly(startDate.Year, startDate.Month, 1);
                    var endMonth = new DateOnly(endDate.Year, endDate.Month, 1);

                    while (currentMonth <= endMonth)
                    {
                        token.ThrowIfCancellationRequested();
                        var monthStart = currentMonth;
                        var monthEnd = monthStart.AddMonths(1).AddDays(-1);

                        var monthDays = data.Days
                            .Where(d => d.Date >= monthStart && d.Date <= monthEnd)
                            .ToList();

                        if (monthDays.Count > 0 && !_absenceCache.Contains(currentMonth.Year, currentMonth.Month))
                        {
                            var monthDto = new AbsenceCalendarDto
                            {
                                EmployeeId = data.EmployeeId,
                                StartDate = monthStart,
                                EndDate = monthEnd,
                                Days = monthDays,
                                Legend = data.Legend
                            };

                            _absenceCache.Set(currentMonth.Year, currentMonth.Month, monthDto);
                        }

                        currentMonth = currentMonth.AddMonths(1);
                    }

                    Log.Information("[PERF] PrefetchAbsenceRangeAsync: Completed in {ElapsedMs}ms, cache now has {MonthCount} months",
                        sw.ElapsedMilliseconds, _absenceCache.Count);
                }
            }
            catch (OperationCanceledException)
            {
                Log.Debug("PrefetchAbsenceRangeAsync: Cancelled for {Start} to {End}", startDate, endDate);
                _absenceCache.RevertPrefetch(PrefetchMonths, isBackward);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "PrefetchAbsenceRangeAsync: Failed to prefetch {Start} to {End}", startDate, endDate);

                // Revert the range update on failure
                _absenceCache.RevertPrefetch(PrefetchMonths, isBackward);
            }
        }, token);
    }

    /// <summary>
    /// Start background prefetch of absence and calendar data after successful login.
    /// This improves perceived performance when navigating to calendar views.
    /// </summary>
    public void StartPostLoginPrefetch()
    {
        if (_client == null || !IsAuthenticated)
        {
            Log.Warning("StartPostLoginPrefetch: Client not authenticated");
            return;
        }

        Log.Information("[PERF] StartPostLoginPrefetch: Starting background data prefetch");

        // Create/reset cancellation token source for background tasks
        _backgroundTasksCts?.Cancel();
        _backgroundTasksCts?.Dispose();
        _backgroundTasksCts = new CancellationTokenSource();
        var token = _backgroundTasksCts.Token;

        // Fire-and-forget: Initialize absence cache in background
        _ = Task.Run(async () =>
        {
            try
            {
                token.ThrowIfCancellationRequested();
                var sw = Stopwatch.StartNew();
                await InitializeAbsenceCacheAsync();
                Log.Information("[PERF] StartPostLoginPrefetch: Absence cache initialized in {ElapsedMs}ms", sw.ElapsedMilliseconds);
            }
            catch (OperationCanceledException)
            {
                Log.Debug("StartPostLoginPrefetch: Absence cache prefetch cancelled");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "StartPostLoginPrefetch: Failed to initialize absence cache");
            }
        }, token);

        // Fire-and-forget: Prefetch current month calendar data
        var today = DateOnly.FromDateTime(DateTime.Today);
        _ = Task.Run(async () =>
        {
            try
            {
                token.ThrowIfCancellationRequested();
                var sw = Stopwatch.StartNew();
                await GetMonthDataAsync(today.Year, today.Month);
                Log.Information("[PERF] StartPostLoginPrefetch: Current month data loaded in {ElapsedMs}ms", sw.ElapsedMilliseconds);
            }
            catch (OperationCanceledException)
            {
                Log.Debug("StartPostLoginPrefetch: Month data prefetch cancelled");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "StartPostLoginPrefetch: Failed to prefetch current month data");
            }
        }, token);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Cancel all background tasks first
        if (_backgroundTasksCts != null)
        {
            Log.Debug("KelioService: Cancelling background tasks");
            _backgroundTasksCts.Cancel();
            _backgroundTasksCts.Dispose();
            _backgroundTasksCts = null;
        }

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
