using System.Collections.Concurrent;
using System.Diagnostics;

namespace Daily_Bread.Services;

/// <summary>
/// Service for monitoring and counting database queries per request.
/// Used to verify performance optimizations.
/// </summary>
public interface IQueryMonitoringService
{
    /// <summary>
    /// Whether query monitoring is currently enabled.
    /// </summary>
    bool IsEnabled { get; set; }
    
    /// <summary>
    /// Whether to log SQL queries to the console.
    /// </summary>
    bool LogQueries { get; set; }
    
    /// <summary>
    /// Starts a new monitoring scope for a named operation.
    /// </summary>
    IDisposable BeginScope(string operationName);
    
    /// <summary>
    /// Increments the query count for the current scope.
    /// Called by the EF Core interceptor.
    /// </summary>
    void IncrementQueryCount();
    
    /// <summary>
    /// Gets the current scope's query count.
    /// </summary>
    int GetCurrentQueryCount();
    
    /// <summary>
    /// Gets all completed operation metrics.
    /// </summary>
    IReadOnlyList<QueryMetrics> GetCompletedMetrics();
    
    /// <summary>
    /// Clears all completed metrics.
    /// </summary>
    void ClearMetrics();
}

/// <summary>
/// Metrics for a single monitored operation.
/// </summary>
public record QueryMetrics
{
    public required string OperationName { get; init; }
    public int QueryCount { get; init; }
    public TimeSpan Duration { get; init; }
    public DateTime CompletedAt { get; init; }
}

public class QueryMonitoringService : IQueryMonitoringService
{
    private readonly AsyncLocal<MonitoringScope?> _currentScope = new();
    private readonly ConcurrentBag<QueryMetrics> _completedMetrics = new();
    private readonly ILogger<QueryMonitoringService> _logger;

    public bool IsEnabled { get; set; } = true;
    public bool LogQueries { get; set; }

    public QueryMonitoringService(ILogger<QueryMonitoringService> logger, IConfiguration configuration)
    {
        _logger = logger;
        // Initialize from configuration
        LogQueries = configuration.GetValue<bool>("QueryMonitoring:LogQueries");
    }

    public IDisposable BeginScope(string operationName)
    {
        if (!IsEnabled)
        {
            return NoOpScope.Instance;
        }
        
        var scope = new MonitoringScope(operationName, this);
        _currentScope.Value = scope;
        return scope;
    }

    public void IncrementQueryCount()
    {
        if (!IsEnabled) return;
        _currentScope.Value?.IncrementCount();
    }

    public int GetCurrentQueryCount()
    {
        return _currentScope.Value?.QueryCount ?? 0;
    }

    public IReadOnlyList<QueryMetrics> GetCompletedMetrics()
    {
        return _completedMetrics.ToArray();
    }

    public void ClearMetrics()
    {
        _completedMetrics.Clear();
    }

    private void CompleteScope(MonitoringScope scope)
    {
        var metrics = new QueryMetrics
        {
            OperationName = scope.OperationName,
            QueryCount = scope.QueryCount,
            Duration = scope.Elapsed,
            CompletedAt = DateTime.UtcNow
        };
        
        _completedMetrics.Add(metrics);
        
        _logger.LogInformation(
            "Query Monitor: {Operation} completed with {QueryCount} queries in {Duration:F2}ms",
            scope.OperationName,
            scope.QueryCount,
            scope.Elapsed.TotalMilliseconds);
        
        _currentScope.Value = null;
    }

    private sealed class NoOpScope : IDisposable
    {
        public static readonly NoOpScope Instance = new();
        public void Dispose() { }
    }

    private class MonitoringScope : IDisposable
    {
        private readonly QueryMonitoringService _service;
        private readonly Stopwatch _stopwatch;
        private int _queryCount;

        public string OperationName { get; }
        public int QueryCount => _queryCount;
        public TimeSpan Elapsed => _stopwatch.Elapsed;

        public MonitoringScope(string operationName, QueryMonitoringService service)
        {
            OperationName = operationName;
            _service = service;
            _stopwatch = Stopwatch.StartNew();
        }

        public void IncrementCount()
        {
            Interlocked.Increment(ref _queryCount);
        }

        public void Dispose()
        {
            _stopwatch.Stop();
            _service.CompleteScope(this);
        }
    }
}

/// <summary>
/// No-op implementation of IQueryMonitoringService for production.
/// </summary>
public class NullQueryMonitoringService : IQueryMonitoringService
{
    private sealed class NoOpScope : IDisposable
    {
        public static readonly NoOpScope Instance = new();
        public void Dispose() { }
    }

    public bool IsEnabled { get; set; }
    public bool LogQueries { get; set; }

    public IDisposable BeginScope(string operationName) => NoOpScope.Instance;
    public void IncrementQueryCount() { }
    public int GetCurrentQueryCount() => 0;
    public IReadOnlyList<QueryMetrics> GetCompletedMetrics() => Array.Empty<QueryMetrics>();
    public void ClearMetrics() { }
}
