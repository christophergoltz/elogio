using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Serilog;

namespace Elogio.Persistence.Api;

/// <summary>
/// HTTP client that uses curl_cffi for TLS fingerprint impersonation.
/// This bypasses server-side TLS fingerprint detection (JA3/JA4).
///
/// Supports two modes:
/// 1. Standalone mode (default): Uses bundled curl_proxy.exe - no Python required
/// 2. Python mode: Falls back to Python script if .exe not found
/// </summary>
public sealed class CurlImpersonateClient : IDisposable
{
    private readonly string _executablePath;
    private readonly string _impersonate;
    private readonly bool _useStandaloneExe;

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
    /// </summary>
    public async Task<CurlResponse> PostWithBodyFileAsync(
        string url,
        string body,
        Dictionary<string, string>? headers = null,
        string? cookies = null,
        CancellationToken cancellationToken = default)
    {
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
        // Nothing to dispose currently
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
