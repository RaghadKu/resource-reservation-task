namespace ResourceReservation.Application.Auth;
public sealed record AuthResponse(
    string AccessToken,
    DateTime ExpiresAt,
    Guid UserId,
    string Email,
    string FullName,
    IReadOnlyList<string> Roles);
