using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

using ResourceReservation.Application.Abstractions;
using ResourceReservation.Infrastructure.Identity;

namespace ResourceReservation.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options) 
    : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>(options), IApplicationDbContext
{
    public DbSet<Resource> Resources => Set<Resource>();
    public DbSet<Reservation> Reservations => Set<Reservation>();
    public DbSet<WaitlistEntry> WaitlistEntries => Set<WaitlistEntry>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Ignore<IdentityUserClaim<Guid>>();
        builder.Ignore<IdentityRoleClaim<Guid>>();
        builder.Ignore<IdentityUserLogin<Guid>>();
        builder.Ignore<IdentityUserToken<Guid>>();

        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        UseUtcForAllDateTimes(builder);
    }

    private static void UseUtcForAllDateTimes(ModelBuilder builder)
    {
        var required = new ValueConverter<DateTime, DateTime>(
            v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
        var optional = new ValueConverter<DateTime?, DateTime?>(
            v => v, v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);

        foreach (var property in builder.Model.GetEntityTypes().SelectMany(e => e.GetProperties()))
        {
            if (property.ClrType == typeof(DateTime)) property.SetValueConverter(required);
            else if (property.ClrType == typeof(DateTime?)) property.SetValueConverter(optional);
        }
    }
}
