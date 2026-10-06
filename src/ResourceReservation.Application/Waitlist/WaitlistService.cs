using Microsoft.EntityFrameworkCore;

using ResourceReservation.Application.Abstractions;
using ResourceReservation.Application.Common;
using ResourceReservation.Application.Common.Exceptions;
using ResourceReservation.Application.Reservations;
using ResourceReservation.Domain.Common;
using ResourceReservation.Domain.Entities;
using ResourceReservation.Domain.Enums;

namespace ResourceReservation.Application.Waitlist;

public sealed class WaitlistService(
    IApplicationDbContext db,
    IResourceLock resourceLock,
    IDateTimeProvider clock,
    ICurrentUser currentUser) : IWaitlistService
{
    public async Task<WaitlistEntryResponse> JoinAsync(
        Guid resourceId, JoinWaitlistRequest request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;

        // 1. Pure Domain validation (400). Nothing is added to the context yet.
        var range = new TimeRange(request.StartTime!.Value.UtcDateTime, request.EndTime!.Value.UtcDateTime);
        var entry = WaitlistEntry.Create(resourceId, userId, range, clock.UtcNow);

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

        // 2. THE GATE: same lock as booking and cancelling.
        var resource = await resourceLock.AcquireAsync(resourceId, cancellationToken)
            ?? throw new NotFoundException($"Resource '{resourceId}' was not found.");           // 404

        if (!resource.IsActive)
            throw new ConflictException("The resource is not active.");                          // 409

        // 3. Only unavailable slots have a queue.
        if (!await db.IsSlotBlockedAsync(resourceId, range.Start, range.End, cancellationToken))
            throw new ConflictException(
                "The requested slot is available. Reserve it directly: POST /api/reservations.");  // 409

        // 4. Duplicate: same user, resource and range already queued or offered.
        var duplicate = await db.WaitlistEntries.AnyAsync(e =>
            e.ResourceId == resourceId && e.UserId == userId
            && e.RequestedStartTime == range.Start && e.RequestedEndTime == range.End
            && (e.Status == WaitlistStatus.Waiting || e.Status == WaitlistStatus.Offered),
            cancellationToken);
        if (duplicate)
            throw new ConflictException("You are already on the waitlist for this period.");     // 409

        db.WaitlistEntries.Add(entry);
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        return await ProjectWithPosition(db.WaitlistEntries.AsNoTracking().Where(e => e.Id == entry.Id))
            .SingleAsync(cancellationToken);
    }

    public async Task<PagedResult<WaitlistEntryResponse>> ListAsync(
        Guid resourceId, WaitlistQuery query, CancellationToken cancellationToken)
    {
        if (!await db.Resources.AnyAsync(r => r.Id == resourceId, cancellationToken))
            throw new NotFoundException($"Resource '{resourceId}' was not found.");

        var entries = db.WaitlistEntries.AsNoTracking().Where(e => e.ResourceId == resourceId);

        // Users see only their own entries. Admins see the whole queue.
        if (!currentUser.IsAdmin)
        {
            var userId = currentUser.UserId;
            entries = entries.Where(e => e.UserId == userId);
        }

        if (query.Status is { } status)
            entries = entries.Where(e => e.Status == status);

        // FIFO, with Id as the deterministic tiebreaker.
        var ordered = entries.OrderBy(e => e.CreatedAt).ThenBy(e => e.Id);

        return await ProjectWithPosition(ordered).ToPagedResultAsync(query, cancellationToken);
    }

    public async Task LeaveAsync(Guid id, CancellationToken cancellationToken)
    {
        // Untracked peek: learn the resource and the owner (neither can change).
        var peek = await db.WaitlistEntries.AsNoTracking()
            .Where(e => e.Id == id)
            .Select(e => new { e.ResourceId, e.UserId })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException($"Waitlist entry '{id}' was not found.");             // 404

        if (peek.UserId != currentUser.UserId && !currentUser.IsAdmin)
            throw new ForbiddenException("You cannot remove another user's waitlist entry.");    // 403

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

        _ = await resourceLock.AcquireAsync(peek.ResourceId, cancellationToken)
            ?? throw new NotFoundException($"Resource '{peek.ResourceId}' was not found.");

        // Re-load tracked, now that we hold the lock.
        var entry = await db.WaitlistEntries.SingleOrDefaultAsync(e => e.Id == id, cancellationToken)
            ?? throw new NotFoundException($"Waitlist entry '{id}' was not found.");

        var now = clock.UtcNow;
        var slotFreed = false;

        // Leaving while Offered = declining the offer: release the held slot as well.
        if (entry.Status == WaitlistStatus.Offered)
        {
            var offer = await db.Reservations.SingleOrDefaultAsync(
                r => r.WaitlistEntryId == id && r.Status == ReservationStatus.Pending, cancellationToken);
            slotFreed = offer?.Cancel(now) ?? false;
        }

        // false = already Cancelled (idempotent 204). Throws 409 for Fulfilled/Expired.
        entry.Cancel(now);

        if (slotFreed)
        {
            // >>> STEP 13 HOOK: process the waitlist for the released range, inside this transaction.
        }

        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }

    // Position = 1 + earlier WAITING entries on the same resource with an overlapping range.
    // Only meaningful while Waiting, so it is null otherwise.
    private IQueryable<WaitlistEntryResponse> ProjectWithPosition(IQueryable<WaitlistEntry> source)
    {
        var all = db.WaitlistEntries.AsNoTracking();

        return source.Select(e => new WaitlistEntryResponse(
            e.Id, e.ResourceId, e.UserId,
            e.RequestedStartTime, e.RequestedEndTime, e.Status,
            e.Status == WaitlistStatus.Waiting
                ? 1 + all.Count(o =>
                    o.ResourceId == e.ResourceId
                    && o.Status == WaitlistStatus.Waiting
                    && o.RequestedStartTime < e.RequestedEndTime
                    && e.RequestedStartTime < o.RequestedEndTime
                    && (o.CreatedAt < e.CreatedAt
                        || (o.CreatedAt == e.CreatedAt && o.Id.CompareTo(e.Id) < 0)))
                : (int?)null,
            e.CreatedAt, e.UpdatedAt));
    }
}
