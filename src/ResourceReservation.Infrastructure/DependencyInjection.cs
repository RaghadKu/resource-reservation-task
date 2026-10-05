using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ResourceReservation.Application.Abstractions;
using ResourceReservation.Infrastructure.Common;
using ResourceReservation.Infrastructure.Identity;
using ResourceReservation.Infrastructure.Persistence;

namespace ResourceReservation.Infrastructure;
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is missing.");

        // No EnableRetryOnFailure on purpose: with retries on, manual transactions must be wrapped
        // in an execution strategy. We decide that in Step 5.
        services.AddDbContext<AppDbContext>(options => options.UseSqlServer(connectionString));

        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();

        // AddIdentityCore (not AddIdentity): no cookies, no UI. We use JWT.
        // No default token providers: they would need the UserTokens table we dropped.
        services.AddIdentityCore<ApplicationUser>(options =>
        {
            options.User.RequireUniqueEmail = true;
            options.Password.RequiredLength = 8;
            options.Password.RequireNonAlphanumeric = false;
        })
            .AddRoles<ApplicationRole>()
            .AddEntityFrameworkStores<AppDbContext>();

        return services;
    }
}
