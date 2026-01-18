using System.Diagnostics;
using System.Text.RegularExpressions;
using Elogio.Persistence.Api.Http;
using Elogio.Persistence.Api.Parsing;
using Elogio.Persistence.Api.Services;
using Elogio.Persistence.Api.Session;
using Elogio.Persistence.Protocol;
using Elogio.Persistence.Services;
using Serilog;

namespace Elogio.Persistence.Api.Calendar;

/// <summary>
/// Initializes the calendar/absence app before making absence-related API calls.
/// Extracted from KelioClient to separate calendar initialization concerns.
/// </summary>
public partial class CalendarAppInitializer
{
    private const string BrowserUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";

    private readonly CurlImpersonateClient _curlClient;
    private readonly GwtRpcRequestBuilder _requestBuilder;
    private readonly BwpCodec _bwpCodec;
    private readonly TranslationLoader _translationLoader;
    private readonly string _baseUrl;

    public CalendarAppInitializer(
        CurlImpersonateClient curlClient,
        GwtRpcRequestBuilder requestBuilder,
        BwpCodec bwpCodec,
        TranslationLoader translationLoader,
        string baseUrl)
    {
        _curlClient = curlClient;
        _requestBuilder = requestBuilder;
        _bwpCodec = bwpCodec;
        _translationLoader = translationLoader;
        _baseUrl = baseUrl;
    }

    /// <summary>
    /// Initialize the calendar/absence app before making absence-related API calls.
    /// Based on HAR capture analysis: load the intranet_calendrier_absence.jsp page and GWT files.
    /// OPTIMIZED: Phase1 is prefetched in background during login via BackgroundTaskManager.
    /// </summary>
    public async Task InitializeAsync(SessionContext session)
    {
        if (session.CalendarAppInitialized)
            return;

        var totalSw = Stopwatch.StartNew();
        try
        {
            Log.Debug("InitializeCalendarApp - starting initialization (OPTIMIZED v3)");

            // Wait briefly for background prefetch to complete (if running)
            if (!session.CalendarNavigationPrefetched)
            {
                var prefetchDone = await BackgroundTaskManager.Instance.WaitForCalendarPrefetchAsync(3000);
                if (prefetchDone && session.CalendarNavigationPrefetched)
                {
                    Log.Information("[PERF] InitCalendarApp: Background prefetch completed just in time");
                }
            }

            // ============================================
            // PHASE 1: Navigation setup
            // OPTIMIZATION: Usually prefetched in background during login
            // ============================================
            var phase1Sw = Stopwatch.StartNew();

            if (session.CalendarNavigationPrefetched)
            {
                Log.Information("[PERF] InitCalendarApp Phase1 (navigation): SKIPPED - already prefetched during login");
            }
            else
            {
                await RunPhase1NavigationAsync(session);
                Log.Information("[PERF] InitCalendarApp Phase1 (navigation): {ElapsedMs}ms", phase1Sw.ElapsedMilliseconds);
            }

            // ============================================
            // PHASE 2: All independent operations in parallel
            // ============================================
            var phase2Sw = Stopwatch.StartNew();
            await RunPhase2ParallelAsync(session);
            Log.Information("[PERF] InitCalendarApp Phase2 (parallel): {ElapsedMs}ms", phase2Sw.ElapsedMilliseconds);

            // ============================================
            // PHASE 3: Final presentation model (must be last)
            // ============================================
            var phase3Sw = Stopwatch.StartNew();
            await RunPhase3FinalModelAsync(session);
            Log.Information("[PERF] InitCalendarApp Phase3 (final model): {ElapsedMs}ms", phase3Sw.ElapsedMilliseconds);

            session.CalendarAppInitialized = true;
            Log.Information("[PERF] InitCalendarApp: TOTAL {ElapsedMs}ms (Phase1 prefetched={Prefetched})",
                totalSw.ElapsedMilliseconds, session.CalendarNavigationPrefetched);
        }
        catch (Exception ex)
        {
            Log.Warning("[PERF] InitCalendarApp: FAILED after {ElapsedMs}ms", totalSw.ElapsedMilliseconds);
            Log.Debug("InitializeCalendarApp - warning: {Message}", ex.Message);
            session.CalendarAppInitialized = true;
        }
    }

    /// <summary>
    /// Phase 1: Navigate to intranet section and load JSP.
    /// </summary>
    private async Task RunPhase1NavigationAsync(SessionContext session)
    {
        Log.Debug("InitializeCalendarApp - Phase1 not prefetched, running now");

        // Step 0: Navigate to intranet section first
        try
        {
            var intranetResponse = await _curlClient.GetAsync(
                $"{_baseUrl}/open/homepage?ACTION=intranet&asked=6&header=0",
                cookies: session.SessionCookie);

            var newCookie = ExtractSessionCookie(intranetResponse.Headers);
            if (!string.IsNullOrEmpty(newCookie))
                session.SessionCookie = newCookie;
        }
        catch (Exception ex)
        {
            Log.Debug("InitializeCalendarApp - intranet navigation warning: {Message}", ex.Message);
        }

        // Step 1: Load JSP (must be after intranet navigation)
        try
        {
            var jspResponse = await _curlClient.GetAsync(
                $"{_baseUrl}{GwtEndpoints.CalendarAbsenceJsp}",
                cookies: session.SessionCookie);

            var newCookie = ExtractSessionCookie(jspResponse.Headers);
            if (!string.IsNullOrEmpty(newCookie))
                session.SessionCookie = newCookie;
        }
        catch (Exception ex)
        {
            Log.Debug("InitializeCalendarApp - JSP page warning: {Message}", ex.Message);
        }
    }

    /// <summary>
    /// Phase 2: Run all independent operations in parallel.
    /// </summary>
    private async Task RunPhase2ParallelAsync(SessionContext session)
    {
        var phase2Tasks = new List<Task>
        {
            // GWT files
            _curlClient.GetAsync($"{_baseUrl}{GwtEndpoints.CalendarAbsenceNoCacheJs}", cookies: session.SessionCookie),
            _curlClient.GetAsync($"{_baseUrl}{GwtEndpoints.CalendarAbsenceCacheJs}", cookies: session.SessionCookie),

            // Calendar GlobalConnect
            CalendarGlobalConnectAsync(session),

            // Global presentation model
            CalendarGetGlobalPresentationModelAsync(session),

            // Parametre intranet
            CalendarGetParametreIntranetAsync(session),

            // Calendar translations
            _translationLoader.LoadCalendarTranslationsAsync(session.SessionId!, session.EmployeeId)
        };

        try
        {
            await Task.WhenAll(phase2Tasks);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[PERF] InitCalendarApp Phase2: some tasks failed");
        }
    }

    /// <summary>
    /// Phase 3: Final presentation model (must be last).
    /// </summary>
    private async Task RunPhase3FinalModelAsync(SessionContext session)
    {
        try
        {
            await CalendarGetPresentationModelAsync(session);
        }
        catch (Exception ex)
        {
            Log.Debug("InitializeCalendarApp - CalendrierAbsencePresentationModel warning: {Message}", ex.Message);
        }
    }

    /// <summary>
    /// Call GlobalBWTService connect for the calendar app.
    /// This uses Short=16 (vs 21 for portal) and the calendar JSP as referer.
    /// </summary>
    private async Task CalendarGlobalConnectAsync(SessionContext session)
    {
        if (string.IsNullOrEmpty(session.SessionId))
            return;

        var timestampSec = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var timestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var gwtRequest = _requestBuilder.BuildCalendarConnectRequest(session.SessionId, timestampSec);

        var url = $"{_baseUrl}/open/bwpDispatchServlet?{timestampMs}";
        var cookies = session.GetCookiesString();

        var headers = BwpHeaders.CreateCalendarHeaders(_baseUrl, GwtEndpoints.CalendarAbsenceJsp, timestampMs, BrowserUserAgent);

        Log.Debug("[curl_cffi] Calendar GlobalBWTService connect to {Url}", url);

        // GlobalBWTService connect is sent RAW (not BWP-encoded)
        var response = await _curlClient.PostAsync(url, gwtRequest, headers, cookies);

        Log.Debug("[curl_cffi] Calendar GlobalBWTService connect response status: {StatusCode}", response.StatusCode);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Calendar GlobalBWTService connect failed with status {response.StatusCode}");
        }

        // Extract session context ID from calendar connect response
        session.CalendarContextId = EmployeeIdExtractor.ExtractFromConnectResponse(response.Body);
        Log.Debug("[curl_cffi] Calendar session context ID: {ContextId}", session.CalendarContextId);
    }

    /// <summary>
    /// Call GlobalBWTService.getPresentationModel for GlobalPresentationModel.
    /// </summary>
    private async Task CalendarGetGlobalPresentationModelAsync(SessionContext session)
    {
        if (string.IsNullOrEmpty(session.SessionId))
            return;

        var timestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var gwtRequest = _requestBuilder.BuildGetGlobalPresentationModelRequest(session.SessionId, session.EmployeeId);

        var url = $"{_baseUrl}/open/bwpDispatchServlet?{timestampMs}";
        var cookies = session.GetCookiesString();

        var headers = BwpHeaders.CreateCalendarHeaders(_baseUrl, GwtEndpoints.CalendarAbsenceJsp, timestampMs, BrowserUserAgent);

        Log.Debug("[curl_cffi] Calendar GlobalPresentationModel request: {Request}", gwtRequest);

        var bodyToSend = _bwpCodec.Encode(gwtRequest);
        var response = await _curlClient.PostWithBodyFileAsync(url, bodyToSend, headers, cookies);

        Log.Debug("[curl_cffi] GlobalPresentationModel response status: {StatusCode}", response.StatusCode);

        if (!response.IsSuccessStatusCode)
        {
            Log.Debug("[curl_cffi] GlobalPresentationModel response body: {Body}",
                response.Body[..Math.Min(300, response.Body.Length)]);
            throw new HttpRequestException($"GlobalPresentationModel failed with status {response.StatusCode}");
        }
    }

    /// <summary>
    /// Call LiensBWTService.getParametreIntranet.
    /// Extracts the REAL employee ID from the response.
    /// </summary>
    private async Task CalendarGetParametreIntranetAsync(SessionContext session)
    {
        if (string.IsNullOrEmpty(session.SessionId))
            return;

        var timestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var gwtRequest = _requestBuilder.BuildGetParametreIntranetRequest(session.SessionId, session.EmployeeId);

        var url = $"{_baseUrl}/open/bwpDispatchServlet?{timestampMs}";
        var cookies = session.GetCookiesString();

        var headers = BwpHeaders.CreateCalendarHeaders(_baseUrl, GwtEndpoints.CalendarAbsenceJsp, timestampMs, BrowserUserAgent);

        Log.Debug("[curl_cffi] Calendar getParametreIntranet request: {Request}", gwtRequest);

        var bodyToSend = _bwpCodec.Encode(gwtRequest);
        var response = await _curlClient.PostWithBodyFileAsync(url, bodyToSend, headers, cookies);

        Log.Debug("[curl_cffi] getParametreIntranet response status: {StatusCode}", response.StatusCode);

        if (!response.IsSuccessStatusCode)
        {
            Log.Debug("[curl_cffi] getParametreIntranet response body: {Body}",
                response.Body[..Math.Min(300, response.Body.Length)]);
            throw new HttpRequestException($"getParametreIntranet failed with status {response.StatusCode}");
        }

        // Decode and extract the REAL employee ID from the response
        var responseBody = response.Body;
        if (_bwpCodec.IsBwp(responseBody))
        {
            var decoded = _bwpCodec.Decode(responseBody);
            responseBody = decoded.Decoded;
        }

        Log.Debug("[curl_cffi] getParametreIntranet decoded response: {Response}", responseBody);

        // Extract the REAL employee ID using the dedicated parser
        session.RealEmployeeId = EmployeeIdExtractor.ExtractFromParametreIntranetResponse(responseBody);
        Log.Debug("[curl_cffi] Extracted REAL employee ID from getParametreIntranet: {EmployeeId}", session.RealEmployeeId);
    }

    /// <summary>
    /// Call GlobalBWTService.getPresentationModel for CalendrierAbsencePresentationModel.
    /// CRITICAL: This MUST be called before getAbsencesEtJoursFeries or we get 401!
    /// </summary>
    private async Task CalendarGetPresentationModelAsync(SessionContext session)
    {
        if (string.IsNullOrEmpty(session.SessionId))
            return;

        var timestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var gwtRequest = _requestBuilder.BuildGetPresentationModelRequest(session.SessionId, session.EmployeeId);

        var url = $"{_baseUrl}/open/bwpDispatchServlet?{timestampMs}";
        var cookies = session.GetCookiesString();

        var headers = BwpHeaders.CreateCalendarHeaders(_baseUrl, GwtEndpoints.CalendarAbsenceJsp, timestampMs, BrowserUserAgent);

        Log.Debug("[curl_cffi] Calendar getPresentationModel to {Url}", url);
        Log.Debug("[curl_cffi] getPresentationModel request: {Request}", gwtRequest);

        // BWP-encode the request and use body file to avoid character corruption
        var bodyToSend = _bwpCodec.Encode(gwtRequest);
        var response = await _curlClient.PostWithBodyFileAsync(url, bodyToSend, headers, cookies);

        Log.Debug("[curl_cffi] getPresentationModel response status: {StatusCode}", response.StatusCode);
        Log.Debug("[curl_cffi] getPresentationModel response (first 300): {Body}",
            response.Body[..Math.Min(300, response.Body.Length)]);

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
            Log.Debug("[curl_cffi] getPresentationModel: Server returned ExceptionBWT!");
        }
    }

    #region Helper Methods

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

    [GeneratedRegex(@"JSESSIONID=([^;]+)", RegexOptions.Compiled)]
    private static partial Regex SessionCookieRegex();

    #endregion
}
