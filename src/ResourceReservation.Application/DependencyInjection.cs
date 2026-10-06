using Microsoft.Extensions.DependencyInjection;

using ResourceReservation.Application.Idempotency;
using ResourceReservation.Application.Reservations;
using ResourceReservation.Application.Resources;
using ResourceReservation.Application.Waitlist;

namespace ResourceReservation.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IResourceService, ResourceService>();
        services.AddScoped<IReservationService, ReservationService>();
        services.AddScoped<IWaitlistService, WaitlistService>();
        services.AddScoped<IWaitlistProcessor, WaitlistProcessor>();
        services.AddScoped<IOfferExpiryService, OfferExpiryService>();
        services.AddScoped<IIdempotencyCleanupService, IdempotencyCleanupService>();
        return services;
    }
}
