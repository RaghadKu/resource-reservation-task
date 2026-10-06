using System.Diagnostics;

using ResourceReservation.Api.Middleware;

namespace ResourceReservation.Api.Extensions;

public static class ExceptionHandlingExtensions
{
    public static IServiceCollection AddApiExceptionHandling(this IServiceCollection services)
    {
        services.AddProblemDetails(options =>
            options.CustomizeProblemDetails = context =>
            {
                // Lets a client report an error and lets you find it in the logs.
                context.ProblemDetails.Extensions["traceId"] =
                    Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;
            });

        services.AddExceptionHandler<GlobalExceptionHandler>();
        return services;
    }
}
