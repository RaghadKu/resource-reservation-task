using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using ResourceReservation.Application.Idempotency;

namespace ResourceReservation.Infrastructure.BackgroundJobs;

public sealed class IdempotencyCleanupWorker(IServiceScopeFactory scopeFactory, ILogger<IdempotencyCleanupWorker> logger)
    : PeriodicWorker(scopeFactory, logger, TimeSpan.FromHours(1))
{
    protected override Task RunOnceAsync(IServiceProvider services, CancellationToken cancellationToken)
        => services.GetRequiredService<IIdempotencyCleanupService>().DeleteExpiredAsync(cancellationToken);
}
