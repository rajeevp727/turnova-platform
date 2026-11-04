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
- DispatchAuditEvent: reasoned record of queue and allocation decisions.

## 6. FIFO dispatch architecture

### Queue partitioning
Create logical queues by service zone and compatible service/vehicle class. Avoid a single citywide queue that routinely creates distant pickups. Zone boundaries and cross-zone fallback are explicit policy.

### Ordering and eligibility
The server assigns queue-entry order. A deterministic sequence and stable tie-breaker establish ordering. Clients cannot submit or modify queue rank.

Candidates must be approved, online, available, active in the relevant queue, document-valid, service/capacity compatible and free of conflicting assignments. Revalidate immediately before assignment.

### Atomicity and concurrency
Prevent assigning one driver to multiple active rides and prevent assigning one ride to multiple drivers. Use Cosmos DB transactional batches or conditional writes within an appropriate logical partition where possible. Cosmos DB transactions are scoped to a logical partition; do not assume cross-partition atomic transactions. Use ETags/optimistic concurrency, idempotency keys and bounded conflict retries. If invariants span partitions, revise the partition model or add a dedicated coordination mechanism before production.

### Queue lifecycle
Model join, unavailable, on-trip, leave, rejoin, offer-pending, accepted, rejected and timed-out states explicitly. Define whether decline/timeout retains, advances or resets position. Record every exception with reason, actor and timestamp. Never silently alter priority.

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
- Profiles and onboarding records.
- Vehicles and document metadata.
- Service zones and configuration.
- Queue memberships and queue events.
- Ride requests, offers and trips.
- Dispatch audit events.

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

SignalR delivers queue-position changes, ride offers, driver location updates and trip lifecycle events. Persist state before publishing notifications. Realtime is not the source of truth: after reconnect, clients fetch authoritative current state.

Authenticate hub connections, scope groups, rate-limit events and protect location privacy. Do not expose precise driver location to unrelated riders.

## 10. Security and privacy

- TLS for all network traffic.
- Short-lived JWT access tokens and securely rotated/revocable refresh tokens.
- Store mobile credentials using platform secure storage.
- Role- and resource-based authorization for rider, driver, fleet and operations.
- Rate limiting, input validation, abuse detection and audit logging.
- Managed secret storage; no secrets in Git.
- Minimize location/identity data and obtain appropriate consent.
- Define retention, deletion, export and incident-response procedures.
- Do not store raw payment-card data; use payment-provider tokenization.
- Validate applicable Indian transport, aggregator, insurance, consumer and privacy obligations with qualified advisors.

## 11. External integrations

Provider decisions remain open for maps/geocoding/routing, OTP/SMS, payment/refunds, push notifications, identity verification and monitoring. Isolate providers behind interfaces/adapters. Verify pricing, quotas, regional coverage and production terms.

## 12. Deployment and operations

Start with a low-cost development environment and separate development, staging and production configuration. CI should build, test and scan backend/mobile code. Use managed secrets, health checks, structured logs, metrics, distributed tracing, database backup and restore testing. Do not assume production hosting is always free; validate availability, data residency, capacity, recovery and cost.

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
