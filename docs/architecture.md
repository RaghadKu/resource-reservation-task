# Architecture

## Layers and the dependency rule

```
Api ──► Application ──► Domain
 │           ▲
 └─► Infrastructure ─┘
```

- **Domain** depends on nothing: no NuGet packages, no EF Core, no ASP.NET Core.
- **Application** depends only on Domain (plus EF Core abstractions for `DbSet<T>` and async query extensions).
- **Infrastructure** implements the abstractions that Application defines.
- **Api** is the composition root. It references Infrastructure only to register services in dependency injection.
  Controllers never use Infrastructure types.

Each layer is a separate project, so the compiler enforces the rule. A single project with folders could not guarantee that
Domain never references EF Core.

## What lives where

### Domain (`ResourceReservation.Domain`)

```
Common/       BaseEntity, FieldLengths, TimeRange, BookingRules
Entities/     Resource, Reservation, WaitlistEntry, IdempotencyRecord
Enums/        ReservationStatus, WaitlistStatus
Exceptions/   DomainException, DomainValidationException, InvalidStateTransitionException
```

- Entities have private setters, private parameterless constructors for EF Core, factory methods (`Create...`) and
  transition methods (`Confirm`, `Cancel`, `Expire`, `CreateOffer`, ...). Nothing outside Domain can set `Status = Confirmed`.
- `TimeRange` is a value object for the half-open interval `[Start, End)`. A range with `Start >= End` cannot exist.
  `Overlaps` is the single definition of the overlap rule.
- Entities never call `DateTime.UtcNow`. They receive `now` as a parameter, which keeps Domain deterministic.
- `Guid.CreateVersion7()` generates ids in Domain, so an id is known before saving (useful to link an offer to its waitlist entry).
- There is no `User` entity. Domain holds a plain `Guid UserId`; the foreign key to the Identity table is configured in Infrastructure.

### Application (`ResourceReservation.Application`)

Organized by feature, not by technical type:

```
Abstractions/   IApplicationDbContext, ICurrentUser, IDateTimeProvider, IResourceLock
Auth/           IAuthService, request and response DTOs
Resources/      ResourceService, DTOs, query objects, mappings
Reservations/   ReservationService, DTOs, SlotQueries, idempotency helpers
Waitlist/       WaitlistService, WaitlistProcessor, OfferExpiryService, options
Idempotency/    IdempotencyCleanupService
Common/         PagedResult, PaginationQuery, Roles, application exceptions
```

Services are plain classes with methods such as `CreateAsync` and `CancelAsync`. They orchestrate: load data, enforce rules that
need the database, call Domain methods, save.

### Infrastructure (`ResourceReservation.Infrastructure`)

```
Persistence/      AppDbContext, ResourceLock, Configurations/ (Fluent API), Migrations/
Identity/         ApplicationUser, ApplicationRole, AuthService, JwtTokenGenerator, IdentitySeeder
BackgroundJobs/   PeriodicWorker, OfferExpiryWorker, IdempotencyCleanupWorker
Common/           SystemDateTimeProvider
DependencyInjection.cs
```

### Api (`ResourceReservation.Api`)

```
Controllers/   Auth, Resources, Reservations, Waitlist
Middleware/    GlobalExceptionHandler
Extensions/    AuthenticationExtensions, ExceptionHandlingExtensions
Services/      CurrentUser (reads the JWT claims from HttpContext)
Program.cs
```

## The path of a request

```
Client ──HTTP + JWT──► Controller ──► Service (Application) ──► Domain entity
                       (maps HTTP     (orchestrates: loads,     (enforces its own
                        to DTOs)       checks, saves)            rules, changes status)
                                           │
                                           ▼
                                   IApplicationDbContext ──► SQL Server
```

1. The controller binds the DTO and calls the service. It contains no logic and no `try/catch`.
2. The service reads the current user from `ICurrentUser`, checks anything that needs data (resource exists, slot free, user limit), and calls the Domain.
3. The Domain validates its own rules and throws a meaningful exception if broken.
4. A global exception handler converts exceptions to Problem Details responses.
5. The database has the last word through foreign keys, unique indexes and check constraints.

## Error model

Domain and Application throw typed exceptions with client-safe messages. They know nothing about HTTP.
The Api layer maps them in one place (`GlobalExceptionHandler`):

| Exception | Status |
|---|---|
| `DomainValidationException`, `BadRequestException` | 400 |
| `UnauthorizedException` | 401 |
| `ForbiddenException` | 403 |
| `NotFoundException` | 404 |
| `ConflictException`, `InvalidStateTransitionException` | 409 |
| `IdempotencyKeyReuseException` | 422 |
| `DbUpdateException` caused by a unique-index violation | 409 |
| Anything else | 500, generic message |

Rule for all code: never put internal data in an exception message, because messages can reach clients.

## Identity

ASP.NET Core Identity is used with exactly three tables: `AspNetUsers`, `AspNetRoles` and `AspNetUserRoles`, using `Guid` keys.
The claims, logins and tokens tables are removed from the model with `builder.Ignore<...>()`.

- `ApplicationUser : IdentityUser<Guid>` and `ApplicationRole : IdentityRole<Guid>` live in Infrastructure because they need the Identity package.
- `AddIdentityCore` (not `AddIdentity`) is used: no cookies, no UI. Authentication is JWT bearer.
- Only `UserManager` methods that touch Users and UserRoles are used (`CreateAsync`, `FindByEmailAsync`, `CheckPasswordAsync`,
  `AddToRoleAsync`, `GetRolesAsync`). Methods that need the removed tables (claims, external logins, token providers) would fail at runtime and are never called.
- `ICurrentUser` is defined in Application and implemented in Api, because only the web layer has an `HttpContext`.
  Background workers have no request and never use it.
- Role names are constants in `Application/Common/Roles.cs`, shared by Api (authorization attributes) and Infrastructure (seeding).

## Cross-cutting choices

- **Time:** `IDateTimeProvider` everywhere. Requests accept `DateTimeOffset`, services convert with `.UtcDateTime`, the database stores `datetime2` in UTC,
  and a value converter marks values read from SQL Server as `DateTimeKind.Utc`.
- **Async:** all I/O is asynchronous, with `CancellationToken` flowing from the controller to the database.
- **Configuration validation:** JWT and waitlist options are validated at startup (`ValidateOnStart`), so a bad configuration fails fast instead of at the first login.
- **DTOs:** entities are never returned. Query projections map to DTOs in SQL through `Expression` mappings.
- **Pagination:** one extension method (`ToPagedResultAsync`) serves every list endpoint.

## Key design decisions

### Four projects instead of one with folders
Layer boundaries need to be enforced, not just agreed. The compiler stops Domain from referencing EF Core. Four projects is the
minimum that proves the architecture without over-engineering.

### No repositories: `IApplicationDbContext` instead
`DbContext` already is a unit of work and `DbSet` already is a repository. A generic repository on top hides useful EF features
(`Include`, projections, transactions) and adds boilerplate. Application defines a small `IApplicationDbContext` that exposes
the `DbSet`s, `Database` (for transactions) and `SaveChangesAsync`, and Infrastructure implements it.
Trade-off: Application references the EF Core package. This is slightly less pure than repositories, and it was accepted to keep the code small and explainable.

### Services, not CQRS or MediatR
One service per feature is easy to read, easy to explain, and enough for this scope. No mediator pipeline or command/query split was needed.

### Hand-written mappings
Projections use `Expression<Func<Entity, Dto>>` that EF translates into a `SELECT` of only the needed columns. The same definition serves single entities.
No mapping library is required.

### No automated test project
The assignment was scoped without one. The concurrency, idempotency and waitlist guarantees are demonstrated with repeatable manual scripts
and SQL invariant queries ([manual-verification.md](manual-verification.md)). A test project can be added later without restructuring:
unit tests would target Domain (`TimeRange`, state transitions), and integration tests would target a real SQL Server, because the in-memory provider cannot prove locking or constraints.
