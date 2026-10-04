using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace ResourceReservation.Application.Abstractions;

public interface IApplicationDbContext
{
    DbSet<Resource> Resources { get; }
    DbSet<Reservation> Reservations { get; }
    DbSet<WaitlistEntry> WaitlistEntries { get; }

    DatabaseFacade Database { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
