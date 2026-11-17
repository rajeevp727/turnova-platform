# TurnOva API contract

**Contract version:** v1  
**Runtime:** .NET 10 / ASP.NET Core minimal APIs  
**Audience:** TurnOva Rider and Driver native apps, approved UnionManager integrations and controlled operations tooling.

## Generated OpenAPI

In Development and automated Testing, the API serves the generated OpenAPI document at:

`GET /openapi/v1.json`

The document is generated from ASP.NET Core endpoint metadata. Contract integration tests assert core routes and their security schemes. The document endpoint is deliberately not mapped in Production; publish this checked-in contract guidance with the application rather than exposing internal route metadata on a public production host. The Testing-only `/__test/throw` exception route is explicitly excluded from API descriptions and never appears in the generated contract.

## Authentication and authorization

| Endpoint family | Authentication |
|---|---|
| `POST /api/v1/auth/{role}/register` | Public; server fixes app ID and allowed Rider/Driver role |
| `POST /api/v1/auth/{role}/login` | Public; server fixes app ID and verifies returned role |
| `POST /api/v1/auth/refresh` | Public refresh-token exchange; opaque token is forwarded unchanged |
| `POST /api/v1/auth/logout` | Best-effort upstream revocation; accepts an expired/invalid bearer so the auth service can decide whether revocation is possible |
| `GET /api/v1/auth/me` | Valid TurnOva JWT required; request is rejected locally before TriSend is called when unauthenticated |
| Rider endpoints | JWT with `role=Rider` |
| Driver endpoints | JWT with `role=Driver` |
| `/api/v1/union/*` | JWT with `role=UnionManager` and one or more server-issued `unionId` claims; handlers filter to those current assignments |
| `/api/v1/ops/*` | Operations-only `X-Driver-Admin-Key`; minimum 32-character server-side secret, constant-time comparison |

The generated document uses the HTTP Bearer scheme for endpoints with authorization metadata and an API-key scheme for the operations routes. Never put the operations key, JWT signing key, Cosmos key or TriSend credentials in either mobile application.

The API project pins transitive `Microsoft.OpenApi` to patched 2.7.5. The initial OpenAPI package restore selected 2.0.0 and failed the repository's NU1903-as-error policy because of GHSA-v5pm-xwqc-g5wc / CVE-2026-49451; the explicit 2.7.5 pin remains on the compatible 2.x line.

## Core route catalogue

### Authentication

- `POST /api/v1/auth/{role}/register`
- `POST /api/v1/auth/{role}/login`
- `POST /api/v1/auth/refresh`
- `POST /api/v1/auth/logout`
- `GET /api/v1/auth/me`

Only `rider` and `driver` route values are accepted. The API owns `appId=turnova` and the corresponding role; clients cannot select an arbitrary role.

### Driver onboarding and eligibility

- `GET /api/v1/driver/onboarding/me`
- `POST /api/v1/driver/onboarding`
- `PUT /api/v1/driver/onboarding/selfie`
- `GET /api/v1/driver/availability`
- `POST /api/v1/driver/availability`
- `POST /api/v1/driver/location`
- `GET /api/v1/driver/union`
- `GET /api/v1/driver/payslips/{year}/{month}.pdf`

New onboarding and ride eligibility are Cab-only. Document values are private storage references, not public URLs or document contents. A public selfie requires approval by the current union.

### Rider discovery and selected booking

- `GET /api/v1/rider/discovery`
- `POST /api/v1/rider/bookings`
- `GET /api/v1/rides/mine`
- `GET /api/v1/rides/{rideId}`

Discovery groups approved Cab-serving unions and returns at most three eligible available Cab drivers per union, ordered by driver-to-pickup proximity. The Rider selects a Driver. Journey distance is not inferred from pickup proximity.

Selected booking uses a caller-provided `Idempotency-Key`, server-owned reservation records and a bounded negotiation lease. The lease remains disabled when its product-configured lifetime is missing or invalid.

### Agreement, trip and reconciliation

- `GET /api/v1/rides/{rideId}/agreements`
- `POST /api/v1/rides/{rideId}/agreements`
- `POST /api/v1/rides/{rideId}/agreements/{version}/confirm`
- `POST /api/v1/rides/{rideId}/start`
- `POST /api/v1/rides/{rideId}/arrive`
- `POST /api/v1/rides/{rideId}/location`
- `POST /api/v1/rides/{rideId}/complete`
- `POST /api/v1/rides/{rideId}/cancel`
- `POST /api/v1/driver/rides/{rideId}/payment-report`
- `GET /api/v1/driver/rides/mine`
- `GET /api/v1/union/reconciliation`

Agreement versions are immutable. Both parties confirm the same current version before the Driver can start. A renegotiation creates a new version and requires both confirmations again. After completion, the Driver reports the amount received directly; the API records it as unverified and calculates agreed amount minus amount received. No gateway, escrow, customer dispute/confirmation or automatic next-ride policy is provided.

### Union and operations

UnionManager routes expose only active Cab assignments within the manager's signed `unionId` claims:

- `GET /api/v1/union/drivers`
- `GET /api/v1/union/reconciliation`
- `PUT /api/v1/union/drivers/{driverId}/selfie-review`

Operations-only endpoints use `X-Driver-Admin-Key` and are not a Fleet/Ops client workflow. Operations may manage union records, assignments and history; public selfie approval remains reserved for the current authenticated UnionManager.

### Legacy compatibility

The zone/queue and `/api/v1/rides/request`, pending-offer, available-ride, accept/reject and offer-timeout endpoints remain legacy compatibility surfaces. They must not introduce customer-facing FIFO ranking, automatic assignment or automatic next-driver advancement into the approved Rider-selected Cab flow. Existing non-Cab history is preserved and is not converted to Cab.

## Health and error behavior

- `GET /health/live` reports process liveness.
- `GET /health/ready` performs a bounded storage dependency probe and returns 503 when storage is unavailable or the probe times out.
- Unexpected exceptions return generic Problem Details with a trace ID; raw exception details, credentials, tokens, exact coordinates and request bodies are not returned.
- Common contract outcomes include 400 validation, 401 missing/invalid authentication, 403 role/scope denial, 404 non-owned/not-found resources, 409 state/concurrency conflict, 503 unavailable configuration/dependency and 504 upstream timeout.

## API phase follow-ups still open

The API phase is not complete. The current audit has identified support-request intake and Cosmos adapter-level deterministic tests as remaining engineering work, in addition to the OpenAPI/auth changes in the current increment. Support categories, severity, assignment, escalation/SLA and retention remain Product/Operations decisions; code must not invent them. Live Cosmos ETag/multi-instance/restart and production TriSend/JWT validation remain external gates.


## Neutral Rider/Driver support intake

`POST /api/v1/support/requests` accepts a Rider or Driver JWT and an `Idempotency-Key` header. The JSON body contains only `subject` and `description`.

- First submission: HTTP 202 with request ID, server timestamp and `replayed=false`.
- Same reporter/key/content: HTTP 200 with the original request ID/timestamp and `replayed=true`.
- Same reporter/key with different content: HTTP 409; the existing item is never overwritten.
- Missing/invalid key or invalid body: HTTP 400.
- Anonymous caller: HTTP 401; other roles: HTTP 403.

The Cosmos `SupportRequests` container uses partition key `/reporterId`. The item ID is a SHA-256 digest of the reporter ID and idempotency key; the raw key is not stored. The response does not echo the reporter ID or request text, and support content is not logged.

There is intentionally no support-request list/read endpoint, attachment upload, category, severity, priority, assignment, escalation, response target or retention setting. Cosmos TTL remains unset until Product/Operations approves a retention policy. Those decisions remain external gates.

Support intake is enabled by default only in Development and Testing. Staging/Production require the explicit `SupportRequests:PolicyApproved=true` setting after Product/Operations has approved the intake, privacy, access and retention policy. Until then the authenticated route returns HTTP 503 without binding or persisting the submitted body. The checked-in default is false.
