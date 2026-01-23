namespace Elogio.Persistence.Api.Session;

/// <summary>
/// Central state container for Kelio session data.
/// Holds all authentication and session-related state that was previously
/// scattered across KelioClient instance fields.
/// </summary>
public class SessionContext
{
    /// <summary>
    /// Base URL of the Kelio server (e.g., "https://company.kelio.io").
    /// </summary>
    public string BaseUrl { get; }

    /// <summary>
    /// CSRF token from the login page form.
    /// </summary>
    public string? CsrfToken { get; set; }

    /// <summary>
    /// Session ID extracted from portal page (hidden div).
    /// Used in GWT-RPC requests.
    /// </summary>
    public string? SessionId { get; set; }

    /// <summary>
    /// CSRF token from BWP connect response.
    /// Required for subsequent BWP requests.
    /// </summary>
    public string? BwpCsrfToken { get; set; }

    /// <summary>
    /// HTTP session cookie (JSESSIONID).
    /// Managed manually for TLS fingerprint consistency.
    /// </summary>
    public string? SessionCookie { get; set; }

    /// <summary>
    /// Session context ID from GlobalBWTService connect (portal).
    /// Used for most API requests.
    /// </summary>
    public int EmployeeId { get; set; }

    /// <summary>
    /// Employee's full name (FirstName LastName).
    /// Extracted during GlobalBWTService connect.
    /// </summary>
    public string? EmployeeName { get; set; }

    /// <summary>
    /// Context ID from Calendar GlobalBWTService connect.
    /// Used for calendar-specific requests.
    /// </summary>
    public int CalendarContextId { get; set; }

    /// <summary>
    /// ACTUAL employee ID from getParametreIntranet.
    /// Used specifically for absence calendar requests.
    /// </summary>
    public int RealEmployeeId { get; set; }

    /// <summary>
    /// Whether the user has successfully authenticated.
    /// </summary>
    public bool IsAuthenticated { get; set; }

    /// <summary>
    /// Whether the absence calendar app has been initialized.
    /// </summary>
    public bool CalendarAppInitialized { get; set; }

    /// <summary>
    /// Whether Phase1 calendar navigation was prefetched during login.
    /// </summary>
    public bool CalendarNavigationPrefetched { get; set; }

    public SessionContext(string baseUrl)
    {
        BaseUrl = baseUrl;
    }

    /// <summary>
    /// Get the session cookie string for HTTP requests.
    /// </summary>
    public string GetCookiesString() => SessionCookie ?? "";

    /// <summary>
    /// Check if the session has a valid session ID.
    /// </summary>
    public bool HasValidSession => !string.IsNullOrEmpty(SessionId);

    /// <summary>
    /// Reset all session state (for logout).
    /// </summary>
    public void Clear()
    {
        CsrfToken = null;
        SessionId = null;
        BwpCsrfToken = null;
        SessionCookie = null;
        EmployeeId = 0;
        EmployeeName = null;
        CalendarContextId = 0;
        RealEmployeeId = 0;
        IsAuthenticated = false;
        CalendarAppInitialized = false;
        CalendarNavigationPrefetched = false;
    }
}
