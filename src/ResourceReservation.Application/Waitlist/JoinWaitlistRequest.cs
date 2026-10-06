using System.ComponentModel.DataAnnotations;

namespace ResourceReservation.Application.Waitlist;

public sealed record JoinWaitlistRequest(
    [Required] DateTimeOffset? StartTime,
    [Required] DateTimeOffset? EndTime);