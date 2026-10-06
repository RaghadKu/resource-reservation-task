namespace ResourceReservation.Application.Resources;

public sealed record TimeWindow(DateTime Start, DateTime End);

public sealed record AvailabilityResponse(
    Guid ResourceId,
    DateTime From,
    DateTime To,
    IReadOnlyList<TimeWindow> BusyPeriods,
    IReadOnlyList<TimeWindow> FreePeriods);
