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
| Join queue | `POST /api/v1/queues/join` | Driver ID, zone ID and service class; server assigns sequence | Implemented (in-memory) |
| Leave queue | `POST /api/v1/queues/leave` | Remove current driver's queue membership | Next |
| Driver availability | `PATCH /api/v1/drivers/{driverId}/availability` | Online/offline/temporarily unavailable | Next |
| Rider quote | `POST /api/v1/rides/quote` | Pickup, drop-off, service class, passenger count; demo estimate | Demo stub |
| Create ride | `POST /api/v1/rides/request` | Pickup, drop-off and passenger count; returns demo ride ID/status | Implemented (in-memory) |
| Ride status | `GET /api/v1/rides/{rideId}` | Retrieve current demo ride request | Implemented (in-memory) |
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

The API currently seeds three synthetic drivers in `hyderabad-central` / `auto` at startup. The ride endpoint returns a demo fare estimate of ₹85; this is not a production fare calculation.

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

- Zone: `hyderabad-central`
- Service class: `auto`
- Seeded drivers: `DRV-1001`, `DRV-1002`, `DRV-1003`
- Driver app demo identity: `DRV-DEMO-004`
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

1. Add queue leave and availability endpoints.
2. Add validation, error handling and API/domain tests for FIFO and duplicate membership.
3. Add rider quote and dispatch/offer lifecycle endpoints.
4. Test the mobile API URL on emulator and physical device; adjust the configured base URL as needed.
5. Run both apps against one local API and rehearse the end-to-end demo.
6. Replace the in-memory store with Cosmos DB only after the database account/configuration is supplied.


## 9. Cosmos DB configuration and automatic migrations

The API supports two storage modes:

- **Cosmos DB mode:** enabled when both `CosmosDb:Endpoint` and `CosmosDb:Key` are configured. Startup runs pending migrations before the API begins accepting requests. Queue membership is persisted in Cosmos DB.
- **Local demo mode:** when neither setting is configured, the API uses the seeded in-memory queue. This mode is for UI demos only and is not durable.

### Configure local secrets

From `src/backend/TurnOva/TurnOva.API`:

```bash
dotnet user-secrets init
dotnet user-secrets set "CosmosDb:Endpoint" "https://YOUR-ACCOUNT.documents.azure.com:443/"
dotnet user-secrets set "CosmosDb:Key" "YOUR-COSMOS-PRIMARY-KEY"
dotnet user-secrets set "CosmosDb:DatabaseName" "TurnOvaDb"
dotnet run
```

Do not put account keys in `appsettings.json`, source control, mobile apps, or screenshots. In hosted environments, use environment variables or a managed identity when configured.

### Migration mechanism

- Migration source: `TurnOva.Infrastructure/Database/Migrations`.
- Migration contract: `ICosmosMigration`.
- Runner: `CosmosMigrationRunner`.
- History container: `MigrationHistory`, partition key `/id`.
- Each migration has a stable, sortable ID and is recorded only after its `UpAsync` completes.
- Applied migrations are skipped on subsequent API starts.
- Container creation uses Cosmos `CreateContainerIfNotExistsAsync`, so the initial schema migration is safe to retry after an interrupted startup.
- Startup fails fast if Cosmos credentials are partially configured or a migration fails; the API does not start in a partially migrated Cosmos mode.

The migration runner creates `TurnOvaDb` with **400 RU/s database-level shared throughput** (not per-container throughput), then creates the containers. This is intended to stay within the account's 1,000 RU/s Lifetime Free Tier allowance when the account is eligible and the rest of the account's provisioned throughput remains within the allowance. Existing databases are not resized by this runner.

Initial migration `202611050001_InitialSchema` creates:
`Drivers` (/driverId), `Riders` (/riderId), `Zones` (/zoneId), `QueueEntries` (/zoneServiceKey), and `Rides` (/rideId), plus `MigrationHistory` (/id).

**Current persistence boundary:** the queue adapter uses Cosmos DB when configured. Ride requests are still stored in process memory in this MVP and will be moved to the `Rides` container in a subsequent migration/implementation. The API's local in-memory mode seeds synthetic queue drivers; Cosmos mode does not seed them.

### Add a future migration

1. Add a class implementing `ICosmosMigration` under `Database/Migrations`.
2. Give it a unique sortable ID, e.g. `202611060001_AddDriverIndexes`.
3. Implement idempotent schema/data changes in `UpAsync`.
4. Register it with dependency injection in `TurnOva.API/Program.cs`.
5. Never edit a migration that has already been applied to a shared environment; add a new migration instead.

### Mobile API URL

Both Expo apps read `EXPO_PUBLIC_API_BASE_URL` and fall back to `http://10.0.2.2:5000` for the Android emulator. Create a local `.env` in each app folder with the API URL reachable from that device, then restart Expo:

```env
EXPO_PUBLIC_API_BASE_URL=http://YOUR-COMPUTER-LAN-IP:5000
```

Use the computer's LAN IP for a physical phone; Android emulator commonly uses `10.0.2.2`; iOS simulator can use `localhost`. The API port must match the URL printed by `dotnet run`.

## 8. Open decisions

- Final zone boundaries and service/vehicle classes.
- Driver behaviour after decline, timeout, cancellation and trip completion.
- Offer timeout and cross-zone fallback policy.
- Fare calculation and cancellation policy.
- Maps, OTP, payment and push-notification providers.
- Authentication provider and KYC workflow.
## 10. Email/password authentication (Feature 2)

Authentication is provided by the existing TriSend Auth API. TurnOva does not store passwords or refresh tokens server-side.

### Mobile-to-service flow

1. Driver and Rider apps register/sign in through the TurnOva API.
2. TurnOva API proxies the request to TriSend `/auth/register` or `/auth/login`, forcing `appId=turnova` and the appropriate `Driver` or `Rider` role from the route (the client cannot choose a role).
3. TriSend returns a 15-minute JWT access token and a rotating refresh token (30-day session).
4. Apps store both tokens using Expo SecureStore. Refresh is attempted after an API 401; the rotated tokens replace the old values.
5. TurnOva validates JWT signature, issuer, audience and lifetime using the same signing key configured in TriSend. Driver-only and Rider-only endpoints enforce the JWT role claim.

### TurnOva auth endpoints

| Method | Path | Purpose |
|---|---|---|
| POST | `/api/v1/auth/driver/register` | Register a Driver account |
| POST | `/api/v1/auth/driver/login` | Driver email/password login |
| POST | `/api/v1/auth/rider/register` | Register a Rider account |
| POST | `/api/v1/auth/rider/login` | Rider email/password login |
| POST | `/api/v1/auth/refresh` | Rotate refresh token through TriSend |
| GET | `/api/v1/auth/me` | Read current token identity from TriSend |
| POST | `/api/v1/auth/logout` | Revoke current TriSend session |

Protected product endpoints:
- Driver: `GET /api/v1/zones/{zoneId}/queues/{serviceClass}`, `POST /api/v1/queues/join`.
- Rider: `POST /api/v1/rides/request`, `GET /api/v1/rides/{rideId}` (ride owner only).
- Zone discovery requires an authenticated TurnOva account.

### Required API configuration

Set these values as .NET User Secrets locally or environment variables in the API host. Never place the JWT signing key in either mobile app or source control:

```text
TriSend__BaseUrl=https://auth.trisend.rajeevstech.in
Auth__Jwt__Issuer=<exact TriSend Auth:Jwt:Issuer>
Auth__Jwt__Audience=<exact TriSend Auth:Jwt:Audience>
Auth__Jwt__SigningKey=<exact TriSend Auth:Jwt:SigningKey>
```

The issuer, audience and signing key must exactly match TriSend. The signing key must be at least 32 characters. TurnOva calls TriSend over HTTPS. Local API HTTP URLs are for development only; production must use HTTPS.

### Mobile setup

Install dependencies in each app folder with `npm install` after pulling the branch. `expo-secure-store` stores access/refresh tokens in platform secure storage. Configure `EXPO_PUBLIC_API_BASE_URL` to the API URL reachable from the emulator/device, then restart Expo.

### Explicit exclusions and limitations

- SSO, Google login, Microsoft login and OAuth are not part of this feature.
- Passwords are forwarded to TriSend over HTTPS; TurnOva does not persist them.
- JWT validation is local and does not query TriSend on every request. Access tokens remain usable until expiry (15 minutes) if a session is revoked, so logout is not instant access-token revocation.
- Ride records remain in-memory demo data; authentication and ownership checks do not make ride storage durable.
- Driver onboarding/KYC approval is not implemented; authenticated Driver role alone is not production eligibility.


## Cosmos DB runtime configuration and readiness

Set `CosmosDb__Endpoint`, `CosmosDb__Key`, and optionally `CosmosDb__DatabaseName` in .NET User Secrets or the API hosting provider's secret store. The endpoint must use HTTPS and the account key must never be committed or exposed to mobile apps. `GET /health` is process-level health; `GET /health/ready` checks database reachability and returns 503 if Cosmos is unavailable. Local in-memory mode reports ready without Cosmos credentials. The migration lease currently expires after 30 minutes and is not renewed; keep migrations short until lease renewal is implemented. Cosmos migration concurrency and readiness failure-injection scenarios still require a real development account.


## 11. Driver onboarding and eligibility (PR #10)

- Driver profile submission: `POST /api/v1/driver/onboarding`.
- Driver status: `GET /api/v1/driver/onboarding/me`.
- Review decision: `POST /api/v1/ops/drivers/{driverId}/review` with `X-Driver-Admin-Key`.
- Configure `DriverOnboarding__AdminApiKey` in the API host secret store (minimum 32 random characters required). Never place it in either mobile app.
- Driver profile and vehicle/document metadata are stored in the existing Cosmos `Drivers` container (partition `/driverId`); local demo mode uses in-memory storage.
- Queue join, available-ride discovery and ride acceptance are blocked unless the driver's profile status is `Approved`.
- Approval requires DrivingLicence and VehicleRegistration references. The API accepts only opaque private object-storage references; it does not upload, serve or verify identity documents.
- The API-key review endpoint is temporary until authenticated Ops roles/permissions and an Ops portal are implemented.

## Union management increment

PR #11 introduces union records, current driver-to-union assignments and per-driver assignment audit events. Only approved drivers can be assigned to active unions that support their approved service zone and service class. Every initial assignment or transfer requires an operations reason. Drivers can read only their own current union; Ops can list union members and inspect assignment history.

Cosmos containers are Unions (/unionId), DriverUnionAssignments (/driverId) and DriverUnionAudit (/driverId). Local tests use in-memory dictionaries. Ops access temporarily reuses DriverOnboarding:AdminApiKey; never expose this key to mobile clients. The audit append and current-assignment upsert span separate Cosmos partitions/containers and are not atomic; add an outbox/reconciliation mechanism before production. Union membership is not yet a dispatch gate. Payroll and payslips are deferred to a separate increment.
