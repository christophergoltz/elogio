using Refit;

namespace Elogio.Core.Api;

/// <summary>
/// Refit interface for Kelio BWP API calls.
/// All requests/responses are automatically BWP-encoded/decoded via BwpDelegatingHandler.
/// </summary>
public interface IKelioApi
{
    /// <summary>
    /// Send a GWT-RPC request to the BWP dispatch servlet.
    /// The timestamp cache-buster is added automatically by BwpDelegatingHandler.
    /// </summary>
    /// <param name="gwtRequest">The GWT-RPC formatted request body (sent as raw string, not JSON)</param>
    [Post("/open/bwpDispatchServlet")]
    Task<string> SendGwtRequestAsync(
        [Body(BodySerializationMethod.Default)] string gwtRequest);

    /// <summary>
    /// Send a GWT-RPC request and return full response with headers.
    /// Used for connect to capture X-CSRF-TOKEN header.
    /// </summary>
    [Post("/open/bwpDispatchServlet")]
    Task<HttpResponseMessage> SendGwtRequestWithHeadersAsync(
        [Body(BodySerializationMethod.Default)] string gwtRequest);

    /// <summary>
    /// Send a GWT-RPC request with X-CSRF-TOKEN header.
    /// </summary>
    [Post("/open/bwpDispatchServlet")]
    Task<string> SendGwtRequestWithCsrfAsync(
        [Body(BodySerializationMethod.Default)] string gwtRequest,
        [Header("X-CSRF-TOKEN")] string csrfToken);
}

/// <summary>
/// Refit interface for Kelio authentication (non-BWP).
/// </summary>
public interface IKelioAuthApi
{
    /// <summary>
    /// Get the login page to extract CSRF token.
    /// </summary>
    [Get("/open/login")]
    Task<string> GetLoginPageAsync();

    /// <summary>
    /// Submit login credentials.
    /// </summary>
    [Post("/open/j_spring_security_check")]
    [Headers(
        "Content-Type: application/x-www-form-urlencoded",
        "Upgrade-Insecure-Requests: 1"
    )]
    Task<HttpResponseMessage> LoginAsync(
        [Body(BodySerializationMethod.UrlEncoded)] LoginRequest request,
        [Header("Referer")] string referer,
        [Header("Origin")] string origin);
}

public class LoginRequest
{
    [AliasAs("ACTION")]
    public string Action { get; set; } = "ACTION_VALIDER_LOGIN";

    [AliasAs("username")]
    public required string Username { get; set; }

    [AliasAs("password")]
    public required string Password { get; set; }

    [AliasAs("_csrf_bodet")]
    public required string CsrfToken { get; set; }
}
