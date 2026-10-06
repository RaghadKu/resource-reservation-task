# Business rules

This document is the contract the code implements.

## Time and intervals

| Rule | Decision |
|---|---|
| Interval type | Half-open `[Start, End)`. 10:00-11:00 and 11:00-12:00 do not conflict. |
| Overlap test | `a.Start < b.End && b.Start < a.End`. Defined once in `TimeRange.Overlaps`. |
| Time zone | The API accepts `DateTimeOffset`, converts to UTC, and stores UTC `datetime2`. Responses carry a `Z` suffix. |
| Valid range | `Start < End`, `Start` in the future, duration between **15 minutes and 12 hours**. |
| Clock | Always `IDateTimeProvider`, never `DateTime.UtcNow` inside business code. |

A half-open interval is used because a closed one would make back-to-back bookings conflict, which is wrong for rooms.
The formula is symmetrical and needs no special cases.

| Existing | Requested | Overlap |
|---|---|---|
| 10-11 | 10:30-11:30 | yes |
| 10-11 | 11-12 | no (touching) |
| 10-11 | 09-10 | no (touching) |
| 10-11 | 09-12 (contains it) | yes |
| 10-11 | 10:15-10:45 (inside it) | yes |
| 10-11 | 10-11 (identical) | yes |

## Resources

- `name`: required, up to 200 characters, unique (enforced by a unique index; the database collation is case-insensitive).
- `capacity`: the number of people the resource holds, between 1 and 10000. It is not the number of concurrent bookings:
  a resource has **at most one active booking at any moment**.
- `description`: optional, up to 1000 characters.
- Only an `Admin` can create, update and deactivate. Any authenticated user can read.
- `DELETE` deactivates. Rows are never physically deleted, so reservation history stays valid.
- Deactivating a resource with **upcoming** Pending or Confirmed reservations (start time in the future) is rejected with 409.
  Reservations already in progress do not block it, because they can no longer be cancelled.
- Deactivating cancels all `Waiting` waitlist entries of the resource.
- Reactivation is `PUT` with `isActive: true`.
- Deactivating an inactive resource is a no-op returning 204.

Auto-cancelling future reservations on deactivation was rejected: it is destructive and would require notifying the affected users.

## Reservations

### Statuses

| Status | Meaning | Blocks the slot |
|---|---|---|
| `Pending` | A waitlist **offer**, waiting for the user to confirm before `offerExpiresAt` | yes |
| `Confirmed` | A direct booking or an accepted offer | yes |
| `Cancelled` | Cancelled by the owner or an admin, or an offer that was declined | no |
| `Expired` | An offer that was not confirmed in time | no |

```
(direct booking) ───────────────► Confirmed ──► Cancelled
(waitlist offer) ─► Pending ─┬──► Confirmed ──► Cancelled
                             ├──► Cancelled   (declined)
                             └──► Expired     (timeout)
```

An offer is a `Reservation` with status `Pending`, not a separate table. While an offer waits for an answer, nobody else may take the
slot, and one table with one conflict rule makes that guarantee simple.

### Creating a reservation

Checks run in this order, so errors are predictable:

1. `Idempotency-Key` header present and well formed (400)
2. Valid time range (400)
3. Resource exists (404)
4. Resource is active (409)
5. The user has fewer than **10** upcoming Pending or Confirmed reservations (409)
6. The slot does not overlap a Pending or Confirmed reservation (409, with a hint to join the waitlist)

The user id always comes from the token, never from the request body.

### Cancelling

- Allowed for the owner or an admin; others get 403.
- Allowed only before `startTime`; after that, 409.
- Cancelling an already cancelled or expired reservation returns 204 (idempotent).
- Cancelling a Pending offer is declining it, and also cancels the linked waitlist entry.
- A successful cancel triggers waitlist processing for the freed range, in the same transaction.

### Confirming an offer

- `POST /api/reservations/{id}/confirm`, **owner only** (admins cannot accept on someone's behalf).
- The reservation must be `Pending` and `now < offerExpiresAt`. The deadline is checked at confirmation time, so a late worker can never let an expired offer be confirmed.

## Waitlist

### Statuses

```
Waiting ─► Offered ─┬─► Fulfilled   (user confirmed)
   │                ├─► Expired     (offer timed out)
   │                └─► Cancelled   (user declined or left)
   ├──────────────────► Cancelled   (user left, or resource deactivated)
   └──────────────────► Expired     (requested period started without an offer)
```

### Joining

- Allowed only if the requested range is **actually unavailable**. If it is free, the API returns 409 ("reserve it directly").
- The range follows the same validity rules as a reservation.
- A user cannot have two `Waiting` or `Offered` entries for the same resource and exact range (409, backed by a unique index).
- The resource must be active.

### Order and position

- FIFO by `createdAt`, with `id` as a deterministic tiebreaker.
- Position is **computed**, not stored: `1 + earlier Waiting entries on the same resource whose range overlaps mine`.
  A stored position would need renumbering whenever someone leaves, creating extra writes and races.
  Position only makes sense among overlapping requests: a request for 09:00-10:00 never competes with one for 14:00-15:00.
- Position 1 means "next in line", not "the slot is free": an offer to someone else may be holding it.

### Processing a freed slot

Triggered after a cancellation, a declined offer, a user leaving while `Offered`, or an offer expiring.

1. The freed range is the range of the released reservation.
2. Candidates are `Waiting` entries on that resource whose range overlaps the freed range, in FIFO order.
3. For each candidate whose **whole requested range** is now free (no partial fulfilment) and whose start is still in the future,
   an offer is created: the entry becomes `Offered` and a `Pending` reservation holds the slot.
4. A candidate whose range is still blocked stays `Waiting` and keeps its place.

Example with a freed 10-12 slot: Bob waits for 10-11, Carol for 10-12, Dave for 11-12. Bob is offered 10-11. Carol's range now overlaps Bob's
hold, so she is skipped and stays in the queue. Dave is offered 11-12.

### Offers

- The window is `Waitlist:OfferWindowMinutes` (15 by default) and never extends past the slot's start time.
- An overdue offer is expired by a background worker: reservation `Pending → Expired`, entry `Offered → Expired`, then the slot passes to the next user.
- **An expired user does not return to the queue.** Moving them to the back would be more forgiving but adds complexity and can loop. Expiry means the turn is lost; they can join again manually.
- An entry still `Waiting` after its requested period has started is marked `Expired` by the same worker.
- Notifications: the offer is written to the log and found with `GET /api/reservations?status=Pending`. There is no email or SMS.

### Statuses always move in pairs

| Event | Reservation | Waitlist entry |
|---|---|---|
| Offer created | new row, `Pending` | `Waiting → Offered` |
| User confirms | `Pending → Confirmed` | `Offered → Fulfilled` |
| User declines or leaves | `Pending → Cancelled` | `Offered → Cancelled` |
| Offer times out | `Pending → Expired` | `Offered → Expired` |

Each pair changes in a single `SaveChanges`, so the database never shows an offer pending while its entry is still waiting.

### Worked example

Room A, 10:00-11:00 tomorrow. Alice, Bob and Carol all want it.

| # | Event | Reservations | Waitlist entries |
|---|---|---|---|
| 1 | Alice books | R1: Alice, Confirmed | none |
| 2 | Bob gets 409, joins the queue | R1 unchanged | W1: Bob, Waiting |
| 3 | Carol gets 409, joins | R1 unchanged | W1: Bob, W2: Carol, both Waiting |
| 4 | Alice cancels | R1: Cancelled | unchanged |
| 5 | Processing: Bob is offered the slot, Carol's range now overlaps his hold | R2: Bob, Pending, with a deadline | W1: Offered, W2: Waiting |
| 6a | Bob confirms in time | R2: Confirmed | W1: Fulfilled, W2: Waiting |
| 6b | Or Bob does nothing and the worker expires R2 | R2: Expired | W1: Expired |
| 7 | After 6b, processing runs again | R3: Carol, Pending | W2: Offered |

## Permissions

| Action | Admin | User |
|---|---|---|
| Create, update, deactivate resource | yes | no |
| Read resources, availability | yes | yes |
| Create reservation, join waitlist | yes (for themselves) | yes (for themselves) |
| View reservations and waitlist entries | all | own only |
| Cancel a reservation or leave a waitlist | any | own only |
| Confirm an offer | no | own only |

## Error mapping

| Situation | Status |
|---|---|
| Invalid input, missing or malformed idempotency key | 400 |
| Missing or invalid token | 401 |
| Not the owner and not an admin, or admin-only endpoint | 403 |
| Resource, reservation or waitlist entry not found | 404 |
| Slot conflict, inactive resource, duplicate waitlist entry, expired offer, already started, user limit reached, name taken | 409 |
| Same idempotency key with a different request | 422 |
| Unexpected error | 500 |

## Edge cases covered

- Two users book the same slot at once (row lock).
- An offer expires while the user is clicking confirm (deadline checked at confirm time).
- A cancel arrives twice (idempotent 204).
- A resource is deactivated while people are waiting (entries cancelled).
- A freed slot only partly satisfies a waiting user (no partial fulfilment).
- The worker is late or the app was stopped (confirm checks the deadline itself, and the first sweep after restart catches up).
- A client retries a reservation request (idempotency key).
- Confirm and cancel race on the same offer (both take the resource lock).
