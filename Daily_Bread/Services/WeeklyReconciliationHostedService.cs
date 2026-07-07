using Microsoft.Extensions.Hosting;

namespace Daily_Bread.Services;

/// <summary>
/// Thin background timer that drives <see cref="IWeeklyReconciliationService"/>. The app otherwise has
/// no scheduler, so weekly reconciliation would never fire on its own (plan §8). On each ~hourly tick —
/// plus one shortly after startup — it checks whether a week has ended and not yet been reconciled, and
/// if so runs reconciliation for the current week end. All real work lives in the reconciliation service
/// (which is testable); this host is only a timer. Every tick is wrapped in try/catch so a bad week can
/// never crash the host.
/// </summary>
public sealed class WeeklyReconciliationHostedService : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromHours(1);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(15);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<WeeklyReconciliationHostedService> _logger;

    public WeeklyReconciliationHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger<WeeklyReconciliationHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Initial check shortly after startup (so a week that ended while the app was down is caught),
        // then on a fixed interval.
        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        await RunTickAsync(stoppingToken);

        using var timer = new PeriodicTimer(TickInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await RunTickAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
    }

    /// <summary>
    /// One tick: create a DI scope, resolve the reconciliation + family-settings services, and run
    /// reconciliation if it is due. Never throws — failures are logged so the host keeps running.
    /// </summary>
    private async Task RunTickAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var reconciliation = scope.ServiceProvider.GetRequiredService<IWeeklyReconciliationService>();

            await ReconcileIfNeededAsync(reconciliation, _logger, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutting down — ignore.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Weekly reconciliation tick failed; will retry on the next interval.");
        }
    }

    /// <summary>
    /// Core tick logic, factored out so it can be unit-tested directly against resolved services. Runs
    /// reconciliation for the most recently completed week when
    /// <see cref="IWeeklyReconciliationService.IsReconciliationNeededAsync"/> reports it is due.
    /// </summary>
    internal static async Task ReconcileIfNeededAsync(
        IWeeklyReconciliationService reconciliation,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (!await reconciliation.IsReconciliationNeededAsync())
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();

        var weekEnd = await reconciliation.GetWeekEndToReconcileAsync();
        logger.LogInformation("Weekly reconciliation is due; running for week ending {WeekEnd}.", weekEnd);

        var results = await reconciliation.RunWeeklyReconciliationAsync(weekEnd);

        logger.LogInformation(
            "Weekly reconciliation completed for week ending {WeekEnd}: {Count} child(ren) processed.",
            weekEnd, results.Count);
    }
}
