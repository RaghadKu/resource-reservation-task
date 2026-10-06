using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using ResourceReservation.Application.Common;
using ResourceReservation.Application.Resources;

namespace ResourceReservation.Api.Controllers;

[ApiController]
[Route("api/resources")]
[Authorize]
public class ResourcesController(IResourceService resources) : ControllerBase
{
    [HttpPost]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType<ResourceResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<ResourceResponse>> Create(
        CreateResourceRequest request, CancellationToken cancellationToken)
    {
        var created = await resources.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<ResourceResponse>>> List(
        [FromQuery] ResourceQuery query, CancellationToken cancellationToken)
        => Ok(await resources.ListAsync(query, cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ResourceResponse>> GetById(Guid id, CancellationToken cancellationToken)
        => Ok(await resources.GetAsync(id, cancellationToken));

    [HttpPut("{id:guid}")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<ActionResult<ResourceResponse>> Update(
        Guid id, UpdateResourceRequest request, CancellationToken cancellationToken)
        => Ok(await resources.UpdateAsync(id, request, cancellationToken));

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken)
    {
        await resources.DeactivateAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpGet("{resourceId:guid}/availability")]
    public async Task<ActionResult<AvailabilityResponse>> GetAvailability(
    Guid resourceId, [FromQuery] AvailabilityQuery query, CancellationToken cancellationToken)
    => Ok(await resources.GetAvailabilityAsync(resourceId, query, cancellationToken));
}
