using ResourceReservation.Application.Common;
using ResourceReservation.Domain.Enums;

namespace ResourceReservation.Application.Reservations;

public sealed class ReservationQuery : PaginationQuery
{
    public Guid? ResourceId { get; init; }
    public ReservationStatus? Status { get; init; }
    public DateTimeOffset? From { get; init; }   // reservations ending after this instant
    public DateTimeOffset? To { get; init; }     // reservations starting before this instant
}
