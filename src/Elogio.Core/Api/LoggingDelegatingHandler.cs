using System.Net;
using System.Text;

namespace Elogio.Core.Api;

/// <summary>
/// DelegatingHandler that logs all HTTP requests and responses to a file.
/// </summary>
public class LoggingDelegatingHandler : DelegatingHandler
{
    /// <summary>
    /// Optional CookieContainer to log cookies from.
    /// </summary>
    public CookieContainer? CookieContainer { get; set; }
    private static readonly string LogFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Elogio",
        "http_log.txt");

    static LoggingDelegatingHandler()
    {
        var dir = Path.GetDirectoryName(LogFile)!;
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"\n{'=',-60}");
        sb.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] REQUEST");
        sb.AppendLine($"{'=',-60}");
        sb.AppendLine($"{request.Method} {request.RequestUri}");

        sb.AppendLine("\n--- Request Headers ---");
        foreach (var header in request.Headers)
        {
            sb.AppendLine($"  {header.Key}: {string.Join(", ", header.Value)}");
        }

        // Log cookies from CookieContainer if available
        if (CookieContainer != null && request.RequestUri != null)
        {
            var cookies = CookieContainer.GetCookies(request.RequestUri);
            sb.AppendLine($"\n--- Cookies (from CookieContainer for {request.RequestUri.Host}) ---");
            sb.AppendLine($"  Total cookies: {cookies.Count}");
            foreach (Cookie cookie in cookies)
            {
                sb.AppendLine($"  {cookie.Name}={cookie.Value} (Path={cookie.Path}, Domain={cookie.Domain}, Secure={cookie.Secure}, HttpOnly={cookie.HttpOnly}, Expires={cookie.Expires})");
            }
            if (cookies.Count == 0)
            {
                // Check all cookies in container
                var allCookies = CookieContainer.GetAllCookies();
                sb.AppendLine($"  All cookies in container: {allCookies.Count}");
                foreach (Cookie c in allCookies)
                {
                    sb.AppendLine($"    {c.Name} for {c.Domain}{c.Path}");
                }
            }
        }

        if (request.Content != null)
        {
            sb.AppendLine("\n--- Content Headers ---");
            foreach (var header in request.Content.Headers)
            {
                sb.AppendLine($"  {header.Key}: {string.Join(", ", header.Value)}");
            }

            try
            {
                var body = await request.Content.ReadAsStringAsync(cancellationToken);
                if (!string.IsNullOrEmpty(body))
                {
                    // Truncate very long bodies
                    if (body.Length > 2000)
                    {
                        body = body[..2000] + "... [truncated]";
                    }
                    sb.AppendLine($"\n--- Request Body ({body.Length} chars) ---");
                    sb.AppendLine(body);
                }
            }
            catch
            {
                sb.AppendLine("\n--- Request Body: [could not read] ---");
            }
        }

        await File.AppendAllTextAsync(LogFile, sb.ToString(), cancellationToken);

        HttpResponseMessage response;
        try
        {
            response = await base.SendAsync(request, cancellationToken);
        }
        catch (Exception ex)
        {
            var errorSb = new StringBuilder();
            errorSb.AppendLine($"\n--- EXCEPTION ---");
            errorSb.AppendLine($"{ex.GetType().Name}: {ex.Message}");
            errorSb.AppendLine(ex.StackTrace);
            await File.AppendAllTextAsync(LogFile, errorSb.ToString(), cancellationToken);
            throw;
        }

        var respSb = new StringBuilder();
        respSb.AppendLine($"\n--- Response: {(int)response.StatusCode} {response.StatusCode} ---");

        respSb.AppendLine("\n--- Response Headers ---");
        foreach (var header in response.Headers)
        {
            respSb.AppendLine($"  {header.Key}: {string.Join(", ", header.Value)}");
        }

        if (response.Content != null)
        {
            respSb.AppendLine("\n--- Content Headers ---");
            foreach (var header in response.Content.Headers)
            {
                respSb.AppendLine($"  {header.Key}: {string.Join(", ", header.Value)}");
            }

            try
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                if (!string.IsNullOrEmpty(body))
                {
                    // Truncate very long bodies
                    if (body.Length > 5000)
                    {
                        body = body[..5000] + "... [truncated]";
                    }
                    respSb.AppendLine($"\n--- Response Body ({body.Length} chars) ---");
                    respSb.AppendLine(body);
                }
            }
            catch
            {
                respSb.AppendLine("\n--- Response Body: [could not read] ---");
            }
        }

        respSb.AppendLine($"\n{'=',-60}\n");
        await File.AppendAllTextAsync(LogFile, respSb.ToString(), cancellationToken);

        return response;
    }

    /// <summary>
    /// Get the path to the log file.
    /// </summary>
    public static string GetLogFilePath() => LogFile;

    /// <summary>
    /// Clear the log file.
    /// </summary>
    public static void ClearLog()
    {
        if (File.Exists(LogFile))
        {
            File.Delete(LogFile);
        }
    }
}
