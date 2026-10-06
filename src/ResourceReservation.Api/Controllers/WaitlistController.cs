using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using ResourceReservation.Application.Common;
using ResourceReservation.Application.Waitlist;

namespace ResourceReservation.Api.Controllers;

// Create and list are nested under the resource. Delete is flat: the id alone identifies the entry.
[ApiController]
[Route("api")]
[Authorize]
public class WaitlistController(IWaitlistService waitlist) : ControllerBase
{
    [HttpPost("resources/{resourceId:guid}/waitlist")]
    [ProducesResponseType<WaitlistEntryResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<WaitlistEntryResponse>> Join(
        Guid resourceId, JoinWaitlistRequest request, CancellationToken cancellationToken)
    {
        var created = await waitlist.JoinAsync(resourceId, request, cancellationToken);
        return Created($"/api/resources/{resourceId}/waitlist", created);
    }

    [HttpGet("resources/{resourceId:guid}/waitlist")]
    public async Task<ActionResult<PagedResult<WaitlistEntryResponse>>> List(
        Guid resourceId, [FromQuery] WaitlistQuery query, CancellationToken cancellationToken)
        => Ok(await waitlist.ListAsync(resourceId, query, cancellationToken));

    [HttpDelete("waitlist/{id:guid}")]
    public async Task<IActionResult> Leave(Guid id, CancellationToken cancellationToken)
    {
        await waitlist.LeaveAsync(id, cancellationToken);
        return NoContent();
    }
}
