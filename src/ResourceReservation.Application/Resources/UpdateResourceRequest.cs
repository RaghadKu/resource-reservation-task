using System.ComponentModel.DataAnnotations;

using ResourceReservation.Domain.Common;

namespace ResourceReservation.Application.Resources;

public sealed record UpdateResourceRequest(
    [Required, StringLength(FieldLengths.ResourceName, MinimumLength = 1)] string Name,
    [StringLength(FieldLengths.ResourceDescription)] string? Description,
    [Range(1, 10_000)] int Capacity,
    bool IsActive);
