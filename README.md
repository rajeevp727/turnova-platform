# TurnOva

**Everyone’s Turn Matters.**

TurnOva is a mobile-first ride-hailing platform focused on transparent, fair driver ride allocation through a zone-based First In, First Out (FIFO) dispatch model.

## Product apps

| App | Audience | Release |
|---|---|---|
| TurnOva Driver | Drivers | MVP |
| TurnOva Rider | Passengers | MVP |
| TurnOva Fleet | Fleet owners/managers | Phase 2 |
| TurnOva Ops | Internal operations | Phase 2 |

**Platforms:** Android and iOS only. No passenger or driver web applications are in scope. All four apps share a backend and dispatch engine.

## Product principles

- Explainable FIFO ordering within appropriate geographic zones.
- Eligibility and vehicle/passenger-capacity matching.
- Server-authoritative queue and assignment decisions.
- Auditable exceptions and transparent driver queue status.

## Technology stack

| Layer | Technology |
|---|---|
| Backend | .NET 10 LTS, ASP.NET Core Web API, C# 14, net10.0 |
| Mobile | React Native + Expo |
| Database | Azure Cosmos DB |
| Realtime | SignalR |
| Authentication | JWT access tokens + refresh-token rotation |
| API docs | OpenAPI / Swagger |
| Source control | GitHub |

## Repository structure

    turnova-platform/
    ├── docs/
    │   ├── architecture.md
    │   ├── fifo-allocation.md
    │   └── test-plan.md
    ├── src/
    │   ├── backend/TurnOva/
    │   │   ├── TurnOva.API/
    │   │   ├── TurnOva.Application/
    │   │   ├── TurnOva.Domain/
    │   │   ├── TurnOva.Infrastructure/
    │   │   └── TurnOva.Tests/
    │   └── mobile/
    │       ├── driver-app/
    │       └── rider-app/
    ├── infrastructure/
    ├── PRD.md
    └── README.md

Fleet and Ops mobile apps will be added in Phase 2. This is the planned layout; directories will appear as implementation is added.

## Getting started

Prerequisites: .NET 10 SDK, Node.js LTS/npm, Expo tooling, Android Studio and/or Xcode as appropriate, Git, and access to a development Cosmos DB account.

    git clone https://github.com/rajeevp727/turnova-platform.git
    cd turnova-platform

The backend solution and two Expo mobile apps are implemented under `src/`. Use the .NET 10 SDK and keep backend projects targeting `net10.0`.

## Roadmap

1. Finalize dispatch policy, service zones and data model.
2. Build identity, driver eligibility, FIFO queue and dispatch foundations.
3. Build Driver and Rider MVP apps.
4. Add realtime trip lifecycle, payment integration and support essentials.
5. Test and run a controlled Hyderabad pilot.
6. Deliver Fleet and Ops in Phase 2.

## Infrastructure approach

Use free tiers and low-cost services for development where practical. Production costs may apply to hosting, Cosmos DB usage, maps/geocoding, OTP/SMS, payment gateways, push/realtime infrastructure, monitoring and app-store publishing. Verify provider pricing, quotas, regional availability and production terms before selection.

## Engineering principles

- Keep dispatch and eligibility decisions on the server.
- Make booking and assignment operations idempotent and concurrency-safe.
- Persist queue events and dispatch decisions for auditability.
- Never trust client-supplied queue positions or eligibility.
- Keep secrets out of source control.
- Add unit, integration, concurrency and mobile end-to-end tests.

## Status

The repository contains the initial API, Driver and Rider demo clients, Cosmos DB queue adapter/migrations, and CI validation. Authentication now integrates with TriSend email/password endpoints. Ride storage and dispatch remain demo-level; do not treat the current build as production-ready.

## MVP development notes

See [DEV_NOTES.md](./DEV_NOTES.md) for the consolidated API, Cosmos DB, and Driver/Rider UI feature notes for the Monday demo. Current demo endpoints and screens use synthetic data and an in-memory backend store; they are not production-ready.

## Authentication setup

Email/password authentication uses the TriSend Auth API. SSO is intentionally out of scope.

Configure the TurnOva API host with:

```text
TriSend__BaseUrl=https://auth.trisend.rajeevstech.in
Auth__Jwt__Issuer=<same value as TriSend Auth:Jwt:Issuer>
Auth__Jwt__Audience=<same value as TriSend Auth:Jwt:Audience>
Auth__Jwt__SigningKey=<same secret as TriSend Auth:Jwt:SigningKey>
```

The JWT settings must match TriSend exactly. Keep the signing key in .NET User Secrets or the hosting provider's secret store; never add it to mobile `.env` files or source control. See [DEV_NOTES.md](./DEV_NOTES.md#10-emailpassword-authentication-feature-2) for API routes, local setup and security boundaries.

## Driver queue lifecycle API

- `POST /api/v1/queues/join` — join the requested zone/service-class queue. Driver identity is always taken from the authenticated JWT.
- `GET /api/v1/zones/{zoneId}/queues/{serviceClass}/me` — return the authenticated Driver's queue membership and current position.
- `DELETE /api/v1/zones/{zoneId}/queues/{serviceClass}/me` — leave the queue. Repeating the request is safe.
- Join again after leaving to create a new queue entry at the end of the FIFO order.

Queue membership is persisted through the configured queue store (in-memory for local demo, Cosmos DB when configured). Cosmos DB operations are scoped to the zone/service-class partition.

## Test plan

See [docs/test-plan.md](./docs/test-plan.md) for feature-wise positive, negative, security, and manual acceptance scenarios. Planned scenarios are not represented as executed tests.


## Ride persistence and tracking

Ride requests are persisted in Cosmos DB when configured, with an in-memory fallback for local demo. Drivers can accept rides and share foreground location; the Rider map receives live updates through SignalR. Configure Cosmos DB and validate on physical devices with location permissions enabled. The current flow is an MVP foundation, not production dispatch or turn-by-turn navigation.


## Ride lifecycle and realtime updates

The ride lifecycle is `Requested → Accepted → Arrived → InProgress → Completed`, with Rider cancellation allowed before the trip starts. Ride requests require an `Idempotency-Key` header to prevent duplicate creation on retries. Driver location and ride-state updates are broadcast through an ownership-checked SignalR hub at `/hubs/rides`. The Driver app shares foreground location only while the active Trips screen is open; the Rider receives updates over SignalR.


## API integration tests

The backend test project includes authenticated end-to-end API tests using an in-memory ASP.NET Core test server. These cover Rider booking and idempotent retries, role restrictions, ride ownership, the complete lifecycle, cancellation rules, terminal location-sharing shutdown, and concurrent Driver acceptance. Cosmos-specific concurrency and SignalR WebSocket/device behavior still require integration environments and are tracked in the test plan.


## Cosmos DB configuration and readiness

When Cosmos DB is configured, set the API host's `CosmosDb__Endpoint`, `CosmosDb__Key`, and optional `CosmosDb__DatabaseName` values in its secret/configuration store. The endpoint must be HTTPS; never commit account keys. The API validates these settings at startup and applies schema migrations before accepting traffic.

- `GET /health` reports the API process and selected storage mode.
- `GET /health/ready` checks the configured Cosmos database and returns HTTP 503 if the dependency cannot be reached.
- Schema migrations are serialized across API instances using a Cosmos-backed expiring lease in `MigrationLocks`; applied migration IDs are recorded in `MigrationHistory`.

Local in-memory mode does not require Cosmos credentials. Cosmos readiness, migrations, partition behaviour and ETag concurrency still need validation against a real development Cosmos account before production use.


## Driver onboarding and eligibility

Drivers submit their profile, vehicle details and private document-storage references through `POST /api/v1/driver/onboarding`; they can inspect their own status with `GET /api/v1/driver/onboarding/me`. New submissions enter `Pending` and cannot join queues, discover available rides or accept rides until approved.

An operations reviewer can approve, reject or suspend a driver through `POST /api/v1/ops/drivers/{driverId}/review`. This interim endpoint requires a 32+ character `DriverOnboarding__AdminApiKey` on the API host and the `X-Driver-Admin-Key` header. Approval requires DrivingLicence and VehicleRegistration references. Keep this secret server-side; it is a temporary operations integration, not a replacement for an authenticated Ops portal/RBAC.

Document files are not uploaded or stored by this API. The request accepts opaque object-storage references only. Use private object storage, upload authorization, malware scanning, access auditing, retention and deletion controls before handling real identity documents.

## Union management and driver allocation

Operations can create and manage driver unions through the interim operations API. A union has a unique code, supported service zones and service classes, optional location/manager details, and an Active/Inactive/Suspended status. Only approved drivers can be assigned. The assignment must be compatible with the driver's approved zone and service class.

Use POST /api/v1/ops/unions to create a union, GET /api/v1/ops/unions to list unions, and PUT /api/v1/ops/unions/{unionId}/status to change its status. Assign or transfer a driver with POST /api/v1/ops/drivers/{driverId}/union-assignment; every operation requires a reason. The current driver can read their assignment from GET /api/v1/driver/union. Operations can inspect the driver's transfer history at GET /api/v1/ops/drivers/{driverId}/union-history, and list active members at GET /api/v1/ops/unions/{unionId}/drivers.

These interim operations endpoints use the server-side DriverOnboarding:AdminApiKey and X-Driver-Admin-Key header. Keep the key in backend secret storage only. This is not an Ops portal or a production RBAC model. Driver assignment history is retained as audit events; the assignment record represents the current union. Cosmos mode creates Unions, DriverUnionAssignments and DriverUnionAudit containers. Local demo mode uses in-memory stores.

Union membership is currently an operational/employer association and is not yet a dispatch eligibility gate. Payroll, employment contracts, payslip generation and compensation calculations are separate follow-up work.

## Monthly driver payslips (PDF only)

The Driver app includes a Payslips tab for the most recent 12 completed months. Drivers can download and share the returned PDF using the device share sheet. The app does not render payslips as HTML or offer CSV/spreadsheet exports.

Drivers can download a finalized monthly payslip as a PDF from GET /api/v1/driver/payslips/{year}/{month}.pdf. The endpoint is Driver-role only and returns application/pdf with a month-specific filename; there are no HTML, CSV or spreadsheet payslip representations. Only completed calendar months are available. The pay period uses India Standard Time calendar boundaries.

The PDF contains the driver and union/employer identity, pay period, distinct days with at least one completed ride, rides accepted, rides completed and explicitly rejected rides. A ride accepted and later completed contributes to both accepted and completed counts. Rider cancellations are not counted as driver rejections. A driver rejection is recorded through POST /api/v1/rides/{rideId}/reject and does not change the ride's availability for other drivers.

Work activity is recorded as an append-only per-driver ledger and monthly payslip snapshots are persisted once generated. The Cosmos migration creates DriverWorkActivities and DriverPayslips containers, both partitioned by /driverId. In-memory stores support local tests. The current PDF explicitly states that monetary compensation is not configured; earnings, deductions and net payable are not fabricated. This increment is a work-metrics payslip until the union compensation model is agreed and implemented.
