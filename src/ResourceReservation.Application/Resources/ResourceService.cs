using Microsoft.EntityFrameworkCore;

using ResourceReservation.Application.Abstractions;
using ResourceReservation.Application.Common;
using ResourceReservation.Application.Common.Exceptions;
using ResourceReservation.Domain.Entities;
using ResourceReservation.Domain.Enums;

namespace ResourceReservation.Application.Resources;

public sealed class ResourceService(
    IApplicationDbContext db,
    IResourceLock resourceLock,
    IDateTimeProvider clock) : IResourceService
{
    public async Task<ResourceResponse> CreateAsync(CreateResourceRequest request, CancellationToken cancellationToken)
    {
        // The Domain validates and trims; we check uniqueness against the trimmed name.
        var resource = Resource.Create(request.Name, request.Description, request.Capacity, clock.UtcNow);
        await EnsureNameIsFreeAsync(resource.Name, Guid.Empty, cancellationToken);

        db.Resources.Add(resource);
        await db.SaveChangesAsync(cancellationToken);
        return resource.ToDto();
    }

    public async Task<ResourceResponse> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var resource = await db.Resources.AsNoTracking()
            .Where(r => r.Id == id)
            .Select(ResourceMappings.ToResponse)
            .SingleOrDefaultAsync(cancellationToken);

        return resource ?? throw NotFound(id);
    }

    public async Task<PagedResult<ResourceResponse>> ListAsync(ResourceQuery query, CancellationToken cancellationToken)
    {
        var resources = db.Resources.AsNoTracking();

        if (query.IsActive is { } isActive)
            resources = resources.Where(r => r.IsActive == isActive);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            resources = resources.Where(r => r.Name.Contains(term));
        }

        IOrderedQueryable<Resource> ordered = (query.SortBy, query.SortDirection) switch
        {
            (ResourceSortBy.Capacity, SortDirection.Desc) => resources.OrderByDescending(r => r.Capacity),
            (ResourceSortBy.Capacity, _) => resources.OrderBy(r => r.Capacity),
            (ResourceSortBy.CreatedAt, SortDirection.Desc) => resources.OrderByDescending(r => r.CreatedAt),
            (ResourceSortBy.CreatedAt, _) => resources.OrderBy(r => r.CreatedAt),
            (_, SortDirection.Desc) => resources.OrderByDescending(r => r.Name),
            _ => resources.OrderBy(r => r.Name)
        };

        // Unique tiebreaker: stable pages even when many rows share the same sort value.
        return await ordered
            .ThenBy(r => r.Id)
            .Select(ResourceMappings.ToResponse)
            .ToPagedResultAsync(query, cancellationToken);
    }

    public async Task<ResourceResponse> UpdateAsync(Guid id, UpdateResourceRequest request, CancellationToken cancellationToken)
    {
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

        var resource = await resourceLock.AcquireAsync(id, cancellationToken) ?? throw NotFound(id);
        var now = clock.UtcNow;

        resource.Update(request.Name, request.Description, request.Capacity, now);
        await EnsureNameIsFreeAsync(resource.Name, resource.Id, cancellationToken);

        if (request.IsActive)
            resource.Activate(now);
        else
            await DeactivateCoreAsync(resource, now, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return resource.ToDto();
    }

    public async Task DeactivateAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

        var resource = await resourceLock.AcquireAsync(id, cancellationToken) ?? throw NotFound(id);
        await DeactivateCoreAsync(resource, clock.UtcNow, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }

    // The deactivation rules, written once. The caller holds the resource lock.
    private async Task DeactivateCoreAsync(Resource resource, DateTime now, CancellationToken cancellationToken)
    {
        if (!resource.IsActive) return;

        var hasUpcoming = await db.Reservations
            .Where(r => r.ResourceId == resource.Id)
            .Where(Reservation.UpcomingBlocking(now))
            .AnyAsync(cancellationToken);

        if (hasUpcoming)
            throw new ConflictException(
                "The resource has upcoming reservations. Cancel them before deactivating it.");

        var waiting = await db.WaitlistEntries
            .Where(e => e.ResourceId == resource.Id && e.Status == WaitlistStatus.Waiting)
            .ToListAsync(cancellationToken);

        foreach (var entry in waiting)
            entry.Cancel(now);

        resource.Deactivate(now);
    }

    private async Task EnsureNameIsFreeAsync(string name, Guid excludingId, CancellationToken cancellationToken)
    {
        var taken = await db.Resources.AnyAsync(r => r.Name == name && r.Id != excludingId, cancellationToken);
        if (taken)
            throw new ConflictException($"A resource named '{name}' already exists.");
    }

    private static NotFoundException NotFound(Guid id) => new($"Resource '{id}' was not found.");
}
