using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using ResourceReservation.Application.Abstractions;
using ResourceReservation.Application.Auth;

namespace ResourceReservation.Api.Controllers;

/// <summary>
/// Account registration, login and identity inspection.
/// </summary>
/// <remarks>
/// Authentication uses a JWT bearer access token (60 minutes by default, see <c>Jwt:ExpiryMinutes</c>).
/// Send it on every protected call as <c>Authorization: Bearer {accessToken}</c>.
/// There are two roles: <c>User</c> (everyone who registers) and <c>Admin</c> (seeded from configuration only).
/// </remarks>
[ApiController]
[Route("api/auth")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
public class AuthController(IAuthService authService, ICurrentUser currentUser) : ControllerBase
{
    /// <summary>
    /// Creates a new user account and returns an access token.
    /// </summary>
    /// <remarks>
    /// The new account always gets the <c>User</c> role. Administrators cannot be created here.
    ///
    /// Rules:
    /// - <c>email</c>: valid address, up to 256 characters, must not be registered yet.
    /// - <c>password</c>: 8 to 100 characters with at least one digit, one lowercase and one uppercase letter.
    /// - <c>fullName</c>: 2 to 200 characters.
    ///
    /// Sample request:
    ///
    /// ```json
    /// {
    ///   "email": "alice@example.com",
    ///   "password": "Passw0rd!",
    ///   "fullName": "Alice Johnson"
    /// }
    /// ```
    ///
    /// Sample response (201):
    ///
    /// ```json
    /// {
    ///   "accessToken": "eyJhbGciOiJIUzI1NiJ9...",
    ///   "expiresAt": "2026-10-06T15:30:00Z",
    ///   "userId": "0199c1a2-7b3e-7c11-9a55-3f0d2e8b6a10",
    ///   "email": "alice@example.com",
    ///   "fullName": "Alice Johnson",
    ///   "roles": [ "User" ]
    /// }
    /// ```
    /// </remarks>
    /// <param name="request">Registration details.</param>
    /// <response code="201">Account created. The body contains the access token.</response>
    /// <response code="400">Invalid input: malformed email, weak password or missing fields.</response>
    /// <response code="409">An account with this email already exists.</response>
    /// <response code="500">Unexpected server error.</response>
    [HttpPost("register")]
    [AllowAnonymous]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest request, CancellationToken cancellationToken)
    {
        var response = await authService.RegisterAsync(request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, response);
    }

    /// <summary>
    /// Signs in with email and password and returns an access token.
    /// </summary>
    /// <remarks>
    /// An unknown email and a wrong password return the same 401 message, so the endpoint does not reveal which emails exist.
    ///
    /// Sample request:
    ///
    /// ```json
    /// {
    ///   "email": "alice@example.com",
    ///   "password": "Passw0rd!"
    /// }
    /// ```
    ///
    /// The seeded development administrator is <c>admin@example.com</c> / <c>Admin#12345</c>.
    /// The response has the same shape as the registration response.
    /// </remarks>
    /// <param name="request">Login credentials.</param>
    /// <response code="200">Signed in. The body contains the access token and the user's roles.</response>
    /// <response code="400">Malformed request body (missing or invalid fields).</response>
    /// <response code="401">Invalid email or password.</response>
    /// <response code="500">Unexpected server error.</response>
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request, CancellationToken cancellationToken)
        => Ok(await authService.LoginAsync(request, cancellationToken));

    /// <summary>
    /// Returns the identity the current token belongs to.
    /// </summary>
    /// <remarks>
    /// Useful to check that a token works and which role it carries.
    ///
    /// Sample response:
    ///
    /// ```json
    /// {
    ///   "userId": "0199c1a2-7b3e-7c11-9a55-3f0d2e8b6a10",
    ///   "isAdmin": false
    /// }
    /// ```
    /// </remarks>
    /// <response code="200">The authenticated user's id and admin flag.</response>
    /// <response code="401">Missing, invalid or expired token.</response>
    /// <response code="500">Unexpected server error.</response>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType<CurrentUserResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public ActionResult<CurrentUserResponse> Me()
        => Ok(new CurrentUserResponse(currentUser.UserId, currentUser.IsAdmin));
}
