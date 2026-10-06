using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using ResourceReservation.Application.Common;
using ResourceReservation.Application.Resources;

namespace ResourceReservation.Api.Controllers;

/// <summary>
/// Manages bookable resources (rooms, desks, equipment) and exposes their availability.
/// </summary>
/// <remarks>
/// Any authenticated user can read resources. Creating, updating and deactivating requires the <c>Admin</c> role.
/// Resources are never physically deleted: <c>DELETE</c> deactivates them so reservation history stays intact.
/// </remarks>
[ApiController]
[Route("api/resources")]
[Authorize]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
public class ResourcesController(IResourceService resources) : ControllerBase
{
    /// <summary>
    /// Creates a resource. Admin only.
    /// </summary>
    /// <remarks>
    /// Rules: <c>name</c> is required, up to 200 characters and unique (case-insensitive);
    /// <c>capacity</c> is a number of people between 1 and 10000; <c>description</c> is optional, up to 1000 characters.
    ///
    /// Sample request:
    ///
    /// ```json
    /// {
    ///   "name": "Meeting Room A",
    ///   "description": "Ground floor, projector and whiteboard",
    ///   "capacity": 8
    /// }
    /// ```
    ///
    /// Sample response (201), with a <c>Location</c> header pointing to the new resource:
    ///
    /// ```json
    /// {
    ///   "id": "0199c1a5-1c44-7a02-8d3b-6e1f5a9c2b77",
    ///   "name": "Meeting Room A",
    ///   "description": "Ground floor, projector and whiteboard",
    ///   "capacity": 8,
    ///   "isActive": true,
    ///   "createdAt": "2026-10-06T12:00:00Z",
    ///   "updatedAt": "2026-10-06T12:00:00Z"
    /// }
    /// ```
    /// </remarks>
    /// <param name="request">The resource to create.</param>
    /// <response code="201">Resource created.</response>
    /// <response code="400">Invalid input: blank name, capacity out of range or text too long.</response>
    /// <response code="401">Missing, invalid or expired token.</response>
    /// <response code="403">The caller is not an administrator.</response>
    /// <response code="409">A resource with this name already exists.</response>
    /// <response code="500">Unexpected server error.</response>
    [HttpPost]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType<ResourceResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ResourceResponse>> Create(
        CreateResourceRequest request, CancellationToken cancellationToken)
    {
        var created = await resources.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    /// <summary>
    /// Lists resources with paging, filtering, searching and sorting.
    /// </summary>
    /// <remarks>
    /// Query parameters (all optional):
    ///
    /// | Parameter | Meaning | Default |
    /// |---|---|---|
    /// | <c>pageNumber</c> | 1-based page | 1 |
    /// | <c>pageSize</c> | Items per page, 1 to 100 | 20 |
    /// | <c>isActive</c> | <c>true</c> or <c>false</c> | all |
    /// | <c>search</c> | Text contained in the name (max 100 characters) | none |
    /// | <c>sortBy</c> | <c>Name</c>, <c>Capacity</c> or <c>CreatedAt</c> | <c>Name</c> |
    /// | <c>sortDirection</c> | <c>Asc</c> or <c>Desc</c> | <c>Asc</c> |
    ///
    /// A page size above 100 is rejected with 400, it is not silently reduced.
    ///
    /// Sample request:
    ///
    /// ```
    /// GET /api/resources?pageNumber=1&amp;pageSize=2&amp;isActive=true&amp;sortBy=Capacity&amp;sortDirection=Desc
    /// ```
    ///
    /// Sample response:
    ///
    /// ```json
    /// {
    ///   "items": [
    ///     { "id": "0199c1a5-1c44-7a02-8d3b-6e1f5a9c2b77", "name": "Meeting Room A", "description": null,
    ///       "capacity": 8, "isActive": true, "createdAt": "2026-10-06T12:00:00Z", "updatedAt": "2026-10-06T12:00:00Z" }
    ///   ],
    ///   "pageNumber": 1,
    ///   "pageSize": 2,
    ///   "totalCount": 5,
    ///   "totalPages": 3
    /// }
    /// ```
    /// </remarks>
    /// <response code="200">A page of resources (possibly empty).</response>
    /// <response code="400">Invalid paging, filter or sort values.</response>
    /// <response code="401">Missing, invalid or expired token.</response>
    /// <response code="500">Unexpected server error.</response>
    [HttpGet]
    [ProducesResponseType<PagedResult<ResourceResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<ResourceResponse>>> List(
        [FromQuery] ResourceQuery query, CancellationToken cancellationToken)
        => Ok(await resources.ListAsync(query, cancellationToken));

    /// <summary>
    /// Gets one resource by id.
    /// </summary>
    /// <remarks>
    /// Inactive resources can still be read. The response has the same shape as the creation response.
    /// </remarks>
    /// <param name="id">The resource id (GUID).</param>
    /// <response code="200">The resource.</response>
    /// <response code="401">Missing, invalid or expired token.</response>
    /// <response code="404">No resource with this id exists.</response>
    /// <response code="500">Unexpected server error.</response>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<ResourceResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ResourceResponse>> GetById(Guid id, CancellationToken cancellationToken)
        => Ok(await resources.GetAsync(id, cancellationToken));

    /// <summary>
    /// Replaces a resource's details and active flag. Admin only.
    /// </summary>
    /// <remarks>
    /// This is a full replacement: every field is required, including <c>isActive</c>.
    /// Send <c>isActive: true</c> to reactivate a deactivated resource.
    ///
    /// Sending <c>isActive: false</c> deactivates the resource and follows the same rules as <c>DELETE</c>:
    /// it is rejected with 409 while the resource has upcoming reservations (Pending or Confirmed),
    /// and waiting waitlist entries are cancelled.
    ///
    /// Sample request:
    ///
    /// ```json
    /// {
    ///   "name": "Meeting Room A",
    ///   "description": "Renovated, now with a video wall",
    ///   "capacity": 10,
    ///   "isActive": true
    /// }
    /// ```
    /// </remarks>
    /// <param name="id">The resource id (GUID).</param>
    /// <param name="request">The new values.</param>
    /// <response code="200">The updated resource.</response>
    /// <response code="400">Invalid input.</response>
    /// <response code="401">Missing, invalid or expired token.</response>
    /// <response code="403">The caller is not an administrator.</response>
    /// <response code="404">No resource with this id exists.</response>
    /// <response code="409">The new name is taken, or deactivation is blocked by upcoming reservations.</response>
    /// <response code="500">Unexpected server error.</response>
    [HttpPut("{id:guid}")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType<ResourceResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ResourceResponse>> Update(
        Guid id, UpdateResourceRequest request, CancellationToken cancellationToken)
        => Ok(await resources.UpdateAsync(id, request, cancellationToken));

    /// <summary>
    /// Deactivates a resource (soft delete). Admin only.
    /// </summary>
    /// <remarks>
    /// The row is kept so historical reservations remain valid. Deactivating:
    /// - is rejected with 409 if the resource has upcoming Pending or Confirmed reservations (cancel them first);
    /// - cancels all waiting waitlist entries of the resource;
    /// - is idempotent: deactivating an inactive resource also returns 204.
    ///
    /// Reactivate with <c>PUT</c> and <c>isActive: true</c>.
    /// </remarks>
    /// <param name="id">The resource id (GUID).</param>
    /// <response code="204">The resource is now inactive.</response>
    /// <response code="401">Missing, invalid or expired token.</response>
    /// <response code="403">The caller is not an administrator.</response>
    /// <response code="404">No resource with this id exists.</response>
    /// <response code="409">The resource has upcoming reservations.</response>
    /// <response code="500">Unexpected server error.</response>
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken)
    {
        await resources.DeactivateAsync(id, cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Shows when a resource is busy and when it is free within a time window.
    /// </summary>
    /// <remarks>
    /// Both lists contain half-open windows <c>[start, end)</c>, so 10:00-11:00 followed by 11:00-12:00 does not conflict.
    ///
    /// - <c>busyPeriods</c> are Pending and Confirmed reservations clamped to the window. They contain times only, never user data.
    /// - <c>freePeriods</c> are the gaps between busy periods, starting no earlier than now.
    ///   An inactive resource has no free periods.
    ///
    /// Constraints: <c>from</c> and <c>to</c> are required, <c>from</c> must be before <c>to</c>, and the window is at most 31 days.
    /// This is the only collection endpoint that is not paged, because it is a computed timeline bounded by the 31-day limit.
    /// Use <c>Z</c> for UTC. A <c>+</c> in an offset must be sent as <c>%2B</c> in the URL.
    ///
    /// Sample request:
    ///
    /// ```
    /// GET /api/resources/0199c1a5-1c44-7a02-8d3b-6e1f5a9c2b77/availability?from=2026-11-10T08:00:00Z&amp;to=2026-11-10T14:00:00Z
    /// ```
    ///
    /// Sample response:
    ///
    /// ```json
    /// {
    ///   "resourceId": "0199c1a5-1c44-7a02-8d3b-6e1f5a9c2b77",
    ///   "from": "2026-11-10T08:00:00Z",
    ///   "to": "2026-11-10T14:00:00Z",
    ///   "busyPeriods": [ { "start": "2026-11-10T10:00:00Z", "end": "2026-11-10T11:00:00Z" } ],
    ///   "freePeriods": [
    ///     { "start": "2026-11-10T08:00:00Z", "end": "2026-11-10T10:00:00Z" },
    ///     { "start": "2026-11-10T11:00:00Z", "end": "2026-11-10T14:00:00Z" }
    ///   ]
    /// }
    /// ```
    /// </remarks>
    /// <param name="resourceId">The resource id (GUID).</param>
    /// <param name="query">The time window to inspect.</param>
    /// <response code="200">Busy and free periods for the window.</response>
    /// <response code="400">Missing dates, <c>from</c> not before <c>to</c>, or a window longer than 31 days.</response>
    /// <response code="401">Missing, invalid or expired token.</response>
    /// <response code="404">No resource with this id exists.</response>
    /// <response code="500">Unexpected server error.</response>
    [HttpGet("{resourceId:guid}/availability")]
    [ProducesResponseType<AvailabilityResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AvailabilityResponse>> GetAvailability(
        Guid resourceId, [FromQuery] AvailabilityQuery query, CancellationToken cancellationToken)
        => Ok(await resources.GetAvailabilityAsync(resourceId, query, cancellationToken));
}
