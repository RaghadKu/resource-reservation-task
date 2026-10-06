using System.ComponentModel.DataAnnotations;

namespace ResourceReservation.Application.Resources;

public sealed class AvailabilityQuery
{
    public const int MaxRangeDays = 31;

    [Required] public DateTimeOffset? From { get; init; }
    [Required] public DateTimeOffset? To { get; init; }
}