using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using ResourceReservation.Application.Abstractions;
using ResourceReservation.Application.Auth;
using ResourceReservation.Application.Waitlist;
using ResourceReservation.Infrastructure.BackgroundJobs;
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

        // No EnableRetryOnFailure on purpose (see Step 5): we use manual transactions.
        services.AddDbContext<AppDbContext>(options => options.UseSqlServer(connectionString));

        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddScoped<IResourceLock, ResourceLock>();
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

        // Fail at startup, not at the first login, if the JWT settings are unusable.
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.Issuer)
                           && !string.IsNullOrWhiteSpace(o.Audience)
                           && o.SigningKey.Length >= 32
                           && o.ExpiryMinutes > 0,
                "Invalid Jwt settings: Issuer and Audience are required and SigningKey must be at least 32 characters.")
            .ValidateOnStart();

        services.Configure<SeedAdminOptions>(configuration.GetSection(SeedAdminOptions.SectionName));

        services.AddSingleton<JwtTokenGenerator>();
        services.AddScoped<IAuthService, AuthService>();

        services.AddOptions<WaitlistOptions>()
            .Bind(configuration.GetSection(WaitlistOptions.SectionName))
            .Validate(o => o.OfferWindowMinutes is > 0 and <= 1440, "Waitlist:OfferWindowMinutes must be between 1 and 1440.")
            .ValidateOnStart();

        services.AddBackgroundWorkers();

        return services;
    }
}
