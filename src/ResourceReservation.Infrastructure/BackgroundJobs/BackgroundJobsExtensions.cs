using Microsoft.Extensions.DependencyInjection;

namespace ResourceReservation.Infrastructure.BackgroundJobs;

public static class BackgroundJobsExtensions
{
    public static IServiceCollection AddBackgroundWorkers(this IServiceCollection services)
    {
        services.AddHostedService<OfferExpiryWorker>();
        return services;
    }
}
