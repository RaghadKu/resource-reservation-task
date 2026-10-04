using ResourceReservation.Domain.Exceptions;

namespace ResourceReservation.Domain.Common;
public static class BookingRules
{
    public static readonly TimeSpan MinDuration = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan MaxDuration = TimeSpan.FromHours(12);
    public const int MaxActiveReservationsPerUser = 10;

    public static void EnsureBookable(Guid userId, TimeRange range, DateTime now)
    {
        if (userId == Guid.Empty)
            throw new DomainValidationException("A valid user is required.");

        if (range.Start <= now)
            throw new DomainValidationException("Start time must be in the future.");

        if (range.Duration < MinDuration || range.Duration > MaxDuration)
            throw new DomainValidationException(
                $"Duration must be between {MinDuration.TotalMinutes} minutes and {MaxDuration.TotalHours} hours.");
    }
}
