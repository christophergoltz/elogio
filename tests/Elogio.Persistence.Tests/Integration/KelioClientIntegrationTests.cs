using System.Net;
using System.Reflection;
using System.Text;
using Elogio.Persistence.Api;
using Elogio.Persistence.Protocol;
using Microsoft.Extensions.Configuration;
using Xunit;
using Xunit.Abstractions;

namespace Elogio.Persistence.Tests.Integration;

/// <summary>
/// Integration tests for KelioClient.
/// These tests require valid credentials and network access.
/// Run with: dotnet test --filter "Category=Integration"
///
/// Setup credentials using User Secrets:
///   cd tests/Elogio.Tests
///   dotnet user-secrets set "Kelio:ServerUrl" "https://your-server.kelio.io"
///   dotnet user-secrets set "Kelio:Username" "your-username"
///   dotnet user-secrets set "Kelio:Password" "your-password"
/// </summary>
[Trait("Category", "Integration")]
public class KelioClientIntegrationTests
{
    private readonly ITestOutputHelper _output;

    // Load from User Secrets or environment variables
    private static readonly IConfiguration Configuration = new ConfigurationBuilder()
        .AddUserSecrets(Assembly.GetExecutingAssembly(), optional: true)
        .AddEnvironmentVariables()
        .Build();

    private readonly string _serverUrl = Configuration["Kelio:ServerUrl"]
        ?? Environment.GetEnvironmentVariable("KELIO_SERVER_URL")
        ?? throw new InvalidOperationException("Kelio:ServerUrl not configured. See test class documentation for setup instructions.");
    private readonly string _username = Configuration["Kelio:Username"]
        ?? Environment.GetEnvironmentVariable("KELIO_USERNAME")
        ?? throw new InvalidOperationException("Kelio:Username not configured. See test class documentation for setup instructions.");
    private readonly string _password = Configuration["Kelio:Password"]
        ?? Environment.GetEnvironmentVariable("KELIO_PASSWORD")
        ?? throw new InvalidOperationException("Kelio:Password not configured. See test class documentation for setup instructions.");

    public KelioClientIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task Login_WithValidCredentials_ShouldSucceed()
    {
        using var client = new KelioClient(_serverUrl);

        var result = await client.LoginAsync(_username, _password);

        _output.WriteLine($"Login result: {result}");
        _output.WriteLine($"Session ID: {client.SessionId}");
        _output.WriteLine($"Log file: {KelioClient.GetLogFilePath()}");

        Assert.True(result, "Login should succeed with valid credentials");
        Assert.NotNull(client.SessionId);
    }

    [Fact]
    public async Task GetWeekPresence_AfterLogin_ShouldReturnData()
    {
        using var client = new KelioClient(_serverUrl);

        // Login first
        var loginResult = await client.LoginAsync(_username, _password);
        _output.WriteLine($"Login result: {loginResult}");
        _output.WriteLine($"Session ID: {client.SessionId}");

        Assert.True(loginResult, "Login should succeed");

        // Get current week presence
        var today = DateOnly.FromDateTime(DateTime.Today);
        _output.WriteLine($"Requesting week presence for: {today}");

        var weekPresence = await client.GetWeekPresenceAsync(today);

        _output.WriteLine($"Week presence result: {(weekPresence != null ? "Got data" : "NULL")}");

        if (weekPresence != null)
        {
            _output.WriteLine($"Employee: {weekPresence.EmployeeName}");
            _output.WriteLine($"Week start: {weekPresence.WeekStartDate}");
            _output.WriteLine($"Total worked: {weekPresence.TotalWorked}");
            _output.WriteLine($"Total expected: {weekPresence.TotalExpected}");
            _output.WriteLine($"Days count: {weekPresence.Days.Count}");

            foreach (var day in weekPresence.Days)
            {
                _output.WriteLine($"  {day.Date} ({day.DayOfWeek}): {day.WorkedTime} / {day.ExpectedTime}");
            }
        }

        _output.WriteLine($"\nLog file: {KelioClient.GetLogFilePath()}");

        Assert.NotNull(weekPresence);
    }

    [Fact]
    public async Task GetWeekPresence_PreviousWeek_ShouldReturnData()
    {
        using var client = new KelioClient(_serverUrl);

        var loginResult = await client.LoginAsync(_username, _password);
        Assert.True(loginResult, "Login should succeed");

        _output.WriteLine($"Session ID: {client.SessionId}");
        _output.WriteLine($"Employee ID: {client.EmployeeId}");

        // Get previous week presence
        var previousWeek = DateOnly.FromDateTime(DateTime.Today.AddDays(-7));
        _output.WriteLine($"Requesting week presence for: {previousWeek}");

        var weekPresence = await client.GetWeekPresenceAsync(previousWeek);
        _output.WriteLine($"Week presence result: {(weekPresence != null ? "Got data" : "NULL")}");

        if (weekPresence != null)
        {
            _output.WriteLine($"Employee: {weekPresence.EmployeeName}");
            _output.WriteLine($"Week start: {weekPresence.WeekStartDate}");
            _output.WriteLine($"Total worked: {weekPresence.TotalWorked}");
            _output.WriteLine($"Total expected: {weekPresence.TotalExpected}");

            foreach (var day in weekPresence.Days)
            {
                _output.WriteLine($"  {day.Date} ({day.DayOfWeek}): {day.WorkedTime} / {day.ExpectedTime}");
            }
        }

        Assert.NotNull(weekPresence);
    }

    [Fact]
    public async Task DebugRawResponse_GetSemaine_ShowFullResponse()
    {
        using var client = new KelioClient(_serverUrl);

        var loginResult = await client.LoginAsync(_username, _password);
        _output.WriteLine($"Login: {loginResult}");
        _output.WriteLine($"Session ID: {client.SessionId}");
        _output.WriteLine($"Employee ID: {client.EmployeeId}");

        // Build and send raw getSemaine request with DYNAMIC employee ID
        var requestBuilder = new GwtRpcRequestBuilder();
        var today = DateOnly.FromDateTime(DateTime.Today);
        var kelioDate = GwtRpcRequestBuilder.ToKelioDate(today);

        // CRITICAL: Use dynamic employee ID from login, not hardcoded 227!
        var gwtRequest = requestBuilder.BuildGetSemaineRequest(client.SessionId!, kelioDate, client.EmployeeId);
        _output.WriteLine($"\nGWT Request:\n{gwtRequest}");

        var response = await client.SendGwtRequestAsync(gwtRequest);
        _output.WriteLine($"\nFull GWT Response ({response.Length} chars):\n{response[..Math.Min(1000, response.Length)]}");

        // Check if it's an error response
        if (response.Contains("ExceptionBWT"))
        {
            _output.WriteLine("\n*** SERVER RETURNED AN ERROR ***");
            Assert.Fail("Server returned ExceptionBWT");
        }
        else if (response.Contains("SemainePresenceBWT"))
        {
            _output.WriteLine("\n*** SUCCESS - Got SemainePresenceBWT data ***");
        }

        _output.WriteLine($"\nLog file: {KelioClient.GetLogFilePath()}");
        Assert.Contains("SemainePresenceBWT", response);
    }

    [Fact(Skip = "Demonstrates TLS fingerprinting - .NET HttpClient gets 401 on BWP requests. Use curl_cffi instead.")]
    public async Task ManualHttpRequest_GetServerTime_DemonstratesTlsFingerprinting()
    {
        // This test demonstrates why we need curl_cffi for TLS fingerprint impersonation.
        // Standard .NET HttpClient has a different TLS fingerprint (JA3/JA4) than Chrome,
        // which the Kelio server detects and rejects with 401 Unauthorized.

        // First, login to get session cookies
        var cookies = new CookieContainer();
        var handler = new HttpClientHandler
        {
            CookieContainer = cookies,
            AllowAutoRedirect = true, // Follow redirects like browser
            UseCookies = true,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        };

        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri(_serverUrl) };
        httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");
        httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Encoding", "gzip, deflate, br");

        // Step 1: Get login page (returns 401 but still has content)
        var loginPageResponse = await httpClient.GetAsync("/open/login");
        var loginPage = await loginPageResponse.Content.ReadAsStringAsync();
        var csrfMatch = System.Text.RegularExpressions.Regex.Match(loginPage, @"name=""_csrf_bodet""\s+value=""([^""]+)""");
        var csrfToken = csrfMatch.Success ? csrfMatch.Groups[1].Value : throw new Exception("No CSRF token");
        _output.WriteLine($"CSRF: {csrfToken}");

        // Step 2: Login
        var loginContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["ACTION"] = "ACTION_VALIDER_LOGIN",
            ["username"] = _username,
            ["password"] = _password,
            ["_csrf_bodet"] = csrfToken
        });
        var loginRequest = new HttpRequestMessage(HttpMethod.Post, "/open/j_spring_security_check") { Content = loginContent };
        loginRequest.Headers.Add("Referer", $"{_serverUrl}/open/login");
        loginRequest.Headers.Add("Origin", _serverUrl);
        var loginResponse = await httpClient.SendAsync(loginRequest);
        _output.WriteLine($"Login: {loginResponse.StatusCode} -> {loginResponse.Headers.Location}");

        // Step 3: Get portal page for session ID
        var portalHttpResponse = await httpClient.GetAsync("/open/bwt/portail.jsp");
        var portalResponse = await portalHttpResponse.Content.ReadAsStringAsync();
        _output.WriteLine($"Portal: {portalHttpResponse.StatusCode}");
        var sessionMatch = System.Text.RegularExpressions.Regex.Match(portalResponse, @"<div\s+id=""csrf_token""[^>]*>([^<]+)</div>");
        var sessionId = sessionMatch.Success ? sessionMatch.Groups[1].Value : throw new Exception("No session ID");
        _output.WriteLine($"Session ID: {sessionId}");

        // Step 4: GWT Connect (raw GWT-RPC, no BWP encoding)
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var timestampSec = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var connectBody = $"10,\"com.bodet.bwt.core.type.communication.BWPRequest\",\"java.util.List\",\"java.lang.Long\",\"com.bodet.bwt.portail.serveur.domain.commun.TargetBWT\",\"java.lang.Boolean\",\"NULL\",\"java.lang.String\",\"{sessionId}\",\"connect\",\"com.bodet.bwt.portail.serveur.service.exec.PortailBWTService\",0,1,2,2,411,-{timestampSec},3,4,0,4,0,4,1,5,6,7,6,8,6,9";

        var connectRequest = new HttpRequestMessage(HttpMethod.Post, $"/open/bwpDispatchServlet?{timestamp}");
        connectRequest.Content = new ByteArrayContent(Encoding.UTF8.GetBytes(connectBody));
        connectRequest.Content.Headers.TryAddWithoutValidation("Content-Type", "text/bwp;charset=UTF-8");
        connectRequest.Headers.Add("X-Requested-With", "XMLHttpRequest");
        connectRequest.Headers.Add("Cache-Control", "no-cache");
        connectRequest.Headers.Add("Referer", $"{_serverUrl}/open/bwt/portail.jsp");
        connectRequest.Headers.Add("If-Modified-Since", "Thu, 01 Jan 1970 00:00:00 GMT");
        connectRequest.Headers.TryAddWithoutValidation("x-kelio-stat", $"cst={timestamp}");

        var connectResponse = await httpClient.SendAsync(connectRequest);
        var connectResult = await connectResponse.Content.ReadAsStringAsync();
        _output.WriteLine($"Connect: {connectResponse.StatusCode}");
        _output.WriteLine($"Connect body (first 100): {connectResult[..Math.Min(100, connectResult.Length)]}");

        // Step 4b: Push connect (browser does this after GWT connect!)
        var pushTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var pushRequest = new HttpRequestMessage(HttpMethod.Get, $"/open/push/connect?{pushTimestamp}");
        pushRequest.Headers.Add("X-Requested-With", "XMLHttpRequest");
        pushRequest.Headers.Add("Cache-Control", "no-cache");
        pushRequest.Headers.Add("If-Modified-Since", "Thu, 01 Jan 1970 00:00:00 GMT");
        pushRequest.Headers.TryAddWithoutValidation("x-kelio-stat", $"cst={pushTimestamp}");
        var pushResponse = await httpClient.SendAsync(pushRequest);
        var pushId = await pushResponse.Content.ReadAsStringAsync();
        _output.WriteLine($"Push connect: {pushResponse.StatusCode}, ID: {pushId}");

        // Step 5: getHeureServeur (BWP-encoded, matching browser exactly)
        var timestamp2 = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var rawServerTimeBody = $"7,\"com.bodet.bwt.core.type.communication.BWPRequest\",\"java.util.List\",\"java.lang.Integer\",\"java.lang.String\",\"{sessionId}\",\"getHeureServeur\",\"com.bodet.bwt.global.serveur.service.GlobalBWTService\",0,1,0,2,226,3,4,3,5,3,6";
        _output.WriteLine($"Raw body: {rawServerTimeBody}");

        // Encode with BWP (browser does this!)
        var bwpCodec = new BwpCodec();
        var encodedBody = bwpCodec.Encode(rawServerTimeBody);
        _output.WriteLine($"Encoded body starts with: {encodedBody[..Math.Min(30, encodedBody.Length)]}");

        var serverTimeRequest = new HttpRequestMessage(HttpMethod.Post, $"/open/bwpDispatchServlet?{timestamp2}");
        serverTimeRequest.Content = new ByteArrayContent(Encoding.UTF8.GetBytes(encodedBody));
        serverTimeRequest.Content.Headers.TryAddWithoutValidation("Content-Type", "text/bwp;charset=UTF-8");
        serverTimeRequest.Headers.Add("X-Requested-With", "XMLHttpRequest");
        serverTimeRequest.Headers.Add("Cache-Control", "no-cache");
        serverTimeRequest.Headers.Add("Referer", $"{_serverUrl}/open/bwt/portail.jsp");
        serverTimeRequest.Headers.Add("If-Modified-Since", "Thu, 01 Jan 1970 00:00:00 GMT");
        serverTimeRequest.Headers.TryAddWithoutValidation("x-kelio-stat", $"cst={timestamp2}");
        serverTimeRequest.Headers.TryAddWithoutValidation("Accept", "*/*");
        serverTimeRequest.Headers.TryAddWithoutValidation("Origin", _serverUrl);
        // Add Chrome client hints (sec-ch-ua headers)
        serverTimeRequest.Headers.TryAddWithoutValidation("sec-ch-ua", "\"Chromium\";v=\"131\", \"Not_A Brand\";v=\"24\"");
        serverTimeRequest.Headers.TryAddWithoutValidation("sec-ch-ua-mobile", "?0");
        serverTimeRequest.Headers.TryAddWithoutValidation("sec-ch-ua-platform", "\"Windows\"");

        var serverTimeResponse = await httpClient.SendAsync(serverTimeRequest);
        var serverTimeResult = await serverTimeResponse.Content.ReadAsStringAsync();
        _output.WriteLine($"\ngetHeureServeur: {serverTimeResponse.StatusCode}");
        _output.WriteLine($"Response headers:");
        foreach (var h in serverTimeResponse.Headers)
        {
            _output.WriteLine($"  {h.Key}: {string.Join(", ", h.Value)}");
        }
        _output.WriteLine($"Response body: {serverTimeResult}");

        Assert.Equal(HttpStatusCode.OK, serverTimeResponse.StatusCode);
    }
}
