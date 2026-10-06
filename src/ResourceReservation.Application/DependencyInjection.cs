using Microsoft.Extensions.DependencyInjection;

using ResourceReservation.Application.Resources;

namespace ResourceReservation.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IResourceService, ResourceService>();
        return services;
    }
}
