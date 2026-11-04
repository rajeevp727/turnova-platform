# TurnOva MVP — Development Notes

**Purpose:** Single working reference for the Monday MVP demo.  
**Branch:** `mvp/monday-demo`  
**Stack:** .NET 10 Web API, React Native + Expo, Azure Cosmos DB (planned persistence), SignalR (planned realtime).

> Demo boundary: the current API queue adapter is in-memory. It is suitable only for a single-process local demo. Do not use it for real drivers, production traffic, or multiple API instances. Cosmos DB persistence, authentication and real dispatch assignment must be completed before a pilot.

## 1. Monday demo scope

Show one coherent journey across the two MVP apps:

1. A driver can view availability and join a zone/service-class FIFO queue.
2. The driver can see their queue position and current queue members.
3. A rider can enter pickup/drop-off details and submit a demo ride request.
4. The demo can show the request and explain that dispatch will select the next eligible driver in the matching zone/service class.
5. Both apps show clear demo states; no real payment, OTP, maps, live GPS, or production identity verification.

### Out of scope for Monday

- Real authentication/KYC and document upload.
- Real map/geocoding/routing or live location tracking.
- Payment collection, refunds, commissions and receipts.
- Production Cosmos DB persistence and multi-instance concurrency.
- Automated driver offer timeout/retry and final trip lifecycle.
- Fleet and Ops apps (Phase 2).

## 2. API tab — feature and endpoint notes

Base URL (local): `https://localhost:<port>`  
API prefix: `/api/v1`

| Feature | Method and path | Request / response intent | Demo status |
|---|---|---|---|
| Health | `GET /health` | API status and environment | Implemented |
| Queue status | `GET /api/v1/zones/{zoneId}/queues/{serviceClass}` | Ordered eligible drivers with position | Implemented |
| Join queue | `POST /api/v1/queues/join` | Driver ID, zone ID and service class; server assigns sequence | Next |
| Leave queue | `POST /api/v1/queues/leave` | Remove current driver's queue membership | Next |
| Driver availability | `PATCH /api/v1/drivers/{driverId}/availability` | Online/offline/temporarily unavailable | Next |
| Rider quote | `POST /api/v1/rides/quote` | Pickup, drop-off, service class, passenger count; demo estimate | Demo stub |
| Create ride | `POST /api/v1/rides` | Persist ride request and return request ID/status | Next |
| Ride status | `GET /api/v1/rides/{rideId}` | Current request/dispatch/trip state | Next |
| Driver offers | SignalR `/hubs/driver` | Offer and queue notifications | Later |
| Rider trip updates | SignalR `/hubs/rider` | Assignment and trip status notifications | Later |

### API rules

- Queue sequence and position are assigned by the server; never accept them from the app.
- Queue identity is scoped by zone and service class.
- Only approved, online, available and compatible drivers should be eligible.
- Validate every state transition server-side.
- Use idempotency keys for ride creation and assignment.
- Persist the state before publishing a realtime notification.
- Return authoritative state on reconnect; SignalR is not the source of truth.
- Add authentication and resource-ownership checks before exposing endpoints beyond a local demo.

### Suggested demo response shapes

Queue entry:
```json
{
  "driverId": "DRV-1001",
  "zoneId": "HYD-KPHB",
  "serviceClass": "standard",
  "position": 1,
  "joinedAt": "2026-11-05T09:00:00Z"
}
```

Ride request:
```json
{
  "rideId": "RIDE-DEMO-001",
  "status": "Matching",
  "pickup": "KPHB, Hyderabad",
  "dropoff": "HITEC City, Hyderabad",
  "serviceClass": "standard",
  "passengerCount": 1
}
```

These are illustrative contracts, not claims that the endpoints are already implemented.

## 3. DB tab — Cosmos DB design notes

Use Cosmos DB for durable state after the local demo. Confirm the partition strategy against real query patterns before production.

| Container | Document types | Candidate partition key | Notes |
|---|---|---|---|
| `Profiles` | RiderProfile, DriverProfile | `/userId` | Profile and approval state; avoid putting raw identity documents here |
| `Vehicles` | Vehicle | `/driverId` | Capacity, category, registration and document metadata |
| `ServiceZones` | ServiceZone, ZonePolicy | `/cityId` | Zone geometry reference and service-class rules |
| `DriverQueues` | QueueMembership | `/queueId` | Queue ID represents zone + service class; sequence is server-generated |
| `QueueEvents` | QueueJoined, QueueLeft, AvailabilityChanged | `/queueId` | Append-only audit events |
| `Rides` | RideRequest, DispatchOffer, Trip | `/rideId` | Ride lifecycle and offer history |
| `DispatchAudit` | DispatchDecision | `/rideId` | Candidate considered, outcome, reason and timestamp |

### Data invariants

- A driver must not have duplicate active membership in the same queue.
- A driver must not be assigned to more than one active ride.
- A ride must not be assigned to more than one driver.
- Queue order must be reproducible from persisted server-generated sequence/events.
- Store timestamps in UTC.
- Use ETags/conditional writes and idempotency for concurrent updates.
- Cosmos DB transactional batches are limited to a single logical partition. Do not assume cross-partition transactions; redesign the partition boundary or add coordination if an invariant spans partitions.
- Keep identity files in secured object storage and store protected metadata/references only.
- Define TTL, retention, deletion, backup and restore policies before launch.

## 4. UI tab — app screens and navigation

The product remains two mobile apps (Android/iOS), not a web application. Each Expo app opens at its root entry screen (`/` conceptually; React Native does not use browser URL paths unless a navigation/deep-link layer is added).

### TurnOva Driver app

| Tab / screen | Path or route key | Features for demo |
|---|---|---|
| Home | `/` / `Home` | Online/offline toggle, current zone, service class, queue summary |
| Queue | `/queue` / `Queue` | Join/leave queue, live-looking demo position, ordered drivers |
| Trips | `/trips` / `Trips` | Current offer/trip placeholder and recent demo activity |
| Earnings | `/earnings` / `Earnings` | Demo-only summary placeholder |
| Profile | `/profile` / `Profile` | Driver and vehicle summary, approval status |

### TurnOva Rider app

| Tab / screen | Path or route key | Features for demo |
|---|---|---|
| Book | `/` / `Book` | Pickup, drop-off, passenger count and service class |
| Activity | `/activity` / `Activity` | Current request state and recent demo rides |
| Offers | `/offers` / `Offers` | Demo fare/vehicle options; no real pricing promise |
| Profile | `/profile` / `Profile` | Rider profile placeholder and preferences |

### UI behaviour

- Use a clear bottom-tab navigation pattern on mobile.
- Keep a consistent TurnOva brand header and status colours across both apps.
- Display loading, empty, success and error states.
- Disable duplicate taps while an API request is in progress.
- Refresh queue/ride state from the API when a screen regains focus.
- Clearly label mocked data and simulated features as **Demo**.
- Do not display a real driver location or claim a driver is assigned until the API confirms it.

## 5. Demo data

Use visibly synthetic IDs and Hyderabad sample zones only:

- Zone: `HYD-KPHB`
- Service class: `standard`
- Drivers: `DRV-DEMO-001`, `DRV-DEMO-002`, `DRV-DEMO-003`
- Sample route: KPHB → HITEC City

Never seed real personal information, phone numbers, licence numbers or vehicle registration numbers.

## 6. Local development

### Backend

From `src/backend/TurnOva`:

```bash
dotnet restore
dotnet run --project TurnOva.API
```

Verify `/health`, then call the queue status endpoint. Use the HTTPS URL printed by ASP.NET Core.

### Mobile apps

From either `src/mobile/driver-app` or `src/mobile/rider-app`:

```bash
npm install
npx expo start
```

Run the relevant app on an Android emulator/device or iOS simulator. Configure the API base URL for the device/emulator; do not assume `localhost` from a physical phone points to the development machine.

## 7. Work sequence

1. Complete queue join/leave API and validation.
2. Add demo ride quote/create/status endpoints.
3. Connect Driver Queue and Rider Book screens to the API.
4. Add tab navigation and polished empty/loading/error states.
5. Add API and domain tests for FIFO, duplicate membership and invalid requests.
6. Run both apps against one local API and rehearse the end-to-end demo.
7. Replace the in-memory store with Cosmos DB only after the database account/configuration is supplied.

## 8. Open decisions

- Final zone boundaries and service/vehicle classes.
- Driver behaviour after decline, timeout, cancellation and trip completion.
- Offer timeout and cross-zone fallback policy.
- Fare calculation and cancellation policy.
- Maps, OTP, payment and push-notification providers.
- Cosmos DB account, database/container provisioning and partition-key validation.
- Authentication provider and KYC workflow.
