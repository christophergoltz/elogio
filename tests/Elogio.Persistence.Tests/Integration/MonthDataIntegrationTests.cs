using System.Reflection;
using Elogio.Persistence.Api;
using Elogio.Persistence.Protocol;
using Microsoft.Extensions.Configuration;
using Xunit;
using Xunit.Abstractions;

namespace Elogio.Persistence.Tests.Integration;

/// <summary>
/// Integration tests for debugging monthly data fetching issues.
/// Run with: dotnet test --filter "FullyQualifiedName~MonthDataIntegrationTests"
/// </summary>
[Trait("Category", "Integration")]
public class MonthDataIntegrationTests
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

    public MonthDataIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task Debug_June2025_AllWeeks_ShouldHaveWorkTimes()
    {
        using var client = new KelioClient(_serverUrl);

        var loginResult = await client.LoginAsync(_username, _password);
        Assert.True(loginResult, "Login should succeed");

        _output.WriteLine($"Session ID: {client.SessionId}");
        _output.WriteLine($"Employee ID: {client.EmployeeId}");
        _output.WriteLine("");

        // June 2025 weeks (Monday start dates)
        var weekStarts = new[]
        {
            new DateOnly(2025, 5, 26),  // W22 - overlaps into June
            new DateOnly(2025, 6, 2),   // W23
            new DateOnly(2025, 6, 9),   // W24
            new DateOnly(2025, 6, 16),  // W25
            new DateOnly(2025, 6, 23),  // W26
            new DateOnly(2025, 6, 30),  // W27 - overlaps into July
        };

        var totalWorkedAllWeeks = TimeSpan.Zero;
        var totalExpectedAllWeeks = TimeSpan.Zero;

        foreach (var weekStart in weekStarts)
        {
            _output.WriteLine($"=== Week starting {weekStart:yyyy-MM-dd} ===");

            var weekPresence = await client.GetWeekPresenceAsync(weekStart);

            if (weekPresence == null)
            {
                _output.WriteLine("  RESULT: NULL - No data returned!");
                _output.WriteLine("");
                continue;
            }

            _output.WriteLine($"  Week start from response: {weekPresence.WeekStartDate}");
            _output.WriteLine($"  Total worked: {weekPresence.TotalWorked}");
            _output.WriteLine($"  Total expected: {weekPresence.TotalExpected}");
            _output.WriteLine($"  Days count: {weekPresence.Days.Count}");

            totalWorkedAllWeeks += weekPresence.TotalWorked;
            totalExpectedAllWeeks += weekPresence.TotalExpected;

            foreach (var day in weekPresence.Days)
            {
                var marker = "";
                if (day.Date.Month == 6 && day.Date.Year == 2025)
                {
                    marker = day.WorkedTime > TimeSpan.Zero ? " ✓" : " ⚠️ ZERO";
                }
                _output.WriteLine($"    {day.Date:yyyy-MM-dd} ({day.DayOfWeek,9}): Worked={day.WorkedTime}, Expected={day.ExpectedTime}{marker}");
            }

            _output.WriteLine("");
        }

        _output.WriteLine($"=== TOTALS FOR ALL WEEKS ===");
        _output.WriteLine($"Total Worked: {totalWorkedAllWeeks} ({totalWorkedAllWeeks.TotalHours:F1} hours)");
        _output.WriteLine($"Total Expected: {totalExpectedAllWeeks} ({totalExpectedAllWeeks.TotalHours:F1} hours)");

        // Assert that we got reasonable data
        Assert.True(totalWorkedAllWeeks.TotalHours > 50,
            $"Expected at least 50 hours worked in June 2025, but got {totalWorkedAllWeeks.TotalHours:F1} hours");
    }

    [Fact]
    public async Task Debug_SingleWeek_RawResponse()
    {
        using var client = new KelioClient(_serverUrl);

        var loginResult = await client.LoginAsync(_username, _password);
        Assert.True(loginResult, "Login should succeed");

        _output.WriteLine($"Employee ID: {client.EmployeeId}");

        // Test week W23 (June 2-8, 2025) - one that was showing zero
        var weekStart = new DateOnly(2025, 6, 2);
        var kelioDate = GwtRpcRequestBuilder.ToKelioDate(weekStart);

        _output.WriteLine($"Testing week starting {weekStart} (Kelio date: {kelioDate})");

        var requestBuilder = new GwtRpcRequestBuilder();
        var gwtRequest = requestBuilder.BuildGetSemaineRequest(client.SessionId!, kelioDate, client.EmployeeId);

        _output.WriteLine($"\nGWT Request:\n{gwtRequest}");

        var response = await client.SendGwtRequestAsync(gwtRequest);

        _output.WriteLine($"\nRaw Response Length: {response.Length} chars");
        _output.WriteLine($"\nFirst 2000 chars:\n{response[..Math.Min(2000, response.Length)]}");

        // Also parse it to see what we get
        var parser = new SemainePresenceParser();
        var parsed = parser.Parse(response);

        _output.WriteLine($"\n=== PARSED RESULT ===");
        if (parsed == null)
        {
            _output.WriteLine("Parser returned NULL!");
        }
        else
        {
            _output.WriteLine($"Week start: {parsed.WeekStartDate}");
            _output.WriteLine($"Total worked: {parsed.TotalWorked}");
            _output.WriteLine($"Total expected: {parsed.TotalExpected}");

            foreach (var day in parsed.Days)
            {
                _output.WriteLine($"  {day.Date}: Worked={day.WorkedTime}, Expected={day.ExpectedTime}");
            }
        }

        Assert.NotNull(parsed);
        Assert.True(parsed.TotalWorked > TimeSpan.Zero || parsed.TotalExpected > TimeSpan.Zero,
            "Week should have some worked or expected time");
    }

    [Fact]
    public async Task Debug_ParseRawResponse_ExtractTimeValues()
    {
        using var client = new KelioClient(_serverUrl);

        var loginResult = await client.LoginAsync(_username, _password);
        Assert.True(loginResult, "Login should succeed");

        // Fetch a week that we know has data
        var weekStart = new DateOnly(2025, 6, 2);
        var kelioDate = GwtRpcRequestBuilder.ToKelioDate(weekStart);

        var requestBuilder = new GwtRpcRequestBuilder();
        var gwtRequest = requestBuilder.BuildGetSemaineRequest(client.SessionId!, kelioDate, client.EmployeeId);
        var response = await client.SendGwtRequestAsync(gwtRequest);

        _output.WriteLine($"Response length: {response.Length}");

        // Find all "15,0,{number}" patterns manually
        var pattern = new System.Text.RegularExpressions.Regex(@"15,0,(\d+)");
        var matches = pattern.Matches(response);

        _output.WriteLine($"\nFound {matches.Count} time values (15,0,X pattern):");

        var values = new List<int>();
        foreach (System.Text.RegularExpressions.Match m in matches)
        {
            if (int.TryParse(m.Groups[1].Value, out var val))
            {
                values.Add(val);
            }
        }

        // Show first 50 values
        _output.WriteLine($"\nFirst 50 values:");
        for (int i = 0; i < Math.Min(50, values.Count); i++)
        {
            var hours = values[i] / 3600.0;
            _output.WriteLine($"  [{i,2}] {values[i],6} ({hours:F2}h)");
        }

        // Try to find the daily pattern
        _output.WriteLine($"\n=== Trying to detect daily pattern ===");
        for (int startIdx = 0; startIdx <= Math.Min(20, values.Count - 35); startIdx++)
        {
            // Each day has 5 values, check if indices 3,4,8,9,13,14... look like daily times
            var valid = true;
            var description = $"startIdx={startIdx}: ";

            for (int day = 0; day < 7 && valid; day++)
            {
                var workedIdx = startIdx + (day * 5) + 3;
                var expectedIdx = startIdx + (day * 5) + 4;

                if (expectedIdx >= values.Count)
                {
                    valid = false;
                    break;
                }

                var worked = values[workedIdx];
                var expected = values[expectedIdx];

                // Expected should be 0 (weekend) or 5-10 hours
                var isValidExpected = expected == 0 || (expected >= 18000 && expected <= 36000);
                // Worked should be 0-14 hours
                var isValidWorked = worked >= 0 && worked <= 50400;

                if (!isValidExpected || !isValidWorked)
                {
                    valid = false;
                }

                description += $"D{day}({worked / 3600.0:F1}h/{expected / 3600.0:F1}h) ";
            }

            if (valid)
            {
                _output.WriteLine($"✓ VALID: {description}");
            }
        }
    }

    [Fact]
    public async Task Debug_CompareWorkingVsNotWorking_RawResponses()
    {
        using var client = new KelioClient(_serverUrl);

        var loginResult = await client.LoginAsync(_username, _password);
        Assert.True(loginResult, "Login should succeed");

        var requestBuilder = new GwtRpcRequestBuilder();

        // Week that WORKS (June 16)
        var workingWeek = new DateOnly(2025, 6, 16);
        var workingRequest = requestBuilder.BuildGetSemaineRequest(client.SessionId!, GwtRpcRequestBuilder.ToKelioDate(workingWeek), client.EmployeeId);
        var workingResponse = await client.SendGwtRequestAsync(workingRequest);

        // Week that DOESN'T work (June 2)
        var notWorkingWeek = new DateOnly(2025, 6, 2);
        var notWorkingRequest = requestBuilder.BuildGetSemaineRequest(client.SessionId!, GwtRpcRequestBuilder.ToKelioDate(notWorkingWeek), client.EmployeeId);
        var notWorkingResponse = await client.SendGwtRequestAsync(notWorkingRequest);

        _output.WriteLine($"=== WORKING WEEK (June 16) - Length: {workingResponse.Length} ===");
        _output.WriteLine($"First 3000 chars:\n{workingResponse[..Math.Min(3000, workingResponse.Length)]}");

        _output.WriteLine("");
        _output.WriteLine($"=== NOT WORKING WEEK (June 2) - Length: {notWorkingResponse.Length} ===");
        _output.WriteLine($"First 3000 chars:\n{notWorkingResponse[..Math.Min(3000, notWorkingResponse.Length)]}");

        // Count "15,0," patterns in each
        var workingPattern = System.Text.RegularExpressions.Regex.Matches(workingResponse, @"15,0,\d+");
        var notWorkingPattern = System.Text.RegularExpressions.Regex.Matches(notWorkingResponse, @"15,0,\d+");

        _output.WriteLine("");
        _output.WriteLine($"Working week has {workingPattern.Count} instances of '15,0,X'");
        _output.WriteLine($"Not working week has {notWorkingPattern.Count} instances of '15,0,X'");

        // Look for alternative time patterns
        _output.WriteLine("");
        _output.WriteLine("=== Looking for BDureeHeure type index in string table ===");

        // Find the index of BDureeHeure in the not-working response
        var bDureeMatch = System.Text.RegularExpressions.Regex.Match(notWorkingResponse, @"""com\.bodet\.bwt\.core\.type\.time\.BDureeHeure""");
        if (bDureeMatch.Success)
        {
            _output.WriteLine($"Found BDureeHeure at position {bDureeMatch.Index}");

            // Count commas before it to find its index
            var beforeText = notWorkingResponse[..bDureeMatch.Index];
            var commaCount = beforeText.Count(c => c == ',');
            _output.WriteLine($"Approximate string table index: {commaCount / 2}"); // rough estimate
        }

        // Search for numeric sequences that look like time (18000-36000 range = 5-10 hours)
        _output.WriteLine("");
        _output.WriteLine("=== Searching for values in 5-10 hour range (18000-36000 seconds) ===");
        var timeRangePattern = new System.Text.RegularExpressions.Regex(@",(\d{5}),");
        var timeMatches = timeRangePattern.Matches(notWorkingResponse);
        foreach (System.Text.RegularExpressions.Match m in timeMatches)
        {
            if (int.TryParse(m.Groups[1].Value, out var val) && val >= 18000 && val <= 36000)
            {
                _output.WriteLine($"Found potential time value: {val} ({val / 3600.0:F2}h) at position {m.Index}");
                // Show context
                var start = Math.Max(0, m.Index - 50);
                var end = Math.Min(notWorkingResponse.Length, m.Index + 50);
                _output.WriteLine($"  Context: ...{notWorkingResponse[start..end]}...");
            }
        }
    }
}
