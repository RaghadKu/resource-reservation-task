# Resource Reservation & Smart Waitlist API

A RESTful backend for reserving shared resources (meeting rooms, desks, equipment, parking spaces, labs) for a time period.
Its core promise is simple and strict: **the same resource can never be booked twice for overlapping times, even when many
requests arrive at the same moment.** When a resource is fully booked, users join a fair first-in-first-out waiting queue,
and a freed slot is automatically offered to the next person in line.

Built with C#, ASP.NET Core (.NET 9), Entity Framework Core and SQL Server, following Clean Architecture.

## Contents

1. [Highlights](#highlights)
2. [Tech stack](#tech-stack)
3. [Architecture at a glance](#architecture-at-a-glance)
4. [Getting started from scratch](#getting-started-from-scratch)
5. [Configuration reference](#configuration-reference)
6. [A five-minute tour](#a-five-minute-tour)
7. [API overview](#api-overview)
8. [How the main problems are solved](#how-the-main-problems-are-solved)
9. [Business rules in brief](#business-rules-in-brief)
10. [Known limitations](#known-limitations)
11. [Troubleshooting](#troubleshooting)
12. [Documentation index](#documentation-index)

## Highlights

- **No double booking under concurrency.** Every write path takes a row lock on the resource inside a transaction,
  then checks for overlaps and inserts. Proven with a 20-parallel-request test (one `201`, nineteen `409`).
- **Smart waitlist.** FIFO queue with computed positions. When a slot frees up, the next eligible user gets a time-limited
  offer (a `Pending` reservation that holds the slot). Unconfirmed offers expire automatically and pass to the next person.
- **Idempotent reservation creation.** An `Idempotency-Key` header makes retries and double clicks safe. Keys are stored in SQL Server.
- **Clean Architecture** with four projects, so the compiler enforces the dependency rule.
- **Consistent errors.** One global exception handler returns RFC 9457 Problem Details for every failure.
- **Production-minded API.** JWT authentication with roles, DTOs everywhere, validation, paging with a hard maximum,
  filtering, sorting, cancellation tokens, OpenAPI/Swagger documentation with examples for every endpoint.

## Tech stack

| Area | Choice |
|---|---|
| Language / runtime | C# 13, .NET 9 |
| Web framework | ASP.NET Core Web API (controllers) |
| Data access | Entity Framework Core 9 with the SQL Server provider, Fluent API configuration, code-first migrations |
| Database | SQL Server (2019 or newer) |
| Authentication | ASP.NET Core Identity (users, roles, user-roles tables only) with JWT bearer tokens |
| Background work | `BackgroundService` with `PeriodicTimer` (built into .NET, no extra library) |
| API documentation | Swagger / OpenAPI with XML comments |
| Errors | Problem Details (RFC 9457) through `IExceptionHandler` |
| Architecture | Clean Architecture: Domain, Application, Infrastructure, Api |

No MediatR, no CQRS, no generic repositories, no AutoMapper. The reasons are in [docs/decisions.md](docs/decisions.md).

## Architecture at a glance

```
Api ──► Application ──► Domain
 │           ▲
 └─► Infrastructure ─┘
```

```
ResourceReservation/
├── src/
│   ├── ResourceReservation.Domain/          entities, enums, TimeRange, business rules (no packages)
│   ├── ResourceReservation.Application/     use cases, DTOs, abstractions, pagination
│   ├── ResourceReservation.Infrastructure/  EF Core, Identity, JWT, resource lock, background workers
│   └── ResourceReservation.Api/             controllers, middleware, authentication, Swagger
├── docs/                                    detailed documentation
├── Directory.Build.props                    shared compiler settings
├── ResourceReservation.sln
└── README.md
```

- **Domain** depends on nothing. Entities protect their own state through factory and transition methods.
- **Application** contains the workflows (`ReservationService`, `WaitlistService`, `ResourceService`, ...) and talks to the database through `IApplicationDbContext`.
- **Infrastructure** implements the abstractions: `AppDbContext`, Fluent API configurations, the resource lock, Identity, JWT, workers.
- **Api** is the composition root. Controllers are thin and contain no business logic.

Details: [docs/architecture.md](docs/architecture.md).

## Getting started from scratch

### 1. Install the prerequisites

| Tool | Version | Notes |
|---|---|---|
| .NET SDK | 9.0 or newer 9.x | Check with `dotnet --version` |
| SQL Server | 2019 or newer | Developer, Express, LocalDB, a full instance or a container all work |
| `dotnet-ef` | 9.0.x | Needed only for manual migrations |
| Git | any recent | |
| An editor | Visual Studio 2022, VS Code or Rider | Optional |
| SSMS or Azure Data Studio | any | Optional, handy to inspect the database |

Install the EF Core command-line tool once per machine:

```bash
dotnet tool install --global dotnet-ef --version 9.0.*
```
If it is already installed, run `dotnet tool update --global dotnet-ef --version 9.0.*`.

### 2. Get the code

Clone or download the repository and open a terminal in its root folder (the one that contains `ResourceReservation.sln`).

### 3. Point the API at your SQL Server

The default connection string in `src/ResourceReservation.Api/appsettings.json` is:

```
Server=localhost;Database=ResourceReservationDb;Trusted_Connection=True;TrustServerCertificate=True;
```

It works with a default local SQL Server instance and Windows authentication. For other setups, override it **without
editing tracked files**, using user-secrets:

```bash
dotnet user-secrets init --project src/ResourceReservation.Api
```

| Your SQL Server | Command |
|---|---|
| Named instance (for example SQL Express) with Windows authentication | `dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=.\SQLEXPRESS;Database=ResourceReservationDb;Trusted_Connection=True;TrustServerCertificate=True;" --project src/ResourceReservation.Api` |
| LocalDB | `dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=(localdb)\MSSQLLocalDB;Database=ResourceReservationDb;Trusted_Connection=True;" --project src/ResourceReservation.Api` |
| SQL authentication (user `sa`) | `dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=localhost,1433;Database=ResourceReservationDb;User Id=sa;Password=your-sa-password;TrustServerCertificate=True;" --project src/ResourceReservation.Api` |

Replace `your-sa-password` with your real password in the last command. The database does not need to exist: it is created automatically.

### 4. Run

```bash
dotnet build
dotnet run --project src/ResourceReservation.Api
```

On the first start in the **Development** environment (the default when running with `dotnet run`), the application:

1. applies all EF Core migrations, creating the database and tables;
2. creates the roles `Admin` and `User`;
3. creates a development administrator (see below);
4. starts the two background workers (offer expiry every 30 seconds, idempotency-key cleanup every hour).

The console prints the listening URLs (`Now listening on: https://localhost:...`). Open **`<that URL>/swagger`** for the interactive documentation.

If your browser or curl does not trust the development HTTPS certificate, run `dotnet dev-certs https --trust` once
(Windows and macOS), or use `curl -k`.

### 5. Sign in

The development administrator comes from `appsettings.Development.json`:

| Email | Password | Role |
|---|---|---|
| `admin@example.com` | `Admin#12345` | Admin |

Call `POST /api/auth/login` with these credentials, copy the `accessToken`, and click **Authorize** in Swagger UI
(enter `Bearer <token>` if your Swagger setup asks for the full header value, otherwise just the token).
Regular users register themselves with `POST /api/auth/register`.

### Resetting the database

```bash
dotnet ef database drop --force --project src/ResourceReservation.Infrastructure --startup-project src/ResourceReservation.Api
dotnet run --project src/ResourceReservation.Api
```

### Creating or applying migrations manually

```bash
dotnet ef migrations add <MigrationName> --project src/ResourceReservation.Infrastructure --startup-project src/ResourceReservation.Api --output-dir Persistence/Migrations
dotnet ef database update --project src/ResourceReservation.Infrastructure --startup-project src/ResourceReservation.Api
```
Replace `<MigrationName>` with a descriptive name such as `AddRoomFloorColumn`. Migrations are applied automatically only in
Development; in other environments run `dotnet ef database update` as an explicit deployment step.

### Running outside Development

Set `ASPNETCORE_ENVIRONMENT=Production` and provide the secrets through environment variables (the double underscore is the
configuration separator):

```bash
export ASPNETCORE_ENVIRONMENT=Production
export ConnectionStrings__DefaultConnection="Server=...;Database=ResourceReservationDb;..."
export Jwt__SigningKey="a-random-secret-of-at-least-32-characters"
export SeedAdmin__Email="you@yourcompany.com"
export SeedAdmin__Password="a-strong-password"
dotnet run --project src/ResourceReservation.Api --no-launch-profile
```
The application refuses to start if `Jwt:SigningKey` is shorter than 32 characters. The development key and admin password
committed in `appsettings.Development.json` are for local use only and must never be used in production.

## Configuration reference

| Setting | Default | Purpose |
|---|---|---|
| `ConnectionStrings:DefaultConnection` | local SQL Server | Database connection |
| `Jwt:Issuer` | `ResourceReservation.Api` | Token issuer |
| `Jwt:Audience` | `ResourceReservation.Client` | Token audience |
| `Jwt:SigningKey` | empty in `appsettings.json`, set in Development | HS256 key, at least 32 characters, required |
| `Jwt:ExpiryMinutes` | `60` | Access token lifetime |
| `SeedAdmin:Email` / `Password` / `FullName` | set in Development only | The administrator created at startup. If empty, none is created. |
| `Waitlist:OfferWindowMinutes` | `15` (`1` in Development) | How long a user has to confirm an offer, 1 to 1440 |

## A five-minute tour

Use Swagger UI, or curl. Set `BASE` to the URL the console prints, for example `export BASE=https://localhost:7001` if that
is the URL shown. Dates must be in the future (the examples use November 2026). Always send times with `Z` (UTC).

```bash
# 1. Log in as admin and as two users
ADMIN=$(curl -sk -X POST $BASE/api/auth/login -H "Content-Type: application/json" \
  -d '{"email":"admin@example.com","password":"Admin#12345"}' | jq -r .accessToken)
curl -sk -X POST $BASE/api/auth/register -H "Content-Type: application/json" \
  -d '{"email":"alice@example.com","password":"Passw0rd!","fullName":"Alice"}' > /dev/null
curl -sk -X POST $BASE/api/auth/register -H "Content-Type: application/json" \
  -d '{"email":"bob@example.com","password":"Passw0rd!","fullName":"Bob"}' > /dev/null
ALICE=$(curl -sk -X POST $BASE/api/auth/login -H "Content-Type: application/json" \
  -d '{"email":"alice@example.com","password":"Passw0rd!"}' | jq -r .accessToken)
BOB=$(curl -sk -X POST $BASE/api/auth/login -H "Content-Type: application/json" \
  -d '{"email":"bob@example.com","password":"Passw0rd!"}' | jq -r .accessToken)

# 2. Admin creates a resource
RES=$(curl -sk -X POST $BASE/api/resources -H "Authorization: Bearer $ADMIN" -H "Content-Type: application/json" \
  -d '{"name":"Meeting Room A","description":"Ground floor","capacity":8}' | jq -r .id)

# 3. Alice reserves 10:00-11:00 (201)
BODY='{"resourceId":"'$RES'","startTime":"2026-11-10T10:00:00Z","endTime":"2026-11-10T11:00:00Z"}'
RESV=$(curl -sk -X POST $BASE/api/reservations -H "Authorization: Bearer $ALICE" \
  -H "Content-Type: application/json" -H "Idempotency-Key: tour-key-0001" -d "$BODY" | jq -r .id)

# 4. Retrying with the same key returns the same reservation (201 + Idempotent-Replayed: true)
curl -sk -i -X POST $BASE/api/reservations -H "Authorization: Bearer $ALICE" \
  -H "Content-Type: application/json" -H "Idempotency-Key: tour-key-0001" -d "$BODY"

# 5. Bob tries the same slot: 409. He joins the waitlist instead (201, position 1)
curl -sk -X POST $BASE/api/reservations -H "Authorization: Bearer $BOB" \
  -H "Content-Type: application/json" -H "Idempotency-Key: tour-key-0002" -d "$BODY"
curl -sk -X POST $BASE/api/resources/$RES/waitlist -H "Authorization: Bearer $BOB" \
  -H "Content-Type: application/json" \
  -d '{"startTime":"2026-11-10T10:00:00Z","endTime":"2026-11-10T11:00:00Z"}'

# 6. Alice cancels (204). Bob automatically receives a Pending offer
curl -sk -X DELETE $BASE/api/reservations/$RESV -H "Authorization: Bearer $ALICE"
curl -sk "$BASE/api/reservations?status=Pending" -H "Authorization: Bearer $BOB"

# 7. Bob confirms the offer before it expires (200, status Confirmed)
OFFER=$(curl -sk "$BASE/api/reservations?status=Pending" -H "Authorization: Bearer $BOB" | jq -r '.items[0].id')
curl -sk -X POST $BASE/api/reservations/$OFFER/confirm -H "Authorization: Bearer $BOB"
```
In Development the offer window is 1 minute. If Bob waits longer, the background worker expires the offer within 30 seconds
and the slot becomes free again. The commands use `jq` to extract values; install it or copy the values by hand.

## API overview

All endpoints except register and login require `Authorization: Bearer <token>`. Full request and response examples,
every status code and the validation rules are in Swagger UI and in [docs/api-reference.md](docs/api-reference.md).

| Method and path | Who | Success | Purpose |
|---|---|---|---|
| `POST /api/auth/register` | anyone | 201 | Create a user account |
| `POST /api/auth/login` | anyone | 200 | Get an access token |
| `GET /api/auth/me` | any user | 200 | Who am I |
| `POST /api/resources` | Admin | 201 | Create a resource |
| `GET /api/resources` | any user | 200 | List, filter, search, sort, page |
| `GET /api/resources/{id}` | any user | 200 | Get one |
| `PUT /api/resources/{id}` | Admin | 200 | Replace details and active flag |
| `DELETE /api/resources/{id}` | Admin | 204 | Deactivate (soft delete) |
| `GET /api/resources/{id}/availability?from&to` | any user | 200 | Busy and free periods |
| `POST /api/reservations` + `Idempotency-Key` | any user | 201 | Reserve |
| `GET /api/reservations` | user: own, admin: all | 200 | List with filters |
| `GET /api/reservations/{id}` | owner or Admin | 200 | Get one |
| `POST /api/reservations/{id}/confirm` | owner | 200 | Accept a waitlist offer |
| `DELETE /api/reservations/{id}` | owner or Admin | 204 | Cancel or decline an offer |
| `POST /api/resources/{id}/waitlist` | any user | 201 | Join the queue |
| `GET /api/resources/{id}/waitlist` | user: own, admin: all | 200 | List entries with position |
| `DELETE /api/waitlist/{id}` | owner or Admin | 204 | Leave the queue |

Conventions: JSON only, enums as strings, UTC timestamps, `pageNumber`/`pageSize` paging (default 20, maximum 100, an oversized
page size is rejected with 400), and errors as `application/problem+json`.

## How the main problems are solved

**Concurrency.** Two simultaneous requests can both see a free slot, so "check then insert" is not enough. Each write that
can add a blocking reservation starts a transaction and locks the resource row with `UPDLOCK, ROWLOCK`. The overlap check and
the insert then run with exclusive access to that resource's bookings. Other resources are unaffected, and readers are never
blocked. SQL Server cannot express "no overlapping ranges" as a constraint, so the guarantee comes from this discipline,
which is applied to booking, cancelling, confirming, joining and leaving the waitlist, deactivation and offer expiry.
See [docs/concurrency.md](docs/concurrency.md).

**Overlap rule.** Intervals are half-open `[start, end)`. 10:00-11:00 and 11:00-12:00 do not conflict.
The rule exists in one place (`TimeRange.Overlaps`) with SQL-translatable twins next to the entities.

**Waitlist.** An offer is a reservation with status `Pending`, so one table and one conflict rule cover both bookings and holds.
When a slot is freed, entries overlapping it are processed in FIFO order, a candidate is offered the slot only if its whole range is free,
and the status pairs of reservation and entry change together in one transaction.
See [docs/waitlist-and-background-workers.md](docs/waitlist-and-background-workers.md).

**Idempotency.** The key is claimed inside the same transaction as the reservation, protected by a unique index on
`(UserId, Key)`. Concurrent duplicates wait, then receive the original result. See [docs/idempotency.md](docs/idempotency.md).

**Errors.** Domain and Application throw meaningful exceptions; one handler in the Api layer maps them to status codes and
Problem Details. Internal details are never returned to clients.

## Business rules in brief

- Times are UTC. A reservation must start in the future and last 15 minutes to 12 hours.
- A resource has one active booking at any moment. `capacity` is the number of people it holds, not the number of simultaneous bookings.
- Names are unique. Resources are deactivated, never deleted, and cannot be deactivated while they have upcoming reservations.
- A user can hold at most 10 upcoming reservations.
- Cancelling is allowed only before the start, is idempotent, and triggers waitlist processing.
- A user can join the waitlist only for a slot that is actually unavailable, once per exact range.
- An offer lasts `Waitlist:OfferWindowMinutes` (never past the slot's start). Unconfirmed offers expire and the user does not return to the queue.

Full rules, state machines and the permission matrix: [docs/business-rules.md](docs/business-rules.md).

## Known limitations

- **No automated test project.** This was a deliberate scope decision. Correctness is demonstrated with manual verification
  scripts and SQL invariant checks in [docs/manual-verification.md](docs/manual-verification.md).
- **The 10-upcoming-reservations limit is soft.** The lock is per resource, so one user firing parallel requests at different
  resources could briefly exceed it.
- **Access tokens only, no refresh tokens.** Roles live in the token, so a role change applies after the token expires.
- **No account lockout or rate limiting.** Recommended before real production use.
- **Offers are notified through logs and `GET /api/reservations?status=Pending`.** There is no email or SMS.
- **A replayed idempotent request returns the reservation's current state**, which may be `Cancelled` if it was cancelled since.
- **Expiry granularity is about 30 seconds.** An expired offer still blocks its slot until the next sweep, but it cannot be confirmed in the meantime.
- **Several API instances would each run the workers.** This stays correct because every run takes the resource lock and re-checks state, at the cost of some duplicate work.

More trade-offs and the reasons behind them: [docs/decisions.md](docs/decisions.md).

## Troubleshooting

| Symptom | Likely cause and fix |
|---|---|
| Startup fails with "Connection string 'DefaultConnection' is missing" | The setting is not in `appsettings.json` or user-secrets. See step 3. |
| `A network-related or instance-specific error` | SQL Server is not running, the instance name is wrong, or TCP/IP is disabled. Check the service in SQL Server Configuration Manager. |
| `The certificate chain was issued by an authority that is not trusted` | Add `TrustServerCertificate=True;` to the connection string. |
| Startup fails with an invalid Jwt settings message | `Jwt:SigningKey` is missing or shorter than 32 characters. In Development it comes from `appsettings.Development.json`. |
| `dotnet ef` not found | Install the tool (see prerequisites) and reopen the terminal. |
| `Unable to create a DbContext` when running `dotnet ef` | Pass both `--project src/ResourceReservation.Infrastructure` and `--startup-project src/ResourceReservation.Api`, and make sure the solution builds. |
| 401 on every call | The token expired (60 minutes), was not sent as `Authorization: Bearer <token>`, or the issuer/audience settings changed. Log in again. |
| 403 on a resource endpoint | The endpoint needs the Admin role. Use the seeded administrator. |
| 400 on a date in a query string | A `+` in a time-zone offset became a space. Send `Z` or encode it as `%2B`. |
| 400 on `POST /api/reservations` mentioning the header | The `Idempotency-Key` header is missing or not 8 to 100 characters of letters, digits, `. _ : -`. |
| Offer never expires | The background worker runs every 30 seconds. The offer window in Development is 1 minute, so expect up to about 90 seconds in total. |
| Port already in use | Change the ports in `src/ResourceReservation.Api/Properties/launchSettings.json`. |

## Documentation index

| Document | What it covers |
|---|---|
| [docs/architecture.md](docs/architecture.md) | Layers, dependency rule, project contents, request path, design decisions |
| [docs/business-rules.md](docs/business-rules.md) | Interval rule, validation limits, state machines, waitlist rules, permissions |
| [docs/database.md](docs/database.md) | Tables, relationships, constraints, indexes and what each protects |
| [docs/concurrency.md](docs/concurrency.md) | The double-booking problem, options compared, the lock discipline |
| [docs/idempotency.md](docs/idempotency.md) | Key storage, uniqueness, retries, in-progress handling |
| [docs/waitlist-and-background-workers.md](docs/waitlist-and-background-workers.md) | Waitlist processing, offers, expiry workers |
| [docs/api-reference.md](docs/api-reference.md) | Endpoints, status codes, error format, paging, conventions |
| [docs/decisions.md](docs/decisions.md) | Decision log, rejected alternatives, trade-offs, future work |
| [docs/manual-verification.md](docs/manual-verification.md) | Scripts and SQL checks that prove the guarantees |
