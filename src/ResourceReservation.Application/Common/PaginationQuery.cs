using System.ComponentModel.DataAnnotations;

namespace ResourceReservation.Application.Common;

public class PaginationQuery
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    [Range(1, 1_000_000)]
    public int PageNumber { get; init; } = 1;

    [Range(1, MaxPageSize)]
    public int PageSize { get; init; } = DefaultPageSize;

    public int Skip => (PageNumber - 1) * PageSize;
}