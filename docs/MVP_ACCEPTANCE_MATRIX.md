# TurnOva MVP Acceptance Matrix

**Scope:** TurnOva Rider and TurnOva Driver Android/iOS MVP only. Fleet and Ops applications are Phase 2 and excluded.
**Source of truth:** `PRD.md`, especially sections 7, 8, 9, 10, 12 and 14; `docs/test-plan.md`.
**Last audited:** 2026-11-06 against main after PR #46 merge `98f162c1828bf6c059c3e92eee15d18e030ce299`.
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
| RIDER-01 | Pickup/drop-off selection, location permission, nearby compatible vehicles and capacity | PR #45 adds Rider-only nearby discovery filtered by configured pickup zone, active FIFO membership, approved/online state, service class, passenger capacity, active-trip reservation and 120-second location freshness; Driver app publishes foreground GPS only while Online and pauses availability updates during an active ride; response omits driver identity, exact coordinates and location timestamps | PR #45 CI run 42209346759 passed API tests NEARBY-001–007; Android/iOS permission, foreground/resume, maps/geocoding and live-device checks remain | In progress |
| RIDER-02 | Fare estimate and clear trip details before confirmation | No fare calculation or pre-confirmation estimate is enabled; Rider UI suppresses the legacy demo amount and states that pricing is not configured | Fare policy decision (base fare, distance/time components, taxes and service-class rules); estimate API/UI tests for all service classes and passenger counts | Blocked |
| RIDER-03 | Booking creation, status, cancellation and duplicate-submit safety | Ride lifecycle, ownership and idempotency integration tests; PR #43 adds pickup/drop-off, coordinate, service-class and passenger-count boundary coverage plus invalid-request idempotency retry | Confirm every PRD transition, cancellation policy, persistence/restart and duplicate request against durable storage | In progress |
| DISPATCH-01 | Pickup resolves to configured zone; matching zone and Scooty/Bike/Auto/Cab service class only | Ride requests and nearby discovery resolve pickup GPS against configured zones; nearby discovery restricts results to the matching zone/service-class FIFO queue | Configured production boundaries, boundary/overlap tests, cross-zone policy decision and end-to-end rider-to-driver matching | In progress |
| DISPATCH-02 | Only approved, online, eligible, compatible drivers are considered in server-owned FIFO order | Driver onboarding/approval, Online/Offline, FIFO queue and queue-head enforcement merged in prior PRs; PR #46 adds a 16-way concurrent duplicate-join store test | End-to-end eligibility matrix, Cosmos persistence and multi-instance concurrency tests | In progress |
| DISPATCH-03 | Offers are durable, expire server-side, advance to next driver and retain audit history | PRs #24–#33 merged: durable offer aggregate, expiry, retry-safe rejection, advancement and notification outbox; PR #42 tests stale-notification suppression, transient SignalR retry and concurrent worker duplicate delivery with stable offerId | Cosmos failure injection, process-restart recovery, real Cosmos ETag races and mobile offerId de-duplication/reconnect tests | In progress |
| DISPATCH-04 | A ride and driver cannot be assigned twice; one active ride reservation per driver | PR #27 added durable per-driver reservation and ride ETag protection | Crash recovery/reconciliation for reservation-before-ride-write window; real Cosmos simultaneous acceptance tests across instances | In progress |
| REALTIME-01 | Rider and Driver receive consistent live trip/queue updates and recover after reconnect | SignalR ride hub, driver offer channel and pending-offer recovery documented in README | Android/iOS network interruption, reconnect, stale event, duplicate event and authoritative state refresh tests | In progress |
| DRIVER-01 | Driver onboarding, required identity/licence/vehicle documents, approval and eligibility feedback | Onboarding and interim review endpoints documented; selfie approval by assigned union implemented | Secure private document upload/storage/scanning, consent/retention, reviewer RBAC, audit actor identity and full onboarding device tests | In progress |
| DRIVER-02 | Driver availability, queue position, offers, accept/reject/timeout and re-entry | PR #45 requires in-zone GPS before Online; foreground location updates are accepted only while approved and online; active rides pause availability-location updates; leaving the assigned zone atomically takes the driver offline and removes queue membership | Android/iOS location permission, foreground/resume and zone-exit device tests; full offer/timeout/re-entry acceptance and recovery tests | In progress |
| TRIP-01 | Driver trip lifecycle, location sharing, trip history and rider live progress | Rider trip history API and Activity list return authenticated-owner summaries and restore the latest active ride when Activity opens; driver trip lifecycle and foreground location/SignalR flows documented; Driver history remains outstanding | Driver-owned history API/UI and ownership tests; full device-to-device trip tests, Cosmos history persistence, background/permission edge cases and location privacy validation | In progress |
| DRIVER-03 | Driver earnings and support | Monthly PDF work-metrics payslip exists; monetary compensation explicitly not configured | Compensation/commission policy decision, earnings calculation, payout treatment and support workflow | Blocked |
| RIDER-04 | Payment through enabled methods, trip history, receipts and ratings | Rider-owned trip history API and Activity list filter by authenticated rider and expose only safe ride summary fields; PR #47 adds ownership, ordering, privacy and empty-history API tests | Cosmos history query integration; payment provider/method decision, sandbox integration, webhook/idempotency/refund tests, receipts and rating flows | Blocked |
| SUPPORT-01 | Rider help and issue reporting; driver support | No verified end-to-end support workflow evidence recorded in this audit | Support process, issue categories/escalation owner, API/UI, audit and device acceptance | Blocked |
| PLATFORM-01 | Secure, observable, reliable API with persistent audit trail | .NET 10 API, Cosmos migrations/readiness, health endpoints and selected audit stores documented; PR #45 verifies a native non-container API publish artifact | Real Cosmos readiness/ETag/partition tests, security review, CORS, secrets/logging, metrics/traces/alerts and load tests | In progress |
| RELEASE-01 | Android and iOS builds pass agreed functional, security and pilot-readiness tests | Main CI run 42209834591 passed backend build/tests, non-container API publish/artifact verification, Rider/Driver Expo config and TypeScript checks, and shared mobile auth tests | Android/iOS production builds, signed artifacts, physical-device functional/security/pilot checklist | In progress |

## Current verified count

- Verified rows: **0 / 18** from this audit.
- Implementation evidence exists for **14 / 18** rows (78%); **4 / 18** rows remain blocked by explicit product/provider decisions. This is implementation coverage, not acceptance completion.
- This does **not** mean nothing has been implemented. It means the available evidence did not establish every required test and release gate for any complete acceptance row.
- PR #45 passed backend build/tests, native non-container API publish/artifact verification, both mobile Expo config/typechecks and shared auth tests in [CI run 42209346759](https://github.com/rajeevp727/turnova-platform/actions/runs/42209346759). PR #46 passed the same gates in [CI run 42209694545](https://github.com/rajeevp727/turnova-platform/actions/runs/42209694545); the merged main commit passed [CI run 42209834591](https://github.com/rajeevp727/turnova-platform/actions/runs/42209834591). These are automated CI evidence, not Android/iOS physical-device or provider acceptance proof.
- Update each row with exact PR/commit links and test run URLs/results as evidence becomes available. Split a row if it contains independent acceptance criteria that can be verified separately.

## Highest-priority next work

1. Implement Driver trip history and verify authenticated driver ownership; the Driver app currently exposes the active trip but not a persisted history list.
2. Keep fare estimation blocked until the fare policy (base fare, distance/time components, taxes and service-class rules) is explicitly approved; do not invent pricing.
3. Add real Cosmos integration/failure-injection coverage for driver reservation recovery and outbox/location persistence; these require the development Cosmos account and must remain blocked without credentials.
4. Implement the remaining payment/receipt/rating and help/issue-reporting flows only after their provider and support-policy decisions are recorded.
5. Validate both apps on physical Android and iOS devices and collect release evidence.

## Deployment constraint

The MVP API is validated through a native .NET 10 publish artifact. Docker, container images, container registries and container-based hosting are explicitly excluded. The API hosting provider remains a post-coding infrastructure decision; Azure Static Web Apps is not the API host or native mobile distribution target.

## Audit limitations

This audit reflects main after PR #46 merge `98f162c1828bf6c059c3e92eee15d18e030ce299`, the PRD, README, test plan and current source inventory. It is not a substitute for executing Cosmos integration tests, provider sandbox tests or physical-device acceptance. Keep the matrix current in every feature PR.
