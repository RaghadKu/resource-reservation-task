namespace ResourceReservation.Application.Auth;
public sealed record CurrentUserResponse(Guid UserId, bool IsAdmin);
