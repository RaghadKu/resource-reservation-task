using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using ResourceReservation.Domain.Common;
using ResourceReservation.Domain.Entities;
using ResourceReservation.Infrastructure.Identity;

namespace ResourceReservation.Infrastructure.Persistence.Configurations;

public class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> builder)
    {
        builder.ToTable("IdempotencyRecords");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Id)
            .ValueGeneratedNever();

        builder.Property(r => r.UserId)
            .IsRequired();
        
        builder.Property(r => r.Key)
            .IsRequired()
            .HasMaxLength(FieldLengths.IdempotencyKey);
        
        builder.Property(r => r.RequestHash)
            .IsRequired()
            .HasMaxLength(64);
        
        builder.Property(r => r.ReservationId);
        
        builder.Property(r => r.CreatedAt)
            .IsRequired();
        
        builder.Property(r => r.UpdatedAt)
            .IsRequired();

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(r => r.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Reservation>()
            .WithMany()
            .HasForeignKey(r => r.ReservationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(r => new { r.UserId, r.Key })
            .IsUnique()
            .HasDatabaseName("UX_IdempotencyRecords_User_Key");

        builder.HasIndex(r => r.CreatedAt)
            .HasDatabaseName("IX_IdempotencyRecords_CreatedAt");
    }
}
