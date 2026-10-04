namespace ResourceReservation.Domain.Common;

public abstract class BaseEntity
{
    public Guid Id { get; protected set; } = Guid.CreateVersion7();
    public DateTime CreatedAt { get; protected set; }
    public DateTime UpdatedAt { get; protected set; }
}
