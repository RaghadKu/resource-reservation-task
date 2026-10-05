using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using ResourceReservation.Domain.Common;
using ResourceReservation.Domain.Entities;

namespace ResourceReservation.Infrastructure.Persistence.Configurations;
public class ResourceConfiguration : IEntityTypeConfiguration<Resource>
{
    public void Configure(EntityTypeBuilder<Resource> builder)
    {
        builder.ToTable("Resources", t =>
            t.HasCheckConstraint("CK_Resources_Capacity", "[Capacity] > 0"));

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever(); 

        builder.Property(r => r.Name).IsRequired().HasMaxLength(FieldLengths.ResourceName);
        builder.Property(r => r.Description).HasMaxLength(FieldLengths.ResourceDescription);
        builder.Property(r => r.Capacity).IsRequired();
        builder.Property(r => r.IsActive).IsRequired();
        builder.Property(r => r.CreatedAt).IsRequired();
        builder.Property(r => r.UpdatedAt).IsRequired();

        builder.HasIndex(r => r.Name).IsUnique().HasDatabaseName("UX_Resources_Name");
    }
}
