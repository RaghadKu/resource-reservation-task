using ResourceReservation.Domain.Common;
using System.ComponentModel.DataAnnotations;

public sealed record CreateResourceRequest(
    [Required, StringLength(FieldLengths.ResourceName, MinimumLength = 1)] string Name,
    [StringLength(FieldLengths.ResourceDescription)] string? Description,
    [Range(1, 10_000)] int Capacity);
