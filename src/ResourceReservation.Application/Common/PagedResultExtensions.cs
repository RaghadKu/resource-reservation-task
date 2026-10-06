using Microsoft.EntityFrameworkCore;

namespace ResourceReservation.Application.Common;

public static class PagedResultExtensions
{
    // The query must already be filtered, ordered (with a unique tiebreaker) and projected to the DTO.
    public static async Task<PagedResult<T>> ToPagedResultAsync<T>(
        this IQueryable<T> query, PaginationQuery paging, CancellationToken cancellationToken)
    {
        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query.Skip(paging.Skip).Take(paging.PageSize).ToListAsync(cancellationToken);
        return new PagedResult<T>(items, paging.PageNumber, paging.PageSize, totalCount);
    }
}