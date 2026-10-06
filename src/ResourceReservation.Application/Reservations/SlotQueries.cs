using Microsoft.EntityFrameworkCore;

using ResourceReservation.Application.Abstractions;
using ResourceReservation.Domain.Entities;

namespace ResourceReservation.Application.Reservations;

internal static class SlotQueries
{
    /// <summary>
    /// "Does a Pending/Confirmed reservation overlap [start, end) on this resource?"
    /// The only place this question is asked. Served by IX_Reservations_Resource_Blocking_Time.
    /// For a safe answer the caller must hold the resource lock.
    /// </summary>
    public static Task<bool> IsSlotBlockedAsync(
        this IApplicationDbContext db, Guid resourceId, DateTime start, DateTime end,
        CancellationToken cancellationToken)
        => db.Reservations
            .Where(r => r.ResourceId == resourceId)
            .Where(Reservation.BlocksSlotDuring(start, end))
            .AnyAsync(cancellationToken);
}
