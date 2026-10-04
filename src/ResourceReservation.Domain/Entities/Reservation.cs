using System.Linq.Expressions;

using ResourceReservation.Domain.Common;
using ResourceReservation.Domain.Enums;
using ResourceReservation.Domain.Exceptions;

namespace ResourceReservation.Domain.Entities;

public class Reservation : BaseEntity
{
    public Guid ResourceId { get; private set; }
    public Resource Resource { get; private set; } = null!;

    public Guid UserId { get; private set; }

    public DateTime StartTime { get; private set; }
    public DateTime EndTime { get; private set; }

    public ReservationStatus Status { get; private set; }
    public DateTime? OfferExpiresAt { get; private set; }
    
    public Guid? WaitlistEntryId { get; private set; }

    public TimeRange Range => new(StartTime, EndTime);
    public bool BlocksSlot => Status is ReservationStatus.Pending or ReservationStatus.Confirmed;

    private Reservation() { } 

    public static Reservation CreateConfirmed(Guid resourceId, Guid userId, TimeRange range, DateTime now)
    {
        BookingRules.EnsureBookable(userId, range, now);
        return new Reservation
        {
            ResourceId = resourceId,
            UserId = userId,
            StartTime = range.Start,
            EndTime = range.End,
            Status = ReservationStatus.Confirmed,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    internal static Reservation CreateOffer(WaitlistEntry entry, DateTime offerExpiresAt, DateTime now) => new()
    {
        ResourceId = entry.ResourceId,
        UserId = entry.UserId,
        StartTime = entry.RequestedStartTime,
        EndTime = entry.RequestedEndTime,
        Status = ReservationStatus.Pending,
        OfferExpiresAt = offerExpiresAt,
        WaitlistEntryId = entry.Id,
        CreatedAt = now,
        UpdatedAt = now
    };

    public void Confirm(DateTime now)
    {
        if (Status != ReservationStatus.Pending)
            throw new InvalidStateTransitionException($"Only pending offers can be confirmed (current status: {Status}).");
        if (now >= OfferExpiresAt)
            throw new InvalidStateTransitionException("The offer has expired.");

        Status = ReservationStatus.Confirmed;
        UpdatedAt = now;
    }

    /// <returns>true if a slot was actually freed (so the caller should process the waitlist).</returns>
    public bool Cancel(DateTime now)
    {
        if (!BlocksSlot) return false; // already Cancelled/Expired: idempotent no-op
        if (now >= StartTime)
            throw new InvalidStateTransitionException("The reservation has already started and cannot be cancelled.");

        Status = ReservationStatus.Cancelled;
        UpdatedAt = now;
        return true;
    }

    public void Expire(DateTime now)
    {
        if (Status != ReservationStatus.Pending)
            throw new InvalidStateTransitionException($"Only pending offers can expire (current status: {Status}).");
        if (now < OfferExpiresAt)
            throw new InvalidStateTransitionException("The offer has not expired yet.");

        Status = ReservationStatus.Expired;
        UpdatedAt = now;
    }

    // Query form of "does this reservation block [start, end)?".
    // EF Core can't translate Overlaps() into SQL, so the rule also exists as an expression.
    // Keep it in sync with TimeRange.Overlaps (same half-open formula).
    public static Expression<Func<Reservation, bool>> BlocksSlotDuring(DateTime start, DateTime end) =>
        r => (r.Status == ReservationStatus.Pending || r.Status == ReservationStatus.Confirmed)
             && r.StartTime < end && start < r.EndTime;
}
