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
/// Integration tests for curl_proxy server mode.
/// These tests diagnose session cookie persistence issues in server mode.
///
/// Run with: dotnet test --filter "Category=ServerMode"
/// </summary>
[Trait("Category", "ServerMode")]
[Trait("Category", "Integration")]
public class CurlProxyServerModeTests : IDisposable
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

    public CurlProxyServerModeTests(ITestOutputHelper output)
    {
        _output = output;

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.TestOutput(output, LogEventLevel.Debug,
                outputTemplate: "{Timestamp:HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}")
            .CreateLogger();
    }

    public void Dispose()
    {
        Log.CloseAndFlush();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task ServerMode_Initialize_ShouldStartAndRespond()
    {
        _output.WriteLine("=== SERVER MODE INITIALIZATION TEST ===");
        _output.WriteLine("");

        using var curlClient = new CurlImpersonateClient();
        _output.WriteLine($"Executable path: {curlClient.ExecutablePath}");
        _output.WriteLine($"Using standalone exe: {curlClient.IsUsingStandaloneExe}");

        var sw = Stopwatch.StartNew();
        await curlClient.InitializeAsync(enableServerMode: true);
        sw.Stop();

        _output.WriteLine($"Server mode enabled: {curlClient.IsServerModeEnabled}");
        _output.WriteLine($"Server port: {curlClient.ServerPort}");
        _output.WriteLine($"Startup time: {sw.ElapsedMilliseconds}ms");

        Assert.True(curlClient.IsServerModeEnabled, "Server mode should be enabled");
    }

    [Fact]
    public async Task ServerMode_SimpleGetRequest_ShouldWork()
    {
        _output.WriteLine("=== SERVER MODE SIMPLE GET TEST ===");
        _output.WriteLine("");

        using var curlClient = new CurlImpersonateClient();
        await curlClient.InitializeAsync(enableServerMode: true);

        Assert.True(curlClient.IsServerModeEnabled, "Server mode should be enabled");

        // Simple GET request to login page
        var loginUrl = $"{_serverUrl}/open/login";
        _output.WriteLine($"GET {loginUrl}");

        var response = await curlClient.GetAsync(loginUrl);

        _output.WriteLine($"Status: {response.StatusCode}");
        _output.WriteLine($"Headers count: {response.Headers.Count}");

        foreach (var header in response.Headers)
        {
            if (header.Key.Contains("Cookie", StringComparison.OrdinalIgnoreCase) ||
                header.Key.Contains("Set-Cookie", StringComparison.OrdinalIgnoreCase))
            {
                _output.WriteLine($"  {header.Key}: {header.Value}");
            }
        }

        _output.WriteLine($"Body length: {response.Body.Length}");
        _output.WriteLine($"Contains login form: {response.Body.Contains("username")}");

        Assert.True(response.StatusCode == 200 || response.StatusCode == 401);
        Assert.True(response.Body.Contains("username"), "Should contain login form");
    }

    [Fact]
    public async Task ServerMode_SequentialRequests_ShouldMaintainSession()
    {
        _output.WriteLine("=== SERVER MODE SESSION PERSISTENCE TEST ===");
        _output.WriteLine("Testing if cookies persist across multiple requests in server mode.");
        _output.WriteLine("");

        using var curlClient = new CurlImpersonateClient();
        await curlClient.InitializeAsync(enableServerMode: true);

        Assert.True(curlClient.IsServerModeEnabled, "Server mode should be enabled");

        // Request 1: Get login page - should receive JSESSIONID
        _output.WriteLine("--- REQUEST 1: GET /open/login ---");
        var loginUrl = $"{_serverUrl}/open/login";
        var response1 = await curlClient.GetAsync(loginUrl);

        _output.WriteLine($"Status: {response1.StatusCode}");
        var cookie1 = response1.Headers.TryGetValue("Set-Cookie", out var c1) ? c1 : "NO COOKIE";
        _output.WriteLine($"Set-Cookie: {cookie1}");

        // Extract JSESSIONID from response
        var jsessionId1 = ExtractJSessionId(cookie1);
        _output.WriteLine($"Extracted JSESSIONID: {jsessionId1}");
        _output.WriteLine("");

        // Request 2: Another GET to same URL - should use same session
        _output.WriteLine("--- REQUEST 2: GET /open/login (second time) ---");
        var response2 = await curlClient.GetAsync(loginUrl);

        _output.WriteLine($"Status: {response2.StatusCode}");
        var cookie2 = response2.Headers.TryGetValue("Set-Cookie", out var c2) ? c2 : "NO COOKIE";
        _output.WriteLine($"Set-Cookie: {cookie2}");

        var jsessionId2 = ExtractJSessionId(cookie2);
        _output.WriteLine($"Extracted JSESSIONID: {jsessionId2}");
        _output.WriteLine("");

        // Analysis
        _output.WriteLine("=== ANALYSIS ===");
        if (jsessionId1 == jsessionId2)
        {
            _output.WriteLine("SUCCESS: Session cookie persisted across requests!");
        }
        else
        {
            _output.WriteLine("FAILURE: Session cookie changed between requests!");
            _output.WriteLine($"  Request 1: {jsessionId1}");
            _output.WriteLine($"  Request 2: {jsessionId2}");
        }

        // This is the key assertion - session should persist
        Assert.Equal(jsessionId1, jsessionId2);
    }

    [Fact]
    public async Task ServerMode_LoginFlow_ShouldMaintainSessionAcrossSteps()
    {
        _output.WriteLine("=== SERVER MODE FULL LOGIN FLOW TEST ===");
        _output.WriteLine("Testing complete login flow with session persistence.");
        _output.WriteLine("");

        using var curlClient = new CurlImpersonateClient();
        await curlClient.InitializeAsync(enableServerMode: true);

        Assert.True(curlClient.IsServerModeEnabled, "Server mode should be enabled");

        // Step 1: Get login page
        _output.WriteLine("--- STEP 1: GET /open/login ---");
        var loginPageResponse = await curlClient.GetAsync($"{_serverUrl}/open/login");

        _output.WriteLine($"Status: {loginPageResponse.StatusCode}");
        var cookie1 = loginPageResponse.Headers.TryGetValue("Set-Cookie", out var c1) ? c1 : "NO COOKIE";
        _output.WriteLine($"Set-Cookie: {cookie1}");
        var jsessionId1 = ExtractJSessionId(cookie1);
        _output.WriteLine($"JSESSIONID: {jsessionId1}");

        // Extract CSRF token
        var csrfMatch = System.Text.RegularExpressions.Regex.Match(
            loginPageResponse.Body,
            @"name=""_csrf_bodet""\s+value=""([^""]+)""");
        var csrfToken = csrfMatch.Success ? csrfMatch.Groups[1].Value : null;
        _output.WriteLine($"CSRF Token: {csrfToken ?? "NOT FOUND"}");
        _output.WriteLine("");

        Assert.NotNull(csrfToken);

        // Step 2: POST login
        _output.WriteLine("--- STEP 2: POST /open/j_spring_security_check ---");
        var loginBody = $"ACTION=ACTION_VALIDER_LOGIN&username={Uri.EscapeDataString(_username)}&password={Uri.EscapeDataString(_password)}&_csrf_bodet={csrfToken}";

        var loginHeaders = new Dictionary<string, string>
        {
            ["Content-Type"] = "application/x-www-form-urlencoded",
            ["Referer"] = $"{_serverUrl}/open/login",
            ["Origin"] = _serverUrl
        };

        // NOTE: In server mode, we should NOT pass cookies - let Python's global session handle it
        var loginResponse = await curlClient.PostAsync(
            $"{_serverUrl}/open/j_spring_security_check",
            loginBody,
            loginHeaders,
            cookies: null  // Let server mode handle cookies automatically
        );

        _output.WriteLine($"Status: {loginResponse.StatusCode}");
        var cookie2 = loginResponse.Headers.TryGetValue("Set-Cookie", out var c2) ? c2 : "NO COOKIE";
        _output.WriteLine($"Set-Cookie: {cookie2}");
        var jsessionId2 = ExtractJSessionId(cookie2);
        _output.WriteLine($"JSESSIONID: {jsessionId2}");

        var location = loginResponse.Headers.TryGetValue("location", out var loc) ? loc :
                       loginResponse.Headers.TryGetValue("Location", out var loc2) ? loc2 : "NO LOCATION";
        _output.WriteLine($"Location: {location}");

        var loginSuccess = location.Contains("homepage");
        _output.WriteLine($"Login success (redirects to homepage): {loginSuccess}");
        _output.WriteLine("");

        Assert.True(loginSuccess, $"Login should redirect to homepage, got: {location}");

        // Step 3: GET portal page
        _output.WriteLine("--- STEP 3: GET /open/bwt/portail.jsp ---");
        var portalResponse = await curlClient.GetAsync(
            $"{_serverUrl}/open/bwt/portail.jsp",
            cookies: null  // Let server mode handle cookies automatically
        );

        _output.WriteLine($"Status: {portalResponse.StatusCode}");
        var cookie3 = portalResponse.Headers.TryGetValue("Set-Cookie", out var c3) ? c3 : "NO COOKIE";
        _output.WriteLine($"Set-Cookie: {cookie3}");
        var jsessionId3 = ExtractJSessionId(cookie3);
        _output.WriteLine($"JSESSIONID: {jsessionId3}");
        _output.WriteLine($"Body length: {portalResponse.Body.Length}");
        _output.WriteLine($"Contains csrf_token div: {portalResponse.Body.Contains("csrf_token")}");
        _output.WriteLine($"Redirected to login: {portalResponse.Body.Contains("/open/login") || (portalResponse.Headers.TryGetValue("location", out var l) && l.Contains("login"))}");
        _output.WriteLine("");

        // Analysis
        _output.WriteLine("=== SESSION ANALYSIS ===");
        _output.WriteLine($"Step 1 (GET login):  JSESSIONID = {jsessionId1}");
        _output.WriteLine($"Step 2 (POST login): JSESSIONID = {jsessionId2}");
        _output.WriteLine($"Step 3 (GET portal): JSESSIONID = {jsessionId3}");

        if (jsessionId2 == jsessionId3)
        {
            _output.WriteLine("SUCCESS: Session maintained from login to portal!");
        }
        else if (string.IsNullOrEmpty(jsessionId3) && portalResponse.Body.Contains("csrf_token"))
        {
            _output.WriteLine("SUCCESS: No new cookie set, session maintained (implicit)!");
        }
        else
        {
            _output.WriteLine("FAILURE: Session lost between login and portal request!");
        }

        // The portal page should contain the csrf_token div (proves we're authenticated)
        Assert.True(portalResponse.Body.Contains("csrf_token"),
            "Portal page should contain csrf_token div (proves authentication)");
    }

    [Fact]
    public async Task ServerMode_FullKelioLogin_ShouldSucceed()
    {
        _output.WriteLine("=== SERVER MODE FULL KELIO LOGIN TEST ===");
        _output.WriteLine("Using KelioClient with server mode enabled.");
        _output.WriteLine("");

        using var client = new KelioClient(_serverUrl);

        var sw = Stopwatch.StartNew();
        var result = await client.LoginAsync(_username, _password);
        sw.Stop();

        _output.WriteLine("");
        _output.WriteLine("=== RESULT ===");
        _output.WriteLine($"Login success: {result}");
        _output.WriteLine($"Total time: {sw.ElapsedMilliseconds}ms");
        _output.WriteLine($"Session ID: {client.SessionId}");
        _output.WriteLine($"Employee ID: {client.EmployeeId}");
        _output.WriteLine($"Using standalone exe: {client.IsUsingStandaloneExe}");

        Assert.True(result, "Login should succeed with server mode");
        Assert.NotNull(client.SessionId);
        Assert.True(client.EmployeeId > 0, "Employee ID should be extracted");
    }

    [Fact]
    public async Task ServerMode_PreInitialized_LoginShouldBeFast()
    {
        _output.WriteLine("=== SERVER MODE WITH PRE-INITIALIZATION ===");
        _output.WriteLine("Simulates real-world: PreInit when login page shown, measure only login time.");
        _output.WriteLine("");

        using var client = new KelioClient(_serverUrl);

        // Phase 1: Pre-initialize (happens when login page is shown)
        _output.WriteLine("--- PHASE 1: Pre-initialize (done when login page shows) ---");
        var preInitSw = Stopwatch.StartNew();
        await client.PreInitializeAsync();
        preInitSw.Stop();
        _output.WriteLine($"Pre-init time: {preInitSw.ElapsedMilliseconds}ms");
        _output.WriteLine("");

        // Simulate user typing credentials (1 second pause)
        _output.WriteLine("--- Simulating user typing credentials... ---");
        await Task.Delay(1000);
        _output.WriteLine("");

        // Phase 2: Actual login (what user perceives as login time)
        _output.WriteLine("--- PHASE 2: Actual login (user perception) ---");
        var loginSw = Stopwatch.StartNew();
        var result = await client.LoginAsync(_username, _password);
        loginSw.Stop();

        _output.WriteLine("");
        _output.WriteLine("=== RESULT ===");
        _output.WriteLine($"Login success: {result}");
        _output.WriteLine($"Pre-init time: {preInitSw.ElapsedMilliseconds}ms (not perceived by user)");
        _output.WriteLine($"Login time: {loginSw.ElapsedMilliseconds}ms (PERCEIVED by user)");
        _output.WriteLine($"Session ID: {client.SessionId}");
        _output.WriteLine($"Employee ID: {client.EmployeeId}");

        Assert.True(result, "Login should succeed");
        Assert.NotNull(client.SessionId);
        Assert.True(client.EmployeeId > 0, "Employee ID should be extracted");

        // Target: login should be under 10 seconds when pre-initialized
        // (without pre-init it's ~11s, with pre-init should be ~6-7s)
        Assert.True(loginSw.ElapsedMilliseconds < 10000,
            $"Login should complete in under 10s when pre-initialized, took {loginSw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task CompareServerModeVsProcessPerRequest()
    {
        _output.WriteLine("=== COMPARE SERVER MODE VS PROCESS-PER-REQUEST ===");
        _output.WriteLine("");

        // Test 1: Process-per-request (baseline)
        _output.WriteLine("--- TEST 1: Process-per-request mode ---");
        using (var client1 = new KelioClient(_serverUrl))
        {
            // Force process-per-request by not initializing server mode
            // This requires modifying KelioClient to allow disabling server mode
            var sw1 = Stopwatch.StartNew();
            var result1 = await client1.LoginAsync(_username, _password);
            sw1.Stop();

            _output.WriteLine($"Result: {result1}");
            _output.WriteLine($"Time: {sw1.ElapsedMilliseconds}ms");
        }

        _output.WriteLine("");

        // Test 2: Server mode
        _output.WriteLine("--- TEST 2: Server mode ---");
        using (var client2 = new KelioClient(_serverUrl))
        {
            var sw2 = Stopwatch.StartNew();
            var result2 = await client2.LoginAsync(_username, _password);
            sw2.Stop();

            _output.WriteLine($"Result: {result2}");
            _output.WriteLine($"Time: {sw2.ElapsedMilliseconds}ms");
        }
    }

    private static string? ExtractJSessionId(string? cookieHeader)
    {
        if (string.IsNullOrEmpty(cookieHeader))
            return null;

        var match = System.Text.RegularExpressions.Regex.Match(
            cookieHeader,
            @"JSESSIONID=([^;]+)");

        return match.Success ? match.Groups[1].Value : null;
    }
}
