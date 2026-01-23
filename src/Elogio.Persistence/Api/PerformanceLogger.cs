using System.Diagnostics;
using Serilog;

namespace Elogio.Persistence.Api;

/// <summary>
/// Helper for performance measurement and logging.
/// Provides structured timing data for performance analysis.
/// </summary>
public static class PerformanceLogger
{
    /// <summary>
    /// Measure and log the duration of an operation.
    /// </summary>
    public static async Task<T> MeasureAsync<T>(string operationName, Func<Task<T>> operation)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var result = await operation();
            sw.Stop();
            Log.Information("[PERF] {OperationName} completed in {ElapsedMs}ms", operationName, sw.ElapsedMilliseconds);
            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            Log.Warning("[PERF] {OperationName} failed after {ElapsedMs}ms: {Error}",
                operationName, sw.ElapsedMilliseconds, ex.Message);
            throw;
        }
    }

    /// <summary>
    /// Measure and log the duration of a void operation.
    /// </summary>
    public static async Task MeasureAsync(string operationName, Func<Task> operation)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            await operation();
            sw.Stop();
            Log.Information("[PERF] {OperationName} completed in {ElapsedMs}ms", operationName, sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            Log.Warning("[PERF] {OperationName} failed after {ElapsedMs}ms: {Error}",
                operationName, sw.ElapsedMilliseconds, ex.Message);
            throw;
        }
    }

    /// <summary>
    /// Start a new performance scope for manual timing.
    /// </summary>
    public static PerformanceScope StartScope(string operationName)
    {
        return new PerformanceScope(operationName);
    }

    /// <summary>
    /// Represents a performance measurement scope.
    /// Logs elapsed time on disposal.
    /// </summary>
    public sealed class PerformanceScope : IDisposable
    {
        private readonly string _operationName;
        private readonly Stopwatch _stopwatch;
        private bool _disposed;

        internal PerformanceScope(string operationName)
        {
            _operationName = operationName;
            _stopwatch = Stopwatch.StartNew();
        }

        /// <summary>
        /// Log an intermediate checkpoint.
        /// </summary>
        public void Checkpoint(string checkpoint)
        {
            Log.Information("[PERF] {OperationName} checkpoint '{Checkpoint}' at {ElapsedMs}ms",
                _operationName, checkpoint, _stopwatch.ElapsedMilliseconds);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _stopwatch.Stop();
            Log.Information("[PERF] {OperationName} total time: {ElapsedMs}ms",
                _operationName, _stopwatch.ElapsedMilliseconds);
        }
    }
}
