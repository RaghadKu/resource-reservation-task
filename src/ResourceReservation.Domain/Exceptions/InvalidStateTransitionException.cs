namespace ResourceReservation.Domain.Exceptions;

public class InvalidStateTransitionException(string message) : DomainException(message);
