using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Daily_Bread.Data;

/// <summary>
/// EF Core interceptor that counts database queries for monitoring purposes.
/// Only active in Development environment.
/// </summary>
public class QueryCountingInterceptor : DbCommandInterceptor
{
    private readonly Services.IQueryMonitoringService _monitoringService;
    private readonly ILogger<QueryCountingInterceptor> _logger;

    public QueryCountingInterceptor(
        Services.IQueryMonitoringService monitoringService,
        ILogger<QueryCountingInterceptor> logger)
    {
        _monitoringService = monitoringService;
        _logger = logger;
    }

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result)
    {
        _monitoringService.IncrementQueryCount();
        
        if (_monitoringService.LogQueries)
        {
            _logger.LogDebug("EF Query #{Count}: {Sql}", 
                _monitoringService.GetCurrentQueryCount(),
                command.CommandText.Length > 200 
                    ? command.CommandText[..200] + "..." 
                    : command.CommandText);
        }
        
        return base.ReaderExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        _monitoringService.IncrementQueryCount();
        
        if (_monitoringService.LogQueries)
        {
            _logger.LogDebug("EF Query #{Count}: {Sql}", 
                _monitoringService.GetCurrentQueryCount(),
                command.CommandText.Length > 200 
                    ? command.CommandText[..200] + "..." 
                    : command.CommandText);
        }
        
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result)
    {
        _monitoringService.IncrementQueryCount();
        return base.NonQueryExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        _monitoringService.IncrementQueryCount();
        return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override InterceptionResult<object> ScalarExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result)
    {
        _monitoringService.IncrementQueryCount();
        return base.ScalarExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result,
        CancellationToken cancellationToken = default)
    {
        _monitoringService.IncrementQueryCount();
        return base.ScalarExecutingAsync(command, eventData, result, cancellationToken);
    }
}
