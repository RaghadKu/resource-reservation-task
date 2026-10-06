using System.ComponentModel.DataAnnotations;

namespace ResourceReservation.Application.Reservations;

public sealed record CreateReservationRequest(
    [Required] Guid? ResourceId,
    [Required] DateTimeOffset? StartTime,
    [Required] DateTimeOffset? EndTime);
