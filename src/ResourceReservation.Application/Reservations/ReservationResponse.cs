using ResourceReservation.Domain.Enums;

namespace ResourceReservation.Application.Reservations;

public sealed record ReservationResponse(
    Guid Id,
    Guid ResourceId,
    string ResourceName,
    Guid UserId,
    DateTime StartTime,
    DateTime EndTime,
    ReservationStatus Status,
    DateTime? OfferExpiresAt,
    Guid? WaitlistEntryId,
    DateTime CreatedAt,
    DateTime UpdatedAt);
