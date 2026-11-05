# TurnOva — Product Requirements Document

**Tagline:** Everyone’s Turn Matters.  
**Platforms:** Android and iOS mobile apps only  
**Status:** Initial product definition

## 1. Vision

TurnOva is a ride-hailing platform designed to make driver ride allocation transparent and fair. Its core differentiator is a rules-based First In, First Out (FIFO) dispatch model that gives eligible drivers fair opportunities while respecting geographic zones, vehicle type and passenger capacity.

## 2. Problem and opportunity

Drivers need confidence that ride opportunities are allocated fairly and transparently. Riders need to discover nearby vehicles, understand availability and capacity, book trips and track progress. TurnOva combines clear queue rules with a practical rider experience.

## 3. Goals

- Provide auditable, zone-based FIFO dispatch.
- Give drivers clear queue status and assignment updates.
- Match rides to eligible drivers and compatible vehicles.
- Support reliable booking, trip lifecycle, tracking, payment and support.
- Establish a secure foundation that can scale to additional cities and fleet operations.

## 4. Non-goals for MVP

- Passenger or driver web applications.
- Fleet-owner app or internal operations app.
- A single citywide queue irrespective of driver location.
- Guaranteed ride acceptance or guaranteed driver earnings.
- Unvalidated dynamic pricing or opaque priority overrides.

## 5. Apps and phases

| App | Audience | Phase | Main capabilities |
|---|---|---|---|
| TurnOva Driver | Individual drivers | MVP | Onboarding, availability, FIFO queue, trips, earnings |
| TurnOva Rider | Passengers | MVP | Nearby vehicles, booking, fare estimate, tracking, payment, history |
| TurnOva Fleet | Fleet owners/managers | Phase 2 | Vehicles, driver assignment, documents, availability, reporting |
| TurnOva Ops | Internal operations | Phase 2 | Approvals, zones, queue monitoring, support, disputes, configuration |

All apps are Android/iOS mobile apps. They share one backend and dispatch engine; the backend is not a fifth app.

## 6. User roles

- Rider: searches, books, tracks and pays for rides.
- Driver: completes onboarding, manages availability, joins eligible queues and fulfils trips.
- Fleet manager: manages vehicles, drivers and fleet performance (Phase 2).
- Operations/admin: manages approvals, service zones, incidents and platform configuration (Phase 2).

## 7. MVP requirements

### TurnOva Rider
- Account registration, login and profile management.
- Pickup/drop-off selection and location permission.
- Nearby available vehicles and relevant capacity information.
- Fare estimate and trip details before confirmation.
- Booking creation, status and cancellation under a published policy.
- Driver/vehicle details and live trip progress after assignment.
- Payment through enabled methods, trip history, receipts and ratings.
- Help and issue reporting.

### TurnOva Driver
- Registration and onboarding with identity, licence, vehicle and required documents.
- Approval status and eligibility feedback.
- Online/offline availability controls.
- Join the FIFO queue for an eligible service zone.
- View current zone, queue position and queue state.
- Receive trip offers and accept/reject within a defined response window.
- Clear queue behaviour for rejection, timeout, trip completion and re-entry.
- Trip lifecycle controls, history, earnings and support.

### Shared platform
- Driver eligibility and availability validation.
- Queue membership and ordering by zone and compatible service/vehicle class.
- Matching by pickup geography, service type and passenger capacity.
- Atomic ride/driver assignment to prevent duplicate allocation.
- Realtime queue and trip updates.
- Persistent audit trail for queue changes, dispatch decisions and exceptions.

## 8. FIFO allocation policy

### Queue scope
Queues are scoped to operationally meaningful geographic zones and compatible service/vehicle classes. A citywide queue is not the default because it may produce unreasonable pickup distances. Zone boundaries and cross-zone fallback must be explicitly configured.

### Ordering and eligibility
The server assigns queue-entry order and timestamps. A client cannot set its own queue position. A driver is eligible only when approved, online, available, active in the relevant queue, document-valid, compatible with the requested service and capacity, and free of conflicting assignments.

### Fairness rules
- Consider eligible drivers in queue-entry order.
- A driver on a trip or temporarily unavailable must not block the queue.
- Define consistent rules for rejection, timeout, cancellation, no-show, connectivity loss and rejoining.
- Do not silently penalize drivers or change priority.
- Record every exception/override with a reason, actor and timestamp.
- Prevent duplicate queue entries, queue jumping and client-side manipulation.
- Define boundary and cross-zone behaviour before pilot.

### Dispatch sequence
1. Validate and persist the ride request.
2. Resolve pickup zone and requested service class.
3. Retrieve ordered eligible candidates.
4. Revalidate candidate availability and compatibility.
5. Offer the ride to the next candidate under the published policy.
6. Atomically reserve/assign the ride to prevent double allocation.
7. On decline, timeout or failure, apply the documented policy and consider the next candidate.
8. Notify rider and driver and persist the dispatch audit trail.

Offer timeout, retry, queue advancement and cross-zone fallback are configurable policy decisions and must not be hidden in client code.

## 9. Trip lifecycle

Proposed flow: Requested → Matching → Offered → Accepted → DriverArriving → InProgress → Completed.

Terminal alternatives include Cancelled, Expired and NoDriverAvailable. All state transitions are validated server-side and timestamped.

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

- Riders can request rides, explicitly choose Scooty, Bike, Auto or Cab, and see clear booking states.
- Drivers select the same supported vehicle category during onboarding; queue membership, ride eligibility and union service-class support use the same Scooty, Bike, Auto and Cab values.
- Pickup coordinates resolve to a configured dispatch zone; rides are visible only to drivers in the matching zone and vehicle-class queue.
- Only approved, eligible and compatible drivers are considered.
- Queue order is server-controlled and reproducible from persisted events.
- Eligible drivers are considered in FIFO order within the configured zone/service class, subject to documented exceptions.
- No ride or driver can be assigned twice.
- Rider and driver receive consistent realtime updates and can recover state after reconnecting.
- Queue changes and dispatch outcomes are auditable.
- Android and iOS builds pass agreed functional, security and pilot-readiness tests.

## 13. Roadmap

1. Finalize zone design, dispatch policy, data model and architecture.
2. Build identity, eligibility, queue and dispatch backend foundations.
3. Deliver TurnOva Driver and TurnOva Rider MVP apps.
4. Integrate realtime trip lifecycle, payments and support.
5. Complete automated testing, security/load testing and Hyderabad pilot.
6. Add TurnOva Fleet and TurnOva Ops in Phase 2.

## 14. Open decisions before launch

Initial zones and boundaries; vehicle categories and capacity; fares, taxes, commissions and cancellations; driver offer timeout and re-entry rules; cross-zone fallback; KYC requirements; maps, routing, OTP, payment and notification providers; safety/support escalation; retention and privacy policy; and applicable transport/aggregator requirements. These require product, legal and operational validation.
