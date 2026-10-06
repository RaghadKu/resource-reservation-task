using ResourceReservation.Application.Common;

namespace ResourceReservation.Application.Waitlist;

public interface IWaitlistService
{
    Task<WaitlistEntryResponse> JoinAsync(Guid resourceId, JoinWaitlistRequest request, CancellationToken cancellationToken);
    Task<PagedResult<WaitlistEntryResponse>> ListAsync(Guid resourceId, WaitlistQuery query, CancellationToken cancellationToken);
    Task LeaveAsync(Guid id, CancellationToken cancellationToken);
}