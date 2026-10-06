using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using ResourceReservation.Domain.Entities;
using ResourceReservation.Infrastructure.Identity;

namespace ResourceReservation.Infrastructure.Persistence.Configurations;
public class WaitlistEntryConfiguration : IEntityTypeConfiguration<WaitlistEntry>
{
    public void Configure(EntityTypeBuilder<WaitlistEntry> builder)
    {
        builder.ToTable("WaitlistEntries", t =>
            t.HasCheckConstraint("CK_WaitlistEntries_TimeRange", "[RequestedStartTime] < [RequestedEndTime]"));

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .ValueGeneratedNever();

        builder.Property(e => e.ResourceId)
            .IsRequired();

        builder.Property(e => e.UserId)
            .IsRequired();

        builder.Property(e => e.RequestedStartTime)
            .IsRequired();

        builder.Property(e => e.RequestedEndTime)
            .IsRequired();

        builder.Property(e => e.Status)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(e => e.CreatedAt)
            .IsRequired();

        builder.Property(e => e.UpdatedAt)
            .IsRequired();

        builder.Ignore(e => e.RequestedRange);

        builder.HasOne(e => e.Resource).WithMany()
            .HasForeignKey(e => e.ResourceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        // FIFO queue: waiting entries of a resource, in order. Also feeds the computed Position.
        builder.HasIndex(e => new { e.ResourceId, e.CreatedAt, e.Id })
            .HasFilter("[Status] = 1")
            .HasDatabaseName("IX_WaitlistEntries_Resource_Waiting_Fifo");

        // Duplicate rule: same user, resource and range cannot be active twice.
        builder.HasIndex(e => new { e.ResourceId, e.UserId, e.RequestedStartTime, e.RequestedEndTime })
            .IsUnique()
            .HasFilter("[Status] IN (1, 2)")
            .HasDatabaseName("UX_WaitlistEntries_ActiveDuplicate");
    }
}
