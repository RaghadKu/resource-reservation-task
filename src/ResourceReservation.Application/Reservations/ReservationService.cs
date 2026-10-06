using Microsoft.EntityFrameworkCore;

using ResourceReservation.Application.Abstractions;
using ResourceReservation.Application.Common;
using ResourceReservation.Application.Common.Exceptions;
using ResourceReservation.Application.Waitlist;
using ResourceReservation.Domain.Common;
using ResourceReservation.Domain.Entities;
using ResourceReservation.Domain.Enums;

namespace ResourceReservation.Application.Reservations;

public sealed class ReservationService(
    IApplicationDbContext db,
    IResourceLock resourceLock,
    IDateTimeProvider clock,
    ICurrentUser currentUser,
    IWaitlistProcessor waitlistProcessor) : IReservationService
{
    public async Task<CreateReservationResult> CreateAsync(
        CreateReservationRequest request, string? idempotencyKey, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        var key = IdempotencyKeys.Validate(idempotencyKey);                                   // 400

        var resourceId = request.ResourceId!.Value;
        var startUtc = request.StartTime!.Value.UtcDateTime;
        var endUtc = request.EndTime!.Value.UtcDateTime;
        var hash = IdempotencyKeys.Hash(resourceId, startUtc, endUtc);

        // 1. Replay check comes BEFORE domain validation: a retry sent after the slot's start time
        //    must get its original answer, not "start must be in the future".
        if (await FindRecordAsync(userId, key, cancellationToken) is { } seen)
            return await ReplayAsync(seen, hash, cancellationToken);

        // 2. Pure Domain validation (400). Nothing is added to the context yet.
        var now = clock.UtcNow;
        var range = new TimeRange(startUtc, endUtc);
        var reservation = Reservation.CreateConfirmed(resourceId, userId, range, now);

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

        // 3. Claim the key INSIDE the transaction, before the resource lock (lock order: key, then resource).
        //    A concurrent request with the same key waits right here until we commit or roll back.
        var record = IdempotencyRecord.Start(userId, key, hash, now);
        db.IdempotencyRecords.Add(record);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Duplicate key: another request committed first. Release our transaction and replay its result.
            await tx.RollbackAsync(cancellationToken);
            var winner = await FindRecordAsync(userId, key, cancellationToken);
            if (winner is null) throw;   // not a duplicate: a genuine database error
            return await ReplayAsync(winner, hash, cancellationToken);
        }

        // 4. THE GATE, then the checks. Unchanged from Step 10.
        var resource = await resourceLock.AcquireAsync(resourceId, cancellationToken)
            ?? throw new NotFoundException($"Resource '{resourceId}' was not found.");           // 404

        if (!resource.IsActive)
            throw new ConflictException("The resource is not active.");                          // 409

        await EnsureUserIsUnderLimitAsync(userId, now, cancellationToken);                       // 409

        if (await db.IsSlotBlockedAsync(resource.Id, range.Start, range.End, cancellationToken)) // 409
            throw new ConflictException(
                "The requested time slot is not available. " +
                $"You can join the waitlist: POST /api/resources/{resource.Id}/waitlist.");

        // 5. Reservation + completed key commit together, or neither does.
        db.Reservations.Add(reservation);
        record.Complete(reservation.Id, now);
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        var response = await LoadResponseAsync(reservation.Id, cancellationToken)
            ?? throw new InvalidOperationException("Reservation vanished after commit.");
        return new CreateReservationResult(response, IsReplay: false);
    }

    public async Task<ReservationResponse> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var reservation = await LoadResponseAsync(id, cancellationToken)
            ?? throw new NotFoundException($"Reservation '{id}' was not found.");

        if (reservation.UserId != currentUser.UserId && !currentUser.IsAdmin)
            throw new ForbiddenException("You do not have access to this reservation.");

        return reservation;
    }

    public async Task<PagedResult<ReservationResponse>> ListAsync(ReservationQuery query, CancellationToken cancellationToken)
    {
        if (query.From is { } f && query.To is { } t && f >= t)
            throw new BadRequestException("'from' must be before 'to'.");

        var reservations = db.Reservations.AsNoTracking();

        // Users only ever see their own reservations. Admins see all.
        if (!currentUser.IsAdmin)
        {
            var userId = currentUser.UserId;
            reservations = reservations.Where(r => r.UserId == userId);
        }

        if (query.ResourceId is { } resourceId)
            reservations = reservations.Where(r => r.ResourceId == resourceId);

        if (query.Status is { } status)
            reservations = reservations.Where(r => r.Status == status);

        if (query.From is { } from)
        {
            var fromUtc = from.UtcDateTime;
            reservations = reservations.Where(r => r.EndTime > fromUtc);    // ends after 'from'
        }

        if (query.To is { } to)
        {
            var toUtc = to.UtcDateTime;
            reservations = reservations.Where(r => r.StartTime < toUtc);    // starts before 'to'
        }

        return await reservations
            .OrderByDescending(r => r.StartTime)
            .ThenBy(r => r.Id)                       // unique tiebreaker for stable pages
            .Select(ReservationMappings.ToResponse)
            .ToPagedResultAsync(query, cancellationToken);
    }

    public async Task CancelAsync(Guid id, CancellationToken cancellationToken)
    {
        // 1. Untracked peek: learn the resource and the owner. Neither can change, so no lock is needed yet.
        var peek = await db.Reservations.AsNoTracking()
            .Where(r => r.Id == id)
            .Select(r => new { r.ResourceId, r.UserId })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException($"Reservation '{id}' was not found.");               // 404

        if (peek.UserId != currentUser.UserId && !currentUser.IsAdmin)
            throw new ForbiddenException("You cannot cancel another user's reservation.");       // 403

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

        // 2. THE GATE (same lock as booking). Cancel + waitlist processing become one atomic unit.
        _ = await resourceLock.AcquireAsync(peek.ResourceId, cancellationToken)
            ?? throw new NotFoundException($"Resource '{peek.ResourceId}' was not found.");

        // 3. Re-load TRACKED, now that we hold the lock: the state we act on can't change under us.
        var reservation = await db.Reservations.SingleOrDefaultAsync(r => r.Id == id, cancellationToken)
            ?? throw new NotFoundException($"Reservation '{id}' was not found.");

        var now = clock.UtcNow;
        var wasPendingOffer = reservation.Status == ReservationStatus.Pending;

        // false = already Cancelled/Expired -> idempotent 204. Throws 409 if it already started.
        var slotFreed = reservation.Cancel(now);

        // Declining an offer: keep the pair in sync (Reservation Pending->Cancelled, Entry Offered->Cancelled).
        if (slotFreed && wasPendingOffer && reservation.WaitlistEntryId is { } entryId)
            await CancelOfferedEntryAsync(entryId, now, cancellationToken);

        if (slotFreed)
        {
            // Same transaction, same lock: cancel + hand-over are one atomic unit.
            await waitlistProcessor.ProcessAsync(
                 reservation.ResourceId, reservation.StartTime, reservation.EndTime, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }

    private async Task CancelOfferedEntryAsync(Guid entryId, DateTime now, CancellationToken cancellationToken)
    {
        var entry = await db.WaitlistEntries.SingleOrDefaultAsync(e => e.Id == entryId, cancellationToken);
        entry?.Cancel(now);
    }

    public async Task<ReservationResponse> ConfirmAsync(Guid id, CancellationToken cancellationToken)
    {
        var peek = await db.Reservations.AsNoTracking()
            .Where(r => r.Id == id)
            .Select(r => new { r.ResourceId, r.UserId })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException($"Reservation '{id}' was not found.");                // 404

        // Owner only: an admin can cancel, but cannot accept an offer on someone's behalf.
        if (peek.UserId != currentUser.UserId)
            throw new ForbiddenException("Only the user who received the offer can confirm it.");  // 403

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

        // Confirm and cancel/expire touch the same rows, so confirm takes the same lock (Step 11 correction).
        _ = await resourceLock.AcquireAsync(peek.ResourceId, cancellationToken)
            ?? throw new NotFoundException($"Resource '{peek.ResourceId}' was not found.");

        var reservation = await db.Reservations.SingleOrDefaultAsync(r => r.Id == id, cancellationToken)
            ?? throw new NotFoundException($"Reservation '{id}' was not found.");

        var now = clock.UtcNow;
        reservation.Confirm(now);   // 409 if not Pending, or if now >= OfferExpiresAt (checked here, not only by the worker)

        if (reservation.WaitlistEntryId is { } entryId)
        {
            var entry = await db.WaitlistEntries.SingleOrDefaultAsync(e => e.Id == entryId, cancellationToken);
            entry?.MarkFulfilled(now);   // Offered -> Fulfilled, in the same SaveChanges
        }

        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        return await LoadResponseAsync(id, cancellationToken)
            ?? throw new InvalidOperationException("Reservation vanished after commit.");
    }

    // The caller holds the resource lock. A user's cap is soft (see trade-offs).
    private async Task EnsureUserIsUnderLimitAsync(Guid userId, DateTime now, CancellationToken cancellationToken)
    {
        var active = await db.Reservations
            .Where(r => r.UserId == userId)
            .Where(Reservation.UpcomingBlocking(now))
            .CountAsync(cancellationToken);

        if (active >= BookingRules.MaxActiveReservationsPerUser)
            throw new ConflictException(
                $"You already have {BookingRules.MaxActiveReservationsPerUser} upcoming reservations, which is the maximum.");
    }

    private Task<ReservationResponse?> LoadResponseAsync(Guid id, CancellationToken cancellationToken)
        => db.Reservations.AsNoTracking()
            .Where(r => r.Id == id)
            .Select(ReservationMappings.ToResponse)
            .SingleOrDefaultAsync(cancellationToken);

    private Task<IdempotencyRecord?> FindRecordAsync(Guid userId, string key, CancellationToken cancellationToken)
    => db.IdempotencyRecords.AsNoTracking()
        .SingleOrDefaultAsync(r => r.UserId == userId && r.Key == key, cancellationToken);

    private async Task<CreateReservationResult> ReplayAsync(
        IdempotencyRecord record, string requestHash, CancellationToken cancellationToken)
    {
        if (record.RequestHash != requestHash)
            throw new IdempotencyKeyReuseException(
                "This Idempotency-Key was already used with a different request.");              // 422

        // A committed record always has its reservation (they commit together).
        var reservationId = record.ReservationId
            ?? throw new InvalidOperationException("Idempotency record has no reservation.");

        var response = await LoadResponseAsync(reservationId, cancellationToken)
            ?? throw new InvalidOperationException("Idempotent reservation no longer exists.");

        return new CreateReservationResult(response, IsReplay: true);
    }
}
