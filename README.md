# TurnOva

**Everyone’s Turn Matters.**

TurnOva is a mobile-first, union-connected **Cab discovery and ride-coordination** platform. A Rider discovers eligible Cab drivers through approved unions, selects a driver, negotiates directly, and confirms a versioned agreement. The Driver completes the trip and reports the amount received directly from the Rider.

The authoritative requirements are in [PRD.md](./PRD.md). This README summarizes the approved MVP scope; it does not redefine product policy.

## MVP scope

| App | Audience | Phase |
|---|---|---|
| TurnOva Rider | Passengers | MVP |
| TurnOva Driver | Individual Cab drivers | MVP |
| TurnOva Fleet | Fleet owners/managers | Phase 2 — excluded |
| TurnOva Ops | Internal operations | Phase 2 — excluded |

The MVP consists of native Android and iOS Rider and Driver apps plus the shared API. Rider/Driver web apps, Fleet/Ops apps and Fleet/Ops workflows are not part of this delivery.

New onboarding, discovery and ride workflows are **Cab-only**. Existing Scooty, Bike and Auto records must remain safely readable as historical data; they must not be destructively migrated or silently converted.

## Approved Rider and Driver flow

1. The Rider enters pickup, destination and optional intermediate stops.
2. The API discovers active, approved unions serving Cab within the configured pickup coverage.
3. Each eligible union can return up to three approved, available Cab drivers, ordered by driver-to-pickup proximity. The Rider explicitly selects one.
4. Driver cards expose verified Cab details and a selfie only when approved by the driver's current union. Pickup proximity is not journey distance; journey distance remains unavailable until a routing provider is configured.
5. The Rider and selected Driver negotiate directly. TurnOva does not estimate or recommend a fare.
6. Agreement versions are immutable. Both parties must confirm the same current version; the Driver cannot start before mutual confirmation. A renegotiation creates a new version and requires both parties to confirm it.
7. Ride transitions and reservation rules are server-authoritative, idempotent and concurrency-safe.
8. After completion, the Driver reports the amount received directly. The report is unverified and records the signed delta **agreed amount − amount received**. TurnOva does not collect the payment.

## Product and privacy boundaries

- No customer-facing FIFO ranking, automatic FIFO assignment or automatic next-driver advancement.
- The role of FIFO as an internal Union fairness/availability mechanism remains an unresolved product decision. Legacy queue and offer code is compatibility/history only; it must not influence Rider discovery ranking or selected-driver assignment.
- No platform fare estimates/recommendations, fixed or dynamic pricing, payment gateway, escrow or in-app payment collection. The Rider pays the Driver directly.
- Only active approved Cab-serving unions and eligible approved Cab drivers participate in new discovery. A public selfie requires approval by the driver's current union; replacement or transfer resets/hides the prior approval.
- Union oversight is least-privilege and limited to the manager's assigned union. Do not expose unrelated Rider data, private chat, exact Driver coordinates, phone numbers or private KYC/document references in discovery or Union reporting.
- Identity documents are represented by private storage references; production storage authorization, scanning, access auditing, consent, retention and deletion controls remain release gates.
- Approved supplemental AI-01 functionality is limited to editable trip-intent drafts, approved-content help/escalation and optional Driver offer explanation. AI cannot book, select/assign a Driver, confirm an agreement, set/recommend fares, approve KYC, change eligibility/dispatch/ride state or make safety/legal decisions. Sensitive identity, precise location, contact, payment and token data must not be sent to an AI provider.

## Technology

| Layer | Technology |
|---|---|
| Backend | .NET 10 LTS, ASP.NET Core Web API, C# 14 |
| Mobile | React Native + Expo |
| Database | Azure Cosmos DB |
| Realtime | SignalR |
| Authentication | JWT access tokens and refresh-token lifecycle; external auth integration |
| API contract | OpenAPI / Swagger |
| Source control and CI | GitHub / GitHub Actions |

## Repository layout

```text
turnova-platform/
├── docs/
│   ├── architecture.md
│   ├── test-plan.md
│   ├── MVP_ACCEPTANCE_MATRIX.md
│   └── MVP_PROGRESS.md
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
```

## Local development

Prerequisites: .NET 10 SDK, Node.js 22 (or the version pinned by CI), npm, Expo tooling, Android Studio for Android builds and Xcode for iOS builds.

Clone the repository:

```bash
git clone https://github.com/rajeevp727/turnova-platform.git
cd turnova-platform
```

Restore, build and run backend tests:

```bash
dotnet restore src/backend/TurnOva/TurnOva.sln
dotnet build src/backend/TurnOva/TurnOva.sln --configuration Release --no-restore
dotnet test src/backend/TurnOva/TurnOva.Tests/TurnOva.Tests.csproj --configuration Release
```

For either mobile app, install dependencies and run the repository's validation scripts from its app directory:

```bash
cd src/mobile/rider-app
npm install
npm run typecheck
npx expo config --type public
```

Use `src/mobile/driver-app` for the Driver app. Follow each app's package scripts and the CI workflow for native build and smoke-test commands.

## Configuration and data

- Configure Cosmos DB endpoint, key and database name only in the API host's secret/configuration store. Never put Cosmos credentials in the mobile bundle or source control.
- Configure the external authentication issuer, audience, signing key and auth API URL server-side. Keep signing material in a secret store; never add real values to this repository.
- `EXPO_PUBLIC_API_BASE_URL` is public and embedded in each app. It must contain only the approved API URL, never credentials or server secrets.
- Local in-memory stores are for deterministic tests/local development only. Production must use the configured persistent store.
- The native .NET API publish path is a standard application artifact. Dockerfiles, container builds, registries and container deployments are not used.

See [infrastructure/README.md](./infrastructure/README.md) for placeholders and post-coding infrastructure gates.

## Legacy FIFO compatibility

Legacy queue membership, offer and expiry code remains in the repository to preserve historical records and safe compatibility behavior while the internal FIFO role is unresolved. These endpoints are **not** the approved Rider discovery or assignment flow. Do not extend them into customer-facing ranking, automatic assignment or next-driver advancement without an explicit product decision.

## Testing and release evidence

The [TurnOva CI workflow](./.github/workflows/ci.yml) validates backend tests, Rider/Driver Expo configuration and bundles, native Android/iOS builds, native non-container API publishing and emulator/simulator smoke paths. [docs/test-plan.md](./docs/test-plan.md) distinguishes executed automated evidence from planned/manual acceptance.

A green CI run demonstrates only the checks that actually ran on that commit. It does not establish production readiness, live Cosmos multi-instance/ETag behavior, signed-device Keychain persistence, physical-device acceptance, provider validation, legal approval or a production pilot.

## Remaining external acceptance gates

The acceptance matrix records owners and required inputs. Current external gates include product-approved pickup radius/zones and Driver location freshness; internal FIFO policy; live Cosmos partition/ETag/multi-instance/restart validation; production authentication and UnionManager claim mapping; maps/routing and approved realtime/call/chat/push providers; private document storage/scanning/consent/retention; support and reconciliation policy; Union partnership/legal terms; production telemetry export/alerts; API hosting; Android/iOS signing, physical-device testing and store acceptance.

## Project documents

- [Product requirements](./PRD.md)
- [Architecture](./docs/architecture.md)
- [Feature test plan](./docs/test-plan.md)
- [19-area acceptance matrix](./docs/MVP_ACCEPTANCE_MATRIX.md)
- [Engineering progress and handoff](./docs/MVP_PROGRESS.md)
- [Infrastructure placeholders and gates](./infrastructure/README.md)