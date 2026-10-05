using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using ResourceReservation.Application.Auth;
using ResourceReservation.Application.Common;
using ResourceReservation.Application.Common.Exceptions;
using ResourceReservation.Infrastructure.Persistence;

namespace ResourceReservation.Infrastructure.Identity;

internal sealed class AuthService(
    UserManager<ApplicationUser> userManager,
    AppDbContext db,
    JwtTokenGenerator tokenGenerator) : IAuthService
{
    public async Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        var email = request.Email.Trim();
        var user = new ApplicationUser
        {
            Id = Guid.CreateVersion7(),
            UserName = email,
            Email = email,
            FullName = request.FullName.Trim()
        };

        // Create + assign role must succeed or fail together.
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var created = await userManager.CreateAsync(user, request.Password);
            if (!created.Succeeded) throw ToException(created);

            var roleAdded = await userManager.AddToRoleAsync(user, Roles.User);
            if (!roleAdded.Succeeded) throw ToException(roleAdded);

            await tx.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Two simultaneous registrations with the same email: the unique index on UserName wins.
            throw new ConflictException("A user with this email already exists.");
        }

        return BuildResponse(user, [Roles.User]);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByEmailAsync(request.Email.Trim());

        // Same message for "unknown email" and "wrong password": don't reveal which emails exist.
        if (user is null || !await userManager.CheckPasswordAsync(user, request.Password))
            throw new UnauthorizedException("Invalid email or password.");

        var roles = await userManager.GetRolesAsync(user);
        return BuildResponse(user, roles.ToList());
    }

    private AuthResponse BuildResponse(ApplicationUser user, IReadOnlyList<string> roles)
    {
        var (token, expiresAt) = tokenGenerator.Generate(user, roles);
        return new AuthResponse(token, expiresAt, user.Id, user.Email!, user.FullName, roles);
    }

    private static Exception ToException(IdentityResult result)
    {
        if (result.Errors.Any(e => e.Code is nameof(IdentityErrorDescriber.DuplicateEmail)
                                           or nameof(IdentityErrorDescriber.DuplicateUserName)))
            return new ConflictException("A user with this email already exists.");

        var errors = result.Errors
            .GroupBy(e => e.Code)
            .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray());

        return new BadRequestException("Registration failed.", errors);
    }
}
