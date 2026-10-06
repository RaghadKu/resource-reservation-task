using System.Linq.Expressions;

using ResourceReservation.Domain.Entities;

namespace ResourceReservation.Application.Resources;

internal static class ResourceMappings
{
    // Used inside queries: EF turns it into a SELECT of just these columns.
    public static readonly Expression<Func<Resource, ResourceResponse>> ToResponse = r =>
        new ResourceResponse(r.Id, r.Name, r.Description, r.Capacity, r.IsActive, r.CreatedAt, r.UpdatedAt);

    private static readonly Func<Resource, ResourceResponse> Compiled = ToResponse.Compile();

    // Used for an entity already in memory (after create/update).
    public static ResourceResponse ToDto(this Resource resource) => Compiled(resource);
}
