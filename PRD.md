# TurnOva — Product Requirements Document

**Tagline:** Everyone’s Turn Matters.  
**Platforms:** Android and iOS mobile apps only  
**Status:** Initial product definition

## 1. Vision

TurnOva is a union-connected Cab discovery and ride-coordination platform. Riders discover approved nearby unions, select an eligible Cab driver, and negotiate the fare directly with that driver. The platform records mutually confirmed agreements, trip progress and driver-reported direct-payment reconciliation; it does not set fares or automatically dispatch a FIFO candidate.

## 2. Problem and opportunity

Drivers need a trusted union relationship, verified Cab onboarding and clear ride/agreement records. Riders need to discover approved nearby unions, compare eligible Cab drivers by pickup proximity, select a driver and coordinate the trip directly. TurnOva records the agreement and the Driver’s report of money received without collecting payment.

## 3. Goals

- Discover approved unions serving Cab near the Rider’s pickup and let the Rider select an eligible Driver.
- Verify Cab Driver membership, vehicle details and union-approved public selfie visibility.
- Record direct Rider/Driver fare negotiation and a mutually confirmed, versioned agreement.
- Protect selected Drivers from overlapping negotiations and active rides.
- Record completed trips and Driver-reported direct-payment reconciliation with least-privilege union oversight.

## 4. Non-goals for MVP

- Passenger or driver web applications.
- Fleet-owner app or internal operations app.
- A single citywide queue irrespective of driver location.
- Guaranteed ride acceptance or guaranteed driver earnings.
- Unvalidated dynamic pricing or opaque priority overrides.

## 5. Apps and phases

| App | Audience | Phase | Main capabilities |
|---|---|---|---|
| TurnOva Driver | Individual drivers | MVP | Cab onboarding, union membership, availability, selected-trip negotiation, trip lifecycle, payment reporting |
| TurnOva Rider | Passengers | MVP | Union-grouped Cab discovery, driver selection, direct negotiation, trip tracking and history |
| TurnOva Fleet | Fleet owners/managers | Phase 2 | Vehicles, driver assignment, documents, availability, reporting |
| TurnOva Ops | Internal operations | Phase 2 | Approvals, zones, queue monitoring, support, disputes, configuration |

All apps are Android/iOS mobile apps. They share one backend and dispatch engine; the backend is not a fifth app.

## 6. User roles

- Rider: discovers approved Cab unions, selects a Driver, negotiates and confirms the fare, tracks the trip and pays the Driver directly.
- Driver: completes Cab and union onboarding, manages availability, negotiates and confirms the fare, fulfils the trip and reports the amount received directly from the Rider.
- Fleet manager: manages vehicles, drivers and fleet performance (Phase 2).
- Operations/admin: manages approvals, service zones, incidents and platform configuration (Phase 2).

## 7. MVP requirements

### TurnOva Rider
- Account registration, login and profile management.
- Pickup, destination and optional intermediate-stop entry with location permission.
- Discovery of active, approved unions serving Cab within configured pickup coverage.
- Display up to 2–3 approved, eligible and available Cab drivers per union, ordered by driver-to-pickup proximity; distinguish this from journey distance.
- Show only union-approved driver photos and verified Cab vehicle details.
- Select one Driver and use call/chat actions when the configured providers are available.
- Negotiate directly with the selected Driver and explicitly confirm the current agreement version.
- View trip status, live progress, history and support information.
- Pay the Driver directly outside TurnOva; no in-app payment collection, gateway or escrow.

### TurnOva Driver
- Registration and Cab-only onboarding with identity, licence, verified vehicle and required documents.
- Approved union membership and eligibility feedback; public selfie visibility only after approval by the current union.
- Explicit Online/Offline availability and current pickup location, subject to approval and active-ride reservation.
- Receive Rider-selected Cab negotiation requests; propose/counter-propose and confirm the current agreement.
- Start a ride only after both Rider and Driver confirm the same current agreement version.
- Complete the trip, view history and report the amount received directly from the Rider as unverified.
- Access work-metrics payslips and support; monetary earnings/commission/payout policy is not assumed.

### Shared platform
- Cab-only validation for new Rider requests, Driver onboarding and new Union configuration; retain historical non-Cab records without destructive migration.
- Discover eligible unions and drivers by pickup coverage, approved status, availability, vehicle verification, passenger capacity and pickup proximity.
- Concurrency-safe selected-Driver reservations; no overlapping active negotiation or ride.
- Durable, immutable agreement versions with both-party confirmation and server-owned ride-state transitions.
- Driver-reported amount, signed agreed-minus-reported delta, least-privilege Union visibility and audit history.
- Realtime negotiation/agreement/trip updates and reconnect recovery.
- Persistent audit trail for membership, reservation, agreement, trip and reconciliation changes.

## 8. FIFO and legacy queue compatibility boundary

The approved Cab-only Rider flow is customer-selected-driver discovery, not customer-facing FIFO dispatch.

- Do not rank Rider discovery results by queue position.
- Do not automatically assign a Rider request to the FIFO head or advance the request to another Driver after a decline/timeout.
- Rider discovery is grouped by active, approved unions and ordered by Driver-to-pickup proximity, subject to eligibility and the per-union result limit.
- Whether FIFO remains as an internal Union fairness/availability mechanism or is retired is an unresolved product decision. Do not extend FIFO as a customer-facing ranking or assignment rule until explicitly decided.
- Preserve existing queue and offer history safely while the selected-Driver flow replaces legacy Rider dispatch. Existing FIFO/offer endpoints and expiry workers are legacy compatibility code, not evidence that customer-selected negotiation is implemented.
- Any retained internal queue policy must be server-owned, auditable, idempotent and prevented from overriding a Rider-selected Driver.

## 9. Trip lifecycle

Approved MVP flow: Rider request → Driver selected → Negotiating → AgreementConfirmed → InProgress → Completed → DriverPaymentReported.

Terminal alternatives include Cancelled and Expired. All state transitions are validated server-side, idempotent and timestamped. A Driver cannot start until both parties confirm the same current immutable agreement version. A fare change creates a new agreement version and invalidates prior confirmations for the new version.

## 10. Non-functional requirements

- Fairness: deterministic, testable ordering and auditable exceptions.
- Reliability: idempotent booking/assignment and resilient transient-failure handling.
- Security: TLS, least privilege, protected secrets and secure token handling.
- Privacy: data minimization, consent and defined retention/deletion policies.
- Performance: measurable API, dispatch and realtime targets established through pilot load tests.
- Observability: structured logs, metrics, traces, health checks and alerts.
- Accessibility: readable layouts and accessible status communication.
- Localization: design for Indian regional languages and formats.

## 11. Technology baseline

- Backend: .NET 10 LTS, ASP.NET Core Web API, C# 14, target framework net10.0.
- Mobile: React Native + Expo for Android and iOS.
- Database: Azure Cosmos DB.
- Realtime: SignalR.
- Authentication: JWT access tokens and securely rotated refresh tokens.
- API documentation: OpenAPI/Swagger.
- Source control: GitHub.

A free-tier-first approach is intended for development. Production hosting, maps, geocoding, OTP/SMS, payments, push notifications, realtime services and app-store publishing may incur costs or quotas.

## 12. MVP acceptance criteria

- New Rider requests, Driver onboarding and new Union service configuration accept Cab only; historical Scooty/Bike/Auto records are preserved and excluded from new MVP eligibility.
- Riders can enter pickup, destination and optional stops, discover active approved unions serving Cab, and see at most 2–3 eligible/available Cab drivers per union ordered by pickup proximity.
- Discovery exposes only union-approved driver photos and verified Cab details; journey distance and driver-to-pickup distance are distinct.
- Riders explicitly select a Driver; the customer-facing flow does not rank by FIFO or automatically assign/advance a Driver.
- Rider and selected Driver can negotiate directly and persist immutable agreement versions; both must confirm the current version before ride start.
- A selected Driver cannot be held by overlapping active negotiations or rides; expiry/cancellation releases only the matching reservation.
- Trip transitions, idempotency, live updates, reconnect and history are server-authoritative and auditable.
- After completion, Driver-reported amount received and signed agreed-minus-reported delta are persisted and labelled unverified; authorised Union users see only minimum necessary data.
- No platform fare estimate, payment gateway, escrow or in-app payment collection is part of the MVP.
- Android and iOS builds pass agreed functional, security and pilot-readiness tests.

## 13. Roadmap

1. Finalize zone design, dispatch policy, data model and architecture.
2. Build identity, Cab eligibility, Union discovery, selected-Driver reservation and negotiation foundations.
3. Deliver TurnOva Driver and TurnOva Rider MVP apps.
4. Integrate realtime selected-Driver negotiation, agreement, trip lifecycle, direct-payment reporting and support.
5. Complete automated testing, security/load testing and Hyderabad pilot.
6. Add TurnOva Fleet and TurnOva Ops in Phase 2.

## 14. Open decisions before launch

Pickup radius/zone boundaries; internal FIFO role; negotiation/booking cancellation and expiry policy; customer dispute/confirmation handling; non-zero-delta next-ride eligibility policy; support categories and escalation owner; maps/routing, chat/call and notification providers; Union partnership terms and legal/T&C approval; retention/privacy policy; production hosting and applicable transport/aggregator requirements. Do not invent fares, payment collection or reconciliation outcomes. These require product, legal and operational validation.


### Server-owned expiry increment

The API host runs a bounded background expiry worker in both Cosmos and in-memory modes. Expired offers are resolved by conditional state replacement and the associated driver is moved to the back of the same zone/service-class queue at most once for a given offer ID. A missing queue membership is retried rather than silently resolving the offer. This is an incremental recovery workflow; automatic next-driver delivery and atomic ride/driver reservation are not included.


## Exclusive active-driver reservation

A driver may hold at most one active ride reservation at a time. The backend acquires a durable per-driver reservation before attempting the ride's conditional Requested-to-Accepted transition. The ride's ETag remains the single-winner guard when multiple drivers race for the same ride. A failed ride claim releases the matching driver reservation; successful assignment retains it until ride completion or rider cancellation. Reservation release is scoped to the exact driver/ride pair so a stale request cannot clear a different ride's reservation. The reservation and ride are stored in separate Cosmos partitions, so crash recovery and reconciliation remain required before production.


## 15. Approved MVP business model — Cab only (2026-11-06)

This section supersedes conflicting earlier statements. TurnOva MVP supports CAB rides only. Scooty, Bike and Auto are out of scope for new onboarding, discovery and ride workflows. Preserve historical records without destructive migration.

TurnOva is a union-connected cab discovery and ride-coordination platform, not automatic FIFO dispatch or fixed-fare pricing.

### Rider journey
1. Rider enters pickup, destination and optional stops.
2. Discover active, approved unions serving Cab within the configured pickup radius.
3. Group eligible, approved and available Cab drivers by union; show no more than 2–3 per union, ordered by driver-to-pickup proximity.
4. Show only union-approved driver photos and verified Cab details. Distinguish driver-to-pickup proximity from journey distance.
5. Rider selects a driver and can call/chat when providers are configured.
6. Rider and selected driver negotiate directly. No platform fare estimate or recommendation.

### Agreement and ride
- Record proposals/counterproposals or equivalent negotiation history.
- Persist a durable, immutable, versioned agreement with ride, Rider, Driver, Union, amount, currency, route/stops, timestamps and both confirmations.
- A changed fare creates a new version; both parties must confirm the current version.
- Driver cannot start until the current agreement is mutually confirmed.
- Enforce server-owned legal transitions, idempotency, safe expiry/cancellation and concurrency-safe driver reservations. No overlapping active negotiation or ride.

### Direct payment reporting
- Rider pays Driver directly. No in-app payment collection, gateway or escrow.
- After completion, Driver reports amount received; label it driver-reported/unverified.
- Signed delta = agreed amount minus driver-reported amount: positive=reported shortfall, zero=exact, negative=reported excess.
- Persist amount, delta, currency, timestamps, reporting actor and reconciliation status. Apply least-privilege Driver/Union access and audit.
- Customer dispute/confirmation and next-ride policy for non-zero delta remain unresolved; do not invent them.

### Union and FIFO
- Support approved union participation, Cab driver membership, authorised transfer, Cab/vehicle verification and union selfie approval before public photo display.
- Union oversight exposes minimum necessary ride/reconciliation data, never private chat or unrelated customer data. Partnership terms require legal review.
- FIFO must not rank Rider discovery or automatically assign a driver. Whether it remains internal union fairness/availability is unresolved.

Out of scope: Scooty, Bike, Auto, platform fare estimates/pricing, payment gateway/escrow, fleet/corporate workflows and customer-facing FIFO dispatch.
