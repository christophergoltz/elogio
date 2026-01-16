using System.Diagnostics;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Serilog;

namespace Elogio.Persistence.Api;

/// <summary>
/// HTTP client that uses curl_cffi for TLS fingerprint impersonation.
/// This bypasses server-side TLS fingerprint detection (JA3/JA4).
///
/// Supports three modes:
/// 1. Server mode (default when available): Persistent curl_proxy server for TLS reuse
/// 2. Standalone mode: Uses bundled curl_proxy.exe per request - no Python required
/// 3. Python mode: Falls back to Python script if .exe not found
/// </summary>
public sealed class CurlImpersonateClient : IDisposable
{
    private readonly string _executablePath;
    private readonly string _impersonate;
    private readonly bool _useStandaloneExe;

    // Server mode fields
    private Process? _serverProcess;
    private HttpClient? _httpClient;
    private readonly int _serverPort = 5123;
    private bool _serverModeEnabled;
    private bool _serverModeAvailable;
    private bool _disposed;

    /// <summary>
    /// Creates a new CurlImpersonateClient.
    /// Automatically detects whether to use standalone .exe or Python fallback.
    /// </summary>
    /// <param name="impersonate">Browser to impersonate. Default: "chrome120"</param>
    public CurlImpersonateClient(string impersonate = "chrome120")
    {
        _impersonate = impersonate;

        // Try to find standalone exe first (no Python required)
        var assemblyDir = Path.GetDirectoryName(typeof(CurlImpersonateClient).Assembly.Location) ?? ".";

        // Check multiple possible locations for the exe
        var exePaths = new[]
        {
            Path.Combine(assemblyDir, "tools", "curl_proxy.exe"),
            Path.Combine(assemblyDir, "curl_proxy.exe"),
            Path.Combine(AppContext.BaseDirectory, "tools", "curl_proxy.exe"),
            Path.Combine(AppContext.BaseDirectory, "curl_proxy.exe")
        };

        var exePath = exePaths.FirstOrDefault(File.Exists);

        if (exePath != null)
        {
            _executablePath = exePath;
            _useStandaloneExe = true;
        }
        else
        {
            // Fall back to Python script
            var scriptPaths = new[]
            {
                Path.Combine(assemblyDir, "Scripts", "curl_proxy.py"),
                Path.Combine(AppContext.BaseDirectory, "Scripts", "curl_proxy.py")
            };

            var scriptPath = scriptPaths.FirstOrDefault(File.Exists)
                ?? Path.Combine(assemblyDir, "Scripts", "curl_proxy.py");

            _executablePath = scriptPath;
            _useStandaloneExe = false;
        }
    }

    /// <summary>
    /// Creates a CurlImpersonateClient with explicit path configuration.
    /// </summary>
    /// <param name="executablePath">Path to curl_proxy.exe or curl_proxy.py</param>
    /// <param name="useStandaloneExe">True to use .exe directly, false to use Python</param>
    /// <param name="impersonate">Browser to impersonate. Default: "chrome120"</param>
    public CurlImpersonateClient(
        string executablePath,
        bool useStandaloneExe,
        string impersonate = "chrome120")
    {
        _executablePath = executablePath;
        _useStandaloneExe = useStandaloneExe;
        _impersonate = impersonate;
    }

    /// <summary>
    /// Returns true if using standalone .exe (no Python required).
    /// </summary>
    public bool IsUsingStandaloneExe => _useStandaloneExe;

    /// <summary>
    /// Path to the executable being used.
    /// </summary>
    public string ExecutablePath => _executablePath;

    /// <summary>
    /// Returns true if server mode is enabled and running.
    /// </summary>
    public bool IsServerModeEnabled => _serverModeEnabled && _serverProcess is { HasExited: false };

    /// <summary>
    /// Returns the server port (for diagnostics).
    /// </summary>
    public int ServerPort => _serverPort;

    /// <summary>
    /// Initialize the client, optionally starting server mode for better performance.
    /// Server mode keeps curl_proxy running as an HTTP server, enabling TLS session reuse.
    /// </summary>
    /// <param name="enableServerMode">True to enable server mode (recommended for multiple requests)</param>
    public async Task InitializeAsync(bool enableServerMode = true)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(CurlImpersonateClient));

        // Only standalone exe supports server mode currently
        if (!enableServerMode || !_useStandaloneExe)
        {
            _serverModeEnabled = false;
            Log.Information("CurlImpersonateClient: Server mode disabled (enableServerMode={Enable}, useStandaloneExe={UseExe})",
                enableServerMode, _useStandaloneExe);
            return;
        }

        try
        {
            await StartServerAsync();
            _serverModeEnabled = true;
            Log.Information("CurlImpersonateClient: Server mode enabled on port {Port}", _serverPort);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "CurlImpersonateClient: Failed to start server mode, falling back to process-per-request");
            _serverModeEnabled = false;
        }
    }

    private async Task StartServerAsync()
    {
        var sw = Stopwatch.StartNew();

        _serverProcess = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = _executablePath,
                Arguments = $"--server --port {_serverPort}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            }
        };

        _serverProcess.Start();

        // Wait for "READY" signal from server
        var ready = false;
        var timeout = TimeSpan.FromSeconds(10);
        using var cts = new CancellationTokenSource(timeout);

        try
        {
            while (!cts.Token.IsCancellationRequested)
            {
                var line = await _serverProcess.StandardOutput.ReadLineAsync(cts.Token);
                if (line == "READY")
                {
                    ready = true;
                    break;
                }
                Log.Debug("CurlProxy server: {Line}", line);
            }
        }
        catch (OperationCanceledException)
        {
            // Timeout
        }

        if (!ready)
        {
            _serverProcess.Kill();
            _serverProcess.Dispose();
            _serverProcess = null;
            throw new InvalidOperationException("curl_proxy server did not become ready within timeout");
        }

        // Create HTTP client for server communication
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri($"http://localhost:{_serverPort}"),
            Timeout = TimeSpan.FromSeconds(60)
        };

        // Verify server is responding
        var healthResponse = await _httpClient.GetAsync("/health");
        if (!healthResponse.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"curl_proxy server health check failed: {healthResponse.StatusCode}");
        }

        _serverModeAvailable = true;
        Log.Information("[PERF] CurlProxy: Server started in {ElapsedMs}ms", sw.ElapsedMilliseconds);
    }

    private async Task StopServerAsync()
    {
        if (_httpClient != null)
        {
            try
            {
                await _httpClient.PostAsync("/shutdown", null);
            }
            catch
            {
                // Ignore shutdown errors
            }
            _httpClient.Dispose();
            _httpClient = null;
        }

        if (_serverProcess != null)
        {
            try
            {
                if (!_serverProcess.HasExited)
                {
                    _serverProcess.WaitForExit(1000);
                    if (!_serverProcess.HasExited)
                    {
                        _serverProcess.Kill();
                    }
                }
            }
            catch
            {
                // Ignore cleanup errors
            }
            _serverProcess.Dispose();
            _serverProcess = null;
        }

        _serverModeAvailable = false;
        _serverModeEnabled = false;
    }

    /// <summary>
    /// Send an HTTP request with TLS fingerprint impersonation.
    /// </summary>
    public async Task<CurlResponse> SendAsync(
        HttpMethod method,
        string url,
        string? body = null,
        Dictionary<string, string>? headers = null,
        string? cookies = null,
        CancellationToken cancellationToken = default)
    {
        // Use server mode if available
        if (_serverModeEnabled && _serverModeAvailable && _httpClient != null)
        {
            return await SendViaServerAsync(method, url, body, null, headers, cookies, cancellationToken);
        }

        // Fallback to process-per-request
        var args = BuildArguments(method, url, body, headers, cookies);

        try
        {
            var (exitCode, stdout, stderr) = await RunProcessAsync(args, cancellationToken);

            if (exitCode != 0)
            {
                var mode = _useStandaloneExe ? "Standalone" : "Python";
                return new CurlResponse
                {
                    StatusCode = -1,
                    Body = stderr,
                    Error = $"{mode} process exited with code {exitCode}: {stderr}"
                };
            }

            return ParseResponse(stdout);
        }
        catch (Exception ex)
        {
            return new CurlResponse
            {
                StatusCode = -1,
                Body = string.Empty,
                Error = ex.Message
            };
        }
    }

    private async Task<CurlResponse> SendViaServerAsync(
        HttpMethod method,
        string url,
        string? body,
        string? bodyBase64,
        Dictionary<string, string>? headers,
        string? cookies,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            var request = new ServerRequest
            {
                Method = method.Method,
                Url = url,
                Body = body,
                BodyBase64 = bodyBase64,
                Headers = headers,
                Cookies = cookies,
                Impersonate = _impersonate
            };

            var response = await _httpClient!.PostAsJsonAsync("/request", request, cancellationToken);
            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

            // Debug: Log raw response to see what we're getting
            var headersIdx = responseContent.IndexOf("\"headers\"", StringComparison.Ordinal);
            if (headersIdx >= 0)
            {
                var headersJson = responseContent.Substring(headersIdx, Math.Min(500, responseContent.Length - headersIdx));
                Log.Information("[DEBUG] CurlProxy raw headers section: {Headers}", headersJson);
            }

            var result = JsonSerializer.Deserialize<CurlResponseJson>(responseContent, JsonOptions);

            if (result == null)
            {
                return new CurlResponse
                {
                    StatusCode = -1,
                    Body = string.Empty,
                    Error = "Failed to parse server response"
                };
            }

            return new CurlResponse
            {
                StatusCode = result.StatusCode,
                Body = result.Body ?? string.Empty,
                Headers = result.Headers ?? new Dictionary<string, string>(),
                Error = result.Error
            };
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "CurlProxy: Server request failed, may need to restart server");
            return new CurlResponse
            {
                StatusCode = -1,
                Body = string.Empty,
                Error = $"Server mode error: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Send a GET request.
    /// </summary>
    public Task<CurlResponse> GetAsync(
        string url,
        Dictionary<string, string>? headers = null,
        string? cookies = null,
        CancellationToken cancellationToken = default)
    {
        return SendAsync(HttpMethod.Get, url, null, headers, cookies, cancellationToken);
    }

    /// <summary>
    /// Send a POST request.
    /// </summary>
    public Task<CurlResponse> PostAsync(
        string url,
        string body,
        Dictionary<string, string>? headers = null,
        string? cookies = null,
        CancellationToken cancellationToken = default)
    {
        return SendAsync(HttpMethod.Post, url, body, headers, cookies, cancellationToken);
    }

    /// <summary>
    /// Send a POST request using a body file (avoids character encoding issues with BWP data).
    /// CRITICAL: BWP-encoded data contains special characters (like 0xA4) that get corrupted
    /// when passed through command line arguments. Using a body file avoids this issue.
    /// In server mode, uses base64 encoding instead of body file.
    /// </summary>
    public async Task<CurlResponse> PostWithBodyFileAsync(
        string url,
        string body,
        Dictionary<string, string>? headers = null,
        string? cookies = null,
        CancellationToken cancellationToken = default)
    {
        // Use server mode if available - base64 encoding avoids file I/O and encoding issues
        if (_serverModeEnabled && _serverModeAvailable && _httpClient != null)
        {
            var utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            var bodyBase64 = Convert.ToBase64String(utf8NoBom.GetBytes(body));
            return await SendViaServerAsync(HttpMethod.Post, url, null, bodyBase64, headers, cookies, cancellationToken);
        }

        // Fallback to process-per-request with body file
        string? bodyFilePath = null;
        try
        {
            // Write body to temp file WITHOUT BOM
            // CRITICAL: Encoding.UTF8 includes BOM by default, which corrupts BWP requests
            // The server expects the body to start with 0xA4 marker, not 0xFEFF BOM
            bodyFilePath = Path.GetTempFileName();
            var utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            await File.WriteAllTextAsync(bodyFilePath, body, utf8NoBom, cancellationToken);

            var args = BuildArgumentsWithBodyFile(HttpMethod.Post, url, bodyFilePath, headers, cookies);

            var (exitCode, stdout, stderr) = await RunProcessAsync(args, cancellationToken);

            if (exitCode != 0)
            {
                var mode = _useStandaloneExe ? "Standalone" : "Python";
                return new CurlResponse
                {
                    StatusCode = -1,
                    Body = stderr,
                    Error = $"{mode} process exited with code {exitCode}: {stderr}"
                };
            }

            return ParseResponse(stdout);
        }
        catch (Exception ex)
        {
            return new CurlResponse
            {
                StatusCode = -1,
                Body = string.Empty,
                Error = ex.Message
            };
        }
        finally
        {
            // Clean up temp file
            if (bodyFilePath != null && File.Exists(bodyFilePath))
            {
                try { File.Delete(bodyFilePath); } catch { /* ignore cleanup errors */ }
            }
        }
    }

    private string BuildArguments(
        HttpMethod method,
        string url,
        string? body,
        Dictionary<string, string>? headers,
        string? cookies)
    {
        var args = new StringBuilder();

        // Method and URL
        args.Append($"{method.Method} \"{url}\" ");

        // Impersonate target
        args.Append($"--impersonate {_impersonate} ");

        // Headers
        if (headers != null)
        {
            foreach (var (key, value) in headers)
            {
                // Escape quotes in header values
                var escapedValue = value.Replace("\"", "\\\"");
                args.Append($"--header \"{key}:{escapedValue}\" ");
            }
        }

        // Cookies
        if (!string.IsNullOrEmpty(cookies))
        {
            var escapedCookies = cookies.Replace("\"", "\\\"");
            args.Append($"--cookie \"{escapedCookies}\" ");
        }

        // Body (for POST)
        if (!string.IsNullOrEmpty(body))
        {
            // Escape the body for command line
            var escapedBody = body
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r");
            args.Append($"--body \"{escapedBody}\" ");
        }

        return args.ToString();
    }

    private string BuildArgumentsWithBodyFile(
        HttpMethod method,
        string url,
        string bodyFilePath,
        Dictionary<string, string>? headers,
        string? cookies)
    {
        var args = new StringBuilder();

        // Method and URL
        args.Append($"{method.Method} \"{url}\" ");

        // Impersonate target
        args.Append($"--impersonate {_impersonate} ");

        // Headers
        if (headers != null)
        {
            foreach (var (key, value) in headers)
            {
                var escapedValue = value.Replace("\"", "\\\"");
                args.Append($"--header \"{key}:{escapedValue}\" ");
            }
        }

        // Cookies
        if (!string.IsNullOrEmpty(cookies))
        {
            var escapedCookies = cookies.Replace("\"", "\\\"");
            args.Append($"--cookie \"{escapedCookies}\" ");
        }

        // Body file (avoids character encoding issues)
        args.Append($"--body-file \"{bodyFilePath}\" ");

        return args.ToString();
    }

    private async Task<(int ExitCode, string Stdout, string Stderr)> RunProcessAsync(
        string arguments,
        CancellationToken cancellationToken)
    {
        var totalSw = Stopwatch.StartNew();
        string fileName;
        string processArgs;

        if (_useStandaloneExe)
        {
            // Direct exe execution - no Python required
            fileName = _executablePath;
            processArgs = arguments;
        }
        else
        {
            // Python fallback
            fileName = "python";
            processArgs = $"\"{_executablePath}\" {arguments}";
        }

        using var process = new Process();
        process.StartInfo.FileName = fileName;
        process.StartInfo.Arguments = processArgs;
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.CreateNoWindow = true;
        process.StartInfo.StandardOutputEncoding = Encoding.UTF8;
        process.StartInfo.StandardErrorEncoding = Encoding.UTF8;

        var startSw = Stopwatch.StartNew();
        process.Start();
        var processStartMs = startSw.ElapsedMilliseconds;

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);
        var processExecMs = startSw.ElapsedMilliseconds;

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        totalSw.Stop();

        // Log process overhead separately from total request time
        Log.Debug("[PERF] CurlProxy: Process start={StartMs}ms, exec={ExecMs}ms, total={TotalMs}ms, mode={Mode}",
            processStartMs, processExecMs, totalSw.ElapsedMilliseconds,
            _useStandaloneExe ? "exe" : "python");

        return (process.ExitCode, stdout, stderr);
    }

    private static CurlResponse ParseResponse(string json)
    {
        try
        {
            var result = JsonSerializer.Deserialize<CurlResponseJson>(json, JsonOptions);
            if (result == null)
            {
                return new CurlResponse
                {
                    StatusCode = -1,
                    Body = string.Empty,
                    Error = "Failed to parse response JSON"
                };
            }

            return new CurlResponse
            {
                StatusCode = result.StatusCode,
                Body = result.Body ?? string.Empty,
                Headers = result.Headers ?? new Dictionary<string, string>(),
                Error = result.Error
            };
        }
        catch (JsonException ex)
        {
            return new CurlResponse
            {
                StatusCode = -1,
                Body = json,
                Error = $"JSON parse error: {ex.Message}"
            };
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Stop server asynchronously but don't wait too long
        try
        {
            StopServerAsync().Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    private class CurlResponseJson
    {
        [JsonPropertyName("status_code")]
        public int StatusCode { get; set; }

        [JsonPropertyName("body")]
        public string? Body { get; set; }

        [JsonPropertyName("headers")]
        public Dictionary<string, string>? Headers { get; set; }

        [JsonPropertyName("error")]
        public string? Error { get; set; }
    }

    private class ServerRequest
    {
        [JsonPropertyName("method")]
        public string Method { get; set; } = "GET";

        [JsonPropertyName("url")]
        public string? Url { get; set; }

        [JsonPropertyName("body")]
        public string? Body { get; set; }

        [JsonPropertyName("body_base64")]
        public string? BodyBase64 { get; set; }

        [JsonPropertyName("headers")]
        public Dictionary<string, string>? Headers { get; set; }

        [JsonPropertyName("cookies")]
        public string? Cookies { get; set; }

        [JsonPropertyName("impersonate")]
        public string Impersonate { get; set; } = "chrome120";
    }
}

/// <summary>
/// Response from CurlImpersonateClient.
/// </summary>
public class CurlResponse
{
    public int StatusCode { get; set; }
    public string Body { get; set; } = string.Empty;
    public Dictionary<string, string> Headers { get; set; } = new();
    public string? Error { get; set; }

    public bool IsSuccessStatusCode => StatusCode >= 200 && StatusCode < 300;
}
