namespace ResourceReservation.Application.Waitlist;

public interface IOfferExpiryService
{
    Task ExpireOverdueOffersAsync(CancellationToken cancellationToken);
}
