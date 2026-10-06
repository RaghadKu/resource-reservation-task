using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using ResourceReservation.Application.Common;
using ResourceReservation.Application.Reservations;

namespace ResourceReservation.Api.Controllers;

/// <summary>
/// Creates, reads, confirms and cancels reservations.
/// </summary>
/// <remarks>
/// A reservation has one of four statuses:
/// - <c>Confirmed</c>: a firm booking.
/// - <c>Pending</c>: an offer from the waitlist, held for the user until <c>offerExpiresAt</c>.
/// - <c>Cancelled</c>: cancelled by the owner or an admin, or an offer that was declined.
/// - <c>Expired</c>: an offer that was not confirmed in time.
///
/// Pending and Confirmed reservations block their time slot. All times are UTC and intervals are half-open
/// <c>[startTime, endTime)</c>, so back-to-back bookings do not conflict.
/// </remarks>
[ApiController]
[Route("api/reservations")]
[Authorize]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
public class ReservationsController(IReservationService reservations) : ControllerBase
{
    /// <summary>
    /// Reserves a resource for a time range. Requires an <c>Idempotency-Key</c> header.
    /// </summary>
    /// <remarks>
    /// The reservation always belongs to the authenticated user; the user id is never sent in the body.
    ///
    /// Validation, in this order:
    /// 1. The <c>Idempotency-Key</c> header is present and well formed.
    /// 2. The time range is valid: start in the future, end after start, duration between 15 minutes and 12 hours.
    /// 3. The resource exists (404) and is active (409).
    /// 4. The user has fewer than 10 upcoming reservations (409).
    /// 5. The slot does not overlap a Pending or Confirmed reservation (409).
    ///
    /// Concurrency: simultaneous requests for the same resource are serialized by a row lock,
    /// so exactly one of them can win a contested slot.
    ///
    /// Idempotency: the key must be 8 to 100 characters (letters, digits, <c>. _ : -</c>) and is scoped to the user.
    /// - Same key and same request: the original reservation is returned with status 201 and the header
    ///   <c>Idempotent-Replayed: true</c>. No second reservation is created.
    /// - Same key and a different request: 422.
    /// - Failed requests are not remembered, so the same key can be retried after a 409 or 404.
    /// - Keys are kept for 24 hours.
    ///
    /// Sample request:
    ///
    /// ```
    /// POST /api/reservations
    /// Idempotency-Key: 5f1c7a52-9a1d-4c1e-8f7e-2b6a0d9e41aa
    /// ```
    ///
    /// ```json
    /// {
    ///   "resourceId": "0199c1a5-1c44-7a02-8d3b-6e1f5a9c2b77",
    ///   "startTime": "2026-11-10T10:00:00Z",
    ///   "endTime": "2026-11-10T11:00:00Z"
    /// }
    /// ```
    ///
    /// Sample response (201), with a <c>Location</c> header:
    ///
    /// ```json
    /// {
    ///   "id": "0199c1b0-4d2a-7e55-b1c3-9a7f60e2d814",
    ///   "resourceId": "0199c1a5-1c44-7a02-8d3b-6e1f5a9c2b77",
    ///   "resourceName": "Meeting Room A",
    ///   "userId": "0199c1a2-7b3e-7c11-9a55-3f0d2e8b6a10",
    ///   "startTime": "2026-11-10T10:00:00Z",
    ///   "endTime": "2026-11-10T11:00:00Z",
    ///   "status": "Confirmed",
    ///   "offerExpiresAt": null,
    ///   "waitlistEntryId": null,
    ///   "createdAt": "2026-10-06T12:05:00Z",
    ///   "updatedAt": "2026-10-06T12:05:00Z"
    /// }
    /// ```
    ///
    /// If the slot is taken, the 409 message points to the waitlist endpoint.
    /// </remarks>
    /// <param name="request">Resource and time range to reserve.</param>
    /// <param name="idempotencyKey">Unique key (8-100 characters) that makes retries safe.</param>
    /// <response code="201">Reservation created, or the original reservation replayed for a repeated key.</response>
    /// <response code="400">Missing or malformed idempotency key, missing fields, start in the past, or duration outside 15 minutes to 12 hours.</response>
    /// <response code="401">Missing, invalid or expired token.</response>
    /// <response code="404">The resource does not exist.</response>
    /// <response code="409">The resource is inactive, the user has 10 upcoming reservations, or the slot is already taken.</response>
    /// <response code="422">The idempotency key was already used with a different request.</response>
    /// <response code="500">Unexpected server error.</response>
    [HttpPost]
    [ProducesResponseType<ReservationResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
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

    /// <summary>
    /// Lists reservations with paging and filters.
    /// </summary>
    /// <remarks>
    /// Regular users see only their own reservations. Administrators see everyone's.
    /// Results are ordered by start time, newest first.
    ///
    /// Query parameters (all optional):
    ///
    /// | Parameter | Meaning | Default |
    /// |---|---|---|
    /// | <c>pageNumber</c> | 1-based page | 1 |
    /// | <c>pageSize</c> | Items per page, 1 to 100 | 20 |
    /// | <c>resourceId</c> | Only this resource | all |
    /// | <c>status</c> | <c>Pending</c>, <c>Confirmed</c>, <c>Cancelled</c> or <c>Expired</c> | all |
    /// | <c>from</c> | Reservations ending after this instant | none |
    /// | <c>to</c> | Reservations starting before this instant | none |
    ///
    /// <c>from</c> must be before <c>to</c> when both are given. Use <c>Z</c> for UTC;
    /// a <c>+</c> in an offset must be sent as <c>%2B</c>.
    ///
    /// Tip: <c>?status=Pending</c> lists your waitlist offers waiting for confirmation.
    ///
    /// Sample request:
    ///
    /// ```
    /// GET /api/reservations?status=Confirmed&amp;from=2026-11-01T00:00:00Z&amp;pageSize=10
    /// ```
    ///
    /// The response uses the standard paged shape (<c>items</c>, <c>pageNumber</c>, <c>pageSize</c>,
    /// <c>totalCount</c>, <c>totalPages</c>), where each item has the shape of the creation response.
    /// </remarks>
    /// <response code="200">A page of reservations (possibly empty).</response>
    /// <response code="400">Invalid paging or filter values, or <c>from</c> not before <c>to</c>.</response>
    /// <response code="401">Missing, invalid or expired token.</response>
    /// <response code="500">Unexpected server error.</response>
    [HttpGet]
    [ProducesResponseType<PagedResult<ReservationResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<ReservationResponse>>> List(
        [FromQuery] ReservationQuery query, CancellationToken cancellationToken)
        => Ok(await reservations.ListAsync(query, cancellationToken));

    /// <summary>
    /// Gets one reservation by id.
    /// </summary>
    /// <remarks>
    /// Only the owner or an administrator may read a reservation.
    /// For a waitlist offer, <c>status</c> is <c>Pending</c> and <c>offerExpiresAt</c> shows the confirmation deadline:
    ///
    /// ```json
    /// {
    ///   "id": "0199c1c3-8a10-7b4e-a2d9-51c8e7f30b66",
    ///   "resourceId": "0199c1a5-1c44-7a02-8d3b-6e1f5a9c2b77",
    ///   "resourceName": "Meeting Room A",
    ///   "userId": "0199c1a9-3e72-7d08-8c14-b05a4d9e17f2",
    ///   "startTime": "2026-11-10T10:00:00Z",
    ///   "endTime": "2026-11-10T11:00:00Z",
    ///   "status": "Pending",
    ///   "offerExpiresAt": "2026-10-06T12:30:00Z",
    ///   "waitlistEntryId": "0199c1b8-5f01-7a63-9e2c-d41b7a80c395",
    ///   "createdAt": "2026-10-06T12:15:00Z",
    ///   "updatedAt": "2026-10-06T12:15:00Z"
    /// }
    /// ```
    /// </remarks>
    /// <param name="id">The reservation id (GUID).</param>
    /// <response code="200">The reservation.</response>
    /// <response code="401">Missing, invalid or expired token.</response>
    /// <response code="403">The reservation belongs to another user and the caller is not an administrator.</response>
    /// <response code="404">No reservation with this id exists.</response>
    /// <response code="500">Unexpected server error.</response>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<ReservationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ReservationResponse>> GetById(Guid id, CancellationToken cancellationToken)
        => Ok(await reservations.GetAsync(id, cancellationToken));

    /// <summary>
    /// Accepts a waitlist offer, turning a Pending reservation into a Confirmed one.
    /// </summary>
    /// <remarks>
    /// Only the user who received the offer can confirm it. Administrators cannot confirm on someone's behalf.
    /// The deadline is checked at confirmation time, so an offer that has passed <c>offerExpiresAt</c> is rejected
    /// even if the background cleanup has not run yet. The linked waitlist entry becomes <c>Fulfilled</c>.
    ///
    /// No request body. Sample call:
    ///
    /// ```
    /// POST /api/reservations/0199c1c3-8a10-7b4e-a2d9-51c8e7f30b66/confirm
    /// ```
    ///
    /// The response is the reservation with <c>"status": "Confirmed"</c>.
    /// </remarks>
    /// <param name="id">The id of the Pending reservation (the offer).</param>
    /// <response code="200">The confirmed reservation.</response>
    /// <response code="401">Missing, invalid or expired token.</response>
    /// <response code="403">The caller is not the user who received the offer.</response>
    /// <response code="404">No reservation with this id exists.</response>
    /// <response code="409">The reservation is not Pending (already confirmed, cancelled, expired, or a direct booking) or the offer has expired.</response>
    /// <response code="500">Unexpected server error.</response>
    [HttpPost("{id:guid}/confirm")]
    [ProducesResponseType<ReservationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ReservationResponse>> Confirm(Guid id, CancellationToken cancellationToken)
        => Ok(await reservations.ConfirmAsync(id, cancellationToken));

    /// <summary>
    /// Cancels a reservation, or declines a waitlist offer.
    /// </summary>
    /// <remarks>
    /// The row is kept and its status becomes <c>Cancelled</c>. Allowed for the owner or an administrator,
    /// and only before the reservation starts.
    ///
    /// - Cancelling twice is safe: the second call also returns 204.
    /// - Declining a Pending offer also cancels the linked waitlist entry.
    /// - When a slot is freed, the waitlist is processed in the same transaction and the next eligible
    ///   user in the queue receives a Pending offer.
    /// </remarks>
    /// <param name="id">The reservation id (GUID).</param>
    /// <response code="204">The reservation is cancelled (or already was).</response>
    /// <response code="401">Missing, invalid or expired token.</response>
    /// <response code="403">The reservation belongs to another user and the caller is not an administrator.</response>
    /// <response code="404">No reservation with this id exists.</response>
    /// <response code="409">The reservation has already started and can no longer be cancelled.</response>
    /// <response code="500">Unexpected server error.</response>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken cancellationToken)
    {
        await reservations.CancelAsync(id, cancellationToken);
        return NoContent();
    }
}
