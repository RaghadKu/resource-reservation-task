# Waitlist processing and background workers

## Overview

When a slot becomes free, the system offers it to the next eligible person in the queue. The pieces:

| Piece | Layer | Responsibility |
|---|---|---|
| `WaitlistService` | Application | Join, list (with computed position) and leave |
| `WaitlistProcessor` | Application | Turns a freed range into offers, in FIFO order |
| `OfferExpiryService` | Application | Expires overdue offers and stale entries, then hands the slot on |
| `PeriodicWorker`, `OfferExpiryWorker`, `IdempotencyCleanupWorker` | Infrastructure | Thin scheduling adapters that call the services above |

## Triggers

Processing runs for a freed range after:

1. a reservation is cancelled (`ReservationService.CancelAsync`);
2. an offer is declined (the same endpoint, because declining a Pending offer is cancelling it);
3. a user leaves the waitlist while their entry is `Offered` (`WaitlistService.LeaveAsync`);
4. an offer expires (`OfferExpiryService`).

In the first three cases the call happens **inside the same transaction and under the same resource lock** as the change that freed the slot.
If cancel and processing were separate transactions, a crash between them would free a slot and never offer it. As one unit, both happen or neither does.

## The processor

`IWaitlistProcessor.ProcessAsync(resourceId, freedStart, freedEnd)`:

1. **Requires a transaction.** It throws if none is open, because it relies on the caller holding the resource lock.
2. **Flushes pending changes first** (`SaveChanges`). The cancelled or expired reservation exists only in memory until saved. Without the flush, the overlap query would still see it and the freed slot would look taken.
3. Loads candidates: `Waiting` entries on the resource whose range overlaps the freed range, ordered by `CreatedAt` then `Id`.
4. For each candidate, in order:
   - skip it if its requested start is no longer in the future (the expiry sweep retires it later);
   - skip it if its **whole** requested range is not free (`IsSlotBlockedAsync`): there is no partial fulfilment, and the entry keeps its place;
   - otherwise call `entry.CreateOffer(now, window)`, which moves the entry to `Offered` and returns a `Pending` reservation that holds the slot, add it, and **save immediately**.
5. Saving after each offer means the next candidate's overlap check sees that hold. That is how a freed 10-12 slot can serve "10-11" and "11-12" but not a "10-12" candidate that now overlaps the first offer.

Properties:

- **Fair.** An earlier entry always gets the first chance at a free range.
- **No double processing.** Entries are `Waiting` only under the lock, and `UX_Reservations_WaitlistEntry` makes a second offer for the same entry impossible at the database level.
- **A skipped candidate is not lost.** A later cancellation can still serve it.
- **Offer deadline.** `now + OfferWindowMinutes`, capped at the slot's start time. `Waitlist:OfferWindowMinutes` defaults to 15 (1 in Development, so expiry can be tested quickly).
- **Notification.** The offer is logged, and the user finds it with `GET /api/reservations?status=Pending`. No email or SMS.

## Expiry sweep

`OfferExpiryService.ExpireOverdueOffersAsync` runs every 30 seconds:

1. **Cheap unlocked scan** to find resources with overdue Pending offers (`OfferExpiresAt <= now`) or `Waiting` entries whose requested period already started.
2. For each resource, **one transaction** with the resource lock (one lock per transaction, and one failing resource does not block the others):
   - re-read overdue offers **under the lock**, since a user may have confirmed or declined since the scan;
   - offer `Pending → Expired`, entry `Offered → Expired`;
   - stale `Waiting` entries `→ Expired` (they can never be offered);
   - run the processor for each freed range, so the slot passes to the next user;
   - save and commit.
3. Errors for one resource are logged and the loop continues; the next sweep retries it.

Expiry, the entry update and the hand-over commit together. A crash in between leaves everything as before, and the next sweep does it again.

**An expired user does not return to the queue.** Expiry means the turn is lost; the user can join again manually.

### Why confirm checks the deadline itself

An offer past its deadline stays `Pending` (and still blocks the slot) until the next sweep, up to about 30 seconds. That is conservative: it can
never cause a double booking. `Reservation.Confirm` rejects an expired offer on its own, so a late worker can never let an expired offer be confirmed.

## Background worker design

```csharp
public abstract class PeriodicWorker(IServiceScopeFactory scopeFactory, ILogger logger, TimeSpan interval)
    : BackgroundService
```

- **`BackgroundService` with `PeriodicTimer`**, built into .NET. No extra library, no extra tables.
- **A new DI scope per run.** The worker is a singleton but `DbContext` is scoped, so each run creates its own scope.
- **Exceptions are caught and logged inside the loop.** In .NET 6 and later, an unhandled exception in a `BackgroundService` stops the whole application. The next tick is the retry.
- **No overlapping runs in one process.** The loop awaits a run before waiting for the next tick, and `PeriodicTimer` does not queue missed ticks.
- **Graceful shutdown.** The stopping token flows into the run. An in-flight transaction is disposed, which rolls back and releases the lock.
- **Startup order.** Hosted services start inside `app.Run()`, after migrations and seeding, so the database exists when the first sweep runs.
- **Workers never use `ICurrentUser`**: there is no HTTP request.

| Worker | Interval | Service it calls |
|---|---|---|
| `OfferExpiryWorker` | 30 seconds | `IOfferExpiryService` |
| `IdempotencyCleanupWorker` | 1 hour | `IIdempotencyCleanupService` |

### Several instances

If the API were scaled out, every instance would run the sweep. This stays **correct**: each run takes the resource lock and re-reads state under it,
so the second instance finds nothing left to do. The cost is duplicate work. A distributed lock or a dedicated worker process would remove it.

### A stopped application

Nothing expires while the app is down, but deadlines live in the database and `Confirm` checks them itself. The first sweep after restart catches up.

### Why not Hangfire

Hangfire was considered and dropped. It adds three packages and about a dozen tables of its own, and its main benefits (persistent schedules, a dashboard, run history)
are not needed here. The offer deadline is already persisted in the business tables, and the sweep is stateless and idempotent. The Application-layer service
means swapping the scheduling mechanism later touches only Infrastructure.

## Consistency invariants (checked in manual verification)

Reservation and waitlist-entry statuses must stay in pairs:

| Reservation | Entry |
|---|---|
| `Pending` | `Offered` |
| `Confirmed` | `Fulfilled` |
| `Cancelled` | `Fulfilled` (cancelled after confirming) or `Cancelled` |
| `Expired` | `Expired` |

And no two Pending or Confirmed reservations of the same resource may overlap. Both are verified with SQL queries in [manual-verification.md](manual-verification.md).
