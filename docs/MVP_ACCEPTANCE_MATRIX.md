# TurnOva MVP Acceptance Matrix

**Scope:** TurnOva Rider and TurnOva Driver Android/iOS MVP only. Fleet and Ops applications are Phase 2 and excluded.
**Source of truth:** `PRD.md`, especially sections 7, 8, 9, 10, 12 and 14; `docs/test-plan.md`.
**Last audited:** 2026-11-06 against main after PR #48 merge `2ec84e4eb99b5b1f66fb8b059d71fa36b01c6135`; PLATFORM-01 hardening increment is underway.
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
| RIDER-02 | Fare estimate and clear trip details before confirmation | No fare calculation or pre-confirmation estimate is enabled; API returns a null fare and Rider UI states that pricing is not configured | Fare policy decision (base fare, distance/time components, taxes and service-class rules); estimate API/UI tests for all service classes and passenger counts | Blocked |
| RIDER-03 | Booking creation, status, cancellation and duplicate-submit safety | Ride lifecycle, ownership and idempotency integration tests; PR #43 adds pickup/drop-off, coordinate, service-class and passenger-count boundary coverage plus invalid-request idempotency retry | Confirm every PRD transition, cancellation policy, persistence/restart and duplicate request against durable storage | In progress |
| DISPATCH-01 | Pickup resolves to configured zone; matching zone and Scooty/Bike/Auto/Cab service class only | Ride requests and nearby discovery resolve pickup GPS against configured zones; nearby discovery restricts results to the matching zone/service-class FIFO queue | Configured production boundaries, boundary/overlap tests, cross-zone policy decision and end-to-end rider-to-driver matching | In progress |
| DISPATCH-02 | Only approved, online, eligible, compatible drivers are considered in server-owned FIFO order | Driver onboarding/approval, Online/Offline, FIFO queue and queue-head enforcement merged in prior PRs; PR #46 adds a 16-way concurrent duplicate-join store test | End-to-end eligibility matrix, Cosmos persistence and multi-instance concurrency tests | In progress |
| DISPATCH-03 | Offers are durable, expire server-side, advance to next driver and retain audit history | PRs #24–#33 merged: durable offer aggregate, expiry, retry-safe rejection, advancement and notification outbox; PR #42 tests stale-notification suppression, transient SignalR retry and concurrent worker duplicate delivery with stable offerId | Cosmos failure injection, process-restart recovery, real Cosmos ETag races and mobile offerId de-duplication/reconnect tests | In progress |
| DISPATCH-04 | A ride and driver cannot be assigned twice; one active ride reservation per driver | PR #27 added durable per-driver reservation and ride ETag protection | Crash recovery/reconciliation for reservation-before-ride-write window; real Cosmos simultaneous acceptance tests across instances | In progress |
| REALTIME-01 | Rider and Driver receive consistent live trip/queue updates and recover after reconnect | SignalR ride hub, driver offer channel and pending-offer recovery documented in README | Android/iOS network interruption, reconnect, stale event, duplicate event and authoritative state refresh tests | In progress |
| DRIVER-01 | Driver onboarding, required identity/licence/vehicle documents, approval and eligibility feedback | Onboarding and interim review endpoints documented; selfie approval by assigned union implemented | Secure private document upload/storage/scanning, consent/retention, reviewer RBAC, audit actor identity and full onboarding device tests | In progress |
| DRIVER-02 | Driver availability, queue position, offers, accept/reject/timeout and re-entry | PR #45 requires in-zone GPS before Online; foreground location updates are accepted only while approved and online; active rides pause availability-location updates; leaving the assigned zone atomically takes the driver offline and removes queue membership | Android/iOS location permission, foreground/resume and zone-exit device tests; full offer/timeout/re-entry acceptance and recovery tests | In progress |
| TRIP-01 | Driver trip lifecycle, location sharing, trip history and rider live progress | PR #47 merged Rider-owned history API/Activity list with owner-scoped summary, active-ride restoration, stale-response guards and no-fare placeholder; PR #48 adds authenticated Driver-owned history API/UI with safe summary, active-trip restoration and authoritative history refresh after SignalR reconnect; implementation head passed CI run 42215519184 | PR #48 CI/API tests; full device-to-device trip tests, Cosmos cross-partition history persistence, background/permission edge cases and location privacy validation | In progress |
| DRIVER-03 | Driver earnings | Monthly PDF work-metrics payslip exists; monetary compensation explicitly not configured | Compensation/commission policy decision, earnings calculation and payout treatment | Blocked |
| DRIVER-04 | Driver support and issue reporting | No verified end-to-end Driver support workflow evidence recorded in this audit | Driver support process, issue categories, escalation owner, API/UI, audit and device acceptance | Blocked |
| RIDER-04 | Payment through enabled methods, trip history, receipts and ratings | Rider-owned trip history API and Activity list filter by authenticated rider and expose only safe ride summary fields; PR #47 adds ownership, ordering, privacy and empty-history API tests | Cosmos history query integration; payment provider/method decision, sandbox integration, webhook/idempotency/refund tests, receipts and rating flows | Blocked |
| SUPPORT-01 | Rider help and issue reporting | No verified end-to-end Rider support workflow evidence recorded in this audit | Rider help process, issue categories, escalation owner, API/UI, audit and device acceptance | Blocked |
| PLATFORM-01 | Secure, observable, reliable API with persistent audit trail | .NET 10 API, Cosmos migrations and selected audit stores; explicit `Cors:AllowedOrigins` allow-list replaces wildcard CORS, `/health/live` and Cosmos-aware `/health/ready` are distinct, and integration tests cover allowed/disallowed origins and demo readiness in PR #49 (open; CI pending on latest head) | Cosmos-unavailable readiness failure injection, production origin configuration, secrets/logging review, metrics/traces/alerts, audit durability and load tests | In progress |
| RELEASE-01 | Android and iOS builds pass agreed functional, security and pilot-readiness tests | Main CI run 42209834591 passed backend build/tests, non-container API publish/artifact verification, Rider/Driver Expo config and TypeScript checks, and shared mobile auth tests | Android/iOS production builds, signed artifacts, physical-device functional/security/pilot checklist | In progress |

## Current verified count and weighted progress

- Acceptance areas: **19 / 19**, reconciled to the agreed tracker by splitting the combined Driver earnings/support area into the separate PRD capabilities **DRIVER-03 (earnings)** and **DRIVER-04 (support)**. Rider support remains SUPPORT-01. No additional PRD scope was added.
- Status counts: **0 Verified, 14 In progress, 5 Blocked, 0 Not started**.
- Weighted progress: **36.8%** = `(Verified × 1 + In progress × 0.5 + Not started × 0 + Blocked × 0) ÷ 19` = `(0 + 7 + 0 + 0) ÷ 19`.
- Strict fully verified count: **0 / 19**. A weighted score is not a claim that the MVP is accepted or production-ready.
- PR #47 merged at `32cacbf430e1443835e3bd8e4294c15d3c137fe3`; its latest head `1c08b96e78bef42c175b29593de153a151382f96` passed all four jobs in CI run 42214516012. The Rider history feature is now merged; real Cosmos cross-partition/RU verification remains open.
- PR #48 merged at `2ec84e4eb99b5b1f66fb8b059d71fa36b01c6135`; final head `2b19856b02b4722cd76cfb04459c01afb6a4e3ab` passed all four jobs in CI run 42215718229. Rider and Driver history code is merged; TRIP-01 remains In progress pending Cosmos/device acceptance.
- Main CI run 42209834591 passed backend build/tests, native non-container API publish/artifact verification, both mobile Expo config/typechecks and shared mobile auth tests. This remains automated CI evidence, not Android/iOS physical-device or provider acceptance proof.
- Do not mark an area Verified until all evidence in its row has passed and the links/results are recorded.

## Approved supplemental requirement (not counted in the 19 PRD areas)

| ID | Scope | Status | Evidence / remaining work |
|---|---|---|---|
| AI-01 | Provider-agnostic, privacy-safe, bounded Rider/Driver AI assistance: rider trip-intent draft for human review; approved FAQ/ride-state explanations with escalation; optional driver offer explanation. AI must not book, charge, set fares, approve KYC, alter FIFO/eligibility or change ride state. | Not started | Implement the approved service boundary, validated contracts, timeout/cancellation, redaction, rate controls and deterministic fallback with mock-based tests; keep provider/model/credentials as infrastructure placeholders. |

## Ordered 1–19 checklist

1. AUTH-01 — In progress
2. AUTH-02 — In progress
3. RIDER-01 — In progress
4. RIDER-02 — Blocked
5. RIDER-03 — In progress
6. DISPATCH-01 — In progress
7. DISPATCH-02 — In progress
8. DISPATCH-03 — In progress
9. DISPATCH-04 — In progress
10. REALTIME-01 — In progress
11. DRIVER-01 — In progress
12. DRIVER-02 — In progress
13. TRIP-01 — In progress; Rider and Driver history code merged in PRs #47 and #48; Cosmos/device acceptance remains
14. DRIVER-03 — Blocked
15. DRIVER-04 — Blocked
16. RIDER-04 — Blocked
17. SUPPORT-01 — Blocked
18. PLATFORM-01 — In progress; CORS allow-list and health-probe hardening in progress
19. RELEASE-01 — In progress

## Highest-priority next work

1. Complete PLATFORM-01 security/health hardening: verify explicit CORS allow-list and unambiguous liveness/readiness endpoints, then continue metrics/traces/audit and Cosmos reliability evidence.
2. Keep RIDER-02 blocked until base fare, distance/time components, taxes and service-class pricing rules are approved; never invent a quote.
3. Keep DRIVER-03 blocked until compensation, commission and payout treatment are approved; the existing monthly PDF is a work-metrics payslip, not monetary earnings.
4. Keep RIDER-04 blocked until payment method/provider, sandbox, webhook/idempotency/refund, receipt and rating decisions are supplied.
5. Keep DRIVER-04 and SUPPORT-01 blocked until the role-specific support process, categories and escalation owner are approved.
6. Continue independent PLATFORM-01 and RELEASE-01 evidence work in sequence; real Cosmos, observability/security, Android/iOS builds and physical-device validation remain explicit gates.

## Deployment constraint

The MVP API is validated through a native .NET 10 publish artifact. Docker, container images, container registries and container-based hosting are explicitly excluded. The API hosting provider remains a post-coding infrastructure decision; Azure Static Web Apps is not the API host or native mobile distribution target.

## Audit limitations

This audit reflects main after PR #48 merge `2ec84e4eb99b5b1f66fb8b059d71fa36b01c6135`, the PRD, README, test plan and current source inventory. It is not a substitute for executing Cosmos integration tests, provider sandbox tests or physical-device acceptance. Keep the matrix current in every feature PR.
