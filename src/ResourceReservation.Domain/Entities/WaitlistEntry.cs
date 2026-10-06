using System.Linq.Expressions;

using ResourceReservation.Domain.Common;
using ResourceReservation.Domain.Enums;
using ResourceReservation.Domain.Exceptions;

namespace ResourceReservation.Domain.Entities;

public class WaitlistEntry : BaseEntity
{
    public Guid ResourceId { get; private set; }
    public Resource Resource { get; private set; } = null!;
    public Guid UserId { get; private set; }               // FK to AspNetUsers, configured in Infrastructure
    public DateTime RequestedStartTime { get; private set; }
    public DateTime RequestedEndTime { get; private set; }
    public WaitlistStatus Status { get; private set; }

    public TimeRange RequestedRange => new(RequestedStartTime, RequestedEndTime);

    private WaitlistEntry() { }

    public static WaitlistEntry Create(Guid resourceId, Guid userId, TimeRange range, DateTime now)
    {
        BookingRules.EnsureBookable(userId, range, now);
        return new WaitlistEntry
        {
            ResourceId = resourceId,
            UserId = userId,
            RequestedStartTime = range.Start,
            RequestedEndTime = range.End,
            Status = WaitlistStatus.Waiting,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    /// <summary>Waiting → Offered, and returns the Pending reservation that holds the slot.</summary>
    public Reservation CreateOffer(DateTime now, TimeSpan offerWindow)
    {
        if (Status != WaitlistStatus.Waiting)
            throw new InvalidStateTransitionException($"Only waiting entries can be offered a slot (current status: {Status}).");
        if (RequestedStartTime <= now)
            throw new InvalidStateTransitionException("The requested period has already started.");

        var expiresAt = now + offerWindow < RequestedStartTime ? now + offerWindow : RequestedStartTime;

        Status = WaitlistStatus.Offered;
        UpdatedAt = now;
        return Reservation.CreateOffer(this, expiresAt, now);
    }

    public void MarkFulfilled(DateTime now) => Transition(WaitlistStatus.Offered, WaitlistStatus.Fulfilled, now);
    public void MarkExpired(DateTime now) => Transition(WaitlistStatus.Offered, WaitlistStatus.Expired, now);

    /// <returns>true if the entry changed (false if it was already cancelled).</returns>
    public bool Cancel(DateTime now)
    {
        if (Status == WaitlistStatus.Cancelled) return false;
        if (Status is WaitlistStatus.Fulfilled or WaitlistStatus.Expired)
            throw new InvalidStateTransitionException($"A {Status} entry cannot be cancelled.");

        Status = WaitlistStatus.Cancelled;
        UpdatedAt = now;
        return true;
    }

    private void Transition(WaitlistStatus from, WaitlistStatus to, DateTime now)
    {
        if (Status != from)
            throw new InvalidStateTransitionException($"Cannot move a {Status} entry to {to}.");
        Status = to;
        UpdatedAt = now;
    }

    // Candidates for processing a freed range: waiting entries whose range overlaps it.
    public static Expression<Func<WaitlistEntry, bool>> WaitingOverlapping(DateTime start, DateTime end) =>
        e => e.Status == WaitlistStatus.Waiting
             && e.RequestedStartTime < end && start < e.RequestedEndTime;

    /// <summary>Waiting → Expired: the requested period started and nobody was offered the slot.</summary>
    public void ExpireAsUnserved(DateTime now) => Transition(WaitlistStatus.Waiting, WaitlistStatus.Expired, now);

    // Entries that can never be served any more.
    public static Expression<Func<WaitlistEntry, bool>> WaitingAndStarted(DateTime now) =>
        e => e.Status == WaitlistStatus.Waiting && e.RequestedStartTime <= now;
}
