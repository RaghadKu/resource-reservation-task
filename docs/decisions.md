# Decision log

Each entry states the problem, the choice, the alternatives and the trade-off. Details live in the linked documents.

## Technology and structure

| Decision | Chosen | Alternatives considered | Why |
|---|---|---|---|
| Target framework | .NET 9, EF Core 9 | .NET 10 | The environment uses .NET 9. Package versions are pinned to `9.0.*` so NuGet does not pick EF Core 10, which does not support `net9.0`. |
| Project layout | Four projects: Domain, Application, Infrastructure, Api | One project with folders | Layer boundaries are enforced by the compiler. See [architecture.md](architecture.md). |
| Data access abstraction | `IApplicationDbContext` over EF Core | Generic repository, specific repositories | `DbContext` already is a unit of work and `DbSet` a repository. Repositories would hide `Include`, projections and transactions. |
| Application style | One service class per feature | CQRS, MediatR | No requirement justifies a mediator pipeline. Services are easier to read and explain. |
| Mapping | Hand-written `Expression` mappings | AutoMapper | Queries project to DTOs in SQL, and there is no extra dependency. |
| Feature folders | Organize Application by feature | `Services/`, `DTOs/` folders | Related code stays together as the project grows. |
| Compiler strictness | Nullable enabled, warnings as errors | Defaults | Catches a class of bugs at compile time. NuGet audit warnings (NU1901 to NU1904) are kept as warnings. |
| Tests | No automated test project | Unit and integration test projects | Scope decision. Manual verification scripts and SQL invariants stand in. See [manual-verification.md](manual-verification.md). |
| Container tooling | Not used; the database is a connection string to any SQL Server | docker-compose | Scope decision. The README documents the setup from scratch. |

## Domain model

| Decision | Chosen | Why |
|---|---|---|
| Ids | `Guid.CreateVersion7()`, generated in Domain | The id is known before saving, which makes linking an offer to its waitlist entry trivial. Time-ordered, not guessable or countable. Trade-off: SQL Server orders Guids unusually, so not perfectly sequential in a clustered index. Irrelevant at this scale. |
| FIFO tiebreaker | `CreatedAt`, then `Id` | `Id` is a deterministic tiebreaker, not a chronological one. Two entries with identical timestamps were genuinely simultaneous, so only a stable order is needed. |
| Encapsulation | Private setters, factory and transition methods | No code outside Domain can put an entity into an illegal state. |
| Time | Entities receive `now` | Domain stays deterministic and time rules are explainable. |
| Interval | Half-open `[Start, End)` | Back-to-back bookings must not conflict. One central `Overlaps` method. |
| Overlap rule in SQL | An `Expression` twin of `Overlaps` next to each entity | EF Core cannot call methods inside queries. A small, documented duplication beats pulling rows into memory. |
| Offer representation | A `Pending` reservation | One table, one conflict rule, and the slot is held by the same mechanism as every booking. A separate Offer table would spread the guarantee across two tables. |
| Waitlist `Position` | Computed at read time, not stored | A stored value needs renumbering whenever someone leaves, creating writes and races. Only meaningful among overlapping requests. |
| Extra entity fields | `Reservation.OfferExpiresAt`, `Reservation.WaitlistEntryId` | Needed for the offer window, and to find the entry when an offer is confirmed or expires without matching on user and range. |
| Capacity | Physical capacity (people), one active booking at a time | Keeps the concurrency problem simple and well defined. |
| Users in Domain | A plain `Guid UserId`, no User entity | Domain stays free of Identity. The foreign key is configured in Infrastructure. |

## Identity and security

| Decision | Chosen | Why |
|---|---|---|
| Identity tables | Only Users, Roles, UserRoles, with Guid keys | Requirement from the project owner. The four other Identity entities are ignored in the model, and the unsupported `UserManager` calls are never used. |
| Token type | JWT access token only, 60 minutes | Stateless, and no refresh-token table. Trade-off: role changes apply when the token expires. |
| First admin | Seeded from configuration | A public endpoint that could grant `Admin` would make anyone an admin. |
| Auth service location | Interface in Application, implementation in Infrastructure | It needs `UserManager`, which belongs to the Identity package. |
| `ICurrentUser` location | Implemented in Api | Only the web layer has an `HttpContext`. |
| Login errors | Same message for unknown email and wrong password | Does not reveal which emails exist. |
| Auth request validation | DataAnnotations | `[ApiController]` returns automatic 400 Problem Details with no extra library. |
| Startup migrations | Development only | One-step run for reviewers, while production schema changes remain an explicit step. |
| Secrets | Dev-only values committed on purpose; production uses environment variables | A reviewer can run the project immediately. The signing key is validated at startup (at least 32 characters). |

## Concurrency, consistency and reliability

| Decision | Chosen | Why |
|---|---|---|
| Double-booking protection | `UPDLOCK, ROWLOCK` on the Resource row inside a transaction | Deadlock-free, per-resource, readers not blocked. See [concurrency.md](concurrency.md) for the five options compared. |
| Isolation level | Default `READ COMMITTED` | The lock does the serializing. |
| Retry on failure | `EnableRetryOnFailure` off | Retrying strategies do not allow manual transactions, and the locking removes the conflicts retries exist for. |
| Lock scope | Every state change takes the lock | Confirm and cancel can otherwise interleave into inconsistent pairs. |
| Waitlist processing | Inside the transaction of the change that frees the slot | A crash between cancel and processing would otherwise lose the offer. |
| Idempotency | Key inserted inside the reservation's transaction, unique index on `(UserId, Key)` | Concurrent duplicates wait and replay instead of erroring, with no "in progress" state to get stuck. See [idempotency.md](idempotency.md). |
| Failed requests and idempotency | Not remembered | Lets a client retry the same key after the conflict is gone. |
| Background work | `BackgroundService` with `PeriodicTimer` | No extra library or tables. Hangfire was evaluated and removed. |
| Expired users | Do not return to the queue | Avoids loops and extra complexity. They can join again manually. |
| Offer expiry | Checked at confirm time and by a worker | A late worker can never let an expired offer be confirmed. |
| Waitlist notifications | Log line and `?status=Pending` | Email or SMS would be over-engineering for this scope. |

## API design

| Decision | Chosen | Why |
|---|---|---|
| Confirm an offer | `POST /api/reservations/{id}/confirm` | A state transition, not a field update. |
| Cancel and decline | `DELETE /api/reservations/{id}` setting `Cancelled` | Idempotent 204, and the row is kept. |
| Process-waitlist endpoint | Dropped | Processing is automatic. A second path into the same logic is a chance to bypass the lock. |
| Resource deletion | `DELETE` deactivates | Historical reservations must remain valid. |
| Oversized page | 400, never clamped | A silent clamp hides a client bug. |
| Sorting | Fixed enums | No client-supplied column names reach a query. |
| Enums in JSON | Strings | Readable, and bindable by name in query strings. |
| Availability | Unpaginated, bounded to 31 days | A timeline has no meaningful "page 2". |
| Availability privacy | Times only | Anyone sees when a room is taken, not who took it. |
| Nullable required fields | `Guid?` / `DateTimeOffset?` with `[Required]` | A missing value is a 400 instead of silently becoming `Guid.Empty` or `0001-01-01`. |
| Waitlist routes | Create and list nested under the resource, delete flat | An entry exists in relation to a resource, but its id alone identifies it. |
| Errors | One global `IExceptionHandler` and Problem Details | No `try/catch` in controllers, one consistent format, no leaked internals. |

## Decisions that changed during design

| Original idea | Final | Reason |
|---|---|---|
| `UserId` as a string from the JWT, with no Users table | `Guid` with a real foreign key to Identity | The database now refuses reservations for users that do not exist. |
| `int` identity ids | `Guid` v7 | Project owner's requirement, with the FIFO reasoning corrected accordingly. |
| Confirm needs no lock | Confirm takes the lock | Confirm and cancel can interleave on the same Pending row. |
| In-progress idempotent request returns 409 | Second request waits, then replays | Better client experience and no stuck state. |
| Hangfire for expiry and cleanup | `BackgroundService` workers | Fewer dependencies and no extra tables. |
| Process-waitlist admin endpoint | Removed | One path into the processing logic. |

## Known limitations and possible future work

| Limitation | Possible improvement |
|---|---|
| The 10-upcoming-reservations limit is soft | A per-user lock, or a count check inside a user-level lock |
| No refresh tokens, roles live in the token | A refresh-token table and short access tokens |
| No account lockout or rate limiting | Identity lockout and ASP.NET Core rate limiting |
| Notifications are log lines | An `INotificationSender` abstraction with an email implementation |
| Replay returns current state, not a snapshot | Store the response body with the key |
| Several instances duplicate worker effort | A distributed lock or a dedicated worker host |
| No automated tests | Unit tests for Domain, integration tests against a real SQL Server (the in-memory provider cannot prove locking or constraints) |
| Search uses `LIKE '%term%'` | A full-text index if the resource table ever grows large |
| No per-user cap on waitlist entries | A cap similar to the reservation limit |
| A user can queue for a slot they already hold | A rule rejecting it, if it ever matters |
| Offers cannot be extended | An endpoint to extend a deadline once |
