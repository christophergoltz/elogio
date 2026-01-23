namespace Elogio.Persistence.Api.Http;

/// <summary>
/// Provides constants and factory methods for BWP HTTP headers.
/// Consolidates header construction that was previously duplicated across KelioClient methods.
/// </summary>
public static class BwpHeaders
{
    // Chrome Client Hints - required for TLS fingerprint consistency
    public const string SecChUa = "\"Chromium\";v=\"131\", \"Not_A Brand\";v=\"24\"";
    public const string SecChUaMobile = "?0";
    public const string SecChUaPlatform = "\"Windows\"";

    // Common header values
    public const string ContentTypeBwp = "text/bwp;charset=UTF-8";
    public const string IfModifiedSinceEpoch = "Thu, 01 Jan 1970 00:00:00 GMT";

    /// <summary>
    /// Creates headers for portal BWP requests (connect, GlobalBWTService).
    /// Does NOT include Sec-Fetch-* or Origin headers.
    /// </summary>
    /// <param name="baseUrl">The base URL of the Kelio server</param>
    /// <param name="referer">The referer path (e.g., "/open/bwt/portail.jsp")</param>
    /// <param name="timestampMs">Unix timestamp in milliseconds for x-kelio-stat</param>
    /// <param name="userAgent">The browser user agent string</param>
    public static Dictionary<string, string> CreatePortalHeaders(
        string baseUrl,
        string referer,
        long timestampMs,
        string userAgent)
    {
        return new Dictionary<string, string>
        {
            ["Content-Type"] = ContentTypeBwp,
            ["X-Requested-With"] = "XMLHttpRequest",
            ["Cache-Control"] = "no-cache",
            ["Referer"] = $"{baseUrl}{referer}",
            ["If-Modified-Since"] = IfModifiedSinceEpoch,
            ["x-kelio-stat"] = $"cst={timestampMs}",
            ["User-Agent"] = userAgent,
            ["sec-ch-ua"] = SecChUa,
            ["sec-ch-ua-mobile"] = SecChUaMobile,
            ["sec-ch-ua-platform"] = SecChUaPlatform
        };
    }

    /// <summary>
    /// Creates headers for calendar/API BWP requests.
    /// Includes Sec-Fetch-* headers and Origin header for CORS.
    /// </summary>
    /// <param name="baseUrl">The base URL of the Kelio server</param>
    /// <param name="referer">The referer path (e.g., "/open/bwt/intranet_calendrier_absence.jsp")</param>
    /// <param name="timestampMs">Unix timestamp in milliseconds for x-kelio-stat</param>
    /// <param name="userAgent">The browser user agent string</param>
    public static Dictionary<string, string> CreateCalendarHeaders(
        string baseUrl,
        string referer,
        long timestampMs,
        string userAgent)
    {
        return new Dictionary<string, string>
        {
            ["Content-Type"] = ContentTypeBwp,
            ["X-Requested-With"] = "XMLHttpRequest",
            ["Cache-Control"] = "no-cache",
            ["Origin"] = baseUrl,
            ["Referer"] = $"{baseUrl}{referer}",
            ["If-Modified-Since"] = IfModifiedSinceEpoch,
            ["x-kelio-stat"] = $"cst={timestampMs}",
            ["User-Agent"] = userAgent,
            ["Sec-Fetch-Dest"] = "empty",
            ["Sec-Fetch-Mode"] = "cors",
            ["Sec-Fetch-Site"] = "same-origin",
            ["sec-ch-ua"] = SecChUa,
            ["sec-ch-ua-mobile"] = SecChUaMobile,
            ["sec-ch-ua-platform"] = SecChUaPlatform
        };
    }
}
