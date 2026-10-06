using ResourceReservation.Application.Common;
using ResourceReservation.Domain.Enums;

namespace ResourceReservation.Application.Waitlist;

public sealed class WaitlistQuery : PaginationQuery
{
    public WaitlistStatus? Status { get; init; }
}
