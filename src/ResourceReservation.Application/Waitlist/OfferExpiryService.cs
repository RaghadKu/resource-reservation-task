using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using ResourceReservation.Application.Abstractions;
using ResourceReservation.Domain.Entities;
using ResourceReservation.Domain.Enums;

namespace ResourceReservation.Application.Waitlist;

// Runs from a background worker: there is no HTTP request, so it must never use ICurrentUser.
internal sealed class OfferExpiryService(
    IApplicationDbContext db,
    IResourceLock resourceLock,
    IWaitlistProcessor processor,
    IDateTimeProvider clock,
    ILogger<OfferExpiryService> logger) : IOfferExpiryService
{
    public async Task ExpireOverdueOffersAsync(CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;

        // Cheap unlocked scan: which resources have something to do? (Re-checked under the lock below.)
        var overdueOfferResources = db.Reservations.AsNoTracking()
            .Where(r => r.Status == ReservationStatus.Pending && r.OfferExpiresAt <= now)
            .Select(r => r.ResourceId);

        var staleEntryResources = db.WaitlistEntries.AsNoTracking()
            .Where(WaitlistEntry.WaitingAndStarted(now))
            .Select(e => e.ResourceId);

        var resourceIds = await overdueOfferResources.Union(staleEntryResources).ToListAsync(cancellationToken);

        foreach (var resourceId in resourceIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await ProcessResourceAsync(resourceId, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One failing resource must not block the others. The next sweep retries it.
                logger.LogError(ex, "Offer expiry failed for resource {ResourceId}", resourceId);
            }
        }
    }

    private async Task ProcessResourceAsync(Guid resourceId, CancellationToken cancellationToken)
    {
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

        // One lock per transaction, the same gate as booking, cancelling and confirming.
        if (await resourceLock.AcquireAsync(resourceId, cancellationToken) is null) return;

        var now = clock.UtcNow;

        // Re-read under the lock: a user may have confirmed or declined since the scan.
        var overdue = await db.Reservations
            .Where(r => r.ResourceId == resourceId
                        && r.Status == ReservationStatus.Pending && r.OfferExpiresAt <= now)
            .ToListAsync(cancellationToken);

        foreach (var offer in overdue)
        {
            offer.Expire(now);                                    // Pending -> Expired
            if (offer.WaitlistEntryId is { } entryId)
            {
                var entry = await db.WaitlistEntries.SingleOrDefaultAsync(e => e.Id == entryId, cancellationToken);
                entry?.MarkExpired(now);                          // Offered -> Expired (stays out of the queue)
            }
        }

        var stale = await db.WaitlistEntries
            .Where(e => e.ResourceId == resourceId)
            .Where(WaitlistEntry.WaitingAndStarted(now))
            .ToListAsync(cancellationToken);

        foreach (var entry in stale)
            entry.ExpireAsUnserved(now);

        // Pass each freed slot to the next person in line. The processor flushes the expirations first.
        foreach (var offer in overdue)
            await processor.ProcessAsync(resourceId, offer.StartTime, offer.EndTime, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        if (overdue.Count > 0 || stale.Count > 0)
            logger.LogInformation("Resource {ResourceId}: expired {Offers} offer(s) and {Entries} unserved entr(ies)",
                resourceId, overdue.Count, stale.Count);
    }
}
