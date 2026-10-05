namespace ResourceReservation.Application.Common.Exceptions;
public sealed class BadRequestException(string message, IDictionary<string, string[]>? errors = null)
    : Exception(message)
{
    public IDictionary<string, string[]> Errors { get; } = errors ?? new Dictionary<string, string[]>();
}
