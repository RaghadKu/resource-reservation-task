namespace ResourceReservation.Application.Abstractions;
public interface ICurrentUser
{
    Guid UserId { get; }
    bool IsAdmin { get; }
}
