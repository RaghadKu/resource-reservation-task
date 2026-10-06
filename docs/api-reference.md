# API reference

The interactive version, with request and response examples for every endpoint, is the Swagger UI at `/swagger`.
This page summarizes the contract and the conventions that apply everywhere.

## Conventions

| Topic | Rule |
|---|---|
| Format | JSON requests and responses |
| Authentication | `Authorization: Bearer <accessToken>` on everything except register and login |
| Enums | Serialized as strings (`"Confirmed"`), and bound by name in query strings (`?status=Pending`) |
| Times | Requests accept ISO 8601 with offset and are converted to UTC. Responses are UTC with a `Z` suffix. |
| Ids | GUIDs. Every id route is constrained to a GUID, so a malformed id returns 404. |
| `UserId` | Never in a request body. It always comes from the token. |
| Unknown JSON fields | Ignored |

**A `+` in a query string becomes a space.** `from=2026-11-10T10:00:00+03:00` fails to bind. Send `Z`, or encode the plus as `%2B`.

## Endpoints and status codes

| Endpoint | Who | Success | Error codes |
|---|---|---|---|
| `POST /api/auth/register` | anyone | 201 | 400, 409, 500 |
| `POST /api/auth/login` | anyone | 200 | 400, 401, 500 |
| `GET /api/auth/me` | any user | 200 | 401, 500 |
| `POST /api/resources` | Admin | 201 | 400, 401, 403, 409, 500 |
| `GET /api/resources` | any user | 200 | 400, 401, 500 |
| `GET /api/resources/{id}` | any user | 200 | 401, 404, 500 |
| `PUT /api/resources/{id}` | Admin | 200 | 400, 401, 403, 404, 409, 500 |
| `DELETE /api/resources/{id}` | Admin | 204 | 401, 403, 404, 409, 500 |
| `GET /api/resources/{resourceId}/availability` | any user | 200 | 400, 401, 404, 500 |
| `POST /api/reservations` | any user | 201 | 400, 401, 404, 409, 422, 500 |
| `GET /api/reservations` | user: own, admin: all | 200 | 400, 401, 500 |
| `GET /api/reservations/{id}` | owner or Admin | 200 | 401, 403, 404, 500 |
| `POST /api/reservations/{id}/confirm` | owner | 200 | 401, 403, 404, 409, 500 |
| `DELETE /api/reservations/{id}` | owner or Admin | 204 | 401, 403, 404, 409, 500 |
| `POST /api/resources/{resourceId}/waitlist` | any user | 201 | 400, 401, 404, 409, 500 |
| `GET /api/resources/{resourceId}/waitlist` | user: own, admin: all | 200 | 400, 401, 404, 500 |
| `DELETE /api/waitlist/{id}` | owner or Admin | 204 | 401, 403, 404, 409, 500 |

What each code means across the API:

| Code | Meaning |
|---|---|
| 400 | Invalid input: validation failure, bad paging, bad dates, missing or malformed idempotency key |
| 401 | Missing, invalid or expired token, or wrong credentials at login |
| 403 | Authenticated but not allowed: not the owner, or an admin-only endpoint |
| 404 | Resource, reservation or waitlist entry not found |
| 409 | The request is valid but conflicts with the current state: slot taken, inactive resource, duplicate waitlist entry, expired offer, already started, user limit reached, name taken |
| 422 | The idempotency key was reused with a different request |
| 500 | Unexpected error. The body is generic; details are only in the server log. |

## Error format

Every error is `application/problem+json` (RFC 9457):

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.10",
  "title": "Conflict",
  "status": 409,
  "detail": "The requested time slot is not available. You can join the waitlist: POST /api/resources/0199c1a5-1c44-7a02-8d3b-6e1f5a9c2b77/waitlist.",
  "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-00"
}
```

Validation failures (400) add an `errors` object with the same shape as ASP.NET Core's automatic model validation:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": {
    "Password": [ "The field Password must be a string with a minimum length of 8 and a maximum length of 100." ]
  },
  "traceId": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-00"
}
```

`traceId` lets a client report a problem that can be found in the server log. Missing or invalid tokens also return a Problem Details body, not an empty response.

## Pagination

Every collection endpoint except availability is paged.

| Parameter | Default | Rule |
|---|---|---|
| `pageNumber` | 1 | 1 to 1,000,000 |
| `pageSize` | 20 | 1 to 100. A larger value is rejected with 400, never silently reduced. |

```json
{
  "items": [],
  "pageNumber": 1,
  "pageSize": 20,
  "totalCount": 125,
  "totalPages": 7
}
```

- Every paged query ends with a unique tiebreaker (`Id`), so rows with equal sort values never repeat or vanish between pages.
- The count and the page are two queries, so a row inserted between them can make `totalCount` off by one.
- **Availability** is the one deliberate exception: it is a computed timeline, not a table, so "page 2" has no meaning. It is bounded by a 31-day maximum window.

## Filtering and sorting

| Endpoint | Parameters |
|---|---|
| `GET /api/resources` | `isActive`, `search` (name contains), `sortBy` (`Name`, `Capacity`, `CreatedAt`), `sortDirection` (`Asc`, `Desc`) |
| `GET /api/reservations` | `resourceId`, `status`, `from` (ends after), `to` (starts before). Fixed order: start time descending. |
| `GET /api/resources/{id}/waitlist` | `status`. Fixed order: first in, first out. |

Sorting uses fixed enums, never a column name from the client, so an invalid value is an automatic 400 and no string reaches a query.

## Headers

| Header | Where | Meaning |
|---|---|---|
| `Authorization: Bearer <token>` | requests | The access token |
| `Idempotency-Key` | `POST /api/reservations` request | Required, 8 to 100 characters |
| `Idempotent-Replayed: true` | `POST /api/reservations` response | The response is a replay of an earlier request |
| `Location` | 201 responses (except waitlist join) | URL of the created resource. Waitlist join points to the list URL. |

## Authentication details

- Tokens are HS256 JWTs valid for 60 minutes (`Jwt:ExpiryMinutes`), with claims `sub` (user id), `email`, `name`, `role` and `jti`.
- Clock skew tolerance is 30 seconds.
- Roles are `User` (everyone who registers) and `Admin` (seeded from configuration only; there is no way to become an admin through the API).
- Unknown email and wrong password return the same 401 message at login, so account existence is not revealed.

## Availability response

```json
{
  "resourceId": "0199c1a5-1c44-7a02-8d3b-6e1f5a9c2b77",
  "from": "2026-11-10T08:00:00Z",
  "to": "2026-11-10T14:00:00Z",
  "busyPeriods": [ { "start": "2026-11-10T10:00:00Z", "end": "2026-11-10T11:00:00Z" } ],
  "freePeriods": [
    { "start": "2026-11-10T08:00:00Z", "end": "2026-11-10T10:00:00Z" },
    { "start": "2026-11-10T11:00:00Z", "end": "2026-11-10T14:00:00Z" }
  ]
}
```

- Both lists are half-open windows clamped to the requested range.
- Busy periods contain times only, never user ids or names: anyone can see when a room is taken, but not who took it.
- Free periods start no earlier than now, and an inactive resource has none.
- Availability is advice and can be a moment stale. `POST /api/reservations` is the real arbiter.
