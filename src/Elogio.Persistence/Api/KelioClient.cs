using System.Net;
using Elogio.Persistence.Api.Auth;
using Elogio.Persistence.Api.Calendar;
using Elogio.Persistence.Api.Http;
using Elogio.Persistence.Api.Services;
using Elogio.Persistence.Api.Session;
using Elogio.Persistence.Dto;
using Elogio.Persistence.Protocol;
using Elogio.Persistence.Services;
using Refit;
using Serilog;

namespace Elogio.Persistence.Api;

/// <summary>
/// High-level client for Kelio API interactions.
/// Handles authentication, session management, and API calls.
/// Uses curl_cffi for TLS fingerprint impersonation to bypass server-side detection.
/// Supports standalone .exe (no Python required) or Python fallback.
/// </summary>
public class KelioClient : IDisposable
{
    private const string BrowserUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";

    // Thread-safe logging semaphore for parallel requests
    private static readonly SemaphoreSlim LoggingSemaphore = new(1, 1);

    private readonly HttpClient _httpClient;
    private readonly HttpClient _bwpClient;
    private readonly CurlImpersonateClient _curlClient;
    private readonly IKelioApi _kelioApi;
    private readonly GwtRpcRequestBuilder _requestBuilder = new();
    private readonly SemainePresenceParser _presenceParser = new();
    private readonly BadgerSignalerResponseParser _punchParser = new();
    private readonly AbsenceCalendarParser _absenceParser = new();
    private readonly BwpCodec _bwpCodec = new();
    private readonly TranslationLoader _translationLoader;
    private readonly KelioAuthenticator _authenticator;
    private readonly CalendarAppInitializer _calendarInitializer;
    private readonly SessionContext _session;
    private readonly string _baseUrl;

    /// <summary>
    /// If true, use curl_cffi for BWP requests to bypass TLS fingerprinting.
    /// If false, use standard HttpClient (will likely get 401 due to TLS fingerprinting).
    /// </summary>
    public bool UseCurlImpersonate { get; set; } = true;

    public bool IsAuthenticated => _session.IsAuthenticated;
    public string? SessionId => _session.SessionId;
    public int EmployeeId => _session.EmployeeId;
    public string? EmployeeName => _session.EmployeeName;

    /// <summary>
    /// Returns true if using standalone curl_proxy.exe (no Python required).
    /// </summary>
    public bool IsUsingStandaloneExe => _curlClient.IsUsingStandaloneExe;

    public KelioClient(string baseUrl)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        var cookieContainer = new CookieContainer();

        // Clear log file for fresh start
        LoggingDelegatingHandler.ClearLog();

        // Auth handler - don't follow redirects to detect 302
        var authHandler = new HttpClientHandler
        {
            CookieContainer = cookieContainer,
            AllowAutoRedirect = false,
            UseCookies = true
        };

        // Add logging handler in the chain
        var loggingHandler = new LoggingDelegatingHandler
        {
            InnerHandler = authHandler,
            CookieContainer = cookieContainer
        };

        _httpClient = new HttpClient(loggingHandler)
        {
            BaseAddress = new Uri(baseUrl)
        };
        _httpClient.DefaultRequestHeaders.Add("User-Agent", BrowserUserAgent);
        _httpClient.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
        _httpClient.DefaultRequestHeaders.Add("Accept-Language", "de-DE,de;q=0.9,en;q=0.8");


        // BWP API (with encoding handler) - needs separate handler chain (fallback if curl not available)
        var bwpInnerHandler = new HttpClientHandler
        {
            CookieContainer = cookieContainer,
            AllowAutoRedirect = true,
            UseCookies = true,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        };
        var bwpLoggingHandler = new LoggingDelegatingHandler
        {
            InnerHandler = bwpInnerHandler,
            CookieContainer = cookieContainer
        };
        var bwpHandler = new BwpDelegatingHandler { InnerHandler = bwpLoggingHandler };
        _bwpClient = new HttpClient(bwpHandler) { BaseAddress = new Uri(baseUrl) };
        _bwpClient.DefaultRequestHeaders.Add("User-Agent", BrowserUserAgent);
        // Add headers that the browser sends for XHR requests
        _bwpClient.DefaultRequestHeaders.Add("X-Requested-With", "XMLHttpRequest");
        _bwpClient.DefaultRequestHeaders.Add("Cache-Control", "no-cache");
        _bwpClient.DefaultRequestHeaders.Add("Referer", $"{baseUrl}{GwtEndpoints.PortalJsp}");
        _kelioApi = RestService.For<IKelioApi>(_bwpClient);

        // curl_cffi client for TLS fingerprint impersonation (bypasses server detection)
        // Automatically uses standalone .exe if available, falls back to Python
        _curlClient = new CurlImpersonateClient(impersonate: "chrome120");

        // Session context for shared state
        _session = new SessionContext(baseUrl);

        // Authenticator for login flow
        _authenticator = new KelioAuthenticator(_curlClient, _requestBuilder, _baseUrl);

        // Translation loader (initialized with delegate to internal send method)
        _translationLoader = new TranslationLoader(_requestBuilder, SendGwtRequestInternalAsync);

        // Calendar app initializer
        _calendarInitializer = new CalendarAppInitializer(_curlClient, _requestBuilder, _bwpCodec, _translationLoader, _baseUrl);
    }

    /// <summary>
    /// Get the path to the HTTP log file.
    /// </summary>
    public static string GetLogFilePath() => LoggingDelegatingHandler.GetLogFilePath();

    /// <summary>
    /// Pre-initialize the curl_proxy server AND pre-fetch login page for faster login.
    /// Call this early (e.g., when login page is shown) to avoid delays during actual login.
    /// Delegates to KelioAuthenticator.
    /// </summary>
    public Task PreInitializeAsync()
        => _authenticator.PreInitializeAsync(_session);

    /// <summary>
    /// Authenticate with the Kelio server.
    /// IMPORTANT: All requests use curl_cffi to maintain consistent TLS fingerprint.
    /// Delegates to KelioAuthenticator.
    /// </summary>
    public Task<bool> LoginAsync(string username, string password)
        => _authenticator.LoginAsync(_session, username, password);

    /// <summary>
    /// Initialize the calendar/absence app before making absence-related API calls.
    /// Delegates to CalendarAppInitializer for the actual initialization logic.
    /// </summary>
    private Task InitializeCalendarAppAsync()
        => _calendarInitializer.InitializeAsync(_session);

    /// <summary>
    /// Load translations that the browser loads before calling getSemaine.
    /// Delegates to TranslationLoader for the actual loading logic.
    /// </summary>
    private Task LoadTranslationsAsync()
        => _translationLoader.LoadPortalTranslationsAsync(_session.SessionId!, _session.EmployeeId);

    /// <summary>
    /// Get week presence data for a specific date.
    /// Uses the dynamic employee ID extracted from GlobalBWTService connect.
    /// </summary>
    /// <param name="date">Any date within the desired week</param>
    public async Task<WeekPresenceDto?> GetWeekPresenceAsync(DateOnly date)
    {
        if (!_session.IsAuthenticated || string.IsNullOrEmpty(_session.SessionId))
        {
            throw new InvalidOperationException("Not authenticated. Call LoginAsync first.");
        }

        var kelioDate = GwtRpcRequestBuilder.ToKelioDate(date);

        // Use dynamic employee ID from GlobalBWTService connect
        if (_session.EmployeeId <= 0)
        {
            await LogDebugAsync("GetWeekPresence: Employee ID not set - GlobalBWTService connect may have failed");
            throw new InvalidOperationException(
                "Employee ID not available. The GlobalBWTService connect call may have failed during login.");
        }

        var gwtRequest = _requestBuilder.BuildGetSemaineRequest(_session.SessionId, kelioDate, _session.EmployeeId);

        await LogDebugAsync($"GetSemaine GWT request with employeeId={_session.EmployeeId}: {gwtRequest}");

        var response = await SendGwtRequestAsync(gwtRequest);

        // Debug: Log the decoded response
        await LogDebugAsync($"GetWeekPresence response (first 500 chars): {response[..Math.Min(500, response.Length)]}");

        // Check for ExceptionBWT
        if (response.Contains("ExceptionBWT"))
        {
            await LogDebugAsync($"GetWeekPresence: Server returned ExceptionBWT!");
            return null;
        }

        var result = _presenceParser.Parse(response);
        await LogDebugAsync($"Parser result: {(result != null ? $"EmployeeName={result.EmployeeName}, Days={result.Days.Count}" : "null")}");

        return result;
    }

    /// <summary>
    /// Log a debug message to the HTTP log file.
    /// OPTIMIZED: Fire-and-forget to avoid blocking the main execution flow.
    /// </summary>
    private static Task LogDebugAsync(string message)
    {
        // Fire-and-forget: queue the write operation without blocking
        _ = Task.Run(async () =>
        {
            await LoggingSemaphore.WaitAsync();
            try
            {
                var logPath = LoggingDelegatingHandler.GetLogFilePath();
                await File.AppendAllTextAsync(logPath, $"\n[DEBUG] {DateTime.Now:HH:mm:ss} {message}\n");
            }
            catch
            {
                // Ignore logging errors - they should not affect main functionality
            }
            finally
            {
                LoggingSemaphore.Release();
            }
        });

        return Task.CompletedTask;
    }

    /// <summary>
    /// Get week presence data for the current week.
    /// </summary>
    public Task<WeekPresenceDto?> GetCurrentWeekPresenceAsync()
    {
        return GetWeekPresenceAsync(DateOnly.FromDateTime(DateTime.Today));
    }

    /// <summary>
    /// Get absence calendar data (vacation, sick leave, holidays, etc.) for a date range.
    /// </summary>
    /// <param name="startDate">Start date of the range</param>
    /// <param name="endDate">End date of the range</param>
    /// <returns>Absence calendar data or null if failed</returns>
    public async Task<AbsenceCalendarDto?> GetAbsencesAsync(DateOnly startDate, DateOnly endDate)
    {
        if (!_session.IsAuthenticated || string.IsNullOrEmpty(_session.SessionId))
        {
            throw new InvalidOperationException("Not authenticated. Call LoginAsync first.");
        }

        if (_session.EmployeeId <= 0)
        {
            await LogDebugAsync("GetAbsences: Employee ID not set - GlobalBWTService connect may have failed");
            throw new InvalidOperationException(
                "Employee ID not available. The GlobalBWTService connect call may have failed during login.");
        }

        // Initialize the calendar app if not already done
        if (!_session.CalendarAppInitialized)
        {
            await InitializeCalendarAppAsync();
        }

        var kelioStartDate = GwtRpcRequestBuilder.ToKelioDate(startDate);
        var kelioEndDate = GwtRpcRequestBuilder.ToKelioDate(endDate);

        // Use the REAL employee ID (from getParametreIntranet) for the employee parameter
        // Use the session context ID for the context parameter
        // HAR shows: employeeId=52 (real), contextId=1372 (session counter)
        var realEmpId = _session.RealEmployeeId > 0 ? _session.RealEmployeeId : _session.EmployeeId;
        var contextId = _session.CalendarContextId > 0 ? _session.CalendarContextId : _session.EmployeeId;

        var gwtRequest = _requestBuilder.BuildGetAbsencesRequest(
            _session.SessionId, realEmpId, kelioStartDate, kelioEndDate, contextId);

        await LogDebugAsync($"GetAbsences GWT request: realEmployeeId={realEmpId}, contextId={contextId}, start={kelioStartDate}, end={kelioEndDate}");

        // Use custom referer for calendar app requests (required for server authorization)
        var response = await SendGwtRequestAsync(gwtRequest, GwtEndpoints.CalendarAbsenceJsp);

        await LogDebugAsync($"GetAbsences response (first 500 chars): {response[..Math.Min(500, response.Length)]}");

        // Check for ExceptionBWT
        if (response.Contains("ExceptionBWT"))
        {
            await LogDebugAsync("GetAbsences: Server returned ExceptionBWT!");
            return null;
        }

        var result = _absenceParser.Parse(response, _session.EmployeeId, startDate, endDate);

        if (result != null)
        {
            await LogDebugAsync($"GetAbsences result: {result.Days.Count} days, " +
                $"Vacation={result.VacationDays.Count()}, " +
                $"SickLeave={result.SickLeaveDays.Count()}, " +
                $"Holidays={result.PublicHolidays.Count()}");
        }

        return result;
    }

    /// <summary>
    /// Get absence calendar data for the current year.
    /// </summary>
    public Task<AbsenceCalendarDto?> GetCurrentYearAbsencesAsync()
    {
        var today = DateTime.Today;
        var startDate = new DateOnly(today.Year, 1, 1);
        var endDate = new DateOnly(today.Year, 12, 31);
        return GetAbsencesAsync(startDate, endDate);
    }

    /// <summary>
    /// Get server time from Kelio.
    /// </summary>
    public async Task<string> GetServerTimeAsync()
    {
        if (!_session.IsAuthenticated || string.IsNullOrEmpty(_session.SessionId))
        {
            throw new InvalidOperationException("Not authenticated. Call LoginAsync first.");
        }

        var gwtRequest = _requestBuilder.BuildGetServerTimeRequest(_session.SessionId);
        return await SendGwtRequestAsync(gwtRequest);
    }

    /// <summary>
    /// Punch (clock in or clock out).
    /// The server automatically determines whether this is a clock-in or clock-out
    /// based on the employee's current state.
    /// </summary>
    /// <returns>Result of the punch operation including type (ClockIn/ClockOut) and timestamp</returns>
    public async Task<PunchResultDto?> PunchAsync()
    {
        if (!_session.IsAuthenticated || string.IsNullOrEmpty(_session.SessionId))
        {
            throw new InvalidOperationException("Not authenticated. Call LoginAsync first.");
        }

        // Use dynamic employee ID from GlobalBWTService connect
        if (_session.EmployeeId <= 0)
        {
            await LogDebugAsync("PUNCH ERROR: Employee ID not set - GlobalBWTService connect may have failed");
            return new PunchResultDto
            {
                Success = false,
                Type = PunchType.Unknown,
                Error = "Employee ID not available. The GlobalBWTService connect call may have failed during login."
            };
        }

        await LogDebugAsync($"=== PUNCH OPERATION START ===");
        await LogDebugAsync($"Punch: SessionId={_session.SessionId}, EmployeeId={_session.EmployeeId}");

        var gwtRequest = _requestBuilder.BuildBadgerSignalerRequest(_session.SessionId, _session.EmployeeId);

        await LogDebugAsync($"Punch GWT request: {gwtRequest}");

        var response = await SendGwtRequestAsync(gwtRequest);

        await LogDebugAsync($"Punch raw response length: {response.Length}");
        await LogDebugAsync($"Punch raw response: {response}");

        // Check for ExceptionBWT
        if (response.Contains("ExceptionBWT"))
        {
            await LogDebugAsync("PUNCH ERROR: Server returned ExceptionBWT!");
            await LogDebugAsync($"=== PUNCH OPERATION FAILED ===");
            return new PunchResultDto
            {
                Success = false,
                Type = PunchType.Unknown,
                Error = "Server returned ExceptionBWT - see log for full response"
            };
        }

        var result = _punchParser.Parse(response);

        if (result == null)
        {
            await LogDebugAsync("PUNCH ERROR: Parser returned null - response format may be unexpected");
            await LogDebugAsync($"=== PUNCH OPERATION FAILED ===");
            return new PunchResultDto
            {
                Success = false,
                Type = PunchType.Unknown,
                Error = "Failed to parse server response - see log for details"
            };
        }

        await LogDebugAsync($"Punch result: Success={result.Success}, Type={result.Type}");
        await LogDebugAsync($"Punch result: Timestamp={result.Timestamp}, Date={result.Date}");
        await LogDebugAsync($"Punch result: Message={result.Message}");
        await LogDebugAsync($"Punch result: Label={result.Label}");
        if (!string.IsNullOrEmpty(result.Error))
        {
            await LogDebugAsync($"Punch result: Error={result.Error}");
        }
        await LogDebugAsync($"=== PUNCH OPERATION {(result.Success ? "SUCCESS" : "FAILED")} ===");

        return result;
    }

    /// <summary>
    /// Send a raw GWT-RPC request and get the decoded response.
    /// </summary>
    /// <param name="gwtRequest">The GWT-RPC request body</param>
    /// <param name="customReferer">Optional custom referer path (use GwtEndpoints constants)</param>
    public async Task<string> SendGwtRequestAsync(string gwtRequest, string? customReferer = null)
    {
        if (!_session.IsAuthenticated)
        {
            throw new InvalidOperationException("Not authenticated. Call LoginAsync first.");
        }

        return await SendGwtRequestInternalAsync(gwtRequest, customReferer);
    }

    /// <summary>
    /// Internal method to send GWT-RPC request without auth check.
    /// Uses curl_cffi via Python to bypass TLS fingerprint detection.
    /// CRITICAL: BWP-encoded requests use body file to avoid character corruption.
    /// </summary>
    /// <param name="gwtRequest">The GWT-RPC request body</param>
    /// <param name="customReferer">Optional custom referer path (use GwtEndpoints constants)</param>
    private async Task<string> SendGwtRequestInternalAsync(string gwtRequest, string? customReferer = null)
    {
        if (!UseCurlImpersonate)
        {
            // Fallback to standard HttpClient (will likely get 401 due to TLS fingerprinting)
            return await _kelioApi.SendGwtRequestAsync(gwtRequest);
        }

        // Use curl_cffi for TLS fingerprint impersonation
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var url = $"{_baseUrl}/open/bwpDispatchServlet?{timestamp}";

        // BWP-encode the request (connect requests are sent raw, others are encoded)
        var isConnectRequest = gwtRequest.Contains(",\"connect\",");
        var bodyToSend = isConnectRequest ? gwtRequest : _bwpCodec.Encode(gwtRequest);

        // Get cookies from CookieContainer
        var cookies = GetCookiesString();

        // Determine the referer - use custom if provided, otherwise default to portail.jsp
        var refererPath = customReferer ?? GwtEndpoints.PortalJsp;

        // Build headers like the browser sends (matching api_discovery.json captures)
        var headers = BwpHeaders.CreateCalendarHeaders(_baseUrl, refererPath, timestamp, BrowserUserAgent);

        await LogDebugAsync($"[curl_cffi] Sending request to {url}");
        await LogDebugAsync($"[curl_cffi] Cookies: {cookies}");
        await LogDebugAsync($"[curl_cffi] Referer: {refererPath}");
        await LogDebugAsync($"[curl_cffi] IsConnectRequest: {isConnectRequest}");
        await LogDebugAsync($"[curl_cffi] Body (first 100): {bodyToSend[..Math.Min(100, bodyToSend.Length)]}");

        CurlResponse response;
        if (isConnectRequest)
        {
            // Connect requests are sent raw - can use normal POST
            response = await _curlClient.PostAsync(url, bodyToSend, headers, cookies);
        }
        else
        {
            // BWP-encoded requests MUST use body file to avoid character corruption
            // Special characters like 0xA4 get corrupted when passed through command line
            response = await _curlClient.PostWithBodyFileAsync(url, bodyToSend, headers, cookies);
        }

        await LogDebugAsync($"[curl_cffi] Response status: {response.StatusCode}");
        await LogDebugAsync($"[curl_cffi] Response body (first 200): {response.Body[..Math.Min(200, response.Body.Length)]}");

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"BWP request failed with status {response.StatusCode}: {response.Error ?? response.Body}");
        }

        // BWP-decode the response if needed
        var responseBody = response.Body;
        if (_bwpCodec.IsBwp(responseBody))
        {
            var decoded = _bwpCodec.Decode(responseBody);
            responseBody = decoded.Decoded;
        }

        return responseBody;
    }

    /// <summary>
    /// Get cookies for curl requests.
    /// Uses manual cookie management to maintain TLS fingerprint consistency.
    /// </summary>
    private string GetCookiesString()
        => _session.GetCookiesString();

    /// <summary>
    /// Throws InvalidOperationException if the client is not authenticated.
    /// Use this to guard methods that require authentication.
    /// </summary>
    private void EnsureAuthenticated()
    {
        if (string.IsNullOrEmpty(_session.SessionId))
            throw new InvalidOperationException(ErrorMessages.NotAuthenticated);
    }

    /// <summary>
    /// Returns true if the client is authenticated, without throwing an exception.
    /// </summary>
    private bool IsSessionValid => !string.IsNullOrEmpty(_session.SessionId);

    private static class ErrorMessages
    {
        public const string NotAuthenticated = "Not authenticated. Call LoginAsync first.";
    }

    public void Dispose()
    {
        // Cancel any running background tasks (e.g., calendar prefetch)
        BackgroundTaskManager.Instance.Reset();

        _httpClient.Dispose();
        _bwpClient.Dispose();
        _curlClient.Dispose();
        GC.SuppressFinalize(this);
    }
}
