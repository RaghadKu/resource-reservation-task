using Microsoft.EntityFrameworkCore;

using ResourceReservation.Application.Abstractions;
using ResourceReservation.Application.Common;
using ResourceReservation.Application.Common.Exceptions;
using ResourceReservation.Domain.Common;
using ResourceReservation.Domain.Entities;

namespace ResourceReservation.Application.Reservations;

public sealed class ReservationService(
    IApplicationDbContext db,
    IResourceLock resourceLock,
    IDateTimeProvider clock,
    ICurrentUser currentUser) : IReservationService
{
    public async Task<ReservationResponse> CreateAsync(CreateReservationRequest request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        var now = clock.UtcNow;

        // 1. Pure Domain validation (400): range order, future start, duration limits.
        //    Nothing is added to the context yet.
        var range = new TimeRange(request.StartTime!.Value.UtcDateTime, request.EndTime!.Value.UtcDateTime);
        var reservation = Reservation.CreateConfirmed(request.ResourceId!.Value, userId, range, now);

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

        // 2. THE GATE: lock the resource row. Concurrent bookings of this resource queue up here.
        //    Everything below runs with exclusive access to this resource's bookings.
        var resource = await resourceLock.AcquireAsync(reservation.ResourceId, cancellationToken)
            ?? throw new NotFoundException($"Resource '{reservation.ResourceId}' was not found.");   // 404

        if (!resource.IsActive)
            throw new ConflictException("The resource is not active.");                              // 409

        await EnsureUserIsUnderLimitAsync(userId, now, cancellationToken);                           // 409

        if (await db.IsSlotBlockedAsync(resource.Id, range.Start, range.End, cancellationToken))     // 409
            throw new ConflictException(
                "The requested time slot is not available. " +
                $"You can join the waitlist: POST /api/resources/{resource.Id}/waitlist.");

        db.Reservations.Add(reservation);
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);   // releases the lock

        return await LoadResponseAsync(reservation.Id, cancellationToken)
            ?? throw new InvalidOperationException("Reservation vanished after commit.");
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
}
