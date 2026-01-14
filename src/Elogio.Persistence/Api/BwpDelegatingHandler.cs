using System.Net.Http.Headers;
using System.Text;
using Elogio.Persistence.Protocol;

namespace Elogio.Persistence.Api;

/// <summary>
/// HTTP handler that automatically encodes requests and decodes responses using BWP protocol.
/// Also adds timestamp cache-buster to bwpDispatchServlet requests.
/// </summary>
public class BwpDelegatingHandler : DelegatingHandler
{
    private readonly BwpCodec _codec = new();

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        // Add timestamp cache-buster and Kelio-specific headers to bwpDispatchServlet requests
        // Kelio expects format: /open/bwpDispatchServlet?1767898088326 (timestamp as raw query string)
        if (request.RequestUri?.AbsolutePath.EndsWith("bwpDispatchServlet") == true)
        {
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var newUri = new UriBuilder(request.RequestUri)
            {
                Query = timestamp.ToString()
            };
            request.RequestUri = newUri.Uri;

            // Add Kelio-specific headers that the browser sends
            request.Headers.TryAddWithoutValidation("If-Modified-Since", "Thu, 01 Jan 1970 00:00:00 GMT");
            request.Headers.TryAddWithoutValidation("x-kelio-stat", $"cst={timestamp}");
        }

        // Encode request body if present
        // Note: "connect" method requests are NOT BWP-encoded (discovered from API analysis)
        if (request.Content is not null)
        {
            var originalContent = await request.Content.ReadAsStringAsync(cancellationToken);
            if (!string.IsNullOrEmpty(originalContent) && !_codec.IsBwp(originalContent))
            {
                // Skip encoding for connect requests - they're sent as raw GWT-RPC
                // Check for the connect method name in GWT-RPC format
                var isConnectRequest = originalContent.Contains(",\"connect\",");

                string bodyToSend;
                if (isConnectRequest)
                {
                    // Connect is sent as raw GWT-RPC
                    bodyToSend = originalContent;
                }
                else
                {
                    // All other methods are BWP-encoded
                    bodyToSend = _codec.Encode(originalContent);
                }

                var bytes = Encoding.UTF8.GetBytes(bodyToSend);
                request.Content = new ByteArrayContent(bytes);
                request.Content.Headers.TryAddWithoutValidation("Content-Type", "text/bwp;charset=UTF-8");
            }
        }

        // Send request
        var response = await base.SendAsync(request, cancellationToken);

        // Decode response body if BWP-encoded
        if (response.Content is not null)
        {
            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            if (_codec.IsBwp(responseContent))
            {
                var decoded = _codec.Decode(responseContent);
                response.Content = new StringContent(decoded.Decoded, Encoding.UTF8, "text/plain");
            }
        }

        return response;
    }
}
