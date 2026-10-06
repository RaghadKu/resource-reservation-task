using ResourceReservation.Domain.Common;

namespace ResourceReservation.Domain.Entities;

public class IdempotencyRecord : BaseEntity
{
    public Guid UserId { get; private set; }
    public string Key { get; private set; } = string.Empty;
    public string RequestHash { get; private set; } = string.Empty;
    public Guid? ReservationId { get; private set; }

    private IdempotencyRecord() { } // for EF Core

    public static IdempotencyRecord Start(Guid userId, string key, string requestHash, DateTime now) => new()
    {
        UserId = userId,
        Key = key,
        RequestHash = requestHash,
        CreatedAt = now,
        UpdatedAt = now
    };

    public void Complete(Guid reservationId, DateTime now)
    {
        ReservationId = reservationId;
        UpdatedAt = now;
    }
}
