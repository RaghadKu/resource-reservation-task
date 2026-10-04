using Microsoft.EntityFrameworkCore.Metadata.Builders;

using ResourceReservation.Infrastructure.Identity;

namespace ResourceReservation.Infrastructure.Persistence.Configurations;
public class ApplicationUserConfiguration
{
    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        builder
            .Property(u => u.FullName)
            .IsRequired()
            .HasMaxLength(128);
    }
}
