namespace ResourceReservation.Infrastructure.Identity;
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public string SigningKey { get; set; } = string.Empty;   // HS256 needs at least 32 bytes
    public int ExpiryMinutes { get; set; } = 60;
}
