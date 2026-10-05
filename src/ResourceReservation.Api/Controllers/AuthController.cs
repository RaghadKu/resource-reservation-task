using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using ResourceReservation.Application.Abstractions;
using ResourceReservation.Application.Auth;

namespace ResourceReservation.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(IAuthService authService, ICurrentUser currentUser) : ControllerBase
{
    [HttpPost("register")]
    [AllowAnonymous]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest request, CancellationToken cancellationToken)
    {
        var response = await authService.RegisterAsync(request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, response);
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request, CancellationToken cancellationToken)
        => Ok(await authService.LoginAsync(request, cancellationToken));

    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType<CurrentUserResponse>(StatusCodes.Status200OK)]
    public ActionResult<CurrentUserResponse> Me()
        => Ok(new CurrentUserResponse(currentUser.UserId, currentUser.IsAdmin));
}
