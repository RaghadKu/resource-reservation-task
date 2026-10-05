using ResourceReservation.Domain.Entities;

namespace ResourceReservation.Application.Abstractions;
public interface IResourceLock
{
    Task<Resource?> AcquireAsync(Guid resourceId, CancellationToken cancellationToken);
}
