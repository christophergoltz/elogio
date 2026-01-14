using System.Reflection;
using Elogio.Persistence.Api;
using Elogio.Persistence.Dto;
using Microsoft.Extensions.Configuration;
using Xunit;
using Xunit.Abstractions;

namespace Elogio.Persistence.Tests.Integration;

/// <summary>
/// Integration tests for absence calendar functionality.
/// These tests require valid credentials and network access.
/// Run with: dotnet test --filter "Category=Integration"
///
/// Setup credentials using User Secrets:
///   cd tests/Elogio.Persistence.Tests
///   dotnet user-secrets set "Kelio:ServerUrl" "https://your-server.kelio.io"
///   dotnet user-secrets set "Kelio:Username" "your-username"
///   dotnet user-secrets set "Kelio:Password" "your-password"
/// </summary>
[Trait("Category", "Integration")]
public class AbsenceIntegrationTests
{
    private readonly ITestOutputHelper _output;

    private static readonly IConfiguration Configuration = new ConfigurationBuilder()
        .AddUserSecrets(Assembly.GetExecutingAssembly(), optional: true)
        .AddEnvironmentVariables()
        .Build();

    private readonly string _serverUrl = Configuration["Kelio:ServerUrl"]
        ?? Environment.GetEnvironmentVariable("KELIO_SERVER_URL")
        ?? throw new InvalidOperationException("Kelio:ServerUrl not configured.");
    private readonly string _username = Configuration["Kelio:Username"]
        ?? Environment.GetEnvironmentVariable("KELIO_USERNAME")
        ?? throw new InvalidOperationException("Kelio:Username not configured.");
    private readonly string _password = Configuration["Kelio:Password"]
        ?? Environment.GetEnvironmentVariable("KELIO_PASSWORD")
        ?? throw new InvalidOperationException("Kelio:Password not configured.");

    public AbsenceIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task GetAbsences_CurrentYear_ShouldReturnData()
    {
        using var client = new KelioClient(_serverUrl);

        var loginResult = await client.LoginAsync(_username, _password);
        _output.WriteLine($"Login result: {loginResult}");
        _output.WriteLine($"Session ID: {client.SessionId}");
        _output.WriteLine($"Employee ID: {client.EmployeeId}");

        Assert.True(loginResult, "Login should succeed");

        var absences = await client.GetCurrentYearAbsencesAsync();

        _output.WriteLine($"\nAbsence calendar result: {(absences != null ? "Got data" : "NULL")}");

        if (absences != null)
        {
            _output.WriteLine($"Employee ID: {absences.EmployeeId}");
            _output.WriteLine($"Date range: {absences.StartDate} - {absences.EndDate}");
            _output.WriteLine($"Total days: {absences.Days.Count}");

            _output.WriteLine($"\n=== Legend ===");
            foreach (var entry in absences.Legend)
            {
                _output.WriteLine($"  {entry.Label}: {entry.Type} (Color: {entry.ColorValue})");
            }

            _output.WriteLine($"\n=== Vacation Days ({absences.VacationDays.Count()}) ===");
            foreach (var day in absences.VacationDays.Take(10))
            {
                _output.WriteLine($"  {day.Date:dd.MM.yyyy} ({day.Date.DayOfWeek})");
            }

            _output.WriteLine($"\n=== Sick Leave Days ({absences.SickLeaveDays.Count()}) ===");
            foreach (var day in absences.SickLeaveDays.Take(10))
            {
                _output.WriteLine($"  {day.Date:dd.MM.yyyy} ({day.Date.DayOfWeek})");
            }

            _output.WriteLine($"\n=== Public Holidays ({absences.PublicHolidays.Count()}) ===");
            foreach (var day in absences.PublicHolidays.Take(10))
            {
                _output.WriteLine($"  {day.Date:dd.MM.yyyy} ({day.Date.DayOfWeek}) - {day.Label}");
            }

            _output.WriteLine($"\n=== Private Appointments ({absences.PrivateAppointments.Count()}) ===");
            foreach (var day in absences.PrivateAppointments.Take(10))
            {
                _output.WriteLine($"  {day.Date:dd.MM.yyyy} ({day.Date.DayOfWeek})");
            }
        }

        _output.WriteLine($"\nLog file: {KelioClient.GetLogFilePath()}");

        Assert.NotNull(absences);
        Assert.True(absences.Days.Count > 0, "Should have some days");
    }

    [Fact]
    public async Task GetAbsences_SpecificDateRange_ShouldReturnData()
    {
        using var client = new KelioClient(_serverUrl);

        var loginResult = await client.LoginAsync(_username, _password);
        Assert.True(loginResult, "Login should succeed");

        // Get absences for a 3-month range
        var startDate = new DateOnly(2025, 1, 1);
        var endDate = new DateOnly(2025, 12, 31);

        _output.WriteLine($"Requesting absences for: {startDate} - {endDate}");

        var absences = await client.GetAbsencesAsync(startDate, endDate);

        _output.WriteLine($"Result: {(absences != null ? "Got data" : "NULL")}");

        if (absences != null)
        {
            _output.WriteLine($"Days count: {absences.Days.Count}");

            // Group by type
            var byType = absences.Days
                .Where(d => d.Type != AbsenceType.None && d.Type != AbsenceType.Weekend)
                .GroupBy(d => d.Type)
                .OrderBy(g => g.Key);

            foreach (var group in byType)
            {
                _output.WriteLine($"\n{group.Key}: {group.Count()} days");
                foreach (var day in group.Take(5))
                {
                    _output.WriteLine($"  {day.Date:dd.MM.yyyy}");
                }
                if (group.Count() > 5)
                {
                    _output.WriteLine($"  ... and {group.Count() - 5} more");
                }
            }
        }

        Assert.NotNull(absences);
    }

    [Fact]
    public async Task GetAbsences_CheckHolidays_ShouldFindKnownHolidays()
    {
        using var client = new KelioClient(_serverUrl);

        var loginResult = await client.LoginAsync(_username, _password);
        Assert.True(loginResult, "Login should succeed");

        // Get 2026 data (known holidays)
        var absences = await client.GetAbsencesAsync(
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 12, 31));

        Assert.NotNull(absences);

        // Check for known German holidays
        var knownHolidays = new[]
        {
            new DateOnly(2026, 1, 1),   // Neujahr
            new DateOnly(2026, 4, 3),   // Karfreitag
            new DateOnly(2026, 4, 6),   // Ostermontag
            new DateOnly(2026, 5, 1),   // Tag der Arbeit
            new DateOnly(2026, 5, 14),  // Christi Himmelfahrt
            new DateOnly(2026, 5, 25),  // Pfingstmontag
            new DateOnly(2026, 12, 25), // 1. Weihnachtsfeiertag
        };

        _output.WriteLine("Checking known holidays in 2026:");
        foreach (var holiday in knownHolidays)
        {
            var day = absences.Days.FirstOrDefault(d => d.Date == holiday);
            var found = day != null && (day.IsPublicHoliday || day.Type == AbsenceType.PublicHoliday);
            _output.WriteLine($"  {holiday:dd.MM.yyyy}: {(found ? "FOUND" : "MISSING")} - {day?.Type}");
        }

        // Check for half holidays
        var halfHolidays = new[]
        {
            new DateOnly(2026, 2, 16),  // Rosenmontag
            new DateOnly(2026, 12, 24), // Heiligabend
            new DateOnly(2026, 12, 31), // Silvester
        };

        _output.WriteLine("\nChecking half holidays in 2026:");
        foreach (var halfHoliday in halfHolidays)
        {
            var day = absences.Days.FirstOrDefault(d => d.Date == halfHoliday);
            var found = day != null && day.Type == AbsenceType.HalfHoliday;
            _output.WriteLine($"  {halfHoliday:dd.MM.yyyy}: {(found ? "FOUND" : "MISSING")} - {day?.Type}");
        }
    }

    [Fact]
    public async Task GetAbsences_SummaryStatistics_ShouldCalculateCorrectly()
    {
        using var client = new KelioClient(_serverUrl);

        var loginResult = await client.LoginAsync(_username, _password);
        Assert.True(loginResult, "Login should succeed");

        var absences = await client.GetCurrentYearAbsencesAsync();
        Assert.NotNull(absences);

        _output.WriteLine("=== Absence Summary ===");
        _output.WriteLine($"Total days in range: {absences.Days.Count}");
        _output.WriteLine($"Vacation days: {absences.VacationDays.Count()}");
        _output.WriteLine($"Sick leave days: {absences.SickLeaveDays.Count()}");
        _output.WriteLine($"Public holidays: {absences.PublicHolidays.Count()}");
        _output.WriteLine($"Private appointments: {absences.PrivateAppointments.Count()}");

        var weekendCount = absences.Days.Count(d => d.IsWeekend);
        _output.WriteLine($"Weekend days: {weekendCount}");

        var workDays = absences.Days.Count(d =>
            !d.IsWeekend &&
            d.Type == AbsenceType.None);
        _output.WriteLine($"Regular work days: {workDays}");
    }
}
