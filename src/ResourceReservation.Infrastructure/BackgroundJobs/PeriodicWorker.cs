using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ResourceReservation.Infrastructure.BackgroundJobs;

public abstract class PeriodicWorker(
    IServiceScopeFactory scopeFactory, ILogger logger, TimeSpan interval) : BackgroundService
{
    protected abstract Task RunOnceAsync(IServiceProvider services, CancellationToken cancellationToken);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("{Worker} started, running every {Interval}", GetType().Name, interval);
        using var timer = new PeriodicTimer(interval);

        try
        {
            do
            {
                try
                {
                    // Singleton worker, scoped DbContext: every run gets its own scope.
                    await using var scope = scopeFactory.CreateAsyncScope();
                    await RunOnceAsync(scope.ServiceProvider, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Never let an exception escape: it would stop the whole application.
                    logger.LogError(ex, "{Worker} run failed; it will retry on the next tick", GetType().Name);
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
    }
}
