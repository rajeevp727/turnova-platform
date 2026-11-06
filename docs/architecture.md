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
                                             ├── Zone FIFO dispatch engine
                                             ├── SignalR hubs
                                             ├── Cosmos DB repositories
                                             └── External provider adapters
                                                      ├── Maps / Routing
                                                      ├── OTP / SMS
                                                      ├── Payments
                                                      └── Push notifications

Fleet and Ops clients are Phase 2. The backend and dispatch engine are shared platform services, not separate apps.

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
- QueueAuditEvent: append-only queue join/leave/rotation record in the queue partition; RideOfferState.Attempts retain ride-offer decisions; DriverUnionAuditEvent retains union transfers.

## 6. FIFO dispatch architecture

### Queue partitioning
Create logical queues by service zone and compatible service/vehicle class. Avoid a single citywide queue that routinely creates distant pickups. Zone boundaries and cross-zone fallback are explicit policy. The `QueueEntries` container is partitioned by `/zoneServiceKey`; successful queue joins, leaves and rotations append `QueueAuditEvent` documents in the same partition and are committed with the queue-state mutation in one Cosmos transactional batch. In-memory queue storage records equivalent events for tests.

### Ordering and eligibility
The server assigns queue-entry order. A deterministic sequence and stable tie-breaker establish ordering. Clients cannot submit or modify queue rank.

Candidates must be approved, explicitly Online, active in the relevant queue, document-valid, service/capacity compatible and free of conflicting assignments. Online is persisted on the driver profile; the API adds the driver to the queue when they go online and removes them when they go offline. Rejoining appends at the back. A driver with an active ride cannot go offline. Revalidate immediately before assignment.

### Current API enforcement
The Driver available-rides endpoint returns actionable requested rides only to the current head of the driver's approved zone/service-class FIFO queue. Ride acceptance rechecks that queue-head condition on the server; clients cannot self-assign by calling the endpoint directly. Explicit rejection is also restricted to the current queue head and moves that driver to the back. These checks are a transitional fairness safeguard, not a complete offer system: there is not yet a durable offer ID/deadline, a server-owned expiry worker, or exclusive per-ride offer state. The timeout acknowledgement endpoint is not automatic expiry.

### Atomicity and concurrency

The current increment adds a `DriverRideReservations` container partitioned by `/driverId`, with one create-if-absent reservation document per driver. This serializes attempts by the same driver across different rides. The ride document remains partitioned by `/rideId` and uses its ETag for competing-driver/same-ride races. These are separate conditional writes, not a cross-partition transaction: a process stop after reservation creation but before the ride write can leave a reservation held. A reconciliation/lease recovery process is still required before production.

Prevent assigning one driver to multiple active rides and prevent assigning one ride to multiple drivers. Use Cosmos DB transactional batches or conditional writes within an appropriate logical partition where possible. Cosmos DB transactions are scoped to a logical partition; do not assume cross-partition atomic transactions. Use ETags/optimistic concurrency, idempotency keys and bounded conflict retries. If invariants span partitions, revise the partition model or add a dedicated coordination mechanism before production.

### Queue lifecycle
Model Online/Offline availability, join, unavailable, on-trip, leave, rejoin, offer-pending, accepted, rejected and timed-out states explicitly. Define whether decline/timeout retains, advances or resets position. Queue mutations append an event with driver, queue scope, event type and UTC time; offer attempts remain in the ride-partitioned aggregate. Unhandled API exceptions return generic Problem Details and safe logs with traceId and exception type only. Durable exception audit with actor/reason remains an acceptance gate. Never silently alter priority.

### Dispatch flow
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

SignalR delivers queue-position changes, ride offers, driver location updates and trip lifecycle events. Ride offers are persisted with a notification-pending marker before publication. A hosted outbox worker retries pending notifications and marks them sent with an ETag update. Delivery is at-least-once: a crash after SignalR send but before the sent marker can produce duplicates, so clients must de-duplicate by offerId. Realtime is not the source of truth: after reconnect, clients fetch authoritative current state. Initial and subsequent offers share clamped Dispatch:OfferLifetimeSeconds configuration (10–120 seconds, default 30).

Authenticate hub connections, scope groups, rate-limit events and protect location privacy. Do not expose precise driver location to unrelated riders.

## 10. Security and privacy

- TLS for all network traffic.
- Short-lived JWT access tokens and securely rotated/revocable refresh tokens.
- Store mobile credentials using platform secure storage.
- Role- and resource-based authorization for rider, driver, fleet and operations.
- Rate limiting, input validation, abuse detection and audit logging.
- Managed secret storage; no secrets in Git.
- Browser CORS is deny-by-default and uses explicit origins from `Cors:AllowedOrigins`; wildcard origins are rejected. Native Android/iOS clients do not require CORS.
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

Cosmos containers: Unions (partition key /unionId), DriverUnionAssignments (/driverId), and DriverUnionAudit (/driverId). In-memory equivalents support local tests. The interim Ops endpoints reuse the backend-only DriverOnboarding admin API key. This is an integration bridge, not production authorization; replace it with authenticated Ops identities, role/permission checks and actor identity before production. The driver app can read its current union assignment, while union creation, status management, assignment and transfer remain operations workflows. Union membership does not yet gate dispatch; that policy must be explicitly enabled after operational migration and rollout planning.

Employment contracts, payroll rules, earnings/deductions and payslip generation are intentionally outside this increment. Future payslips must be based on immutable work activity and preserve the employer/union active during the relevant pay period.

## 21. Monthly driver payslips (PDF only)

The Driver app offers a Payslips tab with the most recent 12 completed months and downloads the authenticated PDF through the native file/share workflow. There is no HTML payslip view or CSV/spreadsheet export.

A driver can request a finalized monthly payslip from GET /api/v1/driver/payslips/{year}/{month}.pdf. The route requires the Driver role and emits only application/pdf. No HTML, CSV or spreadsheet payslip format is exposed. Only completed months are generated, using India Standard Time calendar-month boundaries.

Ride activity is captured in an append-only ledger: Accepted when the driver accepts a ride, Rejected when the driver explicitly declines an available ride, and Completed when the assigned driver completes the trip. Rider cancellation is not a driver rejection. Deterministic activity IDs (ride ID plus activity type) make ledger insertion idempotent. Days worked are distinct India-local calendar dates containing at least one completed activity. Accepted and completed are separate metrics, so a completed ride contributes to both.

The monthly payslip is an immutable snapshot persisted in DriverPayslips after first generation. Work events are persisted in DriverWorkActivities. Both Cosmos containers are partitioned by /driverId; in-memory stores are used without Cosmos. A driver's union assignment history is used to identify the employer for the pay period. If multiple union assignments occur within one pay period, this initial version must reject generation rather than silently attribute all activity to the wrong employer; split-employer payroll is a later increment.

The PDF contains identity, employer, pay period and work metrics only. Monetary earnings, deductions, incentives and net payable are intentionally not calculated until a compensation policy is agreed. Activity ledger writes happen after ride state changes and are not transactionally coupled to the ride write; production hardening should introduce an outbox or event-driven reconciliation to prevent a crash between ride transition and ledger append from omitting an activity.

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
- Customer dispute/confirmation handling and the next-ride policy for non-zero deltas remain product decisions; do not implement an assumed outcome.
- Union approval controls Cab membership, vehicle verification and public selfie visibility. Existing union approval and assignment audit remain relevant; legal partnership terms and production reviewer authorization require separate review.

