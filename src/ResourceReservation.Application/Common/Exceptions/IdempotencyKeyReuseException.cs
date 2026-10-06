namespace ResourceReservation.Application.Common.Exceptions;

public sealed class IdempotencyKeyReuseException(string message) : Exception(message);
