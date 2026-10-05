using System.ComponentModel.DataAnnotations;

namespace ResourceReservation.Application.Auth;
public sealed record RegisterRequest(
    [Required, EmailAddress, StringLength(256)] string Email,
    [Required, StringLength(100, MinimumLength = 8)] string Password,
    [Required, StringLength(200, MinimumLength = 2)] string FullName);
