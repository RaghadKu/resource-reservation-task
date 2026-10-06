using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using ResourceReservation.Application.Common;
using ResourceReservation.Application.Waitlist;

namespace ResourceReservation.Api.Controllers;

/// <summary>
/// First-in-first-out waiting queue for time slots that are already taken.
/// </summary>
/// <remarks>
/// Entry statuses: <c>Waiting</c> (in the queue), <c>Offered</c> (a slot is being held for the user),
/// <c>Fulfilled</c> (the user confirmed the offer), <c>Expired</c> (the offer timed out, or the requested period started)
/// and <c>Cancelled</c> (the user left, declined, or the resource was deactivated).
///
/// When a reservation is cancelled or an offer expires, the next waiting user whose whole requested range is free
/// receives a Pending reservation (the offer) and has a limited time (15 minutes by default) to confirm it with
/// <c>POST /api/reservations/{id}/confirm</c>. A user whose offer expires does not return to the queue.
///
/// Creating and listing are nested under the resource; removing an entry uses the entry id alone.
/// </remarks>
[ApiController]
[Route("api")]
[Authorize]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
public class WaitlistController(IWaitlistService waitlist) : ControllerBase
{
    /// <summary>
    /// Joins the waiting queue for a time slot that is currently unavailable.
    /// </summary>
    /// <remarks>
    /// The entry belongs to the authenticated user. Joining is only allowed when the requested range overlaps
    /// a Pending or Confirmed reservation; if the slot is free, reserve it directly instead (409).
    /// The time range follows the same rules as a reservation: start in the future, end after start,
    /// duration between 15 minutes and 12 hours.
    ///
    /// A user cannot join twice for the same resource and exact range while the first entry is Waiting or Offered.
    ///
    /// Sample request:
    ///
    /// ```json
    /// {
    ///   "startTime": "2026-11-10T10:00:00Z",
    ///   "endTime": "2026-11-10T11:00:00Z"
    /// }
    /// ```
    ///
    /// Sample response (201). <c>position</c> is 1 plus the number of earlier Waiting entries on the same resource
    /// whose range overlaps yours, so 1 means you are next in line:
    ///
    /// ```json
    /// {
    ///   "id": "0199c1b8-5f01-7a63-9e2c-d41b7a80c395",
    ///   "resourceId": "0199c1a5-1c44-7a02-8d3b-6e1f5a9c2b77",
    ///   "userId": "0199c1a9-3e72-7d08-8c14-b05a4d9e17f2",
    ///   "requestedStartTime": "2026-11-10T10:00:00Z",
    ///   "requestedEndTime": "2026-11-10T11:00:00Z",
    ///   "status": "Waiting",
    ///   "position": 1,
    ///   "createdAt": "2026-10-06T12:10:00Z",
    ///   "updatedAt": "2026-10-06T12:10:00Z"
    /// }
    /// ```
    /// </remarks>
    /// <param name="resourceId">The resource to wait for (GUID).</param>
    /// <param name="request">The requested time range.</param>
    /// <response code="201">Joined the queue. The body shows the entry and its current position.</response>
    /// <response code="400">Missing fields, start in the past, or duration outside 15 minutes to 12 hours.</response>
    /// <response code="401">Missing, invalid or expired token.</response>
    /// <response code="404">The resource does not exist.</response>
    /// <response code="409">The resource is inactive, the slot is actually available, or the user is already queued for this exact range.</response>
    /// <response code="500">Unexpected server error.</response>
    [HttpPost("resources/{resourceId:guid}/waitlist")]
    [ProducesResponseType<WaitlistEntryResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<WaitlistEntryResponse>> Join(
        Guid resourceId, JoinWaitlistRequest request, CancellationToken cancellationToken)
    {
        var created = await waitlist.JoinAsync(resourceId, request, cancellationToken);
        return Created($"/api/resources/{resourceId}/waitlist", created);
    }

    /// <summary>
    /// Lists the waitlist entries of a resource in queue order.
    /// </summary>
    /// <remarks>
    /// Regular users see only their own entries. Administrators see the whole queue.
    /// Entries are ordered first-in-first-out by creation time.
    ///
    /// Query parameters (all optional): <c>status</c> (<c>Waiting</c>, <c>Offered</c>, <c>Fulfilled</c>,
    /// <c>Expired</c> or <c>Cancelled</c>), <c>pageNumber</c> (default 1) and <c>pageSize</c> (default 20, max 100).
    ///
    /// <c>position</c> is only present while the entry is <c>Waiting</c>; otherwise it is <c>null</c>.
    ///
    /// Sample request:
    ///
    /// ```
    /// GET /api/resources/0199c1a5-1c44-7a02-8d3b-6e1f5a9c2b77/waitlist?status=Waiting&amp;pageSize=10
    /// ```
    ///
    /// The response uses the standard paged shape, where each item has the shape of the join response.
    /// </remarks>
    /// <param name="resourceId">The resource id (GUID).</param>
    /// <param name="query">Paging and status filter.</param>
    /// <response code="200">A page of waitlist entries (possibly empty).</response>
    /// <response code="400">Invalid paging or status values.</response>
    /// <response code="401">Missing, invalid or expired token.</response>
    /// <response code="404">The resource does not exist.</response>
    /// <response code="500">Unexpected server error.</response>
    [HttpGet("resources/{resourceId:guid}/waitlist")]
    [ProducesResponseType<PagedResult<WaitlistEntryResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PagedResult<WaitlistEntryResponse>>> List(
        Guid resourceId, [FromQuery] WaitlistQuery query, CancellationToken cancellationToken)
        => Ok(await waitlist.ListAsync(resourceId, query, cancellationToken));

    /// <summary>
    /// Leaves the waiting queue.
    /// </summary>
    /// <remarks>
    /// Allowed for the owner or an administrator.
    ///
    /// - A <c>Waiting</c> entry becomes <c>Cancelled</c>; users behind it move up.
    /// - An <c>Offered</c> entry counts as declining the offer: the held Pending reservation is cancelled too,
    ///   and the freed slot is offered to the next eligible user.
    /// - An already <c>Cancelled</c> entry returns 204 again (safe to repeat).
    /// - A <c>Fulfilled</c> or <c>Expired</c> entry cannot be removed (409). To release a confirmed slot, cancel the reservation instead.
    /// </remarks>
    /// <param name="id">The waitlist entry id (GUID).</param>
    /// <response code="204">The entry is cancelled (or already was).</response>
    /// <response code="401">Missing, invalid or expired token.</response>
    /// <response code="403">The entry belongs to another user and the caller is not an administrator.</response>
    /// <response code="404">No waitlist entry with this id exists.</response>
    /// <response code="409">The entry is already Fulfilled or Expired.</response>
    /// <response code="500">Unexpected server error.</response>
    [HttpDelete("waitlist/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Leave(Guid id, CancellationToken cancellationToken)
    {
        await waitlist.LeaveAsync(id, cancellationToken);
        return NoContent();
    }
}