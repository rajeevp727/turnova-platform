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
    │   └── fifo-allocation.md
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
