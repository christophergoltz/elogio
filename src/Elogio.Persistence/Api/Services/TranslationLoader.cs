using System.Diagnostics;
using Elogio.Persistence.Api.Http;
using Elogio.Persistence.Protocol;
using Serilog;

namespace Elogio.Persistence.Api.Services;

/// <summary>
/// Loads translation resources from the Kelio server.
/// Translations must be loaded before certain API calls to initialize server-side state.
/// </summary>
public class TranslationLoader
{
    private readonly GwtRpcRequestBuilder _requestBuilder;
    private readonly Func<string, string?, Task<string>> _sendGwtRequest;

    /// <summary>
    /// Translation prefixes required for portal/presence functionality.
    /// Must be loaded before getSemaine calls.
    /// </summary>
    private static readonly string[] PortalPrefixes =
    [
        "global_",
        "app.portail.declaration_",
        "app.portail.declaration.presence_"
    ];

    /// <summary>
    /// Translation prefixes required for calendar/absence functionality.
    /// Must be loaded before getAbsencesEtJoursFeries calls.
    /// </summary>
    private static readonly string[] CalendarPrefixes =
    [
        "global_",
        "calendrier.annuel.intranet_"
    ];

    /// <summary>
    /// Creates a new TranslationLoader.
    /// </summary>
    /// <param name="requestBuilder">Builder for GWT-RPC requests</param>
    /// <param name="sendGwtRequest">
    /// Delegate to send GWT requests. Parameters: (gwtRequest, customReferer).
    /// Returns the decoded response body.
    /// </param>
    public TranslationLoader(
        GwtRpcRequestBuilder requestBuilder,
        Func<string, string?, Task<string>> sendGwtRequest)
    {
        _requestBuilder = requestBuilder;
        _sendGwtRequest = sendGwtRequest;
    }

    /// <summary>
    /// Load portal translations required for presence/time tracking API.
    /// Runs all translation requests in parallel for performance.
    /// </summary>
    /// <param name="sessionId">Current session ID</param>
    /// <param name="employeeId">Employee ID from GlobalBWTService connect</param>
    public async Task LoadPortalTranslationsAsync(string sessionId, int employeeId)
    {
        await LoadTranslationsInternalAsync(
            PortalPrefixes,
            sessionId,
            employeeId,
            referer: null, // Uses default portal referer
            logPrefix: "LoadTranslations");
    }

    /// <summary>
    /// Load calendar translations required for absence/calendar API.
    /// Must be called after CalendarGlobalConnect. Uses calendar JSP referer.
    /// Runs all translation requests in parallel for performance.
    /// </summary>
    /// <param name="sessionId">Current session ID</param>
    /// <param name="employeeId">Employee ID from GlobalBWTService connect</param>
    public async Task LoadCalendarTranslationsAsync(string sessionId, int employeeId)
    {
        await LoadTranslationsInternalAsync(
            CalendarPrefixes,
            sessionId,
            employeeId,
            referer: GwtEndpoints.CalendarAbsenceJsp,
            logPrefix: "LoadCalendarTranslations");
    }

    /// <summary>
    /// Internal method that loads translations for given prefixes in parallel.
    /// </summary>
    private async Task LoadTranslationsInternalAsync(
        string[] prefixes,
        string sessionId,
        int employeeId,
        string? referer,
        string logPrefix)
    {
        var totalSw = Stopwatch.StartNew();

        var tasks = prefixes.Select(async prefix =>
        {
            var sw = Stopwatch.StartNew();
            try
            {
                Log.Debug("{LogPrefix} - loading {Prefix} with employeeId={EmployeeId}",
                    logPrefix, prefix, employeeId);

                var gwtRequest = _requestBuilder.BuildGetTraductionsRequest(sessionId, prefix, employeeId);
                var response = await _sendGwtRequest(gwtRequest, referer);
                var hasException = response.Contains("ExceptionBWT");

                Log.Information("[PERF] {LogPrefix}: '{Prefix}' took {ElapsedMs}ms",
                    logPrefix, prefix, sw.ElapsedMilliseconds);
                Log.Debug("{LogPrefix} - {Prefix} response: exception={HasException}, length={Length}",
                    logPrefix, prefix, hasException, response.Length);

                return (prefix, success: !hasException);
            }
            catch (Exception ex)
            {
                Log.Warning("[PERF] {LogPrefix}: '{Prefix}' failed after {ElapsedMs}ms: {Error}",
                    logPrefix, prefix, sw.ElapsedMilliseconds, ex.Message);
                Log.Debug("{LogPrefix} - {Prefix} warning: {Message}",
                    logPrefix, prefix, ex.Message);

                return (prefix, success: false);
            }
        }).ToList();

        var results = await Task.WhenAll(tasks);
        var successCount = results.Count(r => r.success);

        Log.Information("[PERF] {LogPrefix}: PARALLEL - TOTAL {ElapsedMs}ms ({SuccessCount}/{TotalCount} succeeded)",
            logPrefix, totalSw.ElapsedMilliseconds, successCount, prefixes.Length);
    }
}
