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
- `POST /api/v1/driver/onboarding/media`
- `GET /api/v1/driver/onboarding/media/{reference}`
- `GET /api/v1/driver/onboarding/media/{reference}/status`
- `POST /api/v1/driver/onboarding`
- `PUT /api/v1/driver/onboarding/selfie`
- `GET /api/v1/driver/availability`
- `POST /api/v1/driver/availability`
- `POST /api/v1/driver/location`
- `GET /api/v1/driver/union`
- `GET /api/v1/driver/payslips/{year}/{month}.pdf`

New onboarding and ride eligibility are Cab-only. New selfie/document uploads use the provider-agnostic `IPrivateDriverMediaStore` boundary. `POST /api/v1/driver/onboarding/media` validates the declared document type, MIME type and file signature, then returns a random opaque `drvmedia_` reference bound to the authenticated Driver and media type. The API never accepts a client-supplied URL as a storage location. New Driver profiles persist `ProfilePhotoStorageReference`; `ProfilePhotoBase64` is left null for new uploads and remains readable only for historical records. The Driver can retrieve their own media through the authenticated owner route; a UnionManager can retrieve a document only while the Driver is actively assigned to a Union managed by that account. Union document responses are private/no-store and only scan-cleared media is served. Rider discovery still exposes a selfie only after approval by the current Union and never returns KYC document references or content.

The current adapters are deliberately non-production: automated Testing uses a deterministic in-memory store with an explicit fake Clean result; Development uses an in-memory PendingScan store; Staging/Production use `UnavailablePrivateDriverMediaStore` and fail closed with HTTP 503. No real object-storage provider, malware scanner, managed identity, encryption/retention/consent policy is configured or claimed. Production onboarding requires server-issued Driver-bound, scan-cleared references. Development/Testing temporarily accept legacy document-reference fixtures for compatibility; this exception is not available in Staging/Production. Historical inline selfie records are preserved and are not bulk-migrated by this change.

### Private Driver media

- `POST /api/v1/driver/onboarding/media` — authenticated Driver upload. Request: `{ documentType, contentType, contentBase64 }`; accepted types are ProfilePhoto, DrivingLicence, VehicleRegistration, Insurance and Permit. The server validates JPEG/PNG/PDF signatures and bounded decoded size, stores through `IPrivateDriverMediaStore`, and returns an opaque Driver-bound reference plus scan status.
- `GET /api/v1/driver/onboarding/media/{reference}` — authenticated owner-only retrieval; response is `Cache-Control: private, no-store` with `X-Content-Type-Options: nosniff`. `GET /api/v1/driver/onboarding/media/{reference}/status` returns owner-only scan status and metadata without bytes, also private/no-store.
- `GET /api/v1/union/drivers/{driverId}/documents/{documentType}` — UnionManager-only retrieval. The handler rechecks the current active Driver-to-Union assignment, active Union and document membership; another Union cannot retrieve the document. Only scan-cleared media is served.
- `POST /api/v1/driver/onboarding` and `PUT /api/v1/driver/onboarding/selfie` accept exactly one of the legacy Base64 payload or a server-issued reference. Base64 input is immediately written through the private-media boundary; the new profile stores only the reference.
- A reference is not authorization by itself. Every read checks the authenticated Driver or the current UnionManager assignment. Rider endpoints never receive document references or document bytes.
- `PendingScan` media cannot be used for onboarding approval, Union review or public selfie discovery. The deterministic test adapter simulates Clean only in automated Testing; this is not malware-scan evidence.
- Production/Staging remain blocked until a real private object-storage provider, malware scanner, managed identity, consent evidence and approved retention/deletion policy are configured. The current unavailable adapter returns 503 rather than persisting Base64 in Cosmos.

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
- Common contract outcomes include 400 validation, 401 missing/invalid authentication, 403 role/scope denial, 404 non-owned/not-found resources, 409 state/concurrency conflict, 413 oversized request body, 503 unavailable configuration/dependency and 504 upstream timeout.

## API phase follow-ups still open

PR [#179](https://github.com/rajeevp727/turnova-platform/pull/179) implements neutral Rider/Driver support intake, reporter-partitioned Cosmos persistence, deterministic ETag/transactional-batch and migration contracts, OpenAPI documentation and API-first CI gates. The preceding docs head `aaee8df8585013aa95d89d19dad5f68a8bcf099c` passed API-only CI [#44115448770](https://github.com/rajeevp727/turnova-platform/actions/runs/44115448770): backend **724/724**, native non-container publish artifact [14341634913](https://github.com/rajeevp727/turnova-platform/actions/runs/44115448770/artifacts/14341634913), scope passed and every mobile/Android/iOS job correctly skipped. Retention code head `fb4e0c92a27796d2ec1d5cfbb6b5bc6af4fe1e31` now adds a null-by-default `SupportRequests:RetentionDays` placeholder, positive/overflow-safe validation, startup and per-request fail-closed checks for persistent support intake, per-item `ttl` seconds and `DefaultTimeToLive=-1` container enforcement. Each request captures the validated TTL once; a configuration reload that removes policy approval or required retention returns 503 before store access. The retention changes and current documentation synchronization require fresh exact-head API CI.

Support categories, severity, assignment, escalation/SLA, attachments and the retention duration remain Product/Operations decisions; code must not invent them. `SupportRequests:RetentionDays` is null in checked-in configuration. Any enabled persistent Cosmos support intake requires a positive approved value; the container uses `DefaultTimeToLive=-1` and each item stores `ttl = RetentionDays × 86400`. Staging/Production support intake requires `SupportRequests:PolicyApproved=true`, valid retention days and durable Cosmos configuration. Missing/invalid retention or missing Staging Cosmos configuration fails closed; with policy disabled, the route returns 503 before binding or persisting content. Live Cosmos ETag/multi-instance/restart, production TriSend/JWT identity, hosted API and provider validation remain external gates.


## Neutral Rider/Driver support intake

`POST /api/v1/support/requests` accepts a Rider or Driver JWT and an `Idempotency-Key` header. The JSON body contains only `subject` and `description`, with a 16 KiB maximum request body.

- First submission: HTTP 202 with request ID, server timestamp and `replayed=false`.
- Same reporter/key/content: HTTP 200 with the original request ID/timestamp and `replayed=true`.
- Same reporter/key with different content: HTTP 409; the existing item is never overwritten.
- Missing/invalid key or invalid body: HTTP 400.
- Anonymous caller: HTTP 401; other roles: HTTP 403.

The Cosmos `SupportRequests` container uses partition key `/reporterId`. The item ID is a SHA-256 digest of the reporter ID and idempotency key; the raw key is not stored. The response does not echo the reporter ID or request text, and support content is not logged.

There is intentionally no support-request list/read endpoint, attachment upload, category, severity, priority, assignment, escalation or response target. No retention duration is selected. `SupportRequests:RetentionDays` remains an unconfigured placeholder; when a persistent store is enabled, the API requires a positive approved value, the container enables per-item TTL with `DefaultTimeToLive=-1`, and each item receives `ttl` in seconds. Development/Testing in-memory intake may omit retention configuration.

Support intake is enabled by default only in Development and Testing. Staging/Production require `SupportRequests:PolicyApproved=true`, a positive Product/Operations-approved `SupportRequests:RetentionDays`, and configured Cosmos persistence. A policy-approved Staging/Production host without valid retention fails startup; Staging without `CosmosDb:Endpoint` and `CosmosDb:Key` also fails closed instead of accepting requests into an in-memory store. Production already requires Cosmos at startup. The checked-in policy default is false and retention days remain null until approved.


## Private media and KYC storage — production gate

- Current selfie bytes are stored inline in the Cosmos Driver profile as `ProfilePhotoBase64`; they are returned to the Driver/authorized Union review paths and included in public discovery only after current-Union approval.
- Driving-licence and vehicle document references are supplied by the Driver client. The API rejects absolute URLs and traversal-like strings, but no configured provider verifies reference ownership, scans/quarantines content, enforces consent/retention, or serves private content.
- Before production, implement a provider-agnostic private-media boundary with server-issued Driver-bound references, upload quarantine/malware scanning, consent evidence, retention/deletion, and current-Union-only retrieval. Riders must never receive KYC document references or content. No storage provider, credentials or legal retention policy are configured.
- Owner/input: Infrastructure/Security must provide the private object-storage/scanning service contract and managed identity; Product/Legal must approve consent and retention; Union operations must approve reviewer access. Until then this is an explicit external gate, not completed acceptance.

## Cosmos startup migration lease

The API creates the `MigrationHistory` and `MigrationLocks` containers before applying ordered schema migrations. A runner acquires the `schema-migrations` lease using create-if-absent or expired-lease recovery with an ETag precondition. The lease lasts 30 minutes and is renewed every 10 minutes using the latest ETag while migrations run.

Every renewal preserves the current owner and uses `IfMatchEtag`. If renewal loses its ETag race or the lease is replaced, the coordinator cancels the migration token. The runner checks cancellation after each migration before writing its history marker. The heartbeat is stopped and awaited before release; release uses the latest ETag and cannot delete another instance's replacement lease.

Automated fake-store and Cosmos adapter tests cover expired-lease recovery, active-lease retry, renewal, lost-lease cancellation, ETag preconditions and latest-ETag release. Live multi-instance contention and process crash/restart recovery still require a configured Cosmos environment and are not claimed by deterministic tests.
