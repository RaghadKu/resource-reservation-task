using ResourceReservation.Application.Abstractions;
using ResourceReservation.Application.Common.Exceptions;
using ResourceReservation.Application.Common;
using System.Security.Claims;

namespace ResourceReservation.Api.Services;

public sealed class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public Guid UserId =>
        Guid.TryParse(Principal?.FindFirstValue("sub"), out var id)
            ? id
            : throw new UnauthorizedException("Authentication is required.");

    public bool IsAdmin => Principal?.IsInRole(Roles.Admin) ?? false;
}
