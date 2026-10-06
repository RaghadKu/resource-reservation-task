using ResourceReservation.Domain.Enums;

namespace ResourceReservation.Application.Waitlist;

public sealed record WaitlistEntryResponse(
    Guid Id,
    Guid ResourceId,
    Guid UserId,
    DateTime RequestedStartTime,
    DateTime RequestedEndTime,
    WaitlistStatus Status,
    int? Position,            // computed; only present while Status == Waiting
    DateTime CreatedAt,
    DateTime UpdatedAt);
