using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using ResourceReservation.Application.Abstractions;
using System.Security.Claims;
using System.Text;

namespace ResourceReservation.Infrastructure.Identity;
internal sealed class JwtTokenGenerator(IOptions<JwtOptions> options, IDateTimeProvider clock)
{
    public (string Token, DateTime ExpiresAt) Generate(ApplicationUser user, IEnumerable<string> roles)
    {
        var jwt = options.Value;
        var expiresAt = clock.UtcNow.AddMinutes(jwt.ExpiryMinutes);

        // Short claim names ("sub", "role") on purpose; the API reads them the same way.
        var claims = new List<Claim>
        {
            new("sub", user.Id.ToString()),
            new("email", user.Email!),
            new("name", user.FullName),
            new("jti", Guid.NewGuid().ToString())
        };
        claims.AddRange(roles.Select(role => new Claim("role", role)));

        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Issuer = jwt.Issuer,
            Audience = jwt.Audience,
            Expires = expiresAt,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
                SecurityAlgorithms.HmacSha256)
        };

        return (new JsonWebTokenHandler().CreateToken(descriptor), expiresAt);
    }
}
