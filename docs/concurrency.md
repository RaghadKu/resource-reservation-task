# Concurrency strategy

## The problem

```
Request A: SELECT overlapping rows → none   ┐
Request B: SELECT overlapping rows → none   ┘ both see a free slot
Request A: INSERT                           ┐
Request B: INSERT                           ┘ double booking
```

A check followed by an insert is not atomic. The fix is to make "check, then insert" one indivisible unit **per resource**.
Different resources never need to wait for each other.

## Options compared

| Option | How it works | Problem |
|---|---|---|
| A. `SERIALIZABLE` isolation | SQL Server range-locks the rows the overlap query reads | Both transactions take shared range locks, then both try to insert, so they deadlock. One becomes the victim (error 1205) and retry logic is required. It is also hard to reason about which range gets locked. |
| B. `sp_getapplock` | Named application lock such as `resource:{id}` | Works and avoids deadlocks, but the lock is tied to no real row and is a less familiar mechanism |
| **C. Row lock on the Resource (`UPDLOCK`)** | `SELECT ... FROM Resources WITH (UPDLOCK, ROWLOCK) WHERE Id = @id` as the first statement of the transaction | One extra hint. Same-resource transactions queue, different resources run in parallel. **Chosen.** |
| D. Optimistic concurrency (`rowversion` on Resource) | Every booking updates the Resource row, the loser gets `DbUpdateConcurrencyException` | Every booking becomes a write to the Resource row, and the loser needs retry logic |
| E. Pre-generated slot table with a unique key | The database rejects duplicates | The only truly declarative option, but forces fixed slots, which contradicts free-form time ranges |

### Why option C

- The Resource row is the natural thing to lock: the rule is "one booking at a time per resource".
- The same query that locks the row also loads the resource, so "exists" and "is active" are checked **under the lock**.
  Without that, a resource could be deactivated between the check and the insert.
- Each transaction takes exactly one resource lock, always first, so lock-ordering deadlocks cannot occur between booking transactions.
- Isolation stays at the default `READ COMMITTED`. The lock does the serializing.
- It is simple to explain: lock the resource row, check for overlaps, insert, commit.

## How it works

```
Tx A: BEGIN → lock Resource R (acquired) → check overlaps (none) → INSERT → COMMIT (lock released)
Tx B: BEGIN → lock Resource R (WAITS ......................) acquired → check overlaps (sees A's row) → 409
```

B's overlap check starts after A has committed, so it sees A's reservation. This also holds if the database has
`READ_COMMITTED_SNAPSHOT` enabled, because B's check statement begins after the lock was granted. It would not hold under
transaction-level `SNAPSHOT` isolation, which this project never uses.

`UPDLOCK` is compatible with plain reads, so listing and availability endpoints are never blocked by a booking in progress.

## Implementation

`IResourceLock` (Application) and `ResourceLock` (Infrastructure):

```csharp
return await db.Resources
    .FromSqlInterpolated($"SELECT * FROM [Resources] WITH (UPDLOCK, ROWLOCK) WHERE [Id] = {resourceId}")
    .AsTracking()
    .SingleOrDefaultAsync(cancellationToken);
```

- It receives the same scoped `AppDbContext` as the use case, so the lock lives on the same connection and transaction.
- It throws if called outside a transaction, turning a silent and dangerous mistake (a lock released instantly) into a loud one.
- The interpolated Guid becomes a SQL parameter, so there is no injection risk.
- `EnableRetryOnFailure` is intentionally off: retrying execution strategies do not allow manually started transactions unless wrapped,
  and the locking removes the deadlocks and conflicts that retries usually exist for.

The booking pattern:

```csharp
await using var tx = await db.Database.BeginTransactionAsync(ct);

var resource = await resourceLock.AcquireAsync(resourceId, ct) ?? throw new NotFoundException(...);
if (!resource.IsActive) throw new ConflictException(...);
if (await db.IsSlotBlockedAsync(resourceId, start, end, ct)) throw new ConflictException(...);

db.Reservations.Add(reservation);
await db.SaveChangesAsync(ct);
await tx.CommitAsync(ct);          // releases the lock
```

Any exception before `CommitAsync` rolls the transaction back when `tx` is disposed, which also releases the lock. There is no manual cleanup.

## The discipline: every state change takes the lock

The database cannot enforce non-overlap, so the guarantee is **procedural**. The lock is not in the schema, it is in the rule that every write path goes through the same gate.

| Operation | Takes the lock | Why |
|---|---|---|
| Create reservation | yes | Adds a blocking row |
| Join waitlist | yes | Prevents joining "behind" a cancel that already processed the queue |
| Cancel reservation, decline offer | yes | Cancel plus waitlist processing is one atomic unit |
| Leave waitlist | yes | The processor loads `Waiting` entries under the lock; an unlocked leave could be overwritten by a stale `Offered` |
| Confirm offer | yes | Confirm and cancel modify the same Pending row and entry, and could otherwise interleave into inconsistent pairs |
| Offer expiry (worker) | yes | Same rows as above, plus it hands the slot on |
| Update or deactivate resource | yes | Must not race with a booking |
| Create resource | no | A new row has nothing to race with |
| Reads (list, get, availability) | no | Never blocked, may be a moment stale |

Rules that make it hold:

1. Every operation that adds a blocking row takes the lock first.
2. Cancel, expire and confirm also take it, so waitlist processing is atomic with them and two requests cannot process the same entry.
3. **Lock first, then read.** Anything a decision depends on is read after the lock. For cancel, an untracked read learns only the resource id and the owner (immutable), then the lock is taken, then the reservation is re-loaded tracked.
4. **One resource lock per transaction.** This keeps lock-ordering deadlocks impossible. The expiry worker uses one transaction per resource.
5. With idempotency, the order is **key first, then resource**. Same-key requests only wait on each other, so no cycle is possible.

> Early in the design confirm was thought not to need the lock. That was wrong once cancel existed, because confirm and cancel can interleave on the same Pending reservation. Confirm now takes the lock like every other state change.

## Database-level backstops

Even if application code had a bug, these hold:

- `UX_Reservations_WaitlistEntry`: one entry produces at most one offer.
- `UX_WaitlistEntries_ActiveDuplicate`: no duplicate active waitlist entry. Violations map to HTTP 409.
- `UX_IdempotencyRecords_User_Key`: one record per user and key.
- `CK_*_TimeRange`: no inverted ranges.

## Accepted trade-offs

| Trade-off | Why it is acceptable |
|---|---|
| Bookings on the same resource are serialized | Each transaction lasts a few milliseconds, and the business rule is inherently one at a time per resource |
| The "max 10 upcoming reservations per user" limit is soft | The lock is per resource, so one user firing parallel requests at different resources could briefly exceed 10. An exact limit would need a per-user lock. |
| No explicit lock timeout | A stuck holder would make others wait until the command timeout (30 s by default). Transactions here are short. |
| The guarantee is procedural | A future write path that forgets the lock would break it. Keeping all "claim a slot" logic in a few well-named service methods limits that risk. |

## Interview questions this answers

- Why is "check then insert" unsafe, and what exactly interleaves?
- Why was `SERIALIZABLE` rejected? (Two shared range locks lead to deadlock.)
- Why lock the Resource row rather than a reservation range?
- Why must the lock be first and reads come after it?
- Why can the database not enforce non-overlap here, and how would PostgreSQL differ? (Exclusion constraints.)
- Why are readers not blocked? (`UPDLOCK` is compatible with shared reads.)
- What is the weak point? (It is procedural: every write path must use the gate.)
