namespace ResourceReservation.Application.Abstractions;

public interface IDateTimeProvider
{
    DateTime UtcNow { get; }
}
