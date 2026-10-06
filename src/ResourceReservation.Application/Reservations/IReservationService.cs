using ResourceReservation.Application.Common;

namespace ResourceReservation.Application.Reservations;

public interface IReservationService
{
    Task<ReservationResponse> CreateAsync(CreateReservationRequest request, CancellationToken cancellationToken);
    Task<ReservationResponse> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<PagedResult<ReservationResponse>> ListAsync(ReservationQuery query, CancellationToken cancellationToken);
    Task CancelAsync(Guid id, CancellationToken cancellationToken);
    Task<ReservationResponse> ConfirmAsync(Guid id, CancellationToken cancellationToken);
}
