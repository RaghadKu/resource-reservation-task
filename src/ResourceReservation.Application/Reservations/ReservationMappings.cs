using System.Linq.Expressions;

using ResourceReservation.Domain.Entities;

namespace ResourceReservation.Application.Reservations;

internal static class ReservationMappings
{
    // r.Resource.Name becomes a JOIN inside the query.
    public static readonly Expression<Func<Reservation, ReservationResponse>> ToResponse = r =>
        new ReservationResponse(
            r.Id, r.ResourceId, r.Resource.Name, r.UserId,
            r.StartTime, r.EndTime, r.Status,
            r.OfferExpiresAt, r.WaitlistEntryId,
            r.CreatedAt, r.UpdatedAt);
}