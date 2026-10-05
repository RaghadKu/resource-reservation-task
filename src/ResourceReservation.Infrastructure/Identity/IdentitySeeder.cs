using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using ResourceReservation.Application.Common;
using ResourceReservation.Infrastructure.Persistence;

namespace ResourceReservation.Infrastructure.Identity;

public static class IdentitySeeder
{
    public static async Task InitializeAsync(
        IServiceProvider services, bool applyMigrations, CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var provider = scope.ServiceProvider;
        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("IdentitySeeder");

        if (applyMigrations)
            await provider.GetRequiredService<AppDbContext>().Database.MigrateAsync(cancellationToken);

        var roleManager = provider.GetRequiredService<RoleManager<ApplicationRole>>();
        foreach (var roleName in new[] { Roles.Admin, Roles.User })
        {
            if (await roleManager.RoleExistsAsync(roleName)) continue;
            var result = await roleManager.CreateAsync(new ApplicationRole { Id = Guid.CreateVersion7(), Name = roleName });
            if (!result.Succeeded)
                throw new InvalidOperationException($"Could not create role '{roleName}': {Describe(result)}");
        }

        var seed = provider.GetRequiredService<IOptions<SeedAdminOptions>>().Value;
        if (string.IsNullOrWhiteSpace(seed.Email) || string.IsNullOrWhiteSpace(seed.Password))
        {
            logger.LogWarning("No SeedAdmin configured: no administrator account was created.");
            return;
        }

        var userManager = provider.GetRequiredService<UserManager<ApplicationUser>>();
        if (await userManager.FindByEmailAsync(seed.Email) is not null) return;

        var admin = new ApplicationUser
        {
            Id = Guid.CreateVersion7(),
            UserName = seed.Email,
            Email = seed.Email,
            FullName = seed.FullName
        };

        var created = await userManager.CreateAsync(admin, seed.Password);
        if (!created.Succeeded)
            throw new InvalidOperationException($"Could not create the admin user: {Describe(created)}");

        await userManager.AddToRoleAsync(admin, Roles.Admin);
        logger.LogInformation("Seeded administrator account {Email}.", seed.Email);
    }

    private static string Describe(IdentityResult result) =>
        string.Join("; ", result.Errors.Select(e => e.Description));
}
