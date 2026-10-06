using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using ResourceReservation.Application.Waitlist;

namespace ResourceReservation.Infrastructure.BackgroundJobs;

// A thin adapter: scheduling lives here, the expiry logic lives in Application.
public sealed class OfferExpiryWorker(IServiceScopeFactory scopeFactory, ILogger<OfferExpiryWorker> logger)
    : PeriodicWorker(scopeFactory, logger, TimeSpan.FromSeconds(30))
{
    protected override Task RunOnceAsync(IServiceProvider services, CancellationToken cancellationToken)
        => services.GetRequiredService<IOfferExpiryService>().ExpireOverdueOffersAsync(cancellationToken);
}
