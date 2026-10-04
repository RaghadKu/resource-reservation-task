using ResourceReservation.Domain.Exceptions;

namespace ResourceReservation.Domain.Common;
public readonly record struct TimeRange
{
    public DateTime Start { get; }
    public DateTime End { get; }

    public TimeRange(DateTime start, DateTime end)
    {
        if (start >= end)
            throw new DomainValidationException("Start time must be before end time.");

        Start = start;
        End = end;
    }

    public TimeSpan Duration => End - Start;

    public bool Overlaps(TimeRange other) => Start < other.End && other.Start < End;
}
