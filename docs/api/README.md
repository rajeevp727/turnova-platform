# TurnOva API contract

**Contract version:** v1  
**Runtime:** .NET 10 / ASP.NET Core minimal APIs  
**Audience:** TurnOva Rider and Driver native apps, approved UnionManager integrations and controlled operations tooling.

## Generated OpenAPI

In Development and automated Testing, the API serves the generated OpenAPI document at:

`GET /openapi/v1.json`

The document is generated from ASP.NET Core endpoint metadata. Contract integration tests assert core routes and their security schemes. `OpenApiEndpointPolicy` is a case-insensitive allow-list for Development and automated Testing only; Production, Staging, QA and custom environments do not map the endpoint. `OpenApiEndpointPolicyTests` cover the environment policy and `AuthApiIntegrationTests.OpenApiDocumentIsNotMappedInStaging` verifies the Staging route returns 404. Production host startup is not exercised by this deterministic fixture because Production requires Cosmos; the Production policy is tested directly. Publish this checked-in contract guidance rather than exposing internal route metadata on a public production host. The Testing-only `/__test/throw` exception route is explicitly excluded from API descriptions and never appears in the generated contract.

## Authentication and authorization

| Endpoint family | Authentication |
|---|---|
| `POST /api/v1/auth/{role}/register` | Public; server fixes app ID and allowed Rider/Driver role |
| `POST /api/v1/auth/{role}/login` | Public; server fixes app ID and verifies returned role |
| `POST /api/v1/auth/refresh` | Public refresh-token exchange; opaque token is forwarded unchanged |
| `POST /api/v1/auth/logout` | Best-effort upstream revocation; accepts an expired/invalid bearer so the auth service can decide whether revocation is possible |
| `GET /api/v1/auth/me` | Valid TurnOva JWT required; request is rejected locally before TriSend is called when unauthenticated |
| `POST /api/v1/auth/demo/{role}` | Public but disabled by default; requires explicit DemoAuth:Enabled=true and valid JWT issuer/audience/signing-key configuration; supports only Rider/Driver demo identities |
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
- `POST /api/v1/auth/demo/{role}` — opt-in restricted demo session for rider or driver only

Only `rider` and `driver` route values are accepted. The API owns `appId=turnova` and the corresponding role; clients cannot select an arbitrary role.

### Restricted demo sessions

- POST /api/v1/auth/demo/rider and POST /api/v1/auth/demo/driver issue a sample identity only when DemoAuth:Enabled=true; the default is disabled and disabled routes return 404.
- Only the fixed Rider and Driver roles are supported. Unknown roles return 404.
- The host must provide valid JWT issuer, audience and signing-key configuration; the signing key must contain at least 32 characters. Missing/invalid configuration returns 503. Demo auth is allowed only when explicitly enabled in the named non-production environments Development, Testing, Staging, QA or Demo. Production, blank and every unknown environment name fail closed even when DemoAuth:Enabled=true.
- The API issues a 30-minute access token with a demo=true claim and no refresh token.
- Successful issuance returns the typed `DemoSessionResponse` contract: `accessToken`, `refreshToken` (null), `expiresIn` (1800 seconds), `isDemo`, and `user` (`id`, `email`, `name`, `role`, `isDemo`). The generated OpenAPI operation documents the typed 200 response schema, 404 disabled/unknown-role response, and 429/503 Problem Details responses.
- Middleware rejects demo tokens from normal authenticated application endpoints before their handlers execute. A `/api/v1/demo` path prefix alone is not an authorization grant: the endpoint must also carry `DemoEndpointMetadata`, and only GET/HEAD routes are allowed. Marked mutation routes remain blocked. No production demo business endpoints are currently mapped; demo screens remain local sample content. The middleware also rechecks the allowlisted environment and `DemoAuth:Enabled` on every request, so disabling the flag immediately blocks still-valid previously issued demo tokens.
- SignalR's special query-string `access_token` transport is not an exception: a demo JWT is rejected with HTTP 403 on `POST /hubs/rides/negotiate` before hub negotiation or group membership. The hub remains for real authenticated Rider/Driver sessions only.
- Anonymous demo-token issuance uses an ASP.NET Core in-process fixed-window limiter: 10 requests per server-observed remote IP per process per minute (HTTP 429 after the limit). It does not trust arbitrary client-supplied forwarding headers. Behind a reverse proxy, the observed address may be the proxy address unless trusted forwarded-header middleware is configured. Multi-instance demo hosting must use a shared/distributed limiter or an equivalent trusted gateway limit; the current limiter is not a cross-instance guarantee.
- Demo tokens must never be treated as real Rider/Driver sessions. The API enforces the Production deny; enable the flag only on an isolated non-production demo host.
- The mobile demo screens display sample content only. They do not prove live API/Cosmos connectivity and must keep booking, fare negotiation, payment, availability, ride acceptance and live GPS actions disabled.


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

**Automated verification note (2026-11-17):** owner media responses are tested for both parsed Cache-Control directives, `private` and `no-store`; HTTP serialization order is not part of the contract. The first exact-head run found two order-sensitive test assertions and the test-only correction is in `1d37d685fdda9b20d8967c887cee9647a60fd5cf`. Exact-head CI #44140212462 passed 753/753 backend tests and native non-container .NET 10 publish (artifact 14353189121); mobile/Android/iOS jobs were correctly skipped under the API phase. This documentation update still requires its own exact-head CI.

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

Operations-only routes require `X-Driver-Admin-Key`; they are controlled integration surfaces, not a Fleet/Ops client workflow:

- `POST /api/v1/ops/unions` — creates an active Cab-serving Union. The request must specify exactly the Cab service class. The code is trimmed and uppercased, and a deterministic SHA-256-derived ID is used for new records. A successful create returns HTTP 201; an equivalent normalized code returns HTTP 409. Cosmos uses create-if-absent in the existing `Unions` container partitioned by `/unionId`; the in-memory adapter uses `ConcurrentDictionary.TryAdd`. Existing legacy Union IDs and records are not rewritten.
- `GET /api/v1/ops/unions` and `GET /api/v1/ops/unions/{unionId}` — list/read Union records.
- `PUT /api/v1/ops/unions/{unionId}/status` — update Union status.
- `GET /api/v1/ops/unions/{unionId}/drivers` — read the Union's assigned Driver members.
- `PUT /api/v1/ops/unions/{unionId}/drivers/{driverId}/selfie-review` — controlled Operations review path; public Rider visibility still requires approval by the Driver's current authenticated UnionManager.

The Cosmos create-if-absent boundary treats only HTTP 409 as a duplicate result. The in-memory adapter checks cancellation before ConcurrentDictionary.TryAdd; the Cosmos adapter forwards the caller's token. Other provider failures propagate and are not misreported as duplicate Union codes. Deterministic mocked-container tests cover these mappings; live Cosmos cross-instance contention, ETag and restart validation remain external.

### Legacy compatibility

The zone/queue and `/api/v1/rides/request`, pending-offer, available-ride, accept/reject and offer-timeout endpoints remain legacy compatibility surfaces. They must not introduce customer-facing FIFO ranking, automatic assignment or automatic next-driver advancement into the approved Rider-selected Cab flow. Existing non-Cab history is preserved and is not converted to Cab.

## Approved AI assistance (AI-01 supplemental)

AI-01 is approved supplemental Rider/Driver functionality and is outside the 19-row acceptance denominator.

### Rider trip-intent draft

- `POST /api/v1/assistance/trip-intent` requires a Rider JWT and accepts only `{ "text": "..." }`. The response contains `pickup`, `dropoff`, `serviceClass` (always `Cab`), nullable `passengerCount`, `needsClarification`, `missingFields`, `requiresUserConfirmation` (always `true`) and `source`.
- Route and passenger fields are parsed locally. The response is an editable draft only; it does not create a ride, invoke discovery, select a Driver, propose/confirm an agreement or change ride state.
- If a future provider adapter is enabled, it receives fixed pickup/drop-off placeholders and a minimized allow-list of generic trip-action words. It cannot replace the locally parsed route or supply a passenger count. Exact route text, passenger count and arbitrary personal context remain local.

### Approved-content help

- `POST /api/v1/assistance/help` requires a Rider or Driver JWT and accepts only `{ "question": "...", "rideState": "Completed" }`; `rideState` is optional and must match a known ride state. Unknown states return the safe escalation response.
- The response contains `answer`, `sourceId`, `escalationRequired` and `source`. Help text is selected from approved in-repository content or a deterministic escalation response. A provider may select only an approved source ID; provider-generated free text is never returned.
- Provider prompts contain only allow-listed generic keywords and approved source IDs. Raw questions, ride identifiers, precise locations, identity/KYC data, contacts, payment details, tokens and private chat are not provider inputs.

### Shared controls and limits

- Both routes require authentication and apply a fixed-window rate limit partitioned by authenticated subject. `AiAssistant:MaxRequestsPerMinute` defaults to 10 and is validated from 1 to 60.
- Request bodies are limited to 16 KiB before service/provider processing. `AiAssistant:MaxInputCharacters` defaults to 1000 and is validated from 64 to 2000. Provider timeout defaults to four seconds and is validated from one to ten seconds; caller cancellation is propagated.
- Endpoint logs contain use-case/source/outcome metadata only; prompt text and user-supplied values are not logged. The API integration regression `AssistantRequestLogsNeverContainUserPromptOrSensitiveValues` asserts that route, prompt, contact and payment values do not appear in the assistance logger; unit regression `HelpProviderReceivesOnlyAllowListedKeywordsAndSourceIds` asserts that help providers receive only minimized keywords/source IDs.
- Assistance is disabled by default. The checked-in configuration has empty provider/model/endpoint/credential-reference placeholders and the API registers `UnavailableAiAssistantProvider`; no paid provider, model, credentials or live provider integration is selected or claimed. Deterministic fallback remains available.
- AI-01 cannot set/recommend fares, book, charge, approve KYC/selfies, change eligibility/dispatch/ride state, or make safety/legal decisions. A separate Driver offer-explanation route is optional and is not part of this delivered increment.

## Health and error behavior

- `GET /health/live` reports process liveness.
- `GET /health/ready` performs a bounded storage dependency probe and returns 503 when storage is unavailable or the probe times out. Development/automated Testing in-memory mode is ready for deterministic local use. If Staging, QA or another non-Development/Testing environment has no Cosmos configuration, the API may retain its in-memory adapters for isolated test-harness compatibility, but readiness returns a generic 503; process-local volatile storage must not receive hosted application traffic. Production fails startup without Cosmos. A hosted Staging/QA deployment must configure and validate Cosmos before it can become ready.
- Unexpected exceptions return generic Problem Details with a trace ID; raw exception details, credentials, tokens, exact coordinates and request bodies are not returned.
- Common contract outcomes include 400 validation, 401 missing/invalid authentication, 403 role/scope denial, 404 non-owned/not-found resources, 409 state/concurrency conflict, 413 oversized request body, 503 unavailable configuration/dependency and 504 upstream timeout.

## Historical API phase follow-ups — superseded by the current checkpoint

PR [#179](https://github.com/rajeevp727/turnova-platform/pull/179) implements neutral Rider/Driver support intake, reporter-partitioned Cosmos persistence, deterministic ETag/transactional-batch and migration contracts, OpenAPI documentation and API-first CI gates. The preceding docs head `aaee8df8585013aa95d89d19dad5f68a8bcf099c` passed API-only CI [#44115448770](https://github.com/rajeevp727/turnova-platform/actions/runs/44115448770): backend **724/724**, native non-container publish artifact [14341634913](https://github.com/rajeevp727/turnova-platform/actions/runs/44115448770/artifacts/14341634913), scope passed and every mobile/Android/iOS job correctly skipped. Retention code head `fb4e0c92a27796d2ec1d5cfbb6b5bc6af4fe1e31` now adds a null-by-default `SupportRequests:RetentionDays` placeholder, positive/overflow-safe validation, startup and per-request fail-closed checks for persistent support intake, per-item `ttl` seconds and `DefaultTimeToLive=-1` container enforcement. Each request captures the validated TTL once; a configuration reload that removes policy approval or required retention returns 503 before store access. The retention changes and current documentation synchronization require fresh exact-head API CI.

Support categories, severity, assignment, escalation/SLA, attachments and the retention duration remain Product/Operations decisions; code must not invent them. `SupportRequests:RetentionDays` is null in checked-in configuration. Any enabled persistent Cosmos support intake requires a positive approved value; the container uses `DefaultTimeToLive=-1` and each item stores `ttl = RetentionDays × 86400`. Any non-Development/Testing support intake requires `SupportRequests:PolicyApproved=true`, valid retention days and durable Cosmos configuration. Missing/invalid retention or missing Cosmos fails closed before the API accepts support content; with policy disabled, the route returns 503 before binding or persisting content. Staging/QA/custom non-development hosts may retain in-memory adapters for isolated tests, but readiness is 503 and those adapters must not receive hosted traffic. Live Cosmos ETag/multi-instance/restart, production TriSend/JWT identity, hosted API and provider validation remain external gates.


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

The 16 KiB request-body limit is declared through endpoint metadata and documented as HTTP 413 in OpenAPI. PR [#182](https://github.com/rajeevp727/turnova-platform/pull/182) is merged at `ea42ae49f200738316e72a1505d93ab7265bd0e2`. Its real Kestrel host-level test asserts HTTP 413 for a known Content-Length above 16 KiB and zero support-store writes. Final documentation-head CI [#44144136443](https://github.com/rajeevp727/turnova-platform/actions/runs/44144136443) passed scope, .NET 10 Release build, **754/754 backend tests (0 failed, 0 skipped)** and native non-container API publish/artifact verification; Android/iOS jobs were correctly skipped while API remains active.



## Private media and KYC storage — production gate

- The API exposes a provider-agnostic `IPrivateDriverMediaStore` boundary and issues opaque, random references bound to the authenticated Driver and media type. References are not URLs and are not authorization tokens.
- New Driver selfie/profile writes persist `ProfilePhotoStorageReference` and leave `ProfilePhotoBase64` null. Historical inline Base64 records remain readable for compatibility; no destructive bulk migration is performed.
- Drivers can upload, retrieve and poll scan status for their own media. A UnionManager can retrieve a document only while the Driver has a current active assignment to a Cab-serving Union managed by that account. Riders never receive KYC document references or content.
- Upload validates the declared media type, MIME type, JPEG/PNG/PDF signature and decoded size. PendingScan content cannot be used for onboarding approval, Union review or public discovery. Private responses are no-store and nosniff.
- Automated Testing uses a deterministic in-memory adapter with an explicit fake Clean result; Development returns PendingScan. Staging/Production use an unavailable adapter and fail closed with HTTP 503. The fake Clean result is not malware-scanning evidence.
- Production remains blocked until Infrastructure/Security supplies private object storage, malware scanning/quarantine, managed identity and provider-backed private retrieval, and Product/Legal approves consent, retention and deletion. Live provider/Cosmos validation is not claimed.


## Legacy FIFO compatibility boundary

`POST /api/v1/rides/request` is retained only for Development/Testing compatibility regressions. It returns HTTP 409 outside those local/test environments even if `Dispatch:LegacyFifoRiderRequestsEnabled` is set. The current Rider flow is Union-grouped discovery followed by explicit `POST /api/v1/rider/bookings`; no customer-facing FIFO ranking or automatic Driver assignment is part of the MVP.

When the compatibility route is enabled in Development/Testing, it uses full GUID ride IDs, create-if-absent persistence and Rider-owner-scoped idempotency replay. Both selected-booking and legacy replay lookups verify the referenced ride belongs to the authenticated Rider before returning a response, including the Cosmos legacy conflict path. A stale/corrupt mapping cannot return another Rider's ride, and a ride-ID collision cannot overwrite an existing ride.

Legacy Cosmos cleanup for the Development/Testing compatibility route is also fenced: it point-reads the Rider-partitioned idempotency item, requires exact item ID/Rider ID/ride ID plus a nonblank observed ETag, and deletes with `IfMatchEtag`. A missing or mismatched record is not deleted; post-read HTTP 404/412 are lost cleanup races; non-race provider failures propagate. This route remains disabled outside Development/Testing and does not expose customer-facing FIFO ranking or automatic assignment. Fifteen deterministic `CosmosLegacyRideIdempotencyCleanupTests` cases cover the contract. Live Cosmos contention/restart is not inferred from these mocks.

Persistence environment contract: Production requires CosmosDb:Endpoint and CosmosDb:Key; partial endpoint/key configuration fails in every environment. Development/Testing may use in-memory persistence and report ready. Staging/QA/custom non-Development/Testing hosts may retain in-memory adapters for isolated test harnesses, but `/health/ready` returns generic HTTP 503. Enabling support intake in any non-Development/Testing environment requires explicit policy approval, positive approved retention and Cosmos persistence. This support-intake guard is separate from the Production-only startup requirement for the rest of the API.

## Cosmos startup migration lease

The API creates the `MigrationHistory` and `MigrationLocks` containers before applying ordered schema migrations. A runner acquires the `schema-migrations` lease using create-if-absent or expired-lease recovery with an ETag precondition. The lease lasts 30 minutes and is renewed every 10 minutes using the latest ETag while migrations run.

Every renewal preserves the current owner and uses `IfMatchEtag`. If renewal loses its ETag race or the lease is replaced, the coordinator cancels the migration token. The runner checks cancellation after each migration before writing its history marker. The heartbeat is stopped and awaited before release; nested cleanup still attempts release with the latest ETag if a cancellation callback or heartbeat task faults; cleanup failures are not silently swallowed. Release cannot delete another instance's replacement lease. A conditional replace that returns HTTP 404 (the lease disappeared after the point read) or HTTP 412 (the ETag changed) is treated as a lost race and the coordinator retries acquisition. If create reports HTTP 409 but the following point read returns 404, the coordinator waits the configured retry delay before retrying rather than hot-spinning. Non-race provider failures such as HTTP 503 propagate.

Automated fake-store and Cosmos adapter tests cover expired-lease recovery, active-lease retry, renewal, lost-lease cancellation, ETag preconditions and latest-ETag release, including 404/412 replace races, 503 propagation, disappearing-lease retry/backoff, cancellation-before-in-memory-Union-mutation and fail-closed validation of lease ID/owner/ETag metadata (COSMOS-MIG-021). COSMOS-MIG-022 additionally validates the actual successful create/replace response Resource: it must be non-null, use the fixed lease ID, match the exact requested/new owner and include a nonblank ETag; no local request-body fallback is used. Live multi-instance contention and process crash/restart recovery still require a configured Cosmos environment and are not claimed by deterministic tests.


## Approved AI assistance — AI-01 supplemental API contract

AI-01 is an approved supplemental requirement and remains outside the 19-row acceptance denominator.

- `POST /api/v1/assistance/trip-intent` requires a Rider JWT. It returns an editable Cab trip draft and always requires explicit Rider confirmation; it does not create a ride, select a Driver, set a fare or mutate booking state.
- `POST /api/v1/assistance/help` requires a Rider or Driver JWT. It returns exact approved in-repository help content or a deterministic escalation response; it does not invent support categories, SLAs, fares, ETAs or policy.
- Both endpoints reject request bodies larger than 16 KiB with HTTP 413 before service/provider processing. OpenAPI declares the 413 response.
- Request DTOs reject unmapped JSON properties. Trip intent accepts only `text`; help accepts only `question` and the optional allow-listed `rideState`. Extra ride IDs, exact coordinates, fare, contact, payment or token fields return HTTP 400.
- The service separately enforces `AiAssistant:MaxInputCharacters` (default 1000, validated range 64–2000). Provider prompts use placeholder pickup/drop-off tokens and allow-listed generic keywords; exact route text, passenger count and arbitrary personal context remain local. Help providers receive only allow-listed keywords/source IDs; the API returns the approved repository answer, not provider-generated policy.
- Rate limiting is partitioned by authenticated subject after authentication/authorization. `AiAssistant:MaxRequestsPerMinute` defaults to 10 and is validated from 1 to 60.
- Assistance is disabled by default and uses `UnavailableAiAssistantProvider` until a provider is selected and validated. No paid provider/model, endpoint or credential is selected. Deterministic fallback remains available.
- AI-01 must never book, charge, set/recommend fares, approve KYC, change eligibility/dispatch/ride state, or make safety/legal decisions. Live provider sandbox, managed-secret wiring and data-processing/legal terms remain external gates.
- PR [#183](https://github.com/rajeevp727/turnova-platform/pull/183) merged at `dda08764b25f2a2b4f5e7665e85a59e539d8663e`. Exact-head CI [#44154364366](https://github.com/rajeevp727/turnova-platform/actions/runs/44154364366) passed **758/758 backend tests (0 failed, 0 skipped)** and native non-container API publish/artifact validation (artifact [14360582057](https://github.com/rajeevp727/turnova-platform/actions/runs/44154364366/artifacts/14360582057)).


## API engineering phase exit — 2026-11-18 (superseded 2026-11-19)

The API engineering phase remains `in_progress`. PR #197 merged at `8d5bcb6b913f51250d9f0fc4f0d521a973130747` without independent human approval, so that historical merge does not satisfy the current phase-transition gate. PR #200 is the sole ready/active API follow-up; its current branch head and exact-head verification are recorded in the final handoff below. Android must not resume until independent human approval, a permitted merge and a fresh main/API exit audit. Live Cosmos ETag/multi-instance/restart, hosted Staging/QA readiness, production identity/hosting/providers, private object storage/scanning/consent, product support/dispatch/reconciliation policies and legal terms remain external; no production readiness is claimed.


## Role-scoped ride response contracts (2026-11-19)

Mobile-facing ride endpoints return explicit role-scoped DTOs rather than the internal DemoRide aggregate:

- Rider POST /api/v1/rider/bookings returns RiderSelectedBookingResponse with Rider-visible booking and approved Cab/Union display fields only. It does not echo RiderId or DriverId: the Rider app already supplied the selected Driver in the request and does not consume actor IDs from the response. It omits DispatchZoneId, agreement collections/revision, payment reconciliation and audit events.
- Rider GET /api/v1/rides/{rideId}, POST /api/v1/rides/{rideId}/cancel, and the legacy POST /api/v1/rides/request return RiderRideDetailsResponse. It omits RiderId, DriverId, DispatchZoneId, agreement persistence internals, payment reconciliation and audit events. Pickup coordinates are returned only when valid. Driver coordinates and their timestamps require an approved Union Cab driver snapshot, the stored sharing flag, a location-sharing lifecycle status and valid coordinates. Historical non-Cab/unionless records keep their service-class history label but hide Driver name, vehicle, Union and location details. EstimatedFareInr is always null.
- Driver POST /api/v1/rides/{rideId}/accept, /arrive, /start and /complete, plus the legacy GET /api/v1/driver/rides/available, return DriverRideHistoryItem. It omits RiderId, DriverId, DispatchZoneId, raw agreement collections/revision and audit events while retaining the Driver-visible route/status, current agreement summary and reconciliation record.
- GET /api/v1/rides/{rideId}/agreements and POST /api/v1/rides/{rideId}/agreements return RideAgreementVersionResponse / RideAgreementProposalResponse. They expose versioned terms and both confirmations without actor IDs, UnionId, idempotency key or request hash.

These projections reduce API disclosure; they do not replace authorization. Every route retains its existing Rider/Driver role and ownership checks. API integration tests cover both role projections, legacy compatibility, agreement responses and location-sharing suppression. PR #195 source/test/phase-config head `ef1feddbf71e2f0ea2b72c8ffde33fcbe5a8977e` passed exact-head CI #44497833447. The documentation-bearing head `38dc6d8eb4bdeec075ea7fe995f1d1f3b82fbf6f` passed exact-head CI [#44504446025](https://github.com/rajeevp727/turnova-platform/actions/runs/44504446025): 763/763 backend tests, native non-container .NET 10 Release linux-x64 publish, publish verifier (11 negative cases plus valid fixture), artifact [14520060067](https://github.com/rajeevp727/turnova-platform/actions/runs/44504446025/artifacts/14520060067). A later source/test correction at `b699453202064fcdb99ba4cf66cdbbb78a96ed07` removes unused RiderId/DriverId from RiderSelectedBookingResponse and adds API-PRIV-001 absence coverage. A test-only follow-up at `c211fa03f7b40e042376c0d701efa988b17f684b` corrected a stale concurrency test; exact-head CI [#44513190938](https://github.com/rajeevp727/turnova-platform/actions/runs/44513190938) passed 763/763 backend tests, native non-container .NET 10 Release publish, verifier 11 negative cases plus valid fixture, artifact [14523623180](https://github.com/rajeevp727/turnova-platform/actions/runs/44513190938/artifacts/14523623180). Mobile/Android/iOS jobs remain skipped by the API-only phase gate. PR #195 was merged to main as `d499196ef3333fd9697c6f14d7a88b84bb897474` without an independent review recorded, contrary to `.github/copilot-instructions.md`. This process exception remains unresolved. Do not change phase state or advance to Android until an independent human review of the merged response-minimization change and current API work is recorded, then re-audit main. Live Cosmos ETag/multi-instance/restart and production integrations remain external gates.

## Historical PR #195 exact-head API CI — 2026-11-19

PR #195 documentation-bearing head `2ba41b55b901595e05489458690a2205db5eaa83` passed exact-head [CI #44513877029](https://github.com/rajeevp727/turnova-platform/actions/runs/44513877029): scope/backend success, **763/763 backend tests (0 failed, 0 skipped)**, native non-container .NET 10 Release linux-x64 publish, artifact validation, and publish verifier (11 negative fixtures plus valid fixture). Artifact: [14525350285](https://github.com/rajeevp727/turnova-platform/actions/runs/44513877029/artifacts/14525350285). Mobile-auth, mobile, Android and iOS jobs were skipped by the API-only phase gate. PR #195 was merged to main as `d499196ef3333fd9697c6f14d7a88b84bb897474` without an independent review recorded, contrary to `.github/copilot-instructions.md`. This process exception remains unresolved; do not change the API phase state or advance to Android until an independent human review of the merged response-minimization change is recorded and main is re-audited. No production readiness is claimed.

## Current API phase checkpoint — 2026-11-19

Main is `d499196ef3333fd9697c6f14d7a88b84bb897474`. Its exact-head API CI [#44531886570](https://github.com/rajeevp727/turnova-platform/actions/runs/44531886570) passed **763/763 backend tests**, native non-container .NET 10 Release linux-x64 publish and publish verification (artifact [14534262215](https://github.com/rajeevp727/turnova-platform/actions/runs/44531886570/artifacts/14534262215)). Android and iOS jobs were skipped by the API-only phase gate.

PR [#197](https://github.com/rajeevp727/turnova-platform/pull/197), exact source/test head `2531f1b458d219c4d0bfa3cae879f95b355ebbc3`, passed [CI #44553419519](https://github.com/rajeevp727/turnova-platform/actions/runs/44553419519): scope/backend success, **790/790 backend tests (0 failed, 0 skipped)**, native non-container .NET 10 Release linux-x64 publish, publish verifier (11 negative fixtures plus valid fixture), artifact [14544357218](https://github.com/rajeevp727/turnova-platform/actions/runs/44553419519/artifacts/14544357218). Mobile-auth, mobile, Android and iOS jobs were correctly skipped by the API-only gate.

PR #197 engineering additions:
- `COSMOS-MIG-011`: runner-level lease-loss regression proves an in-flight migration cannot write its MigrationHistory marker after lease ownership is lost.
- `COSMOS-MIG-012`–`015`: deterministic Cosmos lease adapter create/read/replace/release mapping, ETag handling, invalid timing and lease identity/release guards.
- `COSMOS-UNION-001`–`002`: Union create-if-absent Cosmos boundary maps only HTTP 409 to duplicate, does not fall back to memory, forwards the `/unionId` partition/cancellation and propagates other provider failures.
- `COSMOS-STORE-005`–`006`: RideOffer Cosmos create maps 409 to duplicate and propagates non-conflict failures; stale/missing ETag replacement maps 412/404 to false.

These are deterministic mock/fake-store tests, not live Cosmos evidence. Live multi-instance ETag contention, partition consistency, process crash/restart and production persistence remain external.

**Historical review exception and current gate:** PR #197 was merged without independent human approval; this remains a disclosed process exception and does not authorize a phase transition. PR #200 is the sole active API follow-up and has three owner-authored COMMENT-only reviews, no independent approval and zero unresolved threads. Do not self-approve, bypass policy or merge. Keep API `in_progress`, Android/iOS `not_started`, until independent human approval, permitted merge and a fresh main/API phase-exit audit.

Other external API gates remain live Cosmos, production identity/secrets/hosting, approved dispatch radius/location freshness/negotiation lifetime, maps/routing/SignalR, private-media provider/scanning/consent/retention, support/reconciliation policy, Union legal approval and live provider validation. No live Cosmos or production readiness is claimed.


## Latest API idempotency and persistence hardening — 2026-11-20

- Both selected-booking and Development/Testing legacy idempotency create-conflict paths treat a follow-up Cosmos HTTP 404 after HTTP 409 as a retryable conflict; other provider failures propagate. The shared helper also rejects blank IDs and honors caller cancellation before SDK access.
- Selected-booking replay now distinguishes a genuinely missing ride (eligible for bounded interrupted-create recovery) from an existing ride owned by another Rider. The API checks the raw ride identity internally and returns a generic conflict before acquiring reservations; it never projects the foreign ride.
- Selected-booking idempotency cleanup is fenced. In-memory cleanup uses exact key/value compare-and-remove. Cosmos cleanup validates id, Rider, ride, request hash and creation timestamp, then deletes with the observed ETag. A concurrent delete/replacement (404/412) is a lost cleanup race; non-race provider failures are not hidden.
- Deterministic coverage: `CosmosCreateConflictReadTests` (5 cases), `RiderIdempotencyPrivacyIntegrationTests.StaleSelectedBookingIdempotencyReplayCannotRecoverAnotherRidersRide`, `InMemoryRideIdempotencyReservationsTests.TryRemoveExactDoesNotDeleteAReplacementBookingRecord`, and six `CosmosRideIdempotencyCleanupTests`.
- Exact source/test head `d811abe29f67bca4f214ffccf075423583af13e5` passed API-only CI [#44659282694](https://github.com/rajeevp727/turnova-platform/actions/runs/44659282694): **841/841 backend tests**, native non-container .NET 10 Release linux-x64 publish, verifier (11 negative fixtures plus valid fixture), and artifact [14594194463](https://github.com/rajeevp727/turnova-platform/actions/runs/44659282694/artifacts/14594194463). These are deterministic/mock-backed checks, not live Cosmos multi-instance/restart evidence. Mobile/Android/iOS jobs were skipped by the API-only phase gate.


## Current API follow-up handoff — 2026-11-20

The API phase remains `in_progress`; Android and iOS remain `not_started`. PR #197 merged at `8d5bcb6b913f51250d9f0fc4f0d521a973130747` without independent human approval, so its historical merge does not satisfy the current phase-transition gate. PR [#200](https://github.com/rajeevp727/turnova-platform/pull/200) is the sole ready/active API follow-up on `test/api-nondev-support-readiness-20261120`. Source/test head `4afdd81de66b7e7772c27301de0a8fd53a238cee` is included in last verified branch checkpoint `ec226e501c736c6ad449c64e2fc630c06749f127`. Exact-head API-only CI [#44728587496](https://github.com/rajeevp727/turnova-platform/actions/runs/44728587496) passed scope/backend, 933/933 tests (0 failed, 0 skipped), Release build, native non-container .NET 10 linux-x64 publish, publish verifier (11 negative fixtures plus valid fixture), and artifact [14625840809](https://github.com/rajeevp727/turnova-platform/actions/runs/44728587496/artifacts/14625840809). Mobile/Android/iOS jobs were intentionally skipped. The current PR head is a documentation-only successor and must pass its own exact-head CI. The three recorded PR #200 reviews are owner-authored COMMENT-only; independent human approval is absent and there are zero unresolved threads. Do not self-approve or merge. PR #196 and #198 remain Draft/parked.

The remaining external gates are live Cosmos ETag/partition/multi-instance/restart and process recovery; Staging/QA Cosmos provisioning and hosted readiness; shared/edge demo rate limiting and trusted reverse-proxy forwarded headers; production JWT/TriSend identity, secrets, API hosting and live providers; Product-approved radius/zones/location freshness/negotiation lifetime; maps/routing/SignalR/call/chat/push; private-media storage/scanning/managed identity/consent/retention/deletion; support and reconciliation policy; Union legal approval; and Android/iOS physical-device, signing, store and TestFlight evidence. Strict acceptance remains 0/19 Verified (16 In Progress, 3 Blocked); production readiness is not established. After independent approval and a permitted merge, re-read main and repeat the API phase-exit audit before Android Rider.


## Authentication response caching

All /api/v1/auth route responses receive Cache-Control: no-store and Pragma: no-cache before authentication and authorization run. This includes demo-session issuance, Rider/Driver login, refresh, logout and profile reads, including authentication/authorization rejections. Do not cache access tokens, refresh tokens or profile responses. The policy does not change token lifetime, role validation or the upstream identity provider.


## AUTH-API-017 — exception responses retain no-store headers

The auth-route cache middleware uses `HttpResponse.OnStarting` so its `Cache-Control: no-store` and `Pragma: no-cache` headers are re-applied after the outer exception handler clears a failed response. A Testing-only exception route verifies a generic HTTP 500 without exception-message disclosure. Exact source/test head `81d4ac6e1eb565c5b0f3eef9f561edd48433fa9c` passed API-only CI [#44749883561](https://github.com/rajeevp727/turnova-platform/actions/runs/44749883561), including backend build/tests and native non-container publish/verifier (artifact [14633839847](https://github.com/rajeevp727/turnova-platform/actions/runs/44749883561/artifacts/14633839847)).

## Latest AUTH-API-017 verification

Source/test head `81d4ac6e1eb565c5b0f3eef9f561edd48433fa9c` passed API-only CI [#44749883561](https://github.com/rajeevp727/turnova-platform/actions/runs/44749883561), including backend build/tests and native non-container publish/verifier (artifact [14633839847](https://github.com/rajeevp727/turnova-platform/actions/runs/44749883561/artifacts/14633839847)). Last verified docs head `6e6677dfababfaf7ffb082d7ed5ea6e852b35a2b` passed exact-head CI [#44751331326](https://github.com/rajeevp727/turnova-platform/actions/runs/44751331326), with artifact [14635576706](https://github.com/rajeevp727/turnova-platform/actions/runs/44751331326/artifacts/14635576706).

Main is now `c9280eec23dd4fb5fab06b11e24d2a619b489235` after PR #204, which only adds Android `usesCleartextTraffic: true` for trusted-LAN demo calls. It is not Android completion evidence. PR #200 remains API-only and must not advance the platform phase before independent approval and a fresh main/API audit.


## AUTH-API-018 — invalid-bearer cache protection

The auth response cache policy applies to invalid-bearer profile reads as well as successful login, demo issuance, disabled/unknown demo routes, demo rate-limit rejection, demo-token authorization rejection and generic auth-route exceptions. `GET /api/v1/auth/me` with an invalid bearer returns HTTP 401 with `Cache-Control: no-store` and `Pragma: no-cache`; the request is rejected locally and does not call TriSend. The demo-token 403 regression also asserts both headers. These checks are supplemental API security tests and do not change the 19-row acceptance denominator.

## Run 6 — live API handoff (2026-11-20)

- **Current main:** `c9280eec23dd4fb5fab06b11e24d2a619b489235`.
- **Current platform phase:** API `in_progress`; Android and iOS `not_started` for completion work.
- **Sole ready/active feature PR:** [#200](https://github.com/rajeevp727/turnova-platform/pull/200), branch `test/api-nondev-support-readiness-20261120`, latest source/test head `fbcf19240289ad3349f751faaf8983eb3b6b99d0`. GitHub reports mergeable. The branch is 120 commits ahead / 2 behind current main; the two main-only commits are the weekly-build workflow and PR #204's Android trusted-LAN cleartext setting. Do not merge those unrelated mobile config changes into this API PR merely to synchronize.
- **AUTH-API-018:** added `MeEndpointRejectsInvalidBearerTokenWithNoStoreHeaders`; invalid bearer on `GET /api/v1/auth/me` returns 401 with `Cache-Control: no-store` and `Pragma: no-cache`, without calling TriSend. The existing demo-token 403 test now also asserts both headers.
- **Exact source/test-head CI:** [#44759040207](https://github.com/rajeevp727/turnova-platform/actions/runs/44759040207) completed successfully: scope and backend jobs passed; .NET 10 Release build, backend test execution/verifier, native non-container Linux x64 publish, publish-script syntax, native publish verifier and artifact validation all passed. API publish artifact: [14641077685](https://github.com/rajeevp727/turnova-platform/actions/runs/44759040207/artifacts/14641077685). The check-run metadata does not expose the numeric test total; do not invent one. Mobile-auth/mobile/Android/emulator/iOS jobs were intentionally skipped by the API-only phase gate.
- **Previous numeric baseline:** exact-head CI [#44752166958](https://github.com/rajeevp727/turnova-platform/actions/runs/44752166958) passed 934/934 backend tests on prior head `31a522215a9d934f2838d1bc2f901f8bf4f63df2`; AUTH-API-018 adds one integration test after that baseline.
- **Phase discrepancy:** main `.github/turnova-phase.json` still says API complete / Android in_progress. PR #200's phase file correctly says API in_progress / Android not_started / iOS not_started. PR #204 only enables trusted-LAN HTTP for local demos; it is not Rider/Driver Android completion evidence.
- **Review gate:** PR #200 remains open and ready; three reviews are owner-authored COMMENT-only, no independent human approval is recorded, and there are zero unresolved review threads. Never self-approve or bypass repository policy. PR #197 was merged without independent human approval; retain that historical process exception. PR #196 and #198 remain Draft/parked. PR #204 is merged configuration-only work.
- **Acceptance accounting:** 0/19 strictly Verified; 16 In Progress and 3 Blocked (DISPATCH-01, DRIVER-04, SUPPORT-01). This is full-evidence accounting, not an implementation percentage. Production readiness is not established.
- **External gates:** live Cosmos ETag/partition/multi-instance/restart; Staging/QA provisioning and readiness; production JWT/TriSend identity, secrets and API hosting; shared/edge rate limiting and trusted proxy configuration; Product-approved radius/zones/location freshness/negotiation lifetime; maps/routing/SignalR/call/chat/push; private-media storage/scanning/consent/retention; support/reconciliation policy; Union legal approval; Android/iOS physical-device and signing/store/TestFlight evidence.
- **Next exact action:** continue API-only safe code/test/docs work while PR #200 remains open. Obtain independent human approval, verify latest head and required checks, merge only when permitted, then re-read main and perform a fresh API phase-exit audit before Android Rider. Complete Android Rider, Android Driver, iOS Rider and iOS Driver strictly in that order. No live Cosmos, provider or production validation is claimed.
