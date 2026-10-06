using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ResourceReservation.Domain.Entities;

using ResourceReservation.Infrastructure.Identity;

namespace ResourceReservation.Infrastructure.Persistence.Configurations;
public class ReservationConfiguration : IEntityTypeConfiguration<Reservation>
{
    public void Configure(EntityTypeBuilder<Reservation> builder)
    {
        builder.ToTable("Reservations", t =>
            t.HasCheckConstraint("CK_Reservations_TimeRange", "[StartTime] < [EndTime]"));

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Id)
            .ValueGeneratedNever();

        builder.Property(r => r.ResourceId)
            .IsRequired();

        builder.Property(r => r.UserId)
            .IsRequired();

        builder.Property(r => r.StartTime)
            .IsRequired();

        builder.Property(r => r.EndTime)
            .IsRequired();

        builder.Property(r => r.Status)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(r => r.OfferExpiresAt);

        builder.Property(r => r.WaitlistEntryId);
        
        builder.Property(r => r.CreatedAt)
            .IsRequired();

        builder.Property(r => r.UpdatedAt)
            .IsRequired();

        // Computed members of the domain model, not columns.
        builder.Ignore(r => r.Range);
        builder.Ignore(r => r.BlocksSlot);

        // Relationships: Restrict everywhere. History must never disappear through a cascade.
        builder.HasOne(r => r.Resource)
            .WithMany()
            .HasForeignKey(r => r.ResourceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ApplicationUser>()
            .WithMany()   // no navigation: Domain doesn't know users
            .HasForeignKey(r => r.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<WaitlistEntry>()
            .WithMany()
            .HasForeignKey(r => r.WaitlistEntryId)
            .OnDelete(DeleteBehavior.Restrict);

        // Conflict detection: only rows that block a slot (Pending=1, Confirmed=2) are indexed.
        builder.HasIndex(r => new { r.ResourceId, r.StartTime, r.EndTime })
            .HasFilter("[Status] IN (1, 2)")
            .HasDatabaseName("IX_Reservations_Resource_Blocking_Time");

        // The expiry worker only looks at pending offers, ordered by deadline.
        builder.HasIndex(r => r.OfferExpiresAt)
            .HasFilter("[Status] = 1")
            .HasDatabaseName("IX_Reservations_PendingOffers_ExpiresAt");

        // "My reservations" and the "max 10 upcoming" check.
        builder.HasIndex(r => new { r.UserId, r.StartTime })
            .HasDatabaseName("IX_Reservations_User_StartTime");

        // A waitlist entry can produce at most one offer: no double processing, enforced by the database.
        builder.HasIndex(r => r.WaitlistEntryId)
            .IsUnique()
            .HasFilter("[WaitlistEntryId] IS NOT NULL")
            .HasDatabaseName("UX_Reservations_WaitlistEntry");
    }
}
