namespace ResourceReservation.Application.Reservations;

public sealed record CreateReservationResult(ReservationResponse Reservation, bool IsReplay);
