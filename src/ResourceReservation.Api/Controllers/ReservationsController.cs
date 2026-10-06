using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using ResourceReservation.Application.Common;
using ResourceReservation.Application.Reservations;

namespace ResourceReservation.Api.Controllers;

[ApiController]
[Route("api/reservations")]
[Authorize]
public class ReservationsController(IReservationService reservations) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<ReservationResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<ReservationResponse>> Create(
        CreateReservationRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,   // nullable: the service returns the 400
        CancellationToken cancellationToken)
    {
        var result = await reservations.CreateAsync(request, idempotencyKey, cancellationToken);

        if (result.IsReplay)
            Response.Headers["Idempotent-Replayed"] = "true";

        return CreatedAtAction(nameof(GetById), new { id = result.Reservation.Id }, result.Reservation);
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<ReservationResponse>>> List(
        [FromQuery] ReservationQuery query, CancellationToken cancellationToken)
        => Ok(await reservations.ListAsync(query, cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ReservationResponse>> GetById(Guid id, CancellationToken cancellationToken)
        => Ok(await reservations.GetAsync(id, cancellationToken));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken cancellationToken)
    {
        await reservations.CancelAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/confirm")]
    public async Task<ActionResult<ReservationResponse>> Confirm(Guid id, CancellationToken cancellationToken)
    => Ok(await reservations.ConfirmAsync(id, cancellationToken));
}
