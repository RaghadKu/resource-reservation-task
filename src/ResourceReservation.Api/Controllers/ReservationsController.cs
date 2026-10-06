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
        CreateReservationRequest request, CancellationToken cancellationToken)
    {
        var created = await reservations.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<ReservationResponse>>> List(
        [FromQuery] ReservationQuery query, CancellationToken cancellationToken)
        => Ok(await reservations.ListAsync(query, cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ReservationResponse>> GetById(Guid id, CancellationToken cancellationToken)
        => Ok(await reservations.GetAsync(id, cancellationToken));
}
