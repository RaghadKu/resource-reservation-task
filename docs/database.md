# Database design

SQL Server is the source of truth. EF Core with the Fluent API defines every key, relationship, length, index and constraint
explicitly (no reliance on conventions or data annotations for the schema).

## Tables

```
 AspNetUsers ──────┐ (who)                    AspNetRoles
   Id (Guid)       │                          AspNetUserRoles (User ↔ Role)
                   │
 Resources         │ (what)
   Id ◄────────┐   │
               │   │
 Reservations  │   │      WaitlistEntries
   Id ◄──────┐ │   │         Id ◄──────────────┐
   ResourceId┘ │   │         ResourceId ───────┘ (to Resources)
   UserId ─────┼───┘         UserId ──────────── (to AspNetUsers)
   StartTime, EndTime        RequestedStart/End
   Status                    Status
   OfferExpiresAt
   WaitlistEntryId ─────────► (to WaitlistEntries, nullable)

 IdempotencyRecords: UserId ► AspNetUsers, ReservationId ► Reservations
```

| Table | A row means |
|---|---|
| `Resources` | Something that can be booked |
| `Reservations` | A claim on a resource for a time range: firm (`Confirmed`) or on hold (`Pending`), or historical (`Cancelled`, `Expired`) |
| `WaitlistEntries` | "I want this resource for this range and I am in line". A wish, which blocks nothing. |
| `IdempotencyRecords` | "This user already sent this key", with a hash of the request and the resulting reservation |
| `AspNetUsers`, `AspNetRoles`, `AspNetUserRoles` | Identity |

A reservation is a claim on the resource, a waitlist entry is only a wish. They are separate tables because mixing them would force
every conflict check to separate real rows from wishes. The one link is `Reservations.WaitlistEntryId`, filled only when a reservation
was created as an offer for a waitlist entry.

## Keys and types

- Primary keys are `uniqueidentifier`, generated in Domain with `Guid.CreateVersion7()` (time-ordered), configured with `ValueGeneratedNever()`.
- Timestamps are `datetime2` in UTC.
- Enums (`Status`) are stored as `int` with explicit values, so reordering the enum never corrupts data.
- `UserId` columns are real foreign keys to `AspNetUsers`, configured as `HasOne<ApplicationUser>().WithMany()` with no navigation property,
  so Domain stays unaware of Identity while the database still refuses a reservation for a user that does not exist.

Sequential Guids are friendlier to clustered indexes than random ones, but SQL Server orders `uniqueidentifier` by an unusual byte order, so v7 values are not perfectly sequential.
At this scale it is irrelevant.

## Delete behavior

`DeleteBehavior.Restrict` everywhere. History must never disappear through a cascade, and it avoids SQL Server's "multiple cascade paths" error.
Resources are deactivated instead of deleted.

## Constraints

| Constraint | Purpose |
|---|---|
| `CK_Resources_Capacity` (`Capacity > 0`) | Backs the Domain rule |
| `CK_Reservations_TimeRange` (`StartTime < EndTime`) | A reservation can never have a negative or empty range |
| `CK_WaitlistEntries_TimeRange` | Same for waitlist entries |
| `UX_Resources_Name` (unique) | Unique resource names |
| Foreign keys | Reservations and entries point to real resources and users |

## Indexes and why each exists

No index was added "just in case". Indexes slow writes, so each one maps to a specific query or rule.

| Index | Query or rule it serves | Notes |
|---|---|---|
| `IX_Reservations_Resource_Blocking_Time` on `(ResourceId, StartTime, EndTime)` filtered `Status IN (1, 2)` | "Is there a Pending or Confirmed reservation overlapping this range on resource R?" | Filtered, so cancelled and expired history stays out and the index stays small |
| `IX_Reservations_PendingOffers_ExpiresAt` on `OfferExpiresAt` filtered `Status = 1` | The expiry worker's "which offers are overdue?" | Contains only pending offers |
| `IX_Reservations_User_StartTime` on `(UserId, StartTime)` | "My reservations" and the 10-upcoming limit | |
| `UX_Reservations_WaitlistEntry` unique on `WaitlistEntryId` filtered `NOT NULL` | One waitlist entry produces at most one offer | A guard against double processing, even if code has a bug |
| `IX_WaitlistEntries_Resource_Waiting_Fifo` on `(ResourceId, CreatedAt, Id)` filtered `Status = 1` | Candidates for a freed slot and position calculation, already in FIFO order | |
| `UX_WaitlistEntries_ActiveDuplicate` unique on `(ResourceId, UserId, RequestedStartTime, RequestedEndTime)` filtered `Status IN (1, 2)` | No duplicate active entry for the same request | Backs the duplicate rule |
| `UX_IdempotencyRecords_User_Key` unique on `(UserId, Key)` | The idempotency guarantee | Concurrent duplicates serialize on this index |
| `IX_IdempotencyRecords_CreatedAt` | Cleanup of records older than 24 hours | |

## What the database guarantees, and what it cannot

| Protection | Enforced by |
|---|---|
| `StartTime < EndTime` | Check constraint and `TimeRange` |
| Reservations point to a real resource and user | Foreign keys |
| Unique resource names | Unique index |
| No duplicate active waitlist entry | Unique filtered index, plus a service check for a friendly error |
| One offer per waitlist entry | Unique filtered index |
| One record per idempotency key per user | Unique index |
| Legal status transitions | Domain methods (the database does not know the state machine) |
| **No two overlapping active reservations** | **Not expressible as a SQL Server constraint.** Guaranteed by transaction plus row lock, see [concurrency.md](concurrency.md). |

PostgreSQL could express non-overlap declaratively with an exclusion constraint on a range type. SQL Server has no equivalent,
which is why the guarantee is procedural here.

## Identity tables

Only `AspNetUsers`, `AspNetRoles` and `AspNetUserRoles` are mapped. `ApplicationUser` adds a single column, `FullName` (required, up to 200 characters).
The claims, logins, tokens and role-claims tables are excluded from the model.

## Migrations

- Code-first, in `Infrastructure/Persistence/Migrations`.
- Applied automatically at startup only in the Development environment, to make the project runnable in one step.
  In other environments run `dotnet ef database update` as an explicit deployment step.
- `InitialCreate` creates the three Identity tables, `Resources`, `Reservations` and `WaitlistEntries`. `AddIdempotencyRecords` adds `IdempotencyRecords`.
