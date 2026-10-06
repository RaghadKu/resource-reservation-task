using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using ResourceReservation.Application.Abstractions;
using ResourceReservation.Application.Reservations;
using ResourceReservation.Domain.Entities;

namespace ResourceReservation.Application.Waitlist;

internal sealed class WaitlistProcessor(
    IApplicationDbContext db,
    IDateTimeProvider clock,
    IOptions<WaitlistOptions> options,
    ILogger<WaitlistProcessor> logger) : IWaitlistProcessor
{
    public async Task ProcessAsync(
        Guid resourceId, DateTime freedStart, DateTime freedEnd, CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("The waitlist must be processed inside a transaction that holds the resource lock.");

        // Flush the caller's changes (e.g. the cancelled reservation) so the queries below see a free slot.
        await db.SaveChangesAsync(cancellationToken);

        var now = clock.UtcNow;
        var offerWindow = TimeSpan.FromMinutes(options.Value.OfferWindowMinutes);

        // Candidates: waiting entries overlapping the freed range. FIFO, Id as the deterministic tiebreaker.
        var candidates = await db.WaitlistEntries
            .Where(e => e.ResourceId == resourceId)
            .Where(WaitlistEntry.WaitingOverlapping(freedStart, freedEnd))
            .OrderBy(e => e.CreatedAt).ThenBy(e => e.Id)
            .ToListAsync(cancellationToken);

        foreach (var entry in candidates)
        {
            // Started already: can never be offered. The expiry sweep will retire it.
            if (entry.RequestedStartTime <= now) continue;

            // Whole requested range must be free (no partial fulfilment). Otherwise the entry keeps its place.
            if (await db.IsSlotBlockedAsync(resourceId, entry.RequestedStartTime, entry.RequestedEndTime, cancellationToken))
                continue;

            // Waiting -> Offered and a Pending reservation that holds the slot, created together.
            var offer = entry.CreateOffer(now, offerWindow);
            db.Reservations.Add(offer);

            // Save per offer so the next candidate's overlap check sees this hold.
            await db.SaveChangesAsync(cancellationToken);

            // Our "notification" (Step 2): visible in the logs and via GET /api/reservations?status=Pending.
            logger.LogInformation(
                "Offered {Start:o}-{End:o} on resource {ResourceId} to user {UserId} until {ExpiresAt:o}",
                offer.StartTime, offer.EndTime, resourceId, offer.UserId, offer.OfferExpiresAt);
        }
    }
}
