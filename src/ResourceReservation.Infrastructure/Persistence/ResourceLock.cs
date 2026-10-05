using Microsoft.EntityFrameworkCore;

using ResourceReservation.Application.Abstractions;
using ResourceReservation.Domain.Entities;

namespace ResourceReservation.Infrastructure.Persistence;
public sealed class ResourceLock(AppDbContext db) : IResourceLock
{
    public async Task<Resource?> AcquireAsync(Guid resourceId, CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException(
                "A resource lock can only be taken inside a transaction, otherwise it is released immediately.");

        // UPDLOCK: exclusive-intent; other UPDLOCK/writers wait, plain readers don't.
        // ROWLOCK: lock just this row, not the page or table.
        return await db.Resources
            .FromSqlInterpolated($"SELECT * FROM [Resources] WITH (UPDLOCK, ROWLOCK) WHERE [Id] = {resourceId}")
            .AsTracking()
            .SingleOrDefaultAsync(cancellationToken);
    }
}
