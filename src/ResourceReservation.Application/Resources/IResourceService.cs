using ResourceReservation.Application.Common;

namespace ResourceReservation.Application.Resources;

public interface IResourceService
{
    Task<ResourceResponse> CreateAsync(CreateResourceRequest request, CancellationToken cancellationToken);
    Task<ResourceResponse> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<PagedResult<ResourceResponse>> ListAsync(ResourceQuery query, CancellationToken cancellationToken);
    Task<ResourceResponse> UpdateAsync(Guid id, UpdateResourceRequest request, CancellationToken cancellationToken);
    Task DeactivateAsync(Guid id, CancellationToken cancellationToken);
}
