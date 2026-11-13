# TurnOva — Architecture

**Status:** Initial target architecture  
**Product principle:** Everyone’s Turn Matters.

## 1. Scope

TurnOva is a mobile-only Android/iOS ride-hailing platform. The MVP includes TurnOva Driver and TurnOva Rider. TurnOva Fleet and TurnOva Ops are Phase 2 apps. All clients share a backend API, database and dispatch engine. No passenger or driver web app is in scope.

## 2. Technology baseline

| Concern | Standard |
|---|---|
| Backend | .NET 10 LTS, ASP.NET Core Web API |
| Language | C# 14 |
| Target framework | net10.0 across backend projects |
| Mobile | React Native + Expo |
| Database | Azure Cosmos DB |
| Realtime | SignalR |
| Authentication | JWT access tokens + refresh-token rotation |
| API contract | OpenAPI / Swagger |
| Source control | GitHub |

Use the latest supported .NET 10 servicing patch. Keep secrets out of source control.

## 3. High-level topology

    TurnOva Rider ─┐
    TurnOva Driver ├── HTTPS / SignalR ── ASP.NET Core API (.NET 10)
    TurnOva Fleet ─┤                         ├── Authentication / Authorization
    TurnOva Ops ───┘                         ├── Application use cases
                                             ├── Selected-driver booking + legacy queue compatibility
                                             ├── SignalR hubs
                                             ├── Cosmos DB repositories
                                             └── External provider adapters
                                                      ├── Maps / Routing
                                                      ├── OTP / SMS
                                                      ├── Call / Chat (optional)
                                                      └── Push notifications

There is no payment gateway or escrow adapter in the MVP. The Rider pays the Driver directly; the API stores only the Driver-reported/unverified amount received and its signed reconciliation delta. Call/Chat and the other external adapters remain provider placeholders until configured.

Fleet and Ops clients are Phase 2. The backend and ride-coordination services are shared platform services, not separate apps.

## 4. Backend solution structure

    src/backend/TurnOva/
    ├── TurnOva.API/
    ├── TurnOva.Application/
    ├── TurnOva.Domain/
    ├── TurnOva.Infrastructure/
    └── TurnOva.Tests/

- API: HTTP endpoints, validation, auth policies, SignalR hubs and composition.
- Application: use cases, orchestration, dispatch workflows and contracts.
- Domain: driver, vehicle, zone, queue, booking, trip and dispatch invariants.
- Infrastructure: Cosmos DB, external provider adapters and telemetry.
- Tests: domain/unit, integration, concurrency and dispatch tests.

Keep domain and application logic independent of Cosmos DB and transport concerns.

## 5. Core domain concepts

- Driver: identity, approval, availability and active vehicle.
- Vehicle: category, passenger capacity, registration and document validity.
- ServiceZone: geographic boundary and dispatch policy.
- QueueMembership: driver, zone, service class, server-assigned entry order and state.
- RideRequest: rider, pickup/drop-off, service type and passenger count.
- DispatchOffer: request, candidate, expiry and response.
- Trip: accepted assignment and trip lifecycle.
- QueueAuditEvent: append-only queue join/leave/rotation records and separately typed dispatch-exception records in the queue partition; RideOfferState.Attempts retain ride-offer decisions; DriverUnionAuditEvent retains union transfers.

## 6. FIFO dispatch architecture

### Queue partitioning
Create logical queues by service zone and compatible service/vehicle class. Avoid a single citywide queue that routinely creates distant pickups. Zone boundaries and cross-zone fallback are explicit policy. The `QueueEntries` container is partitioned by `/zoneServiceKey`; successful queue joins, leaves and rotations append `QueueAuditEvent` documents in the same partition and are committed with the queue-state mutation in one Cosmos transactional batch. In-memory queue storage records equivalent events for tests. Unexpected non-cancellation queue-read exceptions during legacy Driver acceptance and selected-driver reservation-store exceptions during authenticated Driver acceptance append a separately typed `DispatchException` record in the same partition, with the server trace ID, claim-derived Driver actor reference, allow-listed `QueueReadFailed`, `DriverReservationFailed` or `DriverReservationPromotionRecovered` category and UTC timestamp. The selected-driver case is the active Cab Rider flow; the existing `QueueEntries` partition is used only as the established dispatch-audit store and does not introduce FIFO membership, ranking or assignment. If permanent-reservation promotion throws with an ambiguous outcome, the API point-reads the reservation and proceeds only when the same ride already holds a permanent lease, appending `DriverReservationPromotionRecovered`; otherwise it appends `DriverReservationFailed` and leaves the ride untransitioned. This exception-only record is a single-item append with a deterministic ID derived from the server trace ID, so a repeated write for the same trace is idempotent; a Cosmos 409 is accepted only after a canonical point read in the same partition verifies the deterministic event ID, event type, zone/service partition, claim-derived actor, server trace ID, allow-listed reason category and absence of an offer ID. A missing or mismatched canonical record fails closed; the original timestamp is not part of the retry identity. It is not a queue mutation event and never stores raw exception messages, request bodies, tokens, contact details, exact location or fare. Ordinary conflicts/no-op outcomes and cancellation do not create exception records. Rider discovery and selected-driver assignment remain queue-independent.

### Ordering and eligibility
The server assigns queue-entry order. A deterministic sequence and stable tie-breaker establish ordering. Clients cannot submit or modify queue rank.

Candidates must be approved, explicitly Online, active in the relevant queue, document-valid, service/capacity compatible and free of conflicting assignments. Online is persisted on the driver profile; the API adds the driver to the queue when they go online and removes them when they go offline. Rejoining appends at the back. A driver with an active ride cannot go offline. Revalidate immediately before assignment.

### Current API enforcement
The Driver available-rides endpoint returns actionable requested rides only to the current head of the driver's approved zone/service-class FIFO queue. Ride acceptance rechecks that queue-head condition on the server; clients cannot self-assign by calling the endpoint directly. Explicit rejection is also restricted to the current queue head and moves that driver to the back. These checks are a transitional fairness safeguard, not a complete offer system: there is not yet a durable offer ID/deadline, a server-owned expiry worker, or exclusive per-ride offer state. The timeout acknowledgement endpoint is not automatic expiry.

### Atomicity and concurrency

The selected-driver increment uses a `DriverRideReservations` container partitioned by `/driverId`, with one create-if-absent reservation document per driver. Negotiations receive a bounded lease; expired leases are removed with ETag protection and can be reacquired. After mutual agreement and Driver acceptance the lease is promoted to a permanent reservation. The ride document remains partitioned by `/rideId`, so reservation and ride creation are separate conditional writes rather than a cross-partition transaction. A deterministic ride ID plus a timestamped idempotency record permits retry recovery after interruption between the idempotency, reservation and ride writes. Recovery reuses the original idempotency timestamp for the ride request/audit timestamps and negotiation deadline, keeping the recovered ride deadline aligned with the already-held reservation lease. Terminal reservation cleanup is reconciled on the next discovery/availability check; live Cosmos multi-instance and process-restart validation remains a production gate.

Prevent assigning one driver to multiple active rides and prevent assigning one ride to multiple drivers. Use Cosmos DB transactional batches or conditional writes within an appropriate logical partition where possible. Cosmos DB transactions are scoped to a logical partition; do not assume cross-partition atomic transactions. Use ETags/optimistic concurrency, idempotency keys and bounded conflict retries. If invariants span partitions, revise the partition model or add a dedicated coordination mechanism before production.

### Queue lifecycle
Model Online/Offline availability, join, unavailable, on-trip, leave, rejoin, offer-pending, accepted, rejected and timed-out states explicitly. Define whether decline/timeout retains, advances or resets position. Queue mutations append an event with driver, queue scope, event type and UTC time; offer attempts remain in the ride-partitioned aggregate. Unhandled API exceptions return generic Problem Details and safe logs with traceId and exception type only. Durable exception audit with actor/reason remains an acceptance gate. Never silently alter priority.

### Dispatch flow
This is the legacy FIFO compatibility flow only. It is not the Rider booking path: CAB-DISCOVERY-01 and RIDER-03 require explicit Rider-selected Driver discovery and reservation, with no customer-facing FIFO ranking or automatic assignment.
1. Validate and persist the ride request.
2. Resolve pickup zone and requested service class.
3. Retrieve ordered eligible candidates.
4. Revalidate candidate state.
5. Create a time-bounded offer.
6. Atomically reserve/assign driver and ride on acceptance.
7. On decline/timeout, apply published policy and consider the next candidate.
8. Persist audit events and notify both clients.

## 7. Cosmos DB data design

Candidate entities/containers:
- Profiles and onboarding records, including a size-limited JPEG driver selfie stored as Base64 in the driver profile for authenticated union membership views.
- Vehicles and document metadata.
- Service zones and configuration.
- Queue memberships and queue events.
- Ride requests, offers and trips.
- Queue audit events in `QueueEntries` (`/zoneServiceKey`), offer-attempt history in `RideOffers` (`/rideId`) and union assignment history in `DriverUnionAudit` (`/driverId`).

Partition keys must follow access patterns and transaction boundaries. Candidate keys include a zone/service-class queue identifier for queue operations and ride/request identifiers for trip lookups. Validate hot-partition risk, queue size, query patterns, indexing, TTL and consistency with representative load tests. Avoid cross-partition transaction assumptions.

Store identity documents in protected object storage rather than large Cosmos DB documents; retain secured metadata/references. Apply least privilege and retention controls.

### Union reviewer authorization and selfie lifecycle

- Union reviewers authenticate with a signed `role=UnionManager` claim and one or more signed `unionId` claims. The API does not infer union scope from request-body values.
- `GET /api/v1/union/drivers` returns only active Cab memberships for the caller's claimed unions. It includes only the driver/vehicle fields and document-status metadata needed for review, plus the selfie for the assigned union reviewer. It excludes phone/email, exact coordinates, private chat and Rider/ride data.
- `PUT /api/v1/union/drivers/{driverId}/selfie-review` accepts review only when the driver has an active membership in a union claimed by the caller, the union is Active, the Cab profile and required licence/vehicle documents are approved, and a valid selfie exists.
- Operations admin-key access may reject/hide a selfie, but cannot approve public visibility. Approval must come from the currently assigned union manager.
- A new/changed union assignment resets selfie approval to Pending unless the selfie was already approved by that exact current union. Rider discovery returns the photo only after current-union approval. The Driver app reads its own `/api/v1/driver/union` membership and displays the current union/status.
- TriSend/identity-provider issuance of the `UnionManager` role and `unionId` claims is a configuration gate. Private document storage, malware scanning, consent and retention remain blocked on provider/policy selection; no storage provider or credentials are hardcoded.
- Selfie review first authorizes the current union assignment before acquiring the per-driver exclusive reservation lease, then rechecks that assignment while holding the lease. This prevents an out-of-scope manager from reserving another driver's ID and prevents review/transfer/booking races. The profile update uses the existing ETag-aware conditional update path and rejects a stale selfie/document snapshot.
- Union selfie audit IDs are deterministic for the driver, union, action and review timestamp. Repeated requests and retries after an ambiguous response do not create duplicate review events. Profile and audit records remain separate Cosmos writes; cross-container atomicity is not assumed and live Cosmos retry/failure validation remains a launch gate.

### Driver availability and location lifecycle

- Going Online requires an approved Cab profile, active membership in an Active Cab-serving Union covering the driver's assigned service zone, approved driving-licence and vehicle-registration documents, and a valid current GPS fix inside that zone.
- Online/Offline transitions acquire the same per-driver exclusive reservation lease used by selected bookings and Union transfers. A selected negotiation/ride therefore cannot overlap an availability mutation; ETag-aware profile updates remain the persistence guard.
- A zone-exit location update acquires that lease before clearing location and taking the Driver Offline. If a selected booking owns the lease, the location transition fails closed and retries on a later foreground update.
- Availability location is foreground-only. The mobile app stops its watcher when backgrounded, refreshes the current GPS fix and restarts the watcher on resume, and attempts to go Offline if foreground permission is revoked.
- Active-trip location uses a separate foreground-only watcher. It starts only for the assigned Driver while the ride is Accepted, Arrived or InProgress and server location sharing is active; backgrounding, leaving Trips, terminal ride state or unmounting removes the watcher and late callbacks are ignored. On resume, the app refreshes authenticated Driver ride history, rechecks foreground permission and restarts only if the same ride remains eligible.
- The API rejects non-finite and out-of-range coordinates for availability, pickup and active-trip location inputs. Active-trip updates are accepted only from the assigned Driver while server-owned location sharing is active, then persisted and broadcast to the owning Rider group. Rider reads remain owner-scoped and reconnect restores authoritative state.
- Availability discovery still uses only server-authoritative Online state, current location freshness and the existing union/vehicle eligibility filters; `Dispatch:NearbyLocationMaxAgeSeconds` must be explicitly configured to a positive product-approved value. There is no checked-in production default; discovery and selected booking fail closed with 503 when it is missing or non-positive. The 120-second value used by API integration tests is test-fixture-only. Exact Driver coordinates are not returned in discovery. The Rider discovery endpoint receives foreground device GPS as the pickup coordinate; the free-text pickup value is a landmark/Driver instruction and is not geocoded. Custom pickup pin/geocoding and route-distance calculation remain blocked on a configured maps/routing provider, so `journeyDistanceKm` stays null until then. Call/Chat controls remain disabled until their providers are configured. The approved production radius and zone coverage must be supplied; discovery fails closed when the radius is missing or invalid.

### Driver reservation audit metadata contract

- Driver reservation audit facts use exactly four event types: `ReservationAcquired`, `ReservationMadePermanent`, `ReservationExpiryChanged` and `ReservationReleased`. The domain factory rejects any other or malformed event type before creating an append-only fact.
- Actor roles and reason categories remain allow-listed. Actor identifiers containing only digits and the permitted separators (`-`, `_`, `.`, `:`) are treated as phone-like when they contain at least eight digits and are replaced with the System actor. Opaque internal identifiers containing letters remain unchanged.
- Null/blank actor IDs also fall back to System. Operations selfie-rejection reasons are trimmed once, limited to 1–300 characters and the same normalized value is persisted in both the Driver profile and append-only Union audit event.
- An Operations selfie-rejection event ID is a versioned SHA-256 identity over the Driver ID, current Union ID, selfie capture timestamp, authoritative review timestamp, SHA-256 selfie-content digest and normalized reason. The ID is stable across an identical retry and does not embed those source values. Re-uploading a selfie, changing the reason or performing a new review after another review source creates a distinct identity.
- If the profile already records the same rejection for the same selfie, the API preserves its original review timestamp and ensures the corresponding deterministic audit event exists. This recovers a prior profile-success/audit-failure response loss without rewriting the profile.
- Union audit writes are create-only. A duplicate/409 is followed by a bounded point read using the exact event ID and Driver partition. The write is considered recovered only when every canonical event field (including actor and timestamp) matches; a missing or mismatched record fails closed. The in-memory implementation follows the same identity/content rule.
- Operations audit actors use a validated opaque JWT `sub` only when the principal is authenticated and has the `Ops` role; non-Ops, email-like, phone-like, malformed or overlong subjects fall back to the fixed `Operations` actor. The original actor, reviewed Union ID/name, review timestamp and reason are stored as private profile recovery metadata. The Operations rejection route shares the per-Driver reservation lease with UnionManager review, bookings and transfers; a transfer repairs a pending Operations audit before clearing that metadata. Driver onboarding/selfie responses redact all private recovery fields; the API-key-protected Operations profile view remains the privileged inspection path.
- Driver profile and Union audit remain separate Cosmos containers. This recovery makes retries safe but does not claim cross-container atomicity; live Cosmos conflict visibility, multi-instance and restart behaviour remain external validation gates.
- The UnionManager member-list response uses an allow-listed projection and omits driver phone/email, exact coordinates, private document storage references, Rider identity and chat data while retaining only the minimum selfie/Cab review fields and coarse zone needed for the scoped Union workflow.
- Union assignment/transfer records carry a private last-event recovery snapshot in the same `DriverUnionAssignments` item: versioned deterministic event ID, event type, validated original opaque actor, previous Union ID/name and UTC occurrence time. The assignment reason and destination are the canonical event fields. After assignment upsert, a missing separate `DriverUnionAudit` event can be reconstructed on an identical target+reason retry or before Operations history is returned. Existing events are point-read in the exact Driver partition and compared field-for-field including actor; duplicate/409 races use bounded canonical read-back. Driver `/api/v1/driver/union` redacts all recovery fields. Legacy assignments without snapshots are preserved without synthesising historical events. `DriverUnionAssignments` and `DriverUnionAudit` remain separate containers; this is retry recovery, not cross-container atomicity.
- UnionManager selfie-review audit actors use an authenticated `UnionManager` role and a validated opaque JWT `sub`; contact-like or missing subjects fall back to `UnionManager`. The original actor, reviewed Union ID/name and review timestamp are stored in private profile recovery metadata. Canonical audit read-back compares every event field including the exact original actor; a different authorised manager retry cannot mask an actor mismatch. If the profile review commits but its separate audit event is missing, the API reconstructs the deterministic event from the private snapshot, and a transfer repairs that event before clearing the snapshot.
- Operations assignment/transfer audit actors use the same role-bound opaque-subject resolver as Operations selfie review; email-like or phone-like subjects fall back to the fixed `Operations` actor. No Operations transfer audit writes `Identity.Name`.
- Free-text Union selfie-review and assignment reasons are trimmed and limited to 1–300 characters; obvious email addresses and phone-like sequences are rejected before profile/assignment mutation and before audit identity creation. The bounded validator fails closed on regex timeout and is explicitly not a general PII scanner. The UnionManager member projection omits a legacy review reason if it contains an obvious contact pattern.
- Phone-like reason detection covers Unicode decimal digits and slash-separated groups as well as ASCII digits, while preserving ordinary ISO date/time text; this remains a conservative contact filter rather than a general PII scanner.
- The privileged Operations `/api/v1/ops/drivers/{driverId}/union-history` endpoint returns HTTP 409 without partial history when a pending assignment-audit snapshot is incomplete or inconsistent. The server logs a generic recovery failure without logging the reason or actor value.
- Audit facts remain limited to reservation/actor/reason/time metadata; ride routes, precise location, contact details, credentials and payment values do not belong in this event contract.

## 8. Initial API resource groups

- /api/v1/auth — registration, login, refresh and logout.
- /api/v1/riders — rider profile and preferences.
- /api/v1/drivers — driver profile, onboarding and availability.
- /api/v1/vehicles — vehicle information.
- /api/v1/zones — zone discovery and eligibility.
- /api/v1/queues — join, leave and queue status.
- /api/v1/rides — quote, request, cancel and trip history.
- /api/v1/dispatch — privileged internal dispatch operations only.

Apply resource ownership checks and authorization policies to every endpoint. Never accept queue rank or dispatch priority from a mobile client.

## 9. Realtime

SignalR delivers ride offers, selected-driver notifications, agreement changes, driver location updates and trip lifecycle events. Ride offers are persisted with a notification-pending marker before publication. A hosted outbox worker retries pending notifications and marks them sent with an ETag update. Delivery is at-least-once: a crash after SignalR send but before the sent marker can produce duplicates, so clients must de-duplicate by offerId. Realtime is not the source of truth: on initial connection and every reconnect, Rider reloads owner-scoped ride history/current ride details and agreement versions; Driver reloads its available requests and owner-scoped selected/active ride history. Missed events never determine the recovered state. Initial and subsequent legacy offers share clamped Dispatch:OfferLifetimeSeconds configuration (10–120 seconds, default 30).

Authenticate hub connections, scope groups, rate-limit events and protect location privacy. Do not expose precise driver location to unrelated riders.

## 10. Security and privacy

- TLS for all network traffic.
- Short-lived JWT access tokens and securely rotated/revocable refresh tokens.
- Store mobile credentials using platform secure storage.
- Role- and resource-based authorization for rider, driver, fleet and operations.
- Rate limiting, input validation, abuse detection and audit logging.
- Managed secret storage; no secrets in Git.
- Browser CORS is deny-by-default and uses explicit origins from `Cors:AllowedOrigins`; `CorsOriginValidator` rejects any `*` wildcard character before URI validation, along with paths, credentials, queries and fragments. Only HTTPS origins are accepted outside Development; HTTP is allowed only in Development. Native Android/iOS clients do not require CORS.
- Unhandled API exceptions return generic Problem Details with a traceId; structured error logs contain traceId and exception type only, not request bodies, tokens or exception messages.
- Minimize location/identity data and obtain appropriate consent.
- Define retention, deletion, export and incident-response procedures.
- Do not store raw payment-card data; use payment-provider tokenization.
- Validate applicable Indian transport, aggregator, insurance, consumer and privacy obligations with qualified advisors.

## 11. External integrations

Provider decisions remain open for maps/geocoding/routing, OTP/SMS, payment/refunds, push notifications, identity verification and monitoring. Isolate providers behind interfaces/adapters. Verify pricing, quotas, regional coverage and production terms.

## 12. Deployment and operations

Start with a low-cost development environment and separate development, staging and production configuration. CI should build, test and scan backend/mobile code. Use managed secrets, health checks, structured logs, metrics, distributed tracing, database backup and restore testing. Do not assume production hosting is always free; validate availability, data residency, capacity, recovery and cost. The API exposes `/health/live` for process liveness and one `/health/ready` endpoint for readiness; readiness reads the configured Cosmos database in Cosmos mode and reports ready in explicit in-memory demo/test mode. Unhandled exceptions are logged with traceId and exception type only; metrics, trace exporters, alerting and durable exception audit remain to be completed.

## 13. Test strategy

- Unit tests for FIFO ordering, eligibility and state transitions.
- Property/randomized tests for fairness invariants.
- Cosmos DB integration tests for partition and concurrency behaviour.
- Race tests for simultaneous requests, driver contention, retries and duplicate messages.
- API authorization and validation tests.
- Mobile end-to-end tests for onboarding, booking, queue and trip completion.
- SignalR reconnect and state-recovery tests.
- Load and failure-injection tests before pilot.

## 14. Decisions required before production

1. Queue partition key and atomic transaction boundary.
2. Zone geometry source and boundary policy.
3. Queue behaviour after reject, timeout, cancellation, trip completion and reconnect.
4. Offer timeout and cross-zone fallback.
5. Location update frequency and retention.
6. Maps, payment, OTP and push providers.
7. Hosting region, availability targets, backup and disaster recovery.
8. Monitoring, support tooling and regulatory controls.

## 15. Authentication integration

The Driver and Rider clients use email/password through the TurnOva API, which proxies registration, login, refresh, identity and logout requests to TriSend Auth. TurnOva fixes the `appId` to `turnova` and derives the allowed role from the route; clients cannot request another application's role.

TriSend issues 15-minute JWT access tokens and rotating refresh tokens. Mobile clients store tokens using Expo SecureStore. TurnOva validates JWT signature, issuer, audience and expiry locally and applies Driver/Rider authorization policies. The API requires the exact same issuer, audience and symmetric signing key as TriSend; keep these values only in the backend secret store.

Both mobile apps coordinate unauthorized-request recovery through one in-flight refresh operation. The current stored access/refresh pair is read only after entering that shared operation. If a delayed 401 arrives after another request has already rotated the pair, the client reuses the newer stored access token instead of attempting to reuse the old refresh token. A per-app session epoch invalidates pending requests across logout or a new/restored session so old responses are not retried or returned under a replacement session. A missing or rejected refresh token clears the local session and returns the user to sign-in. Shared session storage writes the access/refresh pair with best-effort rollback if either write fails, clears both keys even if one deletion fails, and rejects partial pairs. Startup restoration checks `/me`, refreshes only after a 401, and preserves tokens on network/5xx failures; offline logout always attempts local deletion without waiting for server revocation. SecureStore token-pair writes and deletes are serialized per storage adapter; if logout begins while a token rotation write is already in progress, the logout clear runs after that write and leaves no stored pair.

Logout revokes the TriSend refresh session, but already-issued access tokens can remain valid until their 15-minute expiry because TurnOva validates them locally. This is an explicit MVP trade-off; immediate revocation requires an online session check or a distributed revocation mechanism.

Email/password only is in scope. Google/Microsoft OAuth and SSO are explicitly excluded. Driver onboarding/KYC and durable ride persistence are also separate work items; a valid Driver role does not by itself prove production driver eligibility.

## 16. Ride persistence and location tracking

Ride requests are written to the `Rides` Cosmos DB container (partition key `/rideId`) when Cosmos is configured; local demo mode uses process memory. Drivers can list requested rides and accept one. Only the authenticated assigned driver can submit GPS coordinates for an accepted ride. The rider can read its own ride and latest driver coordinates. The Rider app polls the authoritative ride resource every five seconds while the Activity screen is open and displays the driver and pickup markers on a native map. Driver foreground location collection is permission-gated and stops when the Trips screen is left or the app unmounts. This is a foreground/demo implementation: production requires ETag/conditional acceptance to prevent concurrent claims, a proper active-trip lifecycle, background location policy and permissions, throttling, retention, map-provider review, and device E2E tests.

## 17. Ride lifecycle and realtime tracking

Ride state transitions are server-authoritative: `Requested → Accepted → Arrived → InProgress → Completed`. A Rider may cancel only before the trip starts (`Requested`, `Accepted`, or `Arrived`). State changes use conditional Cosmos writes (ETags) and atomic in-memory updates. Location sharing is active only for `Accepted`, `Arrived`, and `InProgress`; terminal states disable it. Rider ride creation requires an `Idempotency-Key` header; Cosmos mode reserves the hashed key in the `RideIdempotency` container partitioned by rider, while local demo mode uses a concurrent in-memory key registry.

The Rider connects to `/hubs/rides?rideId={rideId}` using its JWT. The hub verifies Rider role and ride ownership before joining the ride-specific SignalR group. Assigned Driver location updates and ride-state changes are broadcast to that group. The Driver app sends foreground GPS updates only while the Trips screen is active and the ride is in a location-sharing state. SignalR is used for rider updates; no periodic polling is required by the normal connected path.

Operational safeguards still required before production: configure the production `Cors:AllowedOrigins` values, rate-limit location updates, define location retention/deletion policy, add metrics/traces/alerts and durable exception audit, monitor SignalR scale-out/backplane needs, and run multi-instance Cosmos concurrency and mobile-device acceptance tests.

## 18. Cosmos DB startup and migration reliability

When Cosmos mode is enabled, API startup validates the HTTPS endpoint, account key and database identifier, then runs migrations before the host begins accepting requests. Migration execution is serialized across instances with a single Cosmos-backed lease document in `MigrationLocks`. The lease is acquired using create-if-absent semantics; an expired lease can be replaced with an ETag conditional write. Release also uses the acquired ETag so one instance cannot delete a successor's lease. Applied migration IDs remain in `MigrationHistory`; a conflicting history insert is treated as success only after the record is read back.

`GET /health/live` is the process liveness endpoint. `GET /health/ready` performs a Cosmos database read in Cosmos mode and returns 503 when the configured database is unavailable. In-memory demo mode reports ready without a Cosmos dependency. The readiness endpoint does not replace monitoring of request-level Cosmos throttling, latency, RU consumption, or data-plane operations.

The migration lease currently has a 30-minute expiry and is not renewed. Keep migrations short and controlled; migrations expected to exceed this duration require lease renewal before rollout to multiple API instances. Validate the lease and migration behaviour against a real Cosmos development account before production deployment.

## 19. Driver onboarding and dispatch eligibility

Driver onboarding metadata is stored in the existing `Drivers` Cosmos container (partition key `/driverId`); local demo mode uses an in-memory profile registry. The authenticated Driver can submit and read only their own profile. A submission contains identity/contact fields, service zone, vehicle attributes, service class, passenger capacity and opaque references to documents in private object storage. New or resubmitted profiles are `Pending`; the client cannot set verification status.

The interim operations review endpoint supports `Approved`, `Rejected` and `Suspended` decisions. It is protected by a server-side `DriverOnboarding:AdminApiKey` (minimum 32 characters) compared in constant time. Approval requires both DrivingLicence and VehicleRegistration document references. This is a limited integration bridge until an authenticated Ops portal and role/permission model are implemented. Never ship this key in a mobile app or public client.

Queue joining, available-ride discovery and ride acceptance require an `Approved` profile. Rejected drivers may resubmit; approved and suspended profiles cannot be overwritten by the driver onboarding endpoint. Rejection and suspension also remove the driver's current membership from their approved zone/service-class queue. Suspension immediately removes eligibility at the next API request. This is not full KYC: document upload, verification provider/manual evidence review, expiry monitoring, audit trail, vehicle ownership checks and regulatory validation remain future work.

## 20. Union management and driver allocation

A Union is an operating/employer organization for a group of drivers, not a queue or a geographic service zone. A union has a unique code, supported service zones and service classes, optional manager/location metadata, and an operational status (Active, Inactive, Suspended). Only an approved Driver onboarding profile can be assigned to an active union, and its approved zone/service class must be supported by that union.

The current assignment is stored separately from the onboarding profile so a driver can move between unions without rewriting their KYC/onboarding record. Assignment changes require a reason and append an audit event containing the prior union, destination union, event type, actor and UTC timestamp. The current assignment is one active union per driver. Historical assignments are represented by the audit stream.

Cosmos containers: Unions (partition key /unionId), DriverUnionAssignments (/driverId), and DriverUnionAudit (/driverId). In-memory equivalents support local tests. The interim Ops endpoints reuse the backend-only DriverOnboarding admin API key. This is an integration bridge, not production authorization; replace it with authenticated Ops identities, role/permission checks and actor identity before production. The driver app can read its current union assignment, while union creation, status management, assignment and transfer remain operations workflows. Active Union membership is required for the new Rider discovery path; CAB-DISCOVERY-01 enforces the current active assignment and the Union's active Cab/zone coverage. The interim operations-key authorization remains a pre-production limitation.

Employment contracts, payroll rules, earnings/deductions and payslip generation are intentionally outside this increment. Future payslips must be based on immutable work activity and preserve the employer/union active during the relevant pay period.

## 21. Monthly driver payslips (PDF only)

The Driver app offers a Payslips tab with the most recent 12 completed months and downloads the authenticated PDF through the native file/share workflow. There is no HTML payslip view or CSV/spreadsheet export.

A driver can request a finalized monthly payslip from GET /api/v1/driver/payslips/{year}/{month}.pdf. The route requires the Driver role and emits only application/pdf. No HTML, CSV or spreadsheet payslip format is exposed. Only completed months are generated, using India Standard Time calendar-month boundaries.

Ride activity is captured in an append-only ledger: Accepted when the driver accepts a ride, Rejected when the driver explicitly declines an available ride, and Completed when the assigned driver completes the trip. Rider cancellation is not a driver rejection. Deterministic activity IDs (ride ID plus activity type) make ledger insertion idempotent. Days worked are distinct India-local calendar dates containing at least one completed activity. Accepted and completed are separate metrics, so a completed ride contributes to both.

The monthly payslip is an immutable snapshot persisted in DriverPayslips after first generation. Work events are persisted in DriverWorkActivities. Both Cosmos containers are partitioned by /driverId; in-memory stores are used without Cosmos. A driver's union assignment history is used to identify the employer for the pay period. If multiple union assignments occur within one pay period, this initial version must reject generation rather than silently attribute all activity to the wrong employer; split-employer payroll is a later increment.

The PDF contains identity, employer, pay period and work metrics only. Monetary earnings, deductions, incentives and net payable are intentionally not calculated until a compensation policy is agreed. Activity ledger writes remain separate from ride-state writes. Before the first immutable monthly snapshot, the API replays missing selected-driver Accepted, Completed and Driver-decline activities from the ride's append-only audit events using deterministic IDs and create-if-absent writes. Selected rides are identified by their Union linkage rather than requiring an agreement version, because a Driver can decline during the initial Negotiating state before any fare proposal. This read-repair closes the selected-Cab payslip snapshot crash window; live Cosmos cross-container restart/ETag validation remains required. It is not a transactional outbox and does not claim to reconstruct legacy FIFO offer events.

When two API instances create the same first monthly snapshot, the losing CreateItem conflict is resolved by reading the canonical point item from the Driver partition. The API retries a missing point read at most three times with 50 ms and 100 ms delays. If the canonical snapshot remains unavailable, or the read fails, the request fails closed; it never returns the losing request's locally calculated metrics. The in-memory implementation uses `ConcurrentDictionary.GetOrAdd` and returns the winner's value. This deterministic recovery path does not replace live Cosmos consistency, cross-instance, restart or ETag validation.

The Driver app caches only the generated `TurnOva-Payslip-YYYY-MM.pdf` files needed by the native share workflow. It removes those exact filenames on explicit logout, detected session expiry, startup without a restored session, and before a new account signs in. Unrelated app-cache files are preserved. An in-flight PDF write checks the session epoch before and after writing; if logout/expiry wins during the write, the exact generated file is deleted. Cleanup failures are surfaced, and a new account login is blocked if old-account payslip cleanup cannot be completed. Physical Android/iOS cache and share-sheet behavior still requires device acceptance.

The PDF renderer uses SkiaSharp with HarfBuzz shaping for visible vector glyphs and PDFsharp 6.2.4 (MIT) to append a low-visibility searchable Unicode text layer. PDFsharp embeds the bundled Noto Sans fonts and emits ToUnicode mappings for text extraction; the complete SIL Open Font License 1.1 text and per-family attribution notices ship under TurnOva.API/Fonts. The bundled script set is Latin, Devanagari, Bengali, Gurmukhi, Gujarati, Odia, Tamil, Telugu, Kannada and Malayalam. PdfSharpCore 1.3.67 was removed because its minimum transitive SixLabors.ImageSharp 1.0.4 dependency triggered high-severity GIF-decoder advisories; PDFsharp 6.2.4 has no ImageSharp dependency. PDFsharp's transitive System.Security.Cryptography.Pkcs 8.0.1 was then identified with high-severity CVE-2026-84008; the API explicitly pins patched 10.0.13. Backend `Directory.Build.props` audits all transitive dependencies and fails restore on audit-feed errors or high/critical advisories (NU1900/NU1903/NU1904). A Skia-only positioned-text experiment did not emit an embedded Unicode font program or searchable long-name text (two backend tests failed), so the separate PDFsharp layer is retained and serialized around its process-wide font resolver/cache. Immutable Skia typefaces are process-cached; each request owns its HarfBuzz shapers and placement state, preserving concurrent visible rendering. PdfPig regression tests verify all bundled scripts, embedded fonts/ToUnicode, concurrent request isolation, long-name wrapping, immutable snapshots and repeated-download text equality. Unsupported scripts (including Arabic, Sinhala and emoji) fail closed and the authenticated endpoint returns HTTP 422 with a generic message; it never silently substitutes question marks or emits a partial PDF. Extend script coverage only with an appropriately licensed font, a script-specific shaping run and PDF extraction/rendering tests. The brand mark is Cab-only. DaysWorked remains distinct India-local calendar dates containing at least one completed activity; Product confirmation is still required. Monetary compensation, deductions, incentives and net payable remain out of scope until Product approves a compensation model. CI publishes a framework-dependent linux-x64 validation artifact with only the selected native runtime assets and a 120 MiB output-size guard; this is not a production hosting decision or deployment.

## 22. Mobile appearance and theming

Both Expo applications implement a shared visual contract with equivalent local theme tokens: background, header, surface, text, muted text, borders, accent, status colors and navigation states. Theme preference is `system` by default and resolves using React Native `useColorScheme`; explicit `light` and `dark` choices override the system. The selected preference is persisted in Expo SecureStore under `turnova.theme`, so it survives app restarts and can be used by both TurnOva apps on the same device. Theme selection is available before sign-in and in Profile. The Rider map uses a dark map style when Dark is active. No backend preference endpoint or account-level synchronization is required.

### Union approval for driver profile selfies

A driver's selfie is private by default. The driver onboarding profile stores photo approval status, approving union ID, review timestamp and rejection reason. Uploading or replacing a selfie resets approval to Pending and clears the approving union. The review action is allowed only for the driver's currently active union assignment. Approval is scoped to that union: a transfer to another union automatically hides the old approval. Union member-list responses suppress the Base64 image unless the photo is Approved by the union that currently owns the assignment. The member-detail review endpoint remains operations-protected and can return a pending image for inspection. The current union review endpoint uses the shared operations admin key temporarily; authenticated union-manager RBAC and actor-level audit records are required before production.

## 23. FIFO queue rotation after rejection and offer timeout

A driver who rejects an available ride while at the head of their zone/service-class queue is moved to the back of that same queue. The next eligible member becomes the head. An offer-timeout acknowledgement follows the same rule: only the currently queued head driver can submit it, and the API returns the next driver ID and the caller's new position. Drivers cannot rotate another driver's membership. In-memory rotation is serialized under the queue lock. Cosmos rotation uses an ETag-conditional item replacement so concurrent queue changes do not silently overwrite each other.

The timeout endpoint is an authenticated acknowledgement from the Driver client after its offer timer expires; it is not yet an independent server-side offer scheduler. A later dispatch-offer increment must persist offer IDs, deadlines and delivery state, and have the server expire offers even when a mobile client is offline. Ride-to-zone matching, automated offer delivery and server-owned timeout scheduling remain separate dispatch-engine work.

## 24. Rider vehicle selection and zone-based eligibility

The Rider app requires an explicit vehicle category selection: Scooty, Bike, Auto or Cab. The API validates the category and resolves the pickup coordinates against configured dispatch-zone bounding boxes. Requests outside configured zones are rejected with an availability message rather than silently routed to a default zone. The resolved zone and selected service class are persisted on the ride.

Driver availability and acceptance are restricted to the driver's approved zone and service class, in addition to passenger-capacity checks. The API does not expose rides from other vehicle queues or zones. Dispatch zone boundaries are configuration-driven under `Dispatch:Zones`; production rollout must replace the initial Hyderabad development rectangle with reviewed operational boundaries. This increment establishes correct booking and eligibility metadata; server-owned offer scheduling and offline timeout processing remain a separate dispatch-engine increment.

## 25. Shared rider and driver vehicle categories

The Rider and Driver apps use one canonical service-category set: `scooty`, `bike`, `auto` and `cab`. Drivers select their vehicle category during onboarding. The API validates onboarding and union service-class configuration against this set, and queue membership uses the approved driver's selected category. The zone discovery response advertises all four supported categories. This prevents a rider booking category from being invisible to otherwise eligible drivers because the two apps used different category vocabularies. Existing driver profiles or union records using legacy values (`mini`, `sedan`, `suv`) require an explicit data migration or operations correction before those drivers can serve the new categories; values are not silently remapped.

## 26. Durable ride-offer persistence foundation

The dispatch-offer aggregate is stored in a dedicated `RideOffers` Cosmos container partitioned by `/rideId`. There is one aggregate document per ride, containing the current offer identity, driver, zone/service class, offer/deadline timestamps, status and retained attempt history. This gives a single ETag-guarded write boundary for each ride's offer progression and prevents multiple active-offer documents for the same ride.

The store contract supports lookup by ride, lookup of a driver's pending offer, bounded expiry scans, create-if-absent and ETag-conditional replacement. The local adapter is process-memory only; the Cosmos adapter uses item create/replace and ETags. The migration creates the container automatically.

This is the persistence foundation only. API issuance/acceptance/rejection, a server-owned expiry worker, FIFO retry orchestration, durable ride/driver reservation coordination and SignalR/push delivery are not yet wired to this store. Do not claim production dispatch until those stages and Cosmos multi-instance integration tests are complete. Cross-partition atomicity with `Rides` and `QueueEntries` is not assumed.

## Server-owned offer expiry (incremental)

A hosted worker polls the durable offer store in bounded batches. For each overdue pending offer it performs queue rotation through an offer-ID-idempotent operation, then marks the offer and matching attempt Expired using the offer aggregate ETag. If the process stops after queue rotation but before offer resolution, replay sees the same offer ID marker and does not rotate twice. Missing queue membership or ETag conflict leaves the offer unresolved for a later retry. This workflow is not a cross-container transaction and does not yet fence legacy ride acceptance by offer ID; do not treat it as complete assignment/next-driver dispatch.

## FIFO offer delivery (incremental)
The API persists an initial ride offer for the current FIFO head before sending a SignalR event to that driver's authenticated group. The driver can recover the pending offer after reconnect via an authenticated API read. Acceptance verifies offer ownership, pending status and deadline before the existing ride reservation/ETag guards. Ride and offer documents are separate Cosmos aggregates; their writes are not atomic. Durable outbox delivery, automatic progression after rejection/expiry, and crash recovery are still production blockers.


### Idempotent rejection of a persisted offer

For a ride with a durable pending offer, only the assigned driver may reject it, and the offer must still be pending and unexpired. The API rotates the queue using the persisted offer ID as an idempotency key, then conditionally marks the offer and its matching attempt Rejected using the aggregate ETag. If the process stops after queue rotation but before the offer update, retrying the same rejection is safe: the queue store recognizes the offer ID and does not rotate the driver a second time. Legacy rides without a persisted offer retain the previous rotation path. This is retry-safe client recovery, not a durable outbox or autonomous reconciliation; automatic next-driver offer creation and recovery without a client retry remain outstanding.


## Automatic next-driver offer advancement

After a durable offer is rejected or expires, the same ride-partitioned aggregate advances to the next eligible FIFO driver. The service verifies that the ride remains Requested, revalidates candidate approval, Online status, zone, service class and passenger capacity, and removes stale/ineligible queue entries. It appends the next attempt and conditionally persists it with the aggregate ETag before sending SignalR. Rejection and expiry use the same advancer; concurrent calls race on the ETag, so at most one next offer is persisted. If no eligible candidate remains, the ride remains Requested without an active offer.

This is not a transactional outbox. A process stop after offer persistence but before SignalR delivery can lose the realtime event; reconnect can recover through the pending-offer endpoint. Queue/profile coordination spans Cosmos containers and is not transactional. Multi-instance integration tests, autonomous reconciliation and durable notification outbox remain production blockers. Offer lifetime currently uses a 30-second default pending central configuration.


## Deployment and environment boundaries

The API exposes unauthenticated `/health/live` and `/health/ready` probes for hosting-platform health checks. These currently confirm process health; they do not independently prove every downstream provider is healthy. Cosmos schema migrations run before the app accepts traffic. Production startup rejects missing Cosmos endpoint/key configuration, preventing accidental in-memory persistence in production.

The API is published as a standard .NET 10 application directory and hosted on a native .NET-capable platform selected during infrastructure setup. Docker and container-based deployment are explicitly excluded. Hosting secrets are supplied through the selected provider's secret store and are never committed. The API exposes `/health/live` and `/health/ready` probes.

TurnOva MVP clients are native Android/iOS Expo applications, not browser SPAs. Azure Static Web Apps cannot host or distribute native binaries. SWA is documented only as an optional future static companion/API-documentation host; native app builds and distribution require EAS Build and/or the app stores.

## 26. Approved Cab-only MVP business model (2026-11-06)

This section supersedes the earlier four-category and customer-facing FIFO descriptions in sections 6, 17 and 23–25 for the Rider/Driver MVP.

- New Rider bookings and new Driver onboarding are Cab-only. Do not destructively migrate or delete historical Scooty/Bike/Auto records; legacy records remain readable where needed for history.
- Resolve pickup to configured service coverage, discover approved unions serving Cab, and show at most 2–3 eligible/available Cab drivers per union ordered by pickup proximity. The Rider explicitly selects a driver. Do not rank customer-facing choices by FIFO or automatically assign/advance a driver.
- The internal role of FIFO/queue fairness remains unresolved. Preserve legacy implementation until the selected-driver flow is introduced, but do not extend it as customer-facing dispatch behaviour.
- Rider and Driver negotiate the fare directly. Persist immutable, versioned agreement proposals/counterproposals; both parties must confirm the current version. A ride cannot start until mutual confirmation of that exact version.
- Platform fare estimates, in-app payment gateway, escrow and payment collection are out of scope. Do not fabricate or calculate a platform quote.
- After the trip, the Driver reports the amount received. Treat it as driver-reported/unverified, persist the agreed-minus-reported signed delta, and expose only the minimum required fields to authorised union roles. Do not imply bank/customer verification.
- Driver payment reporting is an immutable optional field in the existing Rides aggregate (partitioned by rideId), with the latest mutually confirmed agreement version, Rider/Driver/Union linkage, agreed amount, received amount, signed delta, currency, UTC timestamp, reporting actor, unverified status and hashed idempotency key. The existing ride ETag is the single-writer guard; no separate payment container or migration is needed.
- Driver trip history returns only that Driver's report. `GET /api/v1/union/reconciliation` requires the UnionManager role and filters every result by the signed unionId claims; it returns only ride/Driver references, agreement/report amounts, delta, currency, report timestamp, reporting actor and unverified status, never Rider identity, route/stops, contact data, exact location or private chat.
- A same-key/same-payload retry is idempotent; another payload cannot overwrite the first report. Customer dispute/confirmation handling and the next-ride policy for non-zero deltas remain product decisions; no delta-based next-ride gate is introduced.
- Union approval controls Cab membership, vehicle verification and public selfie visibility. Existing union approval and assignment audit remain relevant; legal partnership terms and production reviewer authorization require separate review.


### CAB-SCOPE-01 API enforcement

New Rider ride requests and nearby discovery accept only the canonical `cab` category. New Driver onboarding, Online/queue eligibility and new Union service configuration are Cab-only. Existing Scooty/Bike/Auto profile, union, ride and queue records are not destructively migrated or silently rewritten; legacy Driver profiles remain readable for history but cannot enter new Cab availability/ride workflows. RIDER-03 now creates an explicitly selected-driver negotiation without touching the FIFO queue. The existing FIFO path remains disabled for Rider requests by default and is retained only for compatibility/internal fairness pending the unresolved product decision.


## 27. Union-grouped Cab Rider discovery (CAB-DISCOVERY-01)

The authenticated Rider endpoint is GET /api/v1/rider/discovery?latitude={lat}&longitude={lon}&passengers={n}. It has no service-class selector: new MVP discovery is Cab-only. The API resolves the pickup to a configured service zone only when exactly one configured rectangle contains the coordinate. Zero matches are outside the configured service area; multiple matches (including inclusive shared boundaries) are ambiguous and fail closed. The resolver never chooses the first match or uses configuration order as precedence. Discovery and selected booking reject ambiguous pickups; Driver availability/location checks do not keep a Driver Online when the coordinate cannot be resolved uniquely. The API also requires a positive Dispatch:NearbyRadiusKm; the checked-in value is deliberately 0, so discovery returns 503 until the product-approved radius is configured. It does not silently substitute a radius or return the whole zone. In the current data model, Union Status=Active is the operational approval/eligibility flag; union creation and status changes remain protected by the interim backend operations key. This is not authenticated union-manager reviewer RBAC or legal partnership approval, both of which remain pre-production gates.

Discovery does not read QueueEntries, queue positions or FIFO offer order. It reads active Unions serving Cab in the resolved zone, current active DriverUnionAssignments, and approved/Online Cab Driver profiles in that zone. A candidate must have approved DrivingLicence and VehicleRegistration document references, sufficient passenger capacity, a fresh GPS update within the explicitly configured positive `Dispatch:NearbyLocationMaxAgeSeconds`, be within the configured radius, and have no active per-driver reservation. No product freshness threshold is assumed; missing or non-positive configuration returns 503 for discovery and selected booking. Inactive unions, missing/transferred assignments, non-Cab or unapproved profiles, stale/out-of-radius locations and reserved drivers are excluded. The current active union assignment is authoritative; the old union cannot authorize a public selfie after transfer.

Candidates are grouped by Union. Each group returns no more than three drivers, sorted by unrounded Haversine driver-to-pickup distance with DriverId as a deterministic tie-breaker. Union groups are sorted by UnionName then UnionId. The response includes opaque DriverId for selection, display name, approved Cab make/model, passenger capacity and rounded pickup proximity. It omits phone number, full registration, exact coordinates and queue data. ProfilePhotoBase64/content type are returned only when the selfie is Approved by the driver's current assigned Union; pending, rejected or old-union approval yields a null public photo.

JourneyDistanceKm is currently null because no routing/maps provider is configured. The Rider app labels journey distance unavailable and separately labels driver-to-pickup proximity; it must not substitute one for the other. Call/chat controls remain unavailable until a provider is configured. RIDER-03 persists the selected Driver, Union, pickup, destination and optional stops; it acquires an expiring driver reservation and creates a Negotiating ride. The Driver receives a RideSelected event and can accept only after both parties confirm the current agreement. The booking response includes the selected Driver's approved display name and Cab make/model plus pickup proximity, but not exact Driver coordinates; location sharing starts only in the assigned ride flow. Call/chat and journey routing remain provider placeholders. The Rider obtains a fresh foreground GPS fix for the first selected-booking attempt instead of reusing the earlier discovery fix. That coordinate pair is held with the attempt's Idempotency-Key and reused unchanged for retries after an ambiguous response; editing route/passengers/stops or selecting another Driver resets both the captured coordinates and key. A reset fences any older in-flight GPS result so it cannot be attached to the changed selection.

The legacy Rider GET /api/v1/rides/nearby endpoint returns 410 Gone so the app cannot consume FIFO-ranked results. Legacy POST /api/v1/rides/request is disabled by default through Dispatch:LegacyFifoRiderRequestsEnabled=false; the true override exists only in integration-test fixtures to preserve regression coverage for legacy ride/offer code. Never enable that override in a deployed Rider API. Internal FIFO policy remains unresolved and is not used by discovery.


## Direct fare agreement persistence (RIDER-02)

- Agreement terms are immutable `RideAgreementVersion` records embedded in the ride aggregate; a new proposal/counterproposal appends a new version rather than editing an earlier amount or route.
- Rider and Driver confirmations are separate append-only `RideAgreementConfirmation` records keyed by ride/version/party. A confirmation for an older version never confirms the current version. The Rider/Driver agreement-history endpoint returns each party's confirmation flag and UTC confirmation timestamp for every version; both apps display those recorded times. Unconfirmed parties have a null timestamp.
- `AgreementStateRevision` increments for every agreement mutation. The ride update uses the existing Rides-container partition (`/rideId`) and ETag conditional replacement; concurrent proposals or confirmations return a conflict and must refresh. Start and completion use the same conditional ride replacement, preventing a stale confirmation check from racing through a state transition.
- Proposal requests require an actor-scoped `Idempotency-Key` and persist an optional SHA-256 `RequestHash` over the canonical two-decimal amount and uppercase currency. A replay with the same key and matching terms returns the immutable version; a changed amount or currency returns HTTP 409 on both the initial lookup and conditional ETag-conflict retry path. Older stored versions without `RequestHash` reconstruct the fingerprint from their immutable amount/currency, so no migration or destructive rewrite is required. The server also validates party ownership, active Cab union membership, approved Cab driver profile, positive two-decimal amount and uppercase three-letter currency.
- Both parties must confirm the latest version before start. If a new version is proposed during an active trip, completion remains blocked until both parties confirm that version.
- No platform fare calculation, estimate, recommendation, payment gateway, escrow or collection is introduced.
- RIDER-03 adds optional stops to the ride aggregate and agreement route snapshot. The selected negotiation reservation lifetime is controlled by Dispatch:SelectedDriverNegotiationLifetimeMinutes; checked-in production appsettings is null and booking returns 503 until the approved policy value is configured. The 15-minute value in the integration-test fixture is test-only. Expiry is enforced lazily when a ride is read/listed, Rider discovery runs or Driver availability is checked. Reservation stores return expired leases to the API and do not delete/steal them; the API first transitions the linked Negotiating/AgreementConfirmed ride to Expired (or confirms an expired orphan), then releases only the matching reservation. Cancellation, decline, expiry and completion release only the matching reservation. Terminal reservations left by an interrupted release are reconciled on subsequent discovery/availability checks. Selected ride creation uses create-if-absent semantics so ambiguous Cosmos write outcomes cannot overwrite an existing ride or prematurely release its driver lease. If the ride write was interrupted, same-key recovery reuses the original idempotency timestamp for RequestedAt, RideCreated audit and NegotiationExpiresAtUtc; it does not extend the ride deadline beyond the existing temporary reservation lease. Union membership assignment/transfer acquires the same per-driver exclusive lease and is rejected while a selected negotiation or ride is active; the bounded transfer lock is released after the audited membership change and expired orphan locks are reconciled like other reservations. Live Cosmos ETag/multi-instance and physical-device acceptance remain open.

- RIDER-03 also enforces one active selected booking per Rider, independently of the existing per-Driver reservation. The active Rider reservation is a fixed `active-selected-booking` item in the existing `RideIdempotency` container, whose partition key is `/riderId`; no new container or migration is introduced. Create-if-absent acquisition and append-only privacy-minimised audit creation use a same-partition transactional batch. Driver acceptance promotes the matching Rider lease to permanent with an ETag-conditional replace plus audit event. Cancellation, Driver decline, expiry and completion release only the matching RiderId+RideId reservation with an ETag-conditional delete plus audit event. The API reconciles the authoritative ride lifecycle before releasing expired temporary/orphan reservations; the store never steals a lease solely because its timestamp elapsed. Same-key replay repairs a missing reservation and returns the existing ride; a different active booking for the same Rider fails with HTTP 409. When no Rider reservation exists, the API also checks the Rider's active ride projection, including pre-deployment/legacy active rides, so those rides cannot be bypassed by a new selected booking or at Driver acceptance. The Rider app hydrates the server-owned active ride after sign-in, routes the user to Activity and disables Book/discovery/request while a ride is active. Live Cosmos transactional-batch/ETag/multi-instance/restart behavior remains unverified until an authorized live integration environment is available.
- Cosmos ETag behaviour and multi-instance contention still require live Cosmos integration tests; CI currently validates the in-memory API concurrency path only.


## PLATFORM-01 — bounded storage readiness

- `/health/live` is a process-liveness endpoint and does not query Cosmos DB.
- `/health/ready` uses a storage readiness probe. In in-memory Development/Testing mode the probe completes locally; in Cosmos mode it performs a database metadata read.
- The probe enforces its fixed three-second upper bound with a cancellation-aware bounded wait, even if a provider delegate ignores the supplied token; late provider faults are observed without logging their details. Genuine caller cancellation propagates. Only the probe-owned deadline is classified as `timeout`; provider-originated `OperationCanceledException` or `TimeoutException` without caller/deadline cancellation is classified as dependency `unavailable`. Both dependency failure and probe timeout become generic HTTP 503 readiness responses; internal SDK diagnostics, storage mode, database name and endpoint details are not returned.
- Public `/` and `/health` projections expose only service identity/version/message and generic health status. `/health/ready` exposes only service/status after the real configured storage probe; its response is identical in shape for demo and Cosmos modes. The bounded failure kind remains server-side and does not include provider exception text.
- This is a bounded health probe, not proof of live Cosmos transactional-batch, ETag, multi-instance or restart recovery. Those remain separate infrastructure validation gates.



## PLATFORM-01 — provider-neutral API diagnostics

- The API exposes a .NET Meter named TurnOva.API and an ActivitySource with the same name. They emit ride-audit write-attempt counters/spans and storage-readiness check counters/duration/spans.
- Metric dimensions are bounded to allow-listed audit event types and outcomes (success, conflict, failure, unavailable, timeout, cancelled). Activities carry only the normalized event type plus bounded write outcome, or the readiness outcome. Ride IDs, actor IDs, Union IDs, route/location, contact data, document references, tokens and exception messages are excluded.
- The diagnostics use framework APIs only. There is no exporter, telemetry vendor, credentials or public metrics endpoint in the MVP code. A hosting environment may attach a compatible listener/exporter without changing ride-domain behavior.
- Structured exception logging and /health/live plus /health/ready remain the existing operational surfaces. Alert rules, production telemetry routing, live Cosmos multi-instance ETag/restart/retention validation, production origin configuration and load tests remain infrastructure gates.

## 16. AI-01 — bounded assistance boundary

The MVP AI boundary lives in `TurnOva.Application.Assistance` and is exposed through authenticated Rider/Driver API endpoints. It is deliberately separate from dispatch, agreement, payment and eligibility services.

- `IAiAssistantProvider` is the provider-agnostic adapter contract. The checked-in `UnavailableAiAssistantProvider` is the safe default; `AiAssistant:Enabled` is false and provider, model, HTTPS endpoint and managed-credential reference are empty placeholders. No provider, model, price or credential is selected.
- `AiAssistanceService` owns request validation, provider timeout/cancellation and deterministic fallback. A provider timeout or transient HTTP failure returns the deterministic result; caller cancellation is propagated.
- Rider trip intent is parsed locally for pickup, destination and passenger count. Before a provider call, the full route span is removed and only fixed `[PICKUP]` / `[DROPOFF]` placeholders plus generic action words are retained. The provider prompt and result contracts contain no passenger-count field. Passenger count is never sent to the provider; if it is absent or invalid locally, the API returns null and lists `passengerCount` in `MissingFields` for manual completion. Family/child context, accessibility/health details, luggage, location-type hints and arbitrary free text are excluded from provider intent tokens. The Rider app clears its stale/default passenger input after such a draft and blocks discovery/booking until the Rider manually enters an integer from 1 through 6.
- Every trip-intent response requires Rider review. The endpoint only returns a draft; it has no booking, fare, agreement, payment or ride-state dependency and cannot trigger those operations.
- Help requests are reduced to allow-listed keywords and a canonical ride-state value. Approved content explains ride states plus the limited queue/availability facts that are safe under the unresolved internal FIFO decision. A provider may return only an approved FAQ/state source ID. The API returns the exact version-controlled answer from `AiAssistanceService`; unknown source IDs, ambiguous generic status questions and unsupported questions fall back to safe escalation text rather than guessing a ride state.
- The Rider-only trip-intent endpoint and Rider/Driver help endpoint use JWT role authorization and a per-authenticated-caller fixed-window rate limit. Input lengths, provider tokens/source IDs, service class and passenger count are validated. Logs include only use-case/source/outcome metadata; prompts, routes, identity, contact, payment and token values are never logged.
- AI is prohibited from setting fares, changing payment decisions, approving identity/KYC/selfies, determining Driver eligibility, ranking discovery/FIFO, accepting/rejecting rides or changing authoritative ride state. The deterministic backend remains the source of truth.
- No provider credentials are required for local fallback or CI. Live provider sandbox validation, data-processing terms and managed-secret wiring remain post-coding infrastructure gates.


### Driver history and direct-payment report projection

- The authenticated Driver history response may include a minimal current-agreement summary (version, agreed amount and currency) so a completed-ride payment-report form uses the actual agreement currency. It does not expose agreement route snapshots or confirmation/audit internals through this summary.
- Driver payment reconciliation remains a separate immutable, Driver-reported/unverified record in the ride aggregate. Rider history does not add the Driver-only current-agreement summary or reconciliation report; the UnionManager reconciliation endpoint remains scoped by signed union claims and its minimum-data response.
- The UnionManager reconciliation Cosmos query uses a flat scalar projection for ride/Union linkage, agreement version, agreed/received amounts, signed delta, currency, report time, reporting Driver and status. It applies `LOWER(c.serviceClass) = 'cab'` at the Cosmos boundary before materializing the projection and does not read the full `DemoRide` aggregate or Rider identity, route/stops, exact location or contact fields for this list. Signed `unionId` claims and matching report/ride Union IDs are rechecked, and the API Cab post-filter remains defense in depth; live Cosmos SQL function/index/query-plan and partition validation remains external.


### Rider active-booking recovery and client hydration

- The Rider reservation is one fixed `active-selected-booking` item per Rider in the existing `RideIdempotency` `/riderId` partition. Acquisition, permanent promotion and release are paired with same-partition audit events in Cosmos transactional batches; ETags protect replace/delete operations. No new container or partition migration is introduced.
- Expired temporary reservations are never stolen by acquisition. The store's explicit `allowExpiredForAuthoritativeActiveRide` option is used only after the API has re-read the same ride and confirmed `Accepted`, `Arrived` or `InProgress`; negotiation states are first expired through the authoritative ride transition. This permits safe repair of a stale temporary lease for an active ride without allowing timestamp-only takeover.
- Rider booking UI fails closed while `/api/v1/rides/mine` and the selected active-ride detail are loading or have failed. The Book tab, discovery and selected-booking handler remain disabled until successful hydration; session reset invalidates the hydrated flag. The API reservation remains the authoritative concurrency boundary.
