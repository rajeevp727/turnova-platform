# TurnOva MVP Acceptance Matrix

**Scope:** TurnOva Rider and TurnOva Driver Android/iOS MVP only. Fleet and Ops applications are Phase 2 and excluded.
**Source of truth:** `PRD.md`, especially sections 7, 8, 9, 10, 12 and 14; `docs/test-plan.md`.
**Last audited:** 2026-11-06 against main after PR #43 merge `50e6fa18851fbca88836365f28726625c274981b`.
**Rule:** A row is **Verified** only when implementation exists and its required automated/manual evidence is recorded. A merged PR or successful typecheck alone does not verify a user-facing acceptance criterion.

## Status definitions

- **Not started:** no implementation evidence found.
- **In progress:** implementation exists or is underway, but one or more required behaviours/tests remain unverified.
- **Verified:** implementation and all evidence listed for the row have passed and links/results are recorded.
- **Blocked:** progress requires a named product decision, credential, external provider, legal approval or physical-device action.

## Acceptance matrix

| ID | In-scope acceptance criterion | Implementation evidence currently known | Evidence still required | Status |
|---|---|---|---|---|
| AUTH-01 | Rider and Driver registration, login, profile and role separation | PR #35 fake-TriSend API integration tests; PR #39 JWT signature/issuer/audience/expiry and Rider/Driver role-boundary integration tests; PR #40 startup JWT/TriSend HTTPS configuration guards | Duplicate-registration contract and mobile runtime/session tests; end-to-end profile lifecycle | In progress |
| AUTH-02 | Secure access/refresh-token lifecycle and logout | Both apps use SecureStore; PR #37 serializes concurrent refreshes; PR #41 extracts and tests the shared single-flight refresh coordinator and retry-after-failure behaviour | Automated token rotation/reuse, app restart, expiry, logout and secure-storage failure tests on both apps | In progress |
| RIDER-01 | Pickup/drop-off selection, location permission, nearby compatible vehicles and capacity | Ride request API rejects missing/out-of-range coordinates and pickups outside configured zones; service class is validated | Physical-device location permission tests, configured-zone boundary tests, nearby vehicle/capacity acceptance | In progress |
| RIDER-02 | Fare estimate and clear trip details before confirmation | No verified implementation evidence recorded in this audit | Fare policy decision; estimate API/UI tests for all service classes and passenger counts | In progress |
| RIDER-03 | Booking creation, status, cancellation and duplicate-submit safety | Ride lifecycle, ownership and idempotency integration tests; PR #43 adds pickup/drop-off, coordinate, service-class and passenger-count boundary coverage plus invalid-request idempotency retry | Confirm every PRD transition, cancellation policy, persistence/restart and duplicate request against durable storage | In progress |
| DISPATCH-01 | Pickup resolves to configured zone; matching zone and Scooty/Bike/Auto/Cab service class only | Canonical vehicle classes, queue scoping and FIFO enforcement documented; zone matching feature branch exists in history | Configured production boundaries, boundary/overlap tests, cross-zone policy decision and end-to-end rider-to-driver matching | In progress |
| DISPATCH-02 | Only approved, online, eligible, compatible drivers are considered in server-owned FIFO order | Driver onboarding/approval, Online/Offline, FIFO queue and queue-head enforcement merged in prior PRs; PR #44 adds a 16-way concurrent duplicate-join store test | End-to-end eligibility matrix, Cosmos persistence and multi-instance concurrency tests | In progress |
| DISPATCH-03 | Offers are durable, expire server-side, advance to next driver and retain audit history | PRs #24–#33 merged: durable offer aggregate, expiry, retry-safe rejection, advancement and notification outbox; PR #42 tests stale-notification suppression, transient SignalR retry and concurrent worker duplicate delivery with stable offerId | Cosmos failure injection, process-restart recovery, real Cosmos ETag races and mobile offerId de-duplication/reconnect tests | In progress |
| DISPATCH-04 | A ride and driver cannot be assigned twice; one active ride reservation per driver | PR #27 added durable per-driver reservation and ride ETag protection | Crash recovery/reconciliation for reservation-before-ride-write window; real Cosmos simultaneous acceptance tests across instances | In progress |
| REALTIME-01 | Rider and Driver receive consistent live trip/queue updates and recover after reconnect | SignalR ride hub, driver offer channel and pending-offer recovery documented in README | Android/iOS network interruption, reconnect, stale event, duplicate event and authoritative state refresh tests | In progress |
| DRIVER-01 | Driver onboarding, required identity/licence/vehicle documents, approval and eligibility feedback | Onboarding and interim review endpoints documented; selfie approval by assigned union implemented | Secure private document upload/storage/scanning, consent/retention, reviewer RBAC, audit actor identity and full onboarding device tests | In progress |
| DRIVER-02 | Driver availability, queue position, offers, accept/reject/timeout and re-entry | Online/Offline, FIFO queue lifecycle and offer state machine increments merged | Full app-level acceptance, temporary-unavailable/on-trip behaviour, timeout/re-entry policy sign-off and recovery tests | In progress |
| TRIP-01 | Driver trip lifecycle, location sharing, trip history and rider live progress | Ride lifecycle and foreground location/SignalR flows documented | Full device-to-device trip tests, durable trip history, background/permission edge cases and location privacy validation | In progress |
| DRIVER-03 | Driver earnings and support | Monthly PDF work-metrics payslip exists; monetary compensation explicitly not configured | Compensation/commission policy decision, earnings calculation, payout treatment and support workflow | Blocked |
| RIDER-04 | Payment through enabled methods, trip history, receipts and ratings | No completed payment/receipt/rating acceptance evidence recorded in this audit | Payment provider/method decision, sandbox integration, webhook/idempotency/refund tests, receipts and rating flows | Blocked |
| SUPPORT-01 | Rider help and issue reporting; driver support | No verified end-to-end support workflow evidence recorded in this audit | Support process, issue categories/escalation owner, API/UI, audit and device acceptance | Blocked |
| PLATFORM-01 | Secure, observable, reliable API with persistent audit trail | .NET 10 API, Cosmos migrations/readiness, health endpoints and selected audit stores documented | Real Cosmos readiness/ETag/partition tests, security review, CORS, secrets/logging, metrics/traces/alerts and load tests | In progress |
| RELEASE-01 | Android and iOS builds pass agreed functional, security and pilot-readiness tests | Expo app projects and CI typechecks exist; CI workflow runs backend tests and mobile typechecks | Actual successful CI evidence for current main, Android/iOS production builds, signed artifacts, physical-device functional/security/pilot checklist | In progress |

## Current verified count

- Verified rows: **0 / 19** from this audit.
- This does **not** mean nothing has been implemented. It means the available evidence did not establish every required test and release gate for any complete acceptance row.
- Merged implementation increments #24–#37 are valuable progress. PR #36 passed backend tests and both Rider/Driver Expo config/typecheck jobs in [CI run #97](https://github.com/rajeevp727/turnova-platform/actions/runs/42183594825). PR #37 passed all three jobs in [CI run #99](https://github.com/rajeevp727/turnova-platform/actions/runs/42183982378). These are CI evidence, not end-to-end acceptance proof.
- Update each row with exact PR/commit links and test run URLs/results as evidence becomes available. Split a row if it contains independent acceptance criteria that can be verified separately.

## Highest-priority next work

1. Add real Cosmos integration/failure-injection coverage for driver reservation recovery and outbox persistence/restart; the new worker tests currently exercise deterministic in-memory concurrency and retry behaviour.
2. Close the P0 authentication/JWT and mobile session tests already enumerated in `docs/test-plan.md`.
3. Implement/verify remaining Rider fare, payment, receipt, rating and support acceptance flows after recording the required provider and policy decisions.
4. Validate both apps on physical Android and iOS devices and collect release evidence.

## Deployment constraint

The MVP API is validated through a native .NET 10 publish artifact. Docker, container images, container registries and container-based hosting are explicitly excluded. The API hosting provider remains a post-coding infrastructure decision; Azure Static Web Apps is not the API host or native mobile distribution target.

## Audit limitations

This is a conservative baseline derived from the PRD, README, test plan, project brain, merged PR #33 and the repository's current source inventory. It is not a substitute for executing CI, Cosmos integration tests, provider sandbox tests or physical-device acceptance. Keep the matrix current in every feature PR.
