using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using ResourceReservation.Application.Abstractions;
using ResourceReservation.Application.Reservations;

namespace ResourceReservation.Application.Idempotency;

public interface IIdempotencyCleanupService
{
    Task<int> DeleteExpiredAsync(CancellationToken cancellationToken);
}

internal sealed class IdempotencyCleanupService(
    IApplicationDbContext db,
    IDateTimeProvider clock,
    ILogger<IdempotencyCleanupService> logger) : IIdempotencyCleanupService
{
    public async Task<int> DeleteExpiredAsync(CancellationToken cancellationToken)
    {
        var cutoff = clock.UtcNow - IdempotencyKeys.Retention;

        // One set-based DELETE, with no entities loaded. Served by IX_IdempotencyRecords_CreatedAt.
        var deleted = await db.IdempotencyRecords
            .Where(r => r.CreatedAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);

        if (deleted > 0)
            logger.LogInformation("Deleted {Count} expired idempotency record(s)", deleted);

        return deleted;
    }
}
