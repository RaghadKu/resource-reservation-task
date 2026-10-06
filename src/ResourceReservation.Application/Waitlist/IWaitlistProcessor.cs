namespace ResourceReservation.Application.Waitlist;

public interface IWaitlistProcessor
{
    /// <summary>
    /// Offers the freed range [freedStart, freedEnd) to waiting users, in FIFO order.
    /// MUST be called inside a transaction that already holds the resource lock.
    /// Pending changes on the context are saved first, so the freed slot is visible to the queries below.
    /// </summary>
    Task ProcessAsync(Guid resourceId, DateTime freedStart, DateTime freedEnd, CancellationToken cancellationToken);
}
