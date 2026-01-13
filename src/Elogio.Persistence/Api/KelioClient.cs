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
    private readonly IKelioAuthApi _authApi;
    private readonly IKelioApi _kelioApi;
    private readonly CookieContainer _cookieContainer;
    private readonly GwtRpcRequestBuilder _requestBuilder = new();
    private readonly SemainePresenceParser _presenceParser = new();
    private readonly BwpCodec _bwpCodec = new();
    private readonly string _baseUrl;

    private string? _csrfToken;
    private string? _sessionId;
    private string? _bwpCsrfToken; // CSRF token from BWP connect response
    private string? _sessionCookie; // Manual cookie management for TLS consistency
    private int _employeeId; // Dynamic employee ID from GlobalBWTService connect
    private bool _isAuthenticated;

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
        _cookieContainer = new CookieContainer();

        // Clear log file for fresh start
        LoggingDelegatingHandler.ClearLog();

        // Auth handler - don't follow redirects to detect 302
        var authHandler = new HttpClientHandler
        {
            CookieContainer = _cookieContainer,
            AllowAutoRedirect = false,
            UseCookies = true
        };

        // Add logging handler in the chain
        var loggingHandler = new LoggingDelegatingHandler
        {
            InnerHandler = authHandler,
            CookieContainer = _cookieContainer
        };

        _httpClient = new HttpClient(loggingHandler)
        {
            BaseAddress = new Uri(baseUrl)
        };
        _httpClient.DefaultRequestHeaders.Add("User-Agent", BrowserUserAgent);
        _httpClient.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
        _httpClient.DefaultRequestHeaders.Add("Accept-Language", "de-DE,de;q=0.9,en;q=0.8");

        // Auth API (no BWP encoding)
        _authApi = RestService.For<IKelioAuthApi>(_httpClient);

        // BWP API (with encoding handler) - needs separate handler chain (fallback if curl not available)
        var bwpInnerHandler = new HttpClientHandler
        {
            CookieContainer = _cookieContainer,
            AllowAutoRedirect = true,
            UseCookies = true,
            AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
        };
        var bwpLoggingHandler = new LoggingDelegatingHandler
        {
            InnerHandler = bwpInnerHandler,
            CookieContainer = _cookieContainer
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
    /// Extract the dynamic employee ID from GlobalBWTService connect response.
    /// The employee ID appears near the end of the response, right before the user's name.
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

            // Extract strings to find user name indices
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

            // Find indices for user name parts (Christopher, Goltz)
            int firstnameIdx = -1, lastnameIdx = -1;
            for (var i = 0; i < strings.Count; i++)
            {
                if (strings[i] == "Christopher")
                    firstnameIdx = i;
                else if (strings[i] == "Goltz")
                    lastnameIdx = i;
            }

            if (firstnameIdx < 0 || lastnameIdx < 0)
            {
                // Try to find any name by looking for patterns in the response
                // Fall back to default if we can't find the name
                return 0;
            }

            // Get data tokens (after all strings)
            var dataTokens = parts[idx..].Select(p => p.Trim()).ToList();

            // Find the employee ID by looking for the firstname reference
            // Pattern: ..., TYPE_REF, EMPLOYEE_ID, SMALL_TYPE_REF, FIRSTNAME_IDX, ...
            for (var i = 0; i < dataTokens.Count; i++)
            {
                if (dataTokens[i] == firstnameIdx.ToString())
                {
                    // Found firstname reference at position i
                    // Look backwards: i-1 should be type ref (4), i-2 should be employee ID
                    if (i >= 2)
                    {
                        var typeRef = dataTokens[i - 1];
                        var employeeIdCandidate = dataTokens[i - 2];

                        // Validate: type_ref should be small (like 4), employee_id should be 3-4 digits
                        if (int.TryParse(typeRef, out var typeRefInt) && typeRefInt < 20 &&
                            int.TryParse(employeeIdCandidate, out var employeeId) &&
                            employeeId >= 100 && employeeId <= 9999)
                        {
                            return employeeId;
                        }
                    }
                    break;
                }
            }

            return 0;
        }
        catch (Exception ex)
        {
            // Log but don't fail - we can try with default ID
            _ = LogDebugAsync($"ExtractEmployeeIdFromConnectResponse error: {ex.Message}");
            return 0;
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

        // Use dynamic employee ID from GlobalBWTService connect (or fallback to 227)
        var effectiveEmployeeId = _employeeId > 0 ? _employeeId : 227;
        var gwtRequest = _requestBuilder.BuildGetSemaineRequest(_sessionId, kelioDate, effectiveEmployeeId);

        await LogDebugAsync($"GetSemaine GWT request with employeeId={effectiveEmployeeId}: {gwtRequest}");

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
    /// Send a raw GWT-RPC request and get the decoded response.
    /// </summary>
    public async Task<string> SendGwtRequestAsync(string gwtRequest)
    {
        if (!_isAuthenticated)
        {
            throw new InvalidOperationException("Not authenticated. Call LoginAsync first.");
        }

        return await SendGwtRequestInternalAsync(gwtRequest);
    }

    /// <summary>
    /// Internal method to send GWT-RPC request without auth check.
    /// Uses curl_cffi via Python to bypass TLS fingerprint detection.
    /// CRITICAL: BWP-encoded requests use body file to avoid character corruption.
    /// </summary>
    private async Task<string> SendGwtRequestInternalAsync(string gwtRequest)
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

        // Build headers like the browser sends (matching api_discovery.json captures)
        var headers = new Dictionary<string, string>
        {
            ["Content-Type"] = "text/bwp;charset=UTF-8",
            ["X-Requested-With"] = "XMLHttpRequest",
            ["Cache-Control"] = "no-cache",
            ["Referer"] = $"{_baseUrl}/open/bwt/portail.jsp",
            ["If-Modified-Since"] = "Thu, 01 Jan 1970 00:00:00 GMT",
            ["x-kelio-stat"] = $"cst={timestamp}",
            ["User-Agent"] = BrowserUserAgent,
            // Chrome client hints (sec-ch-ua headers from browser)
            ["sec-ch-ua"] = "\"Chromium\";v=\"131\", \"Not_A Brand\";v=\"24\"",
            ["sec-ch-ua-mobile"] = "?0",
            ["sec-ch-ua-platform"] = "\"Windows\""
        };

        await LogDebugAsync($"[curl_cffi] Sending request to {url}");
        await LogDebugAsync($"[curl_cffi] Cookies: {cookies}");
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
