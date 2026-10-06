using System.ComponentModel.DataAnnotations;

using ResourceReservation.Application.Common;

namespace ResourceReservation.Application.Resources;

public enum ResourceSortBy { Name = 1, Capacity = 2, CreatedAt = 3 }

public sealed class ResourceQuery : PaginationQuery
{
    public bool? IsActive { get; init; }

    [StringLength(100)]
    public string? Search { get; init; }          // matches Name

    public ResourceSortBy SortBy { get; init; } = ResourceSortBy.Name;
    public SortDirection SortDirection { get; init; } = SortDirection.Asc;
}
