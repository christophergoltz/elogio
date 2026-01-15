using System.Diagnostics;
using System.Reflection;
using Elogio.Persistence.Api;
using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Events;
using Xunit;
using Xunit.Abstractions;

namespace Elogio.Persistence.Tests.Integration;

/// <summary>
/// Performance integration tests to measure and analyze login and data fetch times.
/// These tests use Stopwatch instrumentation to identify bottlenecks.
///
/// Run with: dotnet test --filter "Category=Performance" -- xunit.parallelizeAssembly=false
///
/// Expected output includes detailed timing breakdowns like:
/// - Login total time
/// - Individual step timings (GetLoginPage, PostLogin, GetSessionId, etc.)
/// - Month data fetch timings
/// </summary>
[Trait("Category", "Performance")]
[Trait("Category", "Integration")]
public class PerformanceIntegrationTests : IDisposable
{
    private readonly ITestOutputHelper _output;

    private static readonly IConfiguration Configuration = new ConfigurationBuilder()
        .AddUserSecrets(Assembly.GetExecutingAssembly(), optional: true)
        .AddEnvironmentVariables()
        .Build();

    private readonly string _serverUrl = Configuration["Kelio:ServerUrl"]
        ?? Environment.GetEnvironmentVariable("KELIO_SERVER_URL")
        ?? throw new InvalidOperationException("Kelio:ServerUrl not configured");
    private readonly string _username = Configuration["Kelio:Username"]
        ?? Environment.GetEnvironmentVariable("KELIO_USERNAME")
        ?? throw new InvalidOperationException("Kelio:Username not configured");
    private readonly string _password = Configuration["Kelio:Password"]
        ?? Environment.GetEnvironmentVariable("KELIO_PASSWORD")
        ?? throw new InvalidOperationException("Kelio:Password not configured");

    public PerformanceIntegrationTests(ITestOutputHelper output)
    {
        _output = output;

        // Configure Serilog to output to test output
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.TestOutput(output, LogEventLevel.Information,
                outputTemplate: "{Timestamp:HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}")
            .CreateLogger();
    }

    public void Dispose()
    {
        Log.CloseAndFlush();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task MeasureLoginPerformance()
    {
        _output.WriteLine("=== LOGIN PERFORMANCE TEST ===");
        _output.WriteLine($"Server: {_serverUrl}");
        _output.WriteLine("");

        using var client = new KelioClient(_serverUrl);

        var sw = Stopwatch.StartNew();
        var result = await client.LoginAsync(_username, _password);
        sw.Stop();

        _output.WriteLine("");
        _output.WriteLine("=== SUMMARY ===");
        _output.WriteLine($"Login success: {result}");
        _output.WriteLine($"TOTAL LOGIN TIME: {sw.ElapsedMilliseconds}ms");
        _output.WriteLine($"Using standalone exe: {client.IsUsingStandaloneExe}");
        _output.WriteLine("");
        _output.WriteLine("Check log output above for detailed step-by-step timings.");
        _output.WriteLine("Look for [PERF] entries to see individual operation times.");

        Assert.True(result, "Login should succeed");

        // Performance expectations (generous thresholds for initial baseline)
        // These can be tightened after optimization
        if (sw.ElapsedMilliseconds > 15000)
        {
            _output.WriteLine($"WARNING: Login took {sw.ElapsedMilliseconds}ms - exceeds 15s threshold!");
        }
    }

    [Fact]
    public async Task MeasureWeekFetchPerformance()
    {
        _output.WriteLine("=== WEEK FETCH PERFORMANCE TEST ===");

        using var client = new KelioClient(_serverUrl);
        await client.LoginAsync(_username, _password);

        _output.WriteLine("Login complete, measuring week fetch...");
        _output.WriteLine("");

        var today = DateOnly.FromDateTime(DateTime.Today);

        var sw = Stopwatch.StartNew();
        var weekData = await client.GetWeekPresenceAsync(today);
        sw.Stop();

        _output.WriteLine("");
        _output.WriteLine("=== SUMMARY ===");
        _output.WriteLine($"Week fetch result: {(weekData != null ? "SUCCESS" : "NULL")}");
        _output.WriteLine($"TOTAL WEEK FETCH TIME: {sw.ElapsedMilliseconds}ms");

        if (weekData != null)
        {
            _output.WriteLine($"Days returned: {weekData.Days.Count}");
            _output.WriteLine($"Employee: {weekData.EmployeeName}");
        }

        Assert.NotNull(weekData);
    }

    [Fact]
    public async Task MeasureMonthFetchPerformance()
    {
        _output.WriteLine("=== MONTH FETCH PERFORMANCE TEST ===");
        _output.WriteLine("This test measures fetching all weeks for the current month.");
        _output.WriteLine("");

        using var client = new KelioClient(_serverUrl);
        await client.LoginAsync(_username, _password);

        _output.WriteLine("Login complete, measuring month fetch...");
        _output.WriteLine("");

        var today = DateOnly.FromDateTime(DateTime.Today);
        var weeks = GetWeeksInMonth(today.Year, today.Month);

        _output.WriteLine($"Month: {today.Year}-{today.Month:D2}");
        _output.WriteLine($"Weeks to fetch: {weeks.Count}");
        _output.WriteLine($"Week starts: {string.Join(", ", weeks.Select(w => w.ToString("MM-dd")))}");
        _output.WriteLine("");

        var totalSw = Stopwatch.StartNew();
        var results = new List<(DateOnly weekStart, long elapsedMs, bool success, bool isFuture)>();

        // Fetch sequentially to measure individual times
        foreach (var weekStart in weeks)
        {
            var sw = Stopwatch.StartNew();
            var weekData = await client.GetWeekPresenceAsync(weekStart);
            sw.Stop();

            // A week is considered "future" if its start is after today
            var isFutureWeek = weekStart > today;
            var status = weekData != null ? "OK" : (isFutureWeek ? "NO DATA (future)" : "FAILED");

            results.Add((weekStart, sw.ElapsedMilliseconds, weekData != null, isFutureWeek));
            _output.WriteLine($"Week {weekStart:yyyy-MM-dd}: {sw.ElapsedMilliseconds}ms - {status}");
        }

        totalSw.Stop();

        var pastWeeks = results.Where(r => !r.isFuture).ToList();
        var futureWeeks = results.Where(r => r.isFuture).ToList();

        _output.WriteLine("");
        _output.WriteLine("=== SUMMARY ===");
        _output.WriteLine($"TOTAL MONTH FETCH TIME (sequential): {totalSw.ElapsedMilliseconds}ms");
        _output.WriteLine($"Average per week: {results.Average(r => r.elapsedMs):F0}ms");
        _output.WriteLine($"Fastest week: {results.Min(r => r.elapsedMs)}ms");
        _output.WriteLine($"Slowest week: {results.Max(r => r.elapsedMs)}ms");
        _output.WriteLine($"Past/current weeks: {pastWeeks.Count(r => r.success)}/{pastWeeks.Count} succeeded");
        _output.WriteLine($"Future weeks: {futureWeeks.Count} (no data expected)");
        _output.WriteLine("");
        _output.WriteLine("NOTE: With parallel fetching, total time could be reduced significantly.");

        // Only assert on past/current weeks - future weeks may not have data
        Assert.True(pastWeeks.All(r => r.success),
            $"All past/current weeks should be fetched successfully. Failed: {string.Join(", ", pastWeeks.Where(r => !r.success).Select(r => r.weekStart))}");
    }

    [Fact]
    public async Task MeasureAbsenceFetchPerformance()
    {
        _output.WriteLine("=== ABSENCE FETCH PERFORMANCE TEST ===");
        _output.WriteLine("This test measures calendar app initialization and absence data fetch.");
        _output.WriteLine("");

        using var client = new KelioClient(_serverUrl);

        var loginSw = Stopwatch.StartNew();
        await client.LoginAsync(_username, _password);
        loginSw.Stop();
        _output.WriteLine($"Login time: {loginSw.ElapsedMilliseconds}ms");
        _output.WriteLine("");

        var today = DateTime.Today;
        var startDate = new DateOnly(today.Year, today.Month, 1);
        var endDate = startDate.AddMonths(1).AddDays(-1);

        _output.WriteLine($"Fetching absences for: {startDate} to {endDate}");
        _output.WriteLine("NOTE: First call includes calendar app initialization (8 sequential steps).");
        _output.WriteLine("");

        // First call - includes initialization
        var firstSw = Stopwatch.StartNew();
        var firstResult = await client.GetAbsencesAsync(startDate, endDate);
        firstSw.Stop();

        _output.WriteLine($"FIRST CALL (with init): {firstSw.ElapsedMilliseconds}ms");

        // Second call - should be faster (no init)
        var secondSw = Stopwatch.StartNew();
        var secondResult = await client.GetAbsencesAsync(startDate, endDate);
        secondSw.Stop();

        _output.WriteLine($"SECOND CALL (no init): {secondSw.ElapsedMilliseconds}ms");

        _output.WriteLine("");
        _output.WriteLine("=== SUMMARY ===");
        _output.WriteLine($"First call (with init): {firstSw.ElapsedMilliseconds}ms");
        _output.WriteLine($"Second call (cached): {secondSw.ElapsedMilliseconds}ms");
        _output.WriteLine($"Init overhead: ~{firstSw.ElapsedMilliseconds - secondSw.ElapsedMilliseconds}ms");

        if (firstResult != null)
        {
            _output.WriteLine($"Days returned: {firstResult.Days.Count}");
            _output.WriteLine($"Vacation days: {firstResult.VacationDays.Count()}");
            _output.WriteLine($"Public holidays: {firstResult.PublicHolidays.Count()}");
        }

        Assert.NotNull(firstResult);
    }

    [Fact]
    public async Task MeasureParallelVsSequentialWeekFetch()
    {
        _output.WriteLine("=== PARALLEL VS SEQUENTIAL COMPARISON ===");
        _output.WriteLine("");

        using var client = new KelioClient(_serverUrl);
        await client.LoginAsync(_username, _password);

        var today = DateTime.Today;
        var weeks = GetWeeksInMonth(today.Year, today.Month);

        _output.WriteLine($"Testing with {weeks.Count} weeks");
        _output.WriteLine("");

        // Sequential fetch
        _output.WriteLine("--- Sequential fetch ---");
        var seqSw = Stopwatch.StartNew();
        foreach (var weekStart in weeks)
        {
            await client.GetWeekPresenceAsync(weekStart);
        }
        seqSw.Stop();
        _output.WriteLine($"Sequential total: {seqSw.ElapsedMilliseconds}ms");

        // Parallel fetch with semaphore (current implementation)
        _output.WriteLine("");
        _output.WriteLine("--- Parallel fetch (semaphore=2) ---");
        var par2Sw = Stopwatch.StartNew();
        var semaphore2 = new SemaphoreSlim(2);
        var tasks2 = weeks.Select(async weekStart =>
        {
            await semaphore2.WaitAsync();
            try { return await client.GetWeekPresenceAsync(weekStart); }
            finally { semaphore2.Release(); }
        });
        await Task.WhenAll(tasks2);
        par2Sw.Stop();
        _output.WriteLine($"Parallel (2) total: {par2Sw.ElapsedMilliseconds}ms");

        // Parallel fetch with higher limit
        _output.WriteLine("");
        _output.WriteLine("--- Parallel fetch (semaphore=4) ---");
        var par4Sw = Stopwatch.StartNew();
        var semaphore4 = new SemaphoreSlim(4);
        var tasks4 = weeks.Select(async weekStart =>
        {
            await semaphore4.WaitAsync();
            try { return await client.GetWeekPresenceAsync(weekStart); }
            finally { semaphore4.Release(); }
        });
        await Task.WhenAll(tasks4);
        par4Sw.Stop();
        _output.WriteLine($"Parallel (4) total: {par4Sw.ElapsedMilliseconds}ms");

        // Full parallel (no limit)
        _output.WriteLine("");
        _output.WriteLine("--- Full parallel (no limit) ---");
        var parFullSw = Stopwatch.StartNew();
        var tasksFull = weeks.Select(weekStart => client.GetWeekPresenceAsync(weekStart));
        await Task.WhenAll(tasksFull);
        parFullSw.Stop();
        _output.WriteLine($"Full parallel total: {parFullSw.ElapsedMilliseconds}ms");

        _output.WriteLine("");
        _output.WriteLine("=== SUMMARY ===");
        _output.WriteLine($"Sequential:     {seqSw.ElapsedMilliseconds}ms");
        _output.WriteLine($"Parallel (2):   {par2Sw.ElapsedMilliseconds}ms ({(double)seqSw.ElapsedMilliseconds / par2Sw.ElapsedMilliseconds:F2}x speedup)");
        _output.WriteLine($"Parallel (4):   {par4Sw.ElapsedMilliseconds}ms ({(double)seqSw.ElapsedMilliseconds / par4Sw.ElapsedMilliseconds:F2}x speedup)");
        _output.WriteLine($"Full parallel:  {parFullSw.ElapsedMilliseconds}ms ({(double)seqSw.ElapsedMilliseconds / parFullSw.ElapsedMilliseconds:F2}x speedup)");
    }

    private static List<DateOnly> GetWeeksInMonth(int year, int month)
    {
        var weeks = new List<DateOnly>();
        var firstDayOfMonth = new DateOnly(year, month, 1);
        var lastDayOfMonth = firstDayOfMonth.AddMonths(1).AddDays(-1);

        var currentWeekStart = firstDayOfMonth.AddDays(-(int)firstDayOfMonth.DayOfWeek + (int)DayOfWeek.Monday);
        if (currentWeekStart > firstDayOfMonth)
            currentWeekStart = currentWeekStart.AddDays(-7);

        while (currentWeekStart <= lastDayOfMonth)
        {
            weeks.Add(currentWeekStart);
            currentWeekStart = currentWeekStart.AddDays(7);
        }

        return weeks;
    }
}
