namespace ResourceReservation.Application.Resources;

public sealed record ResourceResponse(
    Guid Id,
    string Name,
    string? Description,
    int Capacity,
    bool IsActive,
    DateTime CreatedAt,
    DateTime UpdatedAt);
