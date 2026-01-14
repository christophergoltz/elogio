using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Elogio.Persistence.Dto;
using Elogio.Persistence.Protocol;
using Refit;

namespace Elogio.Persistence.Api;

/// <summary>
/// High-level client for Kelio API interactions.
/// Handles authentication, session management, and API calls.
/// Uses curl_cffi for TLS fingerprint impersonation to bypass server-side detection.
/// Supports standalone .exe (no Python required) or Python fallback.
/// </summary>
public partial class KelioClient : IDisposable
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
    private readonly string _baseUrl;

    private string? _csrfToken;
    private string? _sessionId;
    private string? _bwpCsrfToken; // CSRF token from BWP connect response
    private string? _sessionCookie; // Manual cookie management for TLS consistency
    private int _employeeId; // Session context ID from GlobalBWTService connect (portal) - used for most requests
    private int _calendarContextId; // Context ID from Calendar GlobalBWTService connect
    private int _realEmployeeId; // ACTUAL employee ID from getParametreIntranet - used for absence requests
    private bool _isAuthenticated;
    private bool _calendarAppInitialized; // Whether the absence calendar app has been launched

    /// <summary>
    /// If true, use curl_cffi for BWP requests to bypass TLS fingerprinting.
    /// If false, use standard HttpClient (will likely get 401 due to TLS fingerprinting).
    /// </summary>
    public bool UseCurlImpersonate { get; set; } = true;

    public bool IsAuthenticated => _isAuthenticated;
    public string? SessionId => _sessionId;
    public int EmployeeId => _employeeId;

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
        _bwpClient.DefaultRequestHeaders.Add("Referer", $"{baseUrl}/open/bwt/portail.jsp");
        _kelioApi = RestService.For<IKelioApi>(_bwpClient);

        // curl_cffi client for TLS fingerprint impersonation (bypasses server detection)
        // Automatically uses standalone .exe if available, falls back to Python
        _curlClient = new CurlImpersonateClient(impersonate: "chrome120");
    }

    /// <summary>
    /// Get the path to the HTTP log file.
    /// </summary>
    public static string GetLogFilePath() => LoggingDelegatingHandler.GetLogFilePath();

    /// <summary>
    /// Authenticate with the Kelio server.
    /// IMPORTANT: All requests use curl_cffi to maintain consistent TLS fingerprint.
    /// </summary>
    public async Task<bool> LoginAsync(string username, string password)
    {
        try
        {
            // 1. Get login page for CSRF token via curl_cffi (Chrome TLS fingerprint)
            await LogDebugAsync("[curl_cffi] Getting login page...");
            var loginPageResponse = await _curlClient.GetAsync($"{_baseUrl}/open/login");

            if (!loginPageResponse.IsSuccessStatusCode && loginPageResponse.StatusCode != 401)
            {
                throw new HttpRequestException($"Failed to get login page: {loginPageResponse.StatusCode}");
            }

            // Extract session cookie from response
            await LogDebugAsync($"[curl_cffi] Login page response headers: {string.Join(", ", loginPageResponse.Headers.Select(h => $"{h.Key}={h.Value}"))}");
            _sessionCookie = ExtractSessionCookie(loginPageResponse.Headers);
            await LogDebugAsync($"[curl_cffi] Got session cookie: {_sessionCookie}");

            _csrfToken = ExtractCsrfToken(loginPageResponse.Body);
            await LogDebugAsync($"[curl_cffi] Got CSRF token: {_csrfToken}");

            if (string.IsNullOrEmpty(_csrfToken))
            {
                throw new InvalidOperationException("Could not extract CSRF token from login page");
            }

            // 2. Submit login via curl_cffi
            var loginBody = $"ACTION=ACTION_VALIDER_LOGIN&username={Uri.EscapeDataString(username)}&password={Uri.EscapeDataString(password)}&_csrf_bodet={Uri.EscapeDataString(_csrfToken)}";
            var loginHeaders = new Dictionary<string, string>
            {
                ["Content-Type"] = "application/x-www-form-urlencoded",
                ["Referer"] = $"{_baseUrl}/open/login",
                ["Origin"] = _baseUrl,
                ["User-Agent"] = BrowserUserAgent
            };

            await LogDebugAsync($"[curl_cffi] Posting login with body: {loginBody[..Math.Min(100, loginBody.Length)]}...");
            await LogDebugAsync($"[curl_cffi] Using cookie: {_sessionCookie}");
            var loginResponse = await _curlClient.PostAsync(
                $"{_baseUrl}/open/j_spring_security_check",
                loginBody, loginHeaders, _sessionCookie);

            await LogDebugAsync($"[curl_cffi] Login response status: {loginResponse.StatusCode}");
            await LogDebugAsync($"[curl_cffi] Login response body (first 500): {loginResponse.Body[..Math.Min(500, loginResponse.Body.Length)]}");
            await LogDebugAsync($"[curl_cffi] Login response headers: {string.Join(", ", loginResponse.Headers.Select(h => $"{h.Key}={h.Value}"))}");

            // Update cookie from response (server may issue new JSESSIONID)
            var newCookie = ExtractSessionCookie(loginResponse.Headers);
            if (!string.IsNullOrEmpty(newCookie))
            {
                _sessionCookie = newCookie;
                await LogDebugAsync($"[curl_cffi] Updated session cookie: {_sessionCookie}");
            }

            // Check if login was successful
            // With allow_redirects=False in curl_proxy.py, we get 302 directly
            // 302 with Location containing "homepage" means success
            var locationHeader = loginResponse.Headers.GetValueOrDefault("Location") ??
                                 loginResponse.Headers.GetValueOrDefault("location") ?? "";
            _isAuthenticated = loginResponse.StatusCode == 302 &&
                               locationHeader.Contains("homepage");

            await LogDebugAsync($"[curl_cffi] Login success check: status={loginResponse.StatusCode}, location={locationHeader}, authenticated={_isAuthenticated}");

            if (_isAuthenticated)
            {
                // Get the session ID from portail.jsp (server-provided)
                _sessionId = await GetSessionIdFromPortalViaCurlAsync();

                if (string.IsNullOrEmpty(_sessionId))
                {
                    throw new InvalidOperationException("Could not extract session ID from portal page");
                }

                // Initialize BWP session with connect call
                await ConnectBwpSessionAsync();

                // Push connect via curl_cffi
                await ConnectPushViaCurlAsync();

                // Call getHeureServeur to complete initialization
                await InitializeServerStateAsync();
            }

            return _isAuthenticated;
        }
        catch (Exception)
        {
            _isAuthenticated = false;
            throw;
        }
    }

    /// <summary>
    /// Load the portal page and extract the server-provided session ID.
    /// Uses curl_cffi to maintain TLS fingerprint consistency.
    /// </summary>
    private async Task<string?> GetSessionIdFromPortalViaCurlAsync()
    {
        // The portal page contains the session ID in a hidden div
        await LogDebugAsync("[curl_cffi] Getting portal page...");
        var portalResponse = await _curlClient.GetAsync(
            $"{_baseUrl}/open/bwt/portail.jsp",
            cookies: _sessionCookie);

        // Update cookie if server sends a new one
        var newCookie = ExtractSessionCookie(portalResponse.Headers);
        if (!string.IsNullOrEmpty(newCookie))
        {
            _sessionCookie = newCookie;
            await LogDebugAsync($"[curl_cffi] Portal updated session cookie: {_sessionCookie}");
        }

        // Extract session ID from: <div id="csrf_token" style="display:none">SESSION_ID</div>
        var match = CsrfTokenDivRegex().Match(portalResponse.Body);
        var sessionId = match.Success ? match.Groups[1].Value : null;

        await LogDebugAsync($"[curl_cffi] Portal session ID extracted: {sessionId}");

        // Load GWT JavaScript files - the server may require these to be downloaded
        try
        {
            await _curlClient.GetAsync(
                $"{_baseUrl}/open/bwt/portail/portail.nocache.js",
                cookies: _sessionCookie);
            await LogDebugAsync("[curl_cffi] Loaded portail.nocache.js");

            await _curlClient.GetAsync(
                $"{_baseUrl}/open/bwt/portail/85D2B992F6111BC9BF615C4D657B05CC.cache.js",
                cookies: _sessionCookie);
            await LogDebugAsync("[curl_cffi] Loaded cache.js");
        }
        catch (Exception ex)
        {
            await LogDebugAsync($"[curl_cffi] Warning: Could not load GWT files: {ex.Message}");
        }

        return sessionId;
    }

    /// <summary>
    /// Establish the BWP session after HTTP login.
    /// This must be called before any other BWP API calls.
    /// IMPORTANT: Must use curl_cffi to maintain consistent TLS fingerprint!
    /// </summary>
    private async Task ConnectBwpSessionAsync()
    {
        if (string.IsNullOrEmpty(_sessionId))
        {
            throw new InvalidOperationException("Session ID not set.");
        }

        // Use seconds (not milliseconds) for the connect request data
        var timestampSec = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var gwtRequest = _requestBuilder.BuildConnectRequest(_sessionId, timestampSec);

        await LogDebugAsync($"Connect GWT request: {gwtRequest}");

        // CRITICAL: Use curl_cffi for connect to maintain TLS fingerprint consistency
        // The server tracks TLS fingerprints - if we use .NET HttpClient for connect
        // and curl_cffi for subsequent requests, the server detects the mismatch
        var timestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var url = $"{_baseUrl}/open/bwpDispatchServlet?{timestampMs}";
        var cookies = GetCookiesString();

        var headers = new Dictionary<string, string>
        {
            ["Content-Type"] = "text/bwp;charset=UTF-8",
            ["X-Requested-With"] = "XMLHttpRequest",
            ["Cache-Control"] = "no-cache",
            ["Referer"] = $"{_baseUrl}/open/bwt/portail.jsp",
            ["If-Modified-Since"] = "Thu, 01 Jan 1970 00:00:00 GMT",
            ["x-kelio-stat"] = $"cst={timestampMs}",
            ["User-Agent"] = BrowserUserAgent,
            // Chrome client hints
            ["sec-ch-ua"] = "\"Chromium\";v=\"131\", \"Not_A Brand\";v=\"24\"",
            ["sec-ch-ua-mobile"] = "?0",
            ["sec-ch-ua-platform"] = "\"Windows\""
        };

        await LogDebugAsync($"[curl_cffi] Connect request to {url}");
        await LogDebugAsync($"[curl_cffi] Connect cookies: {cookies}");

        // Connect is sent RAW (not BWP-encoded)
        var response = await _curlClient.PostAsync(url, gwtRequest, headers, cookies);

        await LogDebugAsync($"[curl_cffi] Connect response status: {response.StatusCode}");
        await LogDebugAsync($"[curl_cffi] Connect response headers: {string.Join(", ", response.Headers.Select(h => $"{h.Key}={h.Value}"))}");
        await LogDebugAsync($"[curl_cffi] Connect response (first 300): {response.Body[..Math.Min(300, response.Body.Length)]}");

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Connect failed with status {response.StatusCode}");
        }

        // Extract x-csrf-token from response headers - required for subsequent BWP requests
        foreach (var key in response.Headers.Keys)
        {
            if (key.Equals("x-csrf-token", StringComparison.OrdinalIgnoreCase))
            {
                _bwpCsrfToken = response.Headers[key];
                await LogDebugAsync($"[curl_cffi] Got BWP CSRF token: {_bwpCsrfToken}");
                break;
            }
        }

        // Push connect is called separately in LoginAsync via ConnectPushViaCurlAsync
    }

    /// <summary>
    /// Connect to push notification service via curl_cffi.
    /// Browser does this after GWT connect - may be required for subsequent API calls.
    /// Uses curl_cffi to maintain TLS fingerprint consistency.
    /// </summary>
    private async Task ConnectPushViaCurlAsync()
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var headers = new Dictionary<string, string>
        {
            ["X-Requested-With"] = "XMLHttpRequest",
            ["Cache-Control"] = "no-cache",
            ["Referer"] = $"{_baseUrl}/open/bwt/portail.jsp",
            ["If-Modified-Since"] = "Thu, 01 Jan 1970 00:00:00 GMT",
            ["x-kelio-stat"] = $"cst={timestamp}",
            ["User-Agent"] = BrowserUserAgent
        };

        await LogDebugAsync("[curl_cffi] Connecting to push...");
        var response = await _curlClient.GetAsync(
            $"{_baseUrl}/open/push/connect?{timestamp}",
            headers, _sessionCookie);

        await LogDebugAsync($"[curl_cffi] Push connect response: {response.StatusCode}, body: {response.Body}");
    }

    /// <summary>
    /// Initialize server state after connect.
    /// Launch the declaration app, then call GlobalBWTService connect to get employee ID.
    /// Browser makes getTraductions calls to initialize i18n before getSemaine.
    /// </summary>
    private async Task InitializeServerStateAsync()
    {
        if (string.IsNullOrEmpty(_sessionId))
            return;

        // Launch the declaration app FIRST - this may enable BWP requests
        try
        {
            await LogDebugAsync($"InitializeServerState - launching declaration app");
            var appLaunchResponse = await _curlClient.GetAsync(
                $"{_baseUrl}/open/bwt/appLauncher.jsp?app=app_declaration_desktop&appParams=idMenuDeclaration=1",
                cookies: _sessionCookie);
            await LogDebugAsync($"InitializeServerState - appLauncher status: {appLaunchResponse.StatusCode}");

            // Update session cookie if the server returned a new one
            var newCookie = ExtractSessionCookie(appLaunchResponse.Headers);
            if (!string.IsNullOrEmpty(newCookie))
            {
                await LogDebugAsync($"InitializeServerState - updated cookie: {newCookie}");
                _sessionCookie = newCookie;
            }

            // Load declaration app GWT files
            await _curlClient.GetAsync(
                $"{_baseUrl}/open/bwt/app_declaration_desktop/app_declaration_desktop.nocache.js",
                cookies: _sessionCookie);
            await _curlClient.GetAsync(
                $"{_baseUrl}/open/bwt/app_declaration_desktop/1A313ED29AA1E74DD777D2CCF3248188.cache.js",
                cookies: _sessionCookie);
        }
        catch (Exception ex)
        {
            await LogDebugAsync($"InitializeServerState - appLauncher warning: {ex.Message}");
        }

        // GlobalBWTService connect - CRITICAL: This returns the dynamic employee ID!
        try
        {
            await GlobalBwtServiceConnectAsync();
        }
        catch (Exception ex)
        {
            await LogDebugAsync($"InitializeServerState - GlobalBWTService connect warning: {ex.Message}");
        }

        // Load translations - browser does this before calling getSemaine
        // This may initialize server-side state for the presence module
        await LoadTranslationsAsync();
    }

    /// <summary>
    /// Call GlobalBWTService connect to get the dynamic employee ID.
    /// CRITICAL: The employee ID is session-specific and must be extracted from this response.
    /// </summary>
    private async Task GlobalBwtServiceConnectAsync()
    {
        if (string.IsNullOrEmpty(_sessionId))
            return;

        var timestampSec = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var timestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var gwtRequest = _requestBuilder.BuildGlobalConnectRequest(_sessionId, timestampSec);

        var url = $"{_baseUrl}/open/bwpDispatchServlet?{timestampMs}";
        var cookies = GetCookiesString();

        var headers = new Dictionary<string, string>
        {
            ["Content-Type"] = "text/bwp;charset=UTF-8",
            ["X-Requested-With"] = "XMLHttpRequest",
            ["Cache-Control"] = "no-cache",
            ["Referer"] = $"{_baseUrl}/open/bwt/portail.jsp",
            ["If-Modified-Since"] = "Thu, 01 Jan 1970 00:00:00 GMT",
            ["x-kelio-stat"] = $"cst={timestampMs}",
            ["User-Agent"] = BrowserUserAgent,
            ["sec-ch-ua"] = "\"Chromium\";v=\"131\", \"Not_A Brand\";v=\"24\"",
            ["sec-ch-ua-mobile"] = "?0",
            ["sec-ch-ua-platform"] = "\"Windows\""
        };

        await LogDebugAsync($"[curl_cffi] GlobalBWTService connect to {url}");

        // GlobalBWTService connect is sent RAW (not BWP-encoded)
        var response = await _curlClient.PostAsync(url, gwtRequest, headers, cookies);

        await LogDebugAsync($"[curl_cffi] GlobalBWTService connect response status: {response.StatusCode}");
        await LogDebugAsync($"[curl_cffi] GlobalBWTService connect response length: {response.Body.Length}");

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"GlobalBWTService connect failed with status {response.StatusCode}");
        }

        // Extract employee ID from response
        _employeeId = ExtractEmployeeIdFromConnectResponse(response.Body);
        await LogDebugAsync($"[curl_cffi] Extracted employee ID: {_employeeId}");
    }

    /// <summary>
    /// Call GlobalBWTService connect for the calendar app.
    /// This uses Short=16 (vs 21 for portal) and the calendar JSP as referer.
    /// This must be called before making calendar/absence API requests.
    /// </summary>
    private async Task CalendarGlobalConnectAsync()
    {
        if (string.IsNullOrEmpty(_sessionId))
            return;

        var timestampSec = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var timestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var gwtRequest = _requestBuilder.BuildCalendarConnectRequest(_sessionId, timestampSec);

        var url = $"{_baseUrl}/open/bwpDispatchServlet?{timestampMs}";
        var cookies = GetCookiesString();

        var headers = new Dictionary<string, string>
        {
            ["Content-Type"] = "text/bwp;charset=UTF-8",
            ["X-Requested-With"] = "XMLHttpRequest",
            ["Cache-Control"] = "no-cache",
            ["Origin"] = _baseUrl,
            ["Referer"] = $"{_baseUrl}/open/bwt/intranet_calendrier_absence.jsp",
            ["If-Modified-Since"] = "Thu, 01 Jan 1970 00:00:00 GMT",
            ["x-kelio-stat"] = $"cst={timestampMs}",
            ["User-Agent"] = BrowserUserAgent,
            ["Sec-Fetch-Dest"] = "empty",
            ["Sec-Fetch-Mode"] = "cors",
            ["Sec-Fetch-Site"] = "same-origin",
            ["sec-ch-ua"] = "\"Chromium\";v=\"131\", \"Not_A Brand\";v=\"24\"",
            ["sec-ch-ua-mobile"] = "?0",
            ["sec-ch-ua-platform"] = "\"Windows\""
        };

        await LogDebugAsync($"[curl_cffi] Calendar GlobalBWTService connect to {url}");

        // GlobalBWTService connect is sent RAW (not BWP-encoded)
        var response = await _curlClient.PostAsync(url, gwtRequest, headers, cookies);

        await LogDebugAsync($"[curl_cffi] Calendar GlobalBWTService connect response status: {response.StatusCode}");

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Calendar GlobalBWTService connect failed with status {response.StatusCode}");
        }

        // Extract session context ID from calendar connect response
        // This is the incrementing session counter used in subsequent requests
        _calendarContextId = ExtractEmployeeIdFromConnectResponse(response.Body);
        await LogDebugAsync($"[curl_cffi] Calendar session context ID: {_calendarContextId}");
    }

    /// <summary>
    /// Extract the dynamic employee ID from GlobalBWTService connect response.
    /// The employee ID appears near the END of the data tokens, before the user's name.
    /// Pattern: [..., TYPE_REF, EMPLOYEE_ID, TYPE_REF, FIRSTNAME_IDX, TYPE_REF, LASTNAME_IDX, ...]
    /// </summary>
    private int ExtractEmployeeIdFromConnectResponse(string responseBody)
    {
        try
        {
            var parts = responseBody.Split(',');

            // Parse GWT-RPC: first number is string count
            if (!int.TryParse(parts[0], out var stringCount))
                return 0;

            // Extract strings (we need to skip past them to get to data tokens)
            var strings = new List<string>();
            var idx = 1;
            while (idx < parts.Length && strings.Count < stringCount)
            {
                var part = parts[idx];
                if (part.StartsWith("\""))
                {
                    var fullString = new StringBuilder(part[1..]);  // Remove opening quote
                    while (idx < parts.Length && !parts[idx].EndsWith("\""))
                    {
                        idx++;
                        if (idx < parts.Length)
                            fullString.Append(',').Append(parts[idx]);
                    }
                    // Remove closing quote
                    var str = fullString.ToString();
                    if (str.EndsWith("\""))
                        str = str[..^1];
                    strings.Add(str);
                }
                idx++;
            }

            // Get data tokens (after all strings)
            var dataTokens = parts[idx..].Select(p => p.Trim()).ToList();

            // Find the last two name strings (firstname, lastname) - they're at the very end
            // These are strings that look like personal names (capitalized, no dots, etc.)
            var lastNameStrIdx = -1;
            var firstNameStrIdx = -1;

            // Search backwards for two consecutive name-like strings
            for (var i = strings.Count - 1; i >= 1; i--)
            {
                var s = strings[i];
                // Name strings: capitalized, no dots, reasonable length, not type names
                if (!s.Contains('.') && s != "NULL" && !string.IsNullOrWhiteSpace(s) &&
                    s.Length >= 2 && s.Length <= 30 &&
                    !s.Contains("java") && !s.Contains("com") && !s.Contains("ENUM") &&
                    char.IsUpper(s[0]) && !s.All(char.IsUpper)) // Starts with capital but not ALL CAPS
                {
                    if (lastNameStrIdx < 0)
                    {
                        lastNameStrIdx = i;
                    }
                    else
                    {
                        firstNameStrIdx = i;
                        break; // Found both
                    }
                }
            }

            _ = LogDebugAsync($"ExtractEmployeeId: firstName=[{firstNameStrIdx}] '{(firstNameStrIdx >= 0 ? strings[firstNameStrIdx] : "?")}', lastName=[{lastNameStrIdx}] '{(lastNameStrIdx >= 0 ? strings[lastNameStrIdx] : "?")}'");

            if (firstNameStrIdx < 0 || lastNameStrIdx < 0)
            {
                _ = LogDebugAsync("ExtractEmployeeId: Could not find name strings!");
                return 0;
            }

            // Log last 50 data tokens for analysis
            _ = LogDebugAsync($"ExtractEmployeeId: Last 50 data tokens: {string.Join(",", dataTokens.TakeLast(50))}");

            // Find pattern: EMPLOYEE_ID, 4, firstNameStrIdx, 4, lastNameStrIdx
            // Search for the firstname index in data tokens, then look backwards for employee ID
            for (var i = dataTokens.Count - 1; i >= 2; i--)
            {
                if (!int.TryParse(dataTokens[i], out var tokenVal))
                    continue;

                // Found firstname index reference
                if (tokenVal == firstNameStrIdx)
                {
                    // Pattern should be: EMPLOYEE_ID, 4, firstNameStrIdx, 4, lastNameStrIdx
                    // So employee ID is at i-2 (loop guarantees i >= 2)
                    if (int.TryParse(dataTokens[i - 1], out var typeRef) && typeRef == 4 &&
                        int.TryParse(dataTokens[i - 2], out var employeeId) &&
                        employeeId >= 100 && employeeId <= 9999)
                    {
                        _ = LogDebugAsync($"ExtractEmployeeId: Found employee ID {employeeId} before name pattern at pos {i-2}");
                        return employeeId;
                    }
                }
            }

            _ = LogDebugAsync("ExtractEmployeeId: No valid employee ID found!");
            return 0;
        }
        catch (Exception ex)
        {
            // Log the error for debugging
            _ = LogDebugAsync($"ExtractEmployeeIdFromConnectResponse error: {ex.Message}");
            return 0;
        }
    }

    /// <summary>
    /// Initialize the calendar/absence app before making absence-related API calls.
    /// Based on HAR capture analysis: load the intranet_calendrier_absence.jsp page and GWT files.
    /// </summary>
    private async Task InitializeCalendarAppAsync()
    {
        if (_calendarAppInitialized)
            return;

        try
        {
            await LogDebugAsync("InitializeCalendarApp - starting initialization");

            // Step 0: Navigate to intranet section first (required per HAR capture)
            // The browser navigates to homepage?ACTION=intranet before the calendar JSP
            try
            {
                await LogDebugAsync("InitializeCalendarApp - navigating to intranet section");
                var intranetResponse = await _curlClient.GetAsync(
                    $"{_baseUrl}/open/homepage?ACTION=intranet&asked=6&header=0",
                    cookies: _sessionCookie);
                await LogDebugAsync($"InitializeCalendarApp - intranet navigation status: {intranetResponse.StatusCode}");

                var newCookie = ExtractSessionCookie(intranetResponse.Headers);
                if (!string.IsNullOrEmpty(newCookie))
                {
                    _sessionCookie = newCookie;
                    await LogDebugAsync("InitializeCalendarApp - updated cookie from intranet navigation");
                }
            }
            catch (Exception ex)
            {
                await LogDebugAsync($"InitializeCalendarApp - intranet navigation warning: {ex.Message}");
            }

            // Step 1: Load the intranet_calendrier_absence.jsp page
            // This sets up the server-side session state for the calendar module
            try
            {
                await LogDebugAsync("InitializeCalendarApp - loading intranet_calendrier_absence.jsp");
                var jspResponse = await _curlClient.GetAsync(
                    $"{_baseUrl}/open/bwt/intranet_calendrier_absence.jsp",
                    cookies: _sessionCookie);
                await LogDebugAsync($"InitializeCalendarApp - JSP page status: {jspResponse.StatusCode}");

                var newCookie = ExtractSessionCookie(jspResponse.Headers);
                if (!string.IsNullOrEmpty(newCookie))
                {
                    _sessionCookie = newCookie;
                    await LogDebugAsync("InitializeCalendarApp - updated cookie from JSP");
                }
            }
            catch (Exception ex)
            {
                await LogDebugAsync($"InitializeCalendarApp - JSP page warning: {ex.Message}");
            }

            // Step 2: Load the GWT nocache.js file
            try
            {
                await LogDebugAsync("InitializeCalendarApp - loading GWT nocache.js");
                await _curlClient.GetAsync(
                    $"{_baseUrl}/open/bwt/intranet_calendrier_absence/intranet_calendrier_absence.nocache.js",
                    cookies: _sessionCookie);
                await LogDebugAsync("InitializeCalendarApp - loaded nocache.js");
            }
            catch (Exception ex)
            {
                await LogDebugAsync($"InitializeCalendarApp - nocache.js warning: {ex.Message}");
            }

            // Step 3: Load the GWT cache.js file (hash from HAR capture)
            try
            {
                await LogDebugAsync("InitializeCalendarApp - loading GWT cache.js");
                await _curlClient.GetAsync(
                    $"{_baseUrl}/open/bwt/intranet_calendrier_absence/B774D9023F6AE5125A0446A2F6C1BC19.cache.js",
                    cookies: _sessionCookie);
                await LogDebugAsync("InitializeCalendarApp - loaded cache.js");
            }
            catch (Exception ex)
            {
                await LogDebugAsync($"InitializeCalendarApp - cache.js warning: {ex.Message}");
            }

            // Step 4: Make GlobalBWTService.connect request for calendar app
            // This activates the calendar module for the session (uses Short=16 vs 21 for portal)
            try
            {
                await LogDebugAsync("InitializeCalendarApp - making GlobalBWTService connect for calendar");
                await CalendarGlobalConnectAsync();
                await LogDebugAsync("InitializeCalendarApp - GlobalBWTService connect complete");
            }
            catch (Exception ex)
            {
                await LogDebugAsync($"InitializeCalendarApp - GlobalBWTService connect warning: {ex.Message}");
            }

            // Step 5: Call getPresentationModel for GlobalPresentationModel
            // HAR shows this is called before the calendar-specific presentation model
            try
            {
                await LogDebugAsync("InitializeCalendarApp - calling getPresentationModel for GlobalPresentationModel");
                await CalendarGetGlobalPresentationModelAsync();
                await LogDebugAsync("InitializeCalendarApp - GlobalPresentationModel complete");
            }
            catch (Exception ex)
            {
                await LogDebugAsync($"InitializeCalendarApp - GlobalPresentationModel warning: {ex.Message}");
            }

            // Step 6: Call getParametreIntranet
            // HAR shows this is called after GlobalPresentationModel, before translations
            try
            {
                await LogDebugAsync("InitializeCalendarApp - calling getParametreIntranet");
                await CalendarGetParametreIntranetAsync();
                await LogDebugAsync("InitializeCalendarApp - getParametreIntranet complete");
            }
            catch (Exception ex)
            {
                await LogDebugAsync($"InitializeCalendarApp - getParametreIntranet warning: {ex.Message}");
            }

            // Step 7: Load calendar-specific translations (required for absence API)
            // HAR capture shows browser loads "calendrier.annuel.intranet_" translations before absence request
            try
            {
                await LogDebugAsync("InitializeCalendarApp - loading calendar translations");
                await LoadCalendarTranslationsAsync();
                await LogDebugAsync("InitializeCalendarApp - calendar translations loaded");
            }
            catch (Exception ex)
            {
                await LogDebugAsync($"InitializeCalendarApp - calendar translations warning: {ex.Message}");
            }

            // Step 8: Call getPresentationModel for CalendrierAbsencePresentationModel
            // CRITICAL: This MUST be called before getAbsencesEtJoursFeries! (discovered from HAR analysis)
            // This initializes the server-side state for the calendar module.
            try
            {
                await LogDebugAsync("InitializeCalendarApp - calling getPresentationModel for CalendrierAbsencePresentationModel");
                await CalendarGetPresentationModelAsync();
                await LogDebugAsync("InitializeCalendarApp - CalendrierAbsencePresentationModel complete");
            }
            catch (Exception ex)
            {
                await LogDebugAsync($"InitializeCalendarApp - CalendrierAbsencePresentationModel warning: {ex.Message}");
            }

            _calendarAppInitialized = true;
            await LogDebugAsync("InitializeCalendarApp - complete");
        }
        catch (Exception ex)
        {
            await LogDebugAsync($"InitializeCalendarApp - warning: {ex.Message}");
            _calendarAppInitialized = true;
        }
    }

    /// <summary>
    /// Load translations that the browser loads before calling getSemaine.
    /// This may be required to initialize server-side state.
    /// Uses the dynamic employee ID extracted from GlobalBWTService connect.
    /// </summary>
    private async Task LoadTranslationsAsync()
    {
        // Translation prefixes that browser requests before getSemaine
        var prefixes = new[]
        {
            "global_",
            "app.portail.declaration_",
            "app.portail.declaration.presence_"
        };

        foreach (var prefix in prefixes)
        {
            try
            {
                await LogDebugAsync($"LoadTranslations - loading {prefix} with employeeId={_employeeId}");
                var gwtRequest = _requestBuilder.BuildGetTraductionsRequest(_sessionId!, prefix, _employeeId);
                var response = await SendGwtRequestInternalAsync(gwtRequest);
                var hasException = response.Contains("ExceptionBWT");
                await LogDebugAsync($"LoadTranslations - {prefix} response: exception={hasException}, length={response.Length}");
            }
            catch (Exception ex)
            {
                await LogDebugAsync($"LoadTranslations - {prefix} warning: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Load calendar-specific translations required for absence API.
    /// Must be called after CalendarGlobalConnectAsync and uses calendar JSP referer.
    /// HAR capture shows browser loads "calendrier.annuel.intranet_" before absence requests.
    /// </summary>
    private async Task LoadCalendarTranslationsAsync()
    {
        // Calendar-specific translation prefixes from HAR capture
        var prefixes = new[]
        {
            "global_",
            "calendrier.annuel.intranet_"
        };

        foreach (var prefix in prefixes)
        {
            try
            {
                await LogDebugAsync($"LoadCalendarTranslations - loading {prefix} with employeeId={_employeeId}");
                var gwtRequest = _requestBuilder.BuildGetTraductionsRequest(_sessionId!, prefix, _employeeId);
                // Use calendar JSP referer for these requests
                var response = await SendGwtRequestInternalAsync(gwtRequest, "/open/bwt/intranet_calendrier_absence.jsp");
                var hasException = response.Contains("ExceptionBWT");
                await LogDebugAsync($"LoadCalendarTranslations - {prefix} response: exception={hasException}, length={response.Length}");
            }
            catch (Exception ex)
            {
                await LogDebugAsync($"LoadCalendarTranslations - {prefix} warning: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Call GlobalBWTService.getPresentationModel for GlobalPresentationModel.
    /// This is called before CalendrierAbsencePresentationModel.
    /// </summary>
    private async Task CalendarGetGlobalPresentationModelAsync()
    {
        if (string.IsNullOrEmpty(_sessionId))
            return;

        var timestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var gwtRequest = _requestBuilder.BuildGetGlobalPresentationModelRequest(_sessionId, _employeeId);

        var url = $"{_baseUrl}/open/bwpDispatchServlet?{timestampMs}";
        var cookies = GetCookiesString();

        var headers = new Dictionary<string, string>
        {
            ["Content-Type"] = "text/bwp;charset=UTF-8",
            ["X-Requested-With"] = "XMLHttpRequest",
            ["Cache-Control"] = "no-cache",
            ["Origin"] = _baseUrl,
            ["Referer"] = $"{_baseUrl}/open/bwt/intranet_calendrier_absence.jsp",
            ["If-Modified-Since"] = "Thu, 01 Jan 1970 00:00:00 GMT",
            ["x-kelio-stat"] = $"cst={timestampMs}",
            ["User-Agent"] = BrowserUserAgent,
            ["Sec-Fetch-Dest"] = "empty",
            ["Sec-Fetch-Mode"] = "cors",
            ["Sec-Fetch-Site"] = "same-origin",
            ["sec-ch-ua"] = "\"Chromium\";v=\"131\", \"Not_A Brand\";v=\"24\"",
            ["sec-ch-ua-mobile"] = "?0",
            ["sec-ch-ua-platform"] = "\"Windows\""
        };

        await LogDebugAsync($"[curl_cffi] Calendar GlobalPresentationModel request: {gwtRequest}");

        var bodyToSend = _bwpCodec.Encode(gwtRequest);
        var response = await _curlClient.PostWithBodyFileAsync(url, bodyToSend, headers, cookies);

        await LogDebugAsync($"[curl_cffi] GlobalPresentationModel response status: {response.StatusCode}");

        if (!response.IsSuccessStatusCode)
        {
            await LogDebugAsync($"[curl_cffi] GlobalPresentationModel response body: {response.Body[..Math.Min(300, response.Body.Length)]}");
            throw new HttpRequestException($"GlobalPresentationModel failed with status {response.StatusCode}");
        }
    }

    /// <summary>
    /// Call LiensBWTService.getParametreIntranet.
    /// This is called after GlobalPresentationModel and before calendar translations.
    /// </summary>
    private async Task CalendarGetParametreIntranetAsync()
    {
        if (string.IsNullOrEmpty(_sessionId))
            return;

        var timestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var gwtRequest = _requestBuilder.BuildGetParametreIntranetRequest(_sessionId, _employeeId);

        var url = $"{_baseUrl}/open/bwpDispatchServlet?{timestampMs}";
        var cookies = GetCookiesString();

        var headers = new Dictionary<string, string>
        {
            ["Content-Type"] = "text/bwp;charset=UTF-8",
            ["X-Requested-With"] = "XMLHttpRequest",
            ["Cache-Control"] = "no-cache",
            ["Origin"] = _baseUrl,
            ["Referer"] = $"{_baseUrl}/open/bwt/intranet_calendrier_absence.jsp",
            ["If-Modified-Since"] = "Thu, 01 Jan 1970 00:00:00 GMT",
            ["x-kelio-stat"] = $"cst={timestampMs}",
            ["User-Agent"] = BrowserUserAgent,
            ["Sec-Fetch-Dest"] = "empty",
            ["Sec-Fetch-Mode"] = "cors",
            ["Sec-Fetch-Site"] = "same-origin",
            ["sec-ch-ua"] = "\"Chromium\";v=\"131\", \"Not_A Brand\";v=\"24\"",
            ["sec-ch-ua-mobile"] = "?0",
            ["sec-ch-ua-platform"] = "\"Windows\""
        };

        await LogDebugAsync($"[curl_cffi] Calendar getParametreIntranet request: {gwtRequest}");

        var bodyToSend = _bwpCodec.Encode(gwtRequest);
        var response = await _curlClient.PostWithBodyFileAsync(url, bodyToSend, headers, cookies);

        await LogDebugAsync($"[curl_cffi] getParametreIntranet response status: {response.StatusCode}");

        if (!response.IsSuccessStatusCode)
        {
            await LogDebugAsync($"[curl_cffi] getParametreIntranet response body: {response.Body[..Math.Min(300, response.Body.Length)]}");
            throw new HttpRequestException($"getParametreIntranet failed with status {response.StatusCode}");
        }

        // Decode and extract the REAL employee ID from the response
        // Response format: ...,"Goltz Christopher",0,1,2,3,0,3,3,4,1,3,0,5,6,3,0,3,52
        // The employee ID (52) appears at the very end
        var responseBody = response.Body;
        if (_bwpCodec.IsBwp(responseBody))
        {
            var decoded = _bwpCodec.Decode(responseBody);
            responseBody = decoded.Decoded;
        }

        await LogDebugAsync($"[curl_cffi] getParametreIntranet decoded response: {responseBody}");

        // Extract employee ID from the end of the response
        // The pattern is: ,3,{employeeId} at the end (where 3 is the Integer type index)
        var parts = responseBody.Split(',');
        if (parts.Length >= 2)
        {
            // The last numeric value is the employee ID
            for (int i = parts.Length - 1; i >= 0; i--)
            {
                if (int.TryParse(parts[i], out var potentialId) && potentialId > 0 && potentialId < 100000)
                {
                    // Verify it's preceded by a type reference (3 for Integer in this context)
                    if (i > 0 && parts[i - 1] == "3")
                    {
                        _realEmployeeId = potentialId;
                        await LogDebugAsync($"[curl_cffi] Extracted REAL employee ID from getParametreIntranet: {_realEmployeeId}");
                        break;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Call GlobalBWTService.getPresentationModel for CalendrierAbsencePresentationModel.
    /// CRITICAL: This MUST be called before getAbsencesEtJoursFeries or we get 401!
    /// HAR shows this uses the employee ID as the context parameter.
    /// </summary>
    private async Task CalendarGetPresentationModelAsync()
    {
        if (string.IsNullOrEmpty(_sessionId))
            return;

        var timestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var gwtRequest = _requestBuilder.BuildGetPresentationModelRequest(_sessionId, _employeeId);

        var url = $"{_baseUrl}/open/bwpDispatchServlet?{timestampMs}";
        var cookies = GetCookiesString();

        var headers = new Dictionary<string, string>
        {
            ["Content-Type"] = "text/bwp;charset=UTF-8",
            ["X-Requested-With"] = "XMLHttpRequest",
            ["Cache-Control"] = "no-cache",
            ["Origin"] = _baseUrl,
            ["Referer"] = $"{_baseUrl}/open/bwt/intranet_calendrier_absence.jsp",
            ["If-Modified-Since"] = "Thu, 01 Jan 1970 00:00:00 GMT",
            ["x-kelio-stat"] = $"cst={timestampMs}",
            ["User-Agent"] = BrowserUserAgent,
            ["Sec-Fetch-Dest"] = "empty",
            ["Sec-Fetch-Mode"] = "cors",
            ["Sec-Fetch-Site"] = "same-origin",
            ["sec-ch-ua"] = "\"Chromium\";v=\"131\", \"Not_A Brand\";v=\"24\"",
            ["sec-ch-ua-mobile"] = "?0",
            ["sec-ch-ua-platform"] = "\"Windows\""
        };

        await LogDebugAsync($"[curl_cffi] Calendar getPresentationModel to {url}");
        await LogDebugAsync($"[curl_cffi] getPresentationModel request: {gwtRequest}");

        // BWP-encode the request and use body file to avoid character corruption
        var bodyToSend = _bwpCodec.Encode(gwtRequest);
        var response = await _curlClient.PostWithBodyFileAsync(url, bodyToSend, headers, cookies);

        await LogDebugAsync($"[curl_cffi] getPresentationModel response status: {response.StatusCode}");
        await LogDebugAsync($"[curl_cffi] getPresentationModel response (first 300): {response.Body[..Math.Min(300, response.Body.Length)]}");

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"getPresentationModel failed with status {response.StatusCode}");
        }

        // Decode the response to check for errors
        var responseBody = response.Body;
        if (_bwpCodec.IsBwp(responseBody))
        {
            var decoded = _bwpCodec.Decode(responseBody);
            responseBody = decoded.Decoded;
        }

        if (responseBody.Contains("ExceptionBWT"))
        {
            await LogDebugAsync("[curl_cffi] getPresentationModel: Server returned ExceptionBWT!");
        }
    }

    /// <summary>
    /// Get week presence data for a specific date.
    /// Uses the dynamic employee ID extracted from GlobalBWTService connect.
    /// </summary>
    /// <param name="date">Any date within the desired week</param>
    public async Task<WeekPresenceDto?> GetWeekPresenceAsync(DateOnly date)
    {
        if (!_isAuthenticated || string.IsNullOrEmpty(_sessionId))
        {
            throw new InvalidOperationException("Not authenticated. Call LoginAsync first.");
        }

        var kelioDate = GwtRpcRequestBuilder.ToKelioDate(date);

        // Use dynamic employee ID from GlobalBWTService connect
        if (_employeeId <= 0)
        {
            await LogDebugAsync("GetWeekPresence: Employee ID not set - GlobalBWTService connect may have failed");
            throw new InvalidOperationException(
                "Employee ID not available. The GlobalBWTService connect call may have failed during login.");
        }

        var gwtRequest = _requestBuilder.BuildGetSemaineRequest(_sessionId, kelioDate, _employeeId);

        await LogDebugAsync($"GetSemaine GWT request with employeeId={_employeeId}: {gwtRequest}");

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

    private static async Task LogDebugAsync(string message)
    {
        await LoggingSemaphore.WaitAsync();
        try
        {
            var logPath = LoggingDelegatingHandler.GetLogFilePath();
            await File.AppendAllTextAsync(logPath, $"\n[DEBUG] {DateTime.Now:HH:mm:ss} {message}\n");
        }
        finally
        {
            LoggingSemaphore.Release();
        }
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
        if (!_isAuthenticated || string.IsNullOrEmpty(_sessionId))
        {
            throw new InvalidOperationException("Not authenticated. Call LoginAsync first.");
        }

        if (_employeeId <= 0)
        {
            await LogDebugAsync("GetAbsences: Employee ID not set - GlobalBWTService connect may have failed");
            throw new InvalidOperationException(
                "Employee ID not available. The GlobalBWTService connect call may have failed during login.");
        }

        // Initialize the calendar app if not already done
        if (!_calendarAppInitialized)
        {
            await InitializeCalendarAppAsync();
        }

        var kelioStartDate = GwtRpcRequestBuilder.ToKelioDate(startDate);
        var kelioEndDate = GwtRpcRequestBuilder.ToKelioDate(endDate);

        // Use the REAL employee ID (from getParametreIntranet) for the employee parameter
        // Use the session context ID for the context parameter
        // HAR shows: employeeId=52 (real), contextId=1372 (session counter)
        var realEmpId = _realEmployeeId > 0 ? _realEmployeeId : _employeeId;
        var contextId = _calendarContextId > 0 ? _calendarContextId : _employeeId;

        var gwtRequest = _requestBuilder.BuildGetAbsencesRequest(
            _sessionId, realEmpId, kelioStartDate, kelioEndDate, contextId);

        await LogDebugAsync($"GetAbsences GWT request: realEmployeeId={realEmpId}, contextId={contextId}, start={kelioStartDate}, end={kelioEndDate}");

        // Use custom referer for calendar app requests (required for server authorization)
        var response = await SendGwtRequestAsync(gwtRequest, "/open/bwt/intranet_calendrier_absence.jsp");

        await LogDebugAsync($"GetAbsences response (first 500 chars): {response[..Math.Min(500, response.Length)]}");

        // Check for ExceptionBWT
        if (response.Contains("ExceptionBWT"))
        {
            await LogDebugAsync("GetAbsences: Server returned ExceptionBWT!");
            return null;
        }

        var result = _absenceParser.Parse(response, _employeeId, startDate, endDate);

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
        if (!_isAuthenticated || string.IsNullOrEmpty(_sessionId))
        {
            throw new InvalidOperationException("Not authenticated. Call LoginAsync first.");
        }

        var gwtRequest = _requestBuilder.BuildGetServerTimeRequest(_sessionId);
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
        if (!_isAuthenticated || string.IsNullOrEmpty(_sessionId))
        {
            throw new InvalidOperationException("Not authenticated. Call LoginAsync first.");
        }

        // Use dynamic employee ID from GlobalBWTService connect
        if (_employeeId <= 0)
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
        await LogDebugAsync($"Punch: SessionId={_sessionId}, EmployeeId={_employeeId}");

        var gwtRequest = _requestBuilder.BuildBadgerSignalerRequest(_sessionId, _employeeId);

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
    /// <param name="customReferer">Optional custom referer path (e.g., "/open/bwt/intranet_calendrier_absence.jsp")</param>
    public async Task<string> SendGwtRequestAsync(string gwtRequest, string? customReferer = null)
    {
        if (!_isAuthenticated)
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
    /// <param name="customReferer">Optional custom referer path (e.g., "/open/bwt/intranet_calendrier_absence.jsp")</param>
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
        var referer = customReferer != null
            ? $"{_baseUrl}{customReferer}"
            : $"{_baseUrl}/open/bwt/portail.jsp";

        // Build headers like the browser sends (matching api_discovery.json captures)
        var headers = new Dictionary<string, string>
        {
            ["Content-Type"] = "text/bwp;charset=UTF-8",
            ["X-Requested-With"] = "XMLHttpRequest",
            ["Cache-Control"] = "no-cache",
            ["Origin"] = _baseUrl,
            ["Referer"] = referer,
            ["If-Modified-Since"] = "Thu, 01 Jan 1970 00:00:00 GMT",
            ["x-kelio-stat"] = $"cst={timestamp}",
            ["User-Agent"] = BrowserUserAgent,
            // CORS/Fetch headers from browser
            ["Sec-Fetch-Dest"] = "empty",
            ["Sec-Fetch-Mode"] = "cors",
            ["Sec-Fetch-Site"] = "same-origin",
            // Chrome client hints (sec-ch-ua headers from browser)
            ["sec-ch-ua"] = "\"Chromium\";v=\"131\", \"Not_A Brand\";v=\"24\"",
            ["sec-ch-ua-mobile"] = "?0",
            ["sec-ch-ua-platform"] = "\"Windows\""
        };

        await LogDebugAsync($"[curl_cffi] Sending request to {url}");
        await LogDebugAsync($"[curl_cffi] Cookies: {cookies}");
        await LogDebugAsync($"[curl_cffi] Referer: {referer}");
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
    {
        return _sessionCookie ?? "";
    }

    private static string? ExtractCsrfToken(string html)
    {
        var match = CsrfTokenRegex().Match(html);
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>
    /// Extract JSESSIONID from Set-Cookie header.
    /// </summary>
    private static string? ExtractSessionCookie(Dictionary<string, string> headers)
    {
        // Try different header name cases
        string? setCookie = null;
        foreach (var key in headers.Keys)
        {
            if (key.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("set-cookie", StringComparison.OrdinalIgnoreCase))
            {
                setCookie = headers[key];
                break;
            }
        }

        if (string.IsNullOrEmpty(setCookie))
            return null;

        // Parse: JSESSIONID=xxx; Path=/open; ...
        var match = SessionCookieRegex().Match(setCookie);
        return match.Success ? $"JSESSIONID={match.Groups[1].Value}" : null;
    }

    [GeneratedRegex(@"name=""_csrf_bodet""\s+value=""([^""]+)""", RegexOptions.Compiled)]
    private static partial Regex CsrfTokenRegex();

    [GeneratedRegex(@"<div\s+id=""csrf_token""[^>]*>([^<]+)</div>", RegexOptions.Compiled)]
    private static partial Regex CsrfTokenDivRegex();

    [GeneratedRegex(@"JSESSIONID=([^;]+)", RegexOptions.Compiled)]
    private static partial Regex SessionCookieRegex();

    public void Dispose()
    {
        _httpClient.Dispose();
        _bwpClient.Dispose();
        _curlClient.Dispose();
        GC.SuppressFinalize(this);
    }
}
