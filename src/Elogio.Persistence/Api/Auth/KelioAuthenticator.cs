using System.Diagnostics;
using System.Text.RegularExpressions;
using Elogio.Persistence.Api.Http;
using Elogio.Persistence.Api.Parsing;
using Elogio.Persistence.Api.Session;
using Elogio.Persistence.Protocol;
using Elogio.Persistence.Services;
using Serilog;

namespace Elogio.Persistence.Api.Auth;

/// <summary>
/// Handles Kelio authentication flow including login, session establishment, and BWP initialization.
/// Extracted from KelioClient to separate authentication concerns.
/// </summary>
public partial class KelioAuthenticator
{
    private const string BrowserUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";

    private readonly CurlImpersonateClient _curlClient;
    private readonly GwtRpcRequestBuilder _requestBuilder;
    private readonly string _baseUrl;

    public KelioAuthenticator(
        CurlImpersonateClient curlClient,
        GwtRpcRequestBuilder requestBuilder,
        string baseUrl)
    {
        _curlClient = curlClient;
        _requestBuilder = requestBuilder;
        _baseUrl = baseUrl;
    }

    /// <summary>
    /// Pre-initialize the curl_proxy server AND pre-fetch login page for faster login.
    /// Call this early (e.g., when login page is shown) to avoid delays during actual login.
    /// </summary>
    public async Task PreInitializeAsync(SessionContext session)
    {
        var totalSw = Stopwatch.StartNew();

        // Start the server
        var serverSw = Stopwatch.StartNew();
        await _curlClient.InitializeAsync(enableServerMode: true);
        var serverMs = serverSw.ElapsedMilliseconds;

        // Pre-fetch the login page to get CSRF token (runs in parallel with nothing, but warms connection)
        var loginPageSw = Stopwatch.StartNew();
        try
        {
            var loginPageResponse = await _curlClient.GetAsync($"{_baseUrl}/open/login");
            if (loginPageResponse.IsSuccessStatusCode || loginPageResponse.StatusCode == 401)
            {
                session.SessionCookie = ExtractSessionCookie(loginPageResponse.Headers);
                session.CsrfToken = ExtractCsrfToken(loginPageResponse.Body);
                Log.Information("[PERF] PreInitialize: Pre-fetched login page, got CSRF token");
            }
        }
        catch (Exception ex)
        {
            // Non-critical - login will fetch it again if needed
            Log.Warning(ex, "[PERF] PreInitialize: Failed to pre-fetch login page");
        }
        var loginPageMs = loginPageSw.ElapsedMilliseconds;

        Log.Information("[PERF] PreInitialize: Server={ServerMs}ms, LoginPage={LoginPageMs}ms, Total={TotalMs}ms (ServerMode={ServerMode})",
            serverMs, loginPageMs, totalSw.ElapsedMilliseconds, _curlClient.IsServerModeEnabled);
    }

    /// <summary>
    /// Authenticate with Kelio server and establish BWP session.
    /// </summary>
    /// <param name="session">Session context to store authentication state</param>
    /// <param name="username">Kelio username</param>
    /// <param name="password">Kelio password</param>
    /// <returns>True if authentication successful</returns>
    public async Task<bool> LoginAsync(SessionContext session, string username, string password)
    {
        var totalSw = Stopwatch.StartNew();
        var sw = new Stopwatch();
        try
        {
            // Initialize curl_proxy server mode if not already done
            if (!_curlClient.IsServerModeEnabled)
            {
                var initSw = Stopwatch.StartNew();
                await _curlClient.InitializeAsync(enableServerMode: true);
                Log.Information("[PERF] LoginAsync: CurlClient.InitializeAsync took {ElapsedMs}ms (ServerMode={ServerMode})",
                    initSw.ElapsedMilliseconds, _curlClient.IsServerModeEnabled);
            }

            // 1. Get login page for CSRF token (skip if already pre-fetched)
            if (string.IsNullOrEmpty(session.CsrfToken))
            {
                sw.Restart();
                var loginPageResponse = await _curlClient.GetAsync($"{_baseUrl}/open/login");
                Log.Information("[PERF] LoginAsync: GetLoginPage took {ElapsedMs}ms", sw.ElapsedMilliseconds);

                if (!loginPageResponse.IsSuccessStatusCode && loginPageResponse.StatusCode != 401)
                {
                    throw new HttpRequestException($"Failed to get login page: {loginPageResponse.StatusCode}");
                }

                session.SessionCookie = ExtractSessionCookie(loginPageResponse.Headers);
                session.CsrfToken = ExtractCsrfToken(loginPageResponse.Body);
            }
            else
            {
                Log.Information("[PERF] LoginAsync: Using pre-fetched CSRF token (skipped GetLoginPage)");
            }

            if (string.IsNullOrEmpty(session.CsrfToken))
            {
                throw new InvalidOperationException("Could not extract CSRF token from login page");
            }

            // 2. Submit login
            var loginBody = $"ACTION=ACTION_VALIDER_LOGIN&username={Uri.EscapeDataString(username)}&password={Uri.EscapeDataString(password)}&_csrf_bodet={Uri.EscapeDataString(session.CsrfToken)}";
            var loginHeaders = new Dictionary<string, string>
            {
                ["Content-Type"] = "application/x-www-form-urlencoded",
                ["Referer"] = $"{_baseUrl}/open/login",
                ["Origin"] = _baseUrl,
                ["User-Agent"] = BrowserUserAgent
            };

            sw.Restart();
            var loginResponse = await _curlClient.PostAsync(
                $"{_baseUrl}/open/j_spring_security_check",
                loginBody, loginHeaders, session.SessionCookie);
            Log.Information("[PERF] LoginAsync: PostLogin took {ElapsedMs}ms", sw.ElapsedMilliseconds);

            // Update cookie from response
            var newCookie = ExtractSessionCookie(loginResponse.Headers);
            if (!string.IsNullOrEmpty(newCookie))
            {
                session.SessionCookie = newCookie;
            }

            // Check if login was successful (302 with Location containing "homepage")
            var locationHeader = loginResponse.Headers.GetValueOrDefault("Location") ??
                                 loginResponse.Headers.GetValueOrDefault("location") ?? "";
            session.IsAuthenticated = loginResponse.StatusCode == 302 &&
                                      locationHeader.Contains("homepage");

            if (session.IsAuthenticated)
            {
                // Start GWT file downloads in parallel with GetSessionIdFromPortal
                sw.Restart();
                var gwtPreloadTask = PreloadGwtFilesAsync(session);

                // Get the session ID from portail.jsp
                var sessionIdTask = GetSessionIdFromPortalOnlyAsync(session);
                session.SessionId = await sessionIdTask;
                Log.Information("[PERF] LoginAsync: GetSessionIdFromPortal took {ElapsedMs}ms", sw.ElapsedMilliseconds);

                if (string.IsNullOrEmpty(session.SessionId))
                {
                    throw new InvalidOperationException("Could not extract session ID from portal page");
                }

                // Wait for GWT preload to finish
                await gwtPreloadTask;

                // Run BWP session initialization
                sw.Restart();
                await InitializeBwpSessionAsync(session);
                Log.Information("[PERF] LoginAsync: BwpSessionInit took {ElapsedMs}ms", sw.ElapsedMilliseconds);
            }

            totalSw.Stop();
            Log.Information("[PERF] LoginAsync: TOTAL LOGIN TIME {ElapsedMs}ms", totalSw.ElapsedMilliseconds);
            return session.IsAuthenticated;
        }
        catch (Exception)
        {
            totalSw.Stop();
            Log.Warning("[PERF] LoginAsync: FAILED after {ElapsedMs}ms", totalSw.ElapsedMilliseconds);
            session.IsAuthenticated = false;
            throw;
        }
    }

    /// <summary>
    /// Get session ID from portal page WITHOUT loading GWT files.
    /// </summary>
    private async Task<string?> GetSessionIdFromPortalOnlyAsync(SessionContext session)
    {
        var portalResponse = await _curlClient.GetAsync(
            $"{_baseUrl}{GwtEndpoints.PortalJsp}",
            cookies: session.SessionCookie);

        var newCookie = ExtractSessionCookie(portalResponse.Headers);
        if (!string.IsNullOrEmpty(newCookie))
        {
            session.SessionCookie = newCookie;
        }

        var match = CsrfTokenDivRegex().Match(portalResponse.Body);
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>
    /// Preload GWT files in parallel. Only needs auth cookie, not BWP session ID.
    /// </summary>
    private async Task PreloadGwtFilesAsync(SessionContext session)
    {
        var sw = Stopwatch.StartNew();

        var tasks = new List<Task>
        {
            _curlClient.GetAsync($"{_baseUrl}{GwtEndpoints.PortalNoCacheJs}", cookies: session.SessionCookie),
            _curlClient.GetAsync($"{_baseUrl}{GwtEndpoints.PortalCacheJs}", cookies: session.SessionCookie),
            _curlClient.GetAsync($"{_baseUrl}{GwtEndpoints.AppLauncherDeclaration}", cookies: session.SessionCookie),
            _curlClient.GetAsync($"{_baseUrl}{GwtEndpoints.DeclarationNoCacheJs}", cookies: session.SessionCookie),
            _curlClient.GetAsync($"{_baseUrl}{GwtEndpoints.DeclarationCacheJs}", cookies: session.SessionCookie)
        };

        try
        {
            await Task.WhenAll(tasks);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[PERF] PreloadGwtFiles: some files failed to preload");
        }

        Log.Information("[PERF] PreloadGwtFiles: completed in {ElapsedMs}ms", sw.ElapsedMilliseconds);
    }

    /// <summary>
    /// Initialize BWP session - requires session ID from portal page.
    /// </summary>
    private async Task InitializeBwpSessionAsync(SessionContext session)
    {
        var sw = Stopwatch.StartNew();

        // Start calendar prefetch in background (fire-and-forget)
        BackgroundTaskManager.Instance.StartCalendarPrefetch(() => PrefetchCalendarNavigationAsync(session));

        // Run BWP connect, push connect, and global connect in parallel
        var tasks = new List<Task>
        {
            ConnectBwpSessionAsync(session),
            ConnectPushAsync(session),
            GlobalBwtServiceConnectAsync(session)
        };

        try
        {
            await Task.WhenAll(tasks);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[PERF] InitializeBwpSession: some tasks failed");
        }

        Log.Information("[PERF] InitializeBwpSession: completed in {ElapsedMs}ms, EmployeeId={EmployeeId}",
            sw.ElapsedMilliseconds, session.EmployeeId);
    }

    /// <summary>
    /// Prefetch calendar navigation (Phase1 of calendar init).
    /// Running this during login saves ~4s when user opens calendar view.
    /// </summary>
    public async Task PrefetchCalendarNavigationAsync(SessionContext session)
    {
        if (session.CalendarNavigationPrefetched)
            return;

        var sw = Stopwatch.StartNew();

        try
        {
            // Navigate to intranet section
            var intranetResponse = await _curlClient.GetAsync(
                $"{_baseUrl}/open/homepage?ACTION=intranet&asked=6&header=0",
                cookies: session.SessionCookie);

            var newCookie = ExtractSessionCookie(intranetResponse.Headers);
            if (!string.IsNullOrEmpty(newCookie))
                session.SessionCookie = newCookie;

            // Load calendar JSP
            var jspResponse = await _curlClient.GetAsync(
                $"{_baseUrl}{GwtEndpoints.CalendarAbsenceJsp}",
                cookies: session.SessionCookie);

            newCookie = ExtractSessionCookie(jspResponse.Headers);
            if (!string.IsNullOrEmpty(newCookie))
                session.SessionCookie = newCookie;

            session.CalendarNavigationPrefetched = true;
            Log.Information("[PERF] PrefetchCalendarNavigation: completed in {ElapsedMs}ms", sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[PERF] PrefetchCalendarNavigation: failed after {ElapsedMs}ms", sw.ElapsedMilliseconds);
        }
    }

    /// <summary>
    /// Establish the BWP session (PortailBWTService.connect).
    /// </summary>
    private async Task ConnectBwpSessionAsync(SessionContext session)
    {
        if (string.IsNullOrEmpty(session.SessionId))
        {
            throw new InvalidOperationException("Session ID not set.");
        }

        var timestampSec = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var gwtRequest = _requestBuilder.BuildConnectRequest(session.SessionId, timestampSec);

        var timestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var url = $"{_baseUrl}/open/bwpDispatchServlet?{timestampMs}";
        var cookies = session.GetCookiesString();

        var headers = BwpHeaders.CreatePortalHeaders(_baseUrl, GwtEndpoints.PortalJsp, timestampMs, BrowserUserAgent);

        // Connect is sent RAW (not BWP-encoded)
        var response = await _curlClient.PostAsync(url, gwtRequest, headers, cookies);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Connect failed with status {response.StatusCode}");
        }

        // Extract x-csrf-token from response headers
        foreach (var key in response.Headers.Keys)
        {
            if (key.Equals("x-csrf-token", StringComparison.OrdinalIgnoreCase))
            {
                session.BwpCsrfToken = response.Headers[key];
                break;
            }
        }
    }

    /// <summary>
    /// Connect to push notification service.
    /// </summary>
    private async Task ConnectPushAsync(SessionContext session)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var headers = new Dictionary<string, string>
        {
            ["X-Requested-With"] = "XMLHttpRequest",
            ["Cache-Control"] = "no-cache",
            ["Referer"] = $"{_baseUrl}{GwtEndpoints.PortalJsp}",
            ["If-Modified-Since"] = BwpHeaders.IfModifiedSinceEpoch,
            ["x-kelio-stat"] = $"cst={timestamp}",
            ["User-Agent"] = BrowserUserAgent
        };

        await _curlClient.GetAsync(
            $"{_baseUrl}{GwtEndpoints.PushConnect}?{timestamp}",
            headers, session.SessionCookie);
    }

    /// <summary>
    /// Call GlobalBWTService connect to get the dynamic employee ID.
    /// </summary>
    private async Task GlobalBwtServiceConnectAsync(SessionContext session)
    {
        if (string.IsNullOrEmpty(session.SessionId))
            return;

        var timestampSec = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var timestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var gwtRequest = _requestBuilder.BuildGlobalConnectRequest(session.SessionId, timestampSec);

        var url = $"{_baseUrl}/open/bwpDispatchServlet?{timestampMs}";
        var cookies = session.GetCookiesString();

        var headers = BwpHeaders.CreatePortalHeaders(_baseUrl, GwtEndpoints.PortalJsp, timestampMs, BrowserUserAgent);

        // GlobalBWTService connect is sent RAW
        var response = await _curlClient.PostAsync(url, gwtRequest, headers, cookies);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"GlobalBWTService connect failed with status {response.StatusCode}");
        }

        // Extract employee ID from response
        session.EmployeeId = EmployeeIdExtractor.ExtractFromConnectResponse(response.Body);
    }

    #region Helper Methods

    private static string? ExtractCsrfToken(string html)
    {
        var match = CsrfTokenRegex().Match(html);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string? ExtractSessionCookie(Dictionary<string, string> headers)
    {
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

        var match = SessionCookieRegex().Match(setCookie);
        return match.Success ? $"JSESSIONID={match.Groups[1].Value}" : null;
    }

    [GeneratedRegex(@"name=""_csrf_bodet""\s+value=""([^""]+)""", RegexOptions.Compiled)]
    private static partial Regex CsrfTokenRegex();

    [GeneratedRegex(@"<div\s+id=""csrf_token""[^>]*>([^<]+)</div>", RegexOptions.Compiled)]
    private static partial Regex CsrfTokenDivRegex();

    [GeneratedRegex(@"JSESSIONID=([^;]+)", RegexOptions.Compiled)]
    private static partial Regex SessionCookieRegex();

    #endregion
}
