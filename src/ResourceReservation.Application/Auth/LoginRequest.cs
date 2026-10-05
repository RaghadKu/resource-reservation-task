using System.ComponentModel.DataAnnotations;

namespace ResourceReservation.Application.Auth;
public sealed record LoginRequest(
    [Required, EmailAddress] string Email,
    [Required] string Password);
