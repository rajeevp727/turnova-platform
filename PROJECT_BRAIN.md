# TurnOva Project Brain

> Compact working context for future development sessions. Read this before coding; update it in the same feature PR whenever project state changes.
> Full requirements remain in `PRD.md`, `docs/architecture.md`, and `docs/test-plan.md`.

**Last verified:** 2026-11-06  
**Repository:** https://github.com/rajeevp727/turnova-platform  
**Default branch:** `main`  
**Current main HEAD:** `1f30ffd7e268a09ba5863f8f9f58e3457e1ef9f0` (PR #51)

## Product in one line
Mobile-first ride-hailing platform where eligible drivers receive rides fairly through zone + vehicle-class FIFO queues.

## Stack and app scope
- Backend: .NET 10 LTS / ASP.NET Core Web API / C# 14.
- Mobile: React Native + Expo; Android and iOS.
- Data: Azure Cosmos DB; local in-memory fallbacks exist for demo/tests.
- Realtime: SignalR.
- Authentication: TurnOva API integrates with TriSend Auth; JWT + refresh-token rotation.
- MVP apps: TurnOva Driver and TurnOva Rider.
- Phase 2: TurnOva Fleet and TurnOva Ops. No driver/rider web apps.

## Current product rules
- Canonical service classes: `scooty`, `bike`, `auto`, `cab`.
- Rider selects service class; pickup coordinates resolve to a configured dispatch zone.
- Driver selects service class during onboarding; queue and union eligibility use that same value.
- Only approved drivers are eligible for queue/ride actions.
- FIFO order is server-owned within a zone + service class.
- Current API exposes actionable requested rides only to the FIFO head; accept/reject recheck head ownership.
- Rejecting at queue head moves that driver to the back.
- Union is an employer/operating association, not a dispatch queue. Driver transfers require reasons and are audited.
- Driver selfie is private until approved by the driver's current union; replacement or union transfer resets/hides approval.
- Monthly payslips are PDF only, for completed IST months; metrics are days worked, accepted, completed and explicitly rejected rides. No compensation amounts until policy is agreed.
- Driver and Rider apps support System/Light/Dark themes.

## Delivery status (verified)
- PRs #1–#19: merged; see PRD, architecture and test plan for detailed scope.
- PR #20: merged — driver onboarding aligned with Scooty/Bike/Auto/Cab.
- PR #21: merged — Copilot agent profiles and repository engineering guardrails.
- PR #22: merged — FIFO queue-head enforcement at available-rides, accept and reject API boundaries.
- PR #22 CI: GitHub Actions `TurnOva CI` completed successfully. Its PR description notes no local build/test was run by the author; CI is the validation evidence.
- PR #23: merged — added this project brain; its GitHub Actions `TurnOva CI` completed successfully.
- PR #24: merged — durable ride-offer aggregate, Cosmos/in-memory stores, migration and state/store tests. Merge commit `1c10ccdb5d191b3dd635135dedad954807a78a47`; GitHub Actions CI passed.
- PR #25: merged — server-owned offer expiry worker and idempotent FIFO recovery. Merge commit `332bd5df8e99d290db98403b08bdca8b704202ce`.
- PR #26: merged — driver Online/Offline availability and FIFO eligibility enforcement. Merge commit `373cdad94006c1b742d15b4a8207a8a9d50f5e20`.
- PR #27–#29: merged — exclusive driver reservations, durable initial FIFO offer delivery/reconnect lookup, and repository cleanup.
- PR #30: merged — retry-safe durable offer rejection and idempotent queue rotation.
- PR #31: merged — queue rotation idempotency regression tests.
- PR #32: merged — automatic next-driver offer advancement after rejection/expiry.
- PR #33: merged — durable SignalR notification outbox and unified offer lifetime.
- PR #34–#43 merged: acceptance matrix baseline, auth contract/JWT/startup guards, SignalR offer delivery, refresh-token single-flight coordination, native non-container API publish, outbox retry/concurrency tests and ride-request validation coverage.
- PR #45 merged: zone-scoped nearby compatible vehicles and online Driver location handling; CI run 42209346759 passed.
- PR #46 merged: 16-way concurrent duplicate queue-join test; CI run 42209694545 passed.
- PR #47 merged: authenticated Rider-owned trip history, Activity restore/history, safe no-fare placeholder, stale-response/stale-active-state guards, 19-area acceptance tracker reconciliation and progress handoff. Merge commit `32cacbf430e1443835e3bd8e4294c15d3c137fe3`; latest-head CI run 42214516012 passed all four jobs.
- PR #48 merged: authenticated Driver-owned trip history API/UI, active-trip restore, SignalR reconnect refresh, session-expiry state cleanup and owner/privacy/role/empty-history tests. Merge commit `2ec84e4eb99b5b1f66fb8b059d71fa36b01c6135`; final-head CI run 42215718229 passed all four jobs.
- PR #49 merged: explicit CORS origin allow-list, wildcard rejection, separate liveness and Cosmos-aware readiness endpoints, and CORS/health integration tests. Merge commit `67621e02b00d823b84d8d5e3f74f199c283fa3e7`; final-head CI run 42216606101 passed all four jobs.
- PR #51 merged: append-only queue audit events in the existing `QueueEntries` partition with transactional Cosmos batches, in-memory audit tests, and generic traceId Problem Details with exception-type-only logs. Merge commit `1f30ffd7e268a09ba5863f8f9f58e3457e1ef9f0`; final-head CI run 42219327319 passed all four jobs.
- The 19 PRD acceptance areas are tracked in `docs/MVP_ACCEPTANCE_MATRIX.md`; AI-01 is a separately approved supplemental requirement and is not counted among the 19.
- No open PR is being treated as approved/merged unless rechecked live.

## Highest-priority unfinished work

1. **CAB-SCOPE-01 (current):** reject Scooty/Bike/Auto for new Rider rides and Driver onboarding; allow Cab only and preserve historical records without destructive migration.
2. **CAB-DISCOVERY-01:** discover approved nearby unions and show at most 2–3 eligible, available Cab drivers per union ordered by driver-to-pickup proximity; Rider selects, no customer-facing FIFO ranking or automatic assignment.
3. **CAB-AGREEMENT-01:** direct Rider/Driver fare negotiation, immutable versioned agreement, both confirm current version, and no ride start until mutual confirmation.
4. **CAB-RECON-01:** Driver reports amount received as unverified; persist agreed-minus-reported signed delta and least-privilege union visibility. Customer dispute/confirmation and non-zero-delta next-ride policy remain unresolved.
5. **CAB-UNION-01:** Cab membership, vehicle verification, union approval/selfie visibility and minimum necessary oversight.
6. **PLATFORM-01 and RELEASE-01:** queue audit and CORS/health increments merged; durable exception audit, metrics/traces/alerts, Cosmos failure/recovery and Android/iOS device/release gates remain.
7. **AI-01 supplemental (not started):** implement the separately approved bounded provider-agnostic AI service after the Cab-only vertical slices; keep it outside the 19-area denominator.

## Known constraints and migration cautions
- Cosmos DB transactions are limited to one logical partition. Do not assume atomic writes across Rides, QueueEntries, Driver profiles or other containers.
- Ride model currently includes in-memory/demo paths; Cosmos-backed persistence exists for selected resources, not proof that all ride/dispatch state is production durable.
- Development zone `hyderabad-central` uses a placeholder bounding rectangle; replace with reviewed operational boundaries before pilot.
- Existing driver/union records with legacy `mini`, `sedan`, `suv` values need explicit migration/operations correction; never silently map them.
- Interim Ops endpoints use a backend-only admin API key. This is not production RBAC.
- Payslip work-activity append is not transactionally coupled to ride transitions; add outbox/reconciliation before production.
- No production compensation, priority tier or commission policy has been agreed.

## Parked product decision
Priority/Normal driver tiers and commission model are intentionally parked until the user's partner is available. Do not invent, implement, or reopen this policy unless the user asks.

## Working agreements
- Work in small, reviewable PRs; inspect the full diff and relevant tests before merge.
- Merge only when automated review finds no unresolved critical/high-risk issue, the latest head has all required checks green, and repository rules permit the merge.
- Do not submit a formal self-approval. Do not bypass an independent required human approval or weaken branch protection.
- Before a new feature: read this brain, inspect current `main`, open PRs, relevant implementation and tests.
- After every feature: update this brain, acceptance matrix and progress handoff in the same focused PR whenever status/next step changes.
- Keep PRD/architecture/test-plan synchronized when product behaviour or architecture changes.
- Distinguish implemented, CI-validated, manually tested and production-ready; never claim one means another.
- Do not commit secrets or real personal data.
- If a decision depends on partner or product input, park it rather than guessing.

## Current implementation increment (merged)
Durable offer persistence foundation was introduced on `feature/durable-ride-offer-foundation`:
- One aggregate document per ride in `RideOffers` (`/rideId`), with current offer identity/deadline/status and retained attempt history.
- Create-if-absent prevents a second aggregate for the same ride.
- ETag-conditional replacement protects concurrent transitions.
- Bounded expired-offer scan and driver-pending-offer lookup are part of the store contract.
- Cosmos migration and in-memory adapter are included.
- PRD, architecture, test plan and this brain are updated in the same branch.

This stage is persistence groundwork, not end-to-end dispatch. Next: implement the server-owned offer state machine/orchestrator and expiry worker; then wire driver offer endpoints and SignalR notifications. Preserve one pending offer per ride and one pending offer per driver. Use conditional writes and explicit recovery for cross-partition ride/queue coordination.

## Server-owned expiry increment (merged in PR #25)

A hosted worker polls overdue durable offers in bounded batches in Cosmos and in-memory modes. Queue rotation is idempotent by offer ID; the Cosmos queue entry persists the last applied offer ID and uses ETag conditional replacement. The offer is marked Expired only after queue rotation succeeds. If the process stops between those operations, replay observes the marker and safely completes offer resolution. Missing queue membership leaves the offer pending for retry. This is not a cross-container transaction and the legacy ride accept/reject endpoints are not yet fenced by durable offer IDs; automatic next-driver dispatch and exclusive reservation remain future work.

## Driver availability increment (merged in PR #26)

- Driver profiles persist AvailabilityStatus (Offline by default for existing and new profiles).
- Approved drivers can go Online/Offline through authenticated driver endpoints.
- Going Online appends the driver to the back of their approved zone/service-class FIFO queue; repeating the request is idempotent.
- Going Offline removes queue membership and is blocked while the driver has an active ride.
- Queue join, available-rides, accept, reject and timeout paths require Online status.
- Driver app displays server-loaded availability and exposes Go Online/Go Offline controls.
- Cosmos driver-profile persistence uses the existing Drivers container; queue changes use the existing queue store. Cross-container writes are not atomic.
- CI is the validation gate; no local build/test is claimed.
- Next: exclusive driver reservation is in PR #27; then implement durable FIFO offer orchestration and delivery.

## FIFO offer delivery increment (in review)
- Initial ride offer is created for the current FIFO queue head in the ride-partitioned `RideOffers` store before the SignalR `RideOfferReceived` event is sent.
- Drivers can reconnect and retrieve their pending offer through `GET /api/v1/driver/offers/pending`; SignalR driver groups are joined only when the authenticated driver ID matches the requested group ID.
- Ride acceptance checks the persisted offer owner, pending state and expiry before applying the existing driver reservation and ride ETag protections.
- Offer creation and ride creation are separate Cosmos operations. A durable outbox/reconciliation workflow and automatic next-driver progression after rejection/expiry remain required before production.


## Idempotent durable offer rejection (merged in PR #30)
- When a ride has a persisted pending offer, rejection validates that the caller owns that offer and that it is still pending and unexpired.
- Queue rotation uses the offer ID as an idempotency key. If the API process stops after rotation but before the offer aggregate is marked Rejected, the driver can retry; the queue store recognizes the same offer ID and does not rotate the driver twice.
- The offer aggregate is conditionally updated with ETag protection and the matching attempt is marked Rejected.
- Legacy rides without an offer aggregate retain the previous queue-rotation behavior.
- This improves retry safety but does not yet provide automatic next-driver offer creation, durable notification outbox, or background reconciliation for a driver who never retries. Those remain production blockers.


## Queue rotation regression coverage (in review)
- Added in-memory queue-store tests for same-offer retry idempotency, a distinct subsequent offer, missing queue membership and empty offer ID validation.
- Updated the test plan with explicit queue rotation scenarios and the remaining Cosmos/API integration-test gaps.
- These tests are CI coverage only until the PR workflow completes; no local build or test execution is claimed.


## Automatic next-driver offer advancement (merged in PR #32)
- Shared advancer handles durable rejection and expiry. It verifies the ride is still Requested and validates candidate approval, Online status, zone, service class and passenger capacity.
- Appends the next pending attempt to the same ride aggregate with ETag protection and persists before SignalR notification.
- Rejection endpoint and expiry processor invoke the same service; competing calls cannot both replace the same ETag.
- Invalid queue entries are removed. No eligible candidate leaves the ride Requested with no pending offer.
- Known gaps: no transactional notification outbox; reconnect pending-offer fetch is recovery after a lost SignalR event. Cosmos cross-container coordination and multi-instance concurrency require integration tests. Offer lifetime currently uses 30 seconds pending configuration unification.
- No local build/test claimed; CI is the validation gate.


## Durable offer notification outbox + unified lifetime (merged in PR #33)
- Initial and advanced offers share clamped `Dispatch:OfferLifetimeSeconds` configuration (10–120 seconds; default 30).
- Offer aggregate stores notification state; newly created offers start Pending and are persisted before delivery.
- Hosted outbox worker reads bounded pending batches, rechecks current offer identity/status, sends `RideOfferReceived`, then ETag-marks notification Sent.
- Delivery is at-least-once: a crash after SignalR send but before the Sent marker can duplicate an event. Clients must de-duplicate by offerId and refresh authoritative pending state after reconnect.
- Multi-instance duplicate delivery and Cosmos failure-injection tests remain required. No local build/test claimed; CI is the validation gate.


## Verification note (2026-11-06)
- PR #33 is confirmed merged to main at c70cc39c5c246ca643574032f6fbd417fb0ec7e5.
- The GitHub connector returned no workflow runs and no commit status entries for this SHA; CI success is therefore **not verified from this audit**.
- The acceptance matrix in `docs/MVP_ACCEPTANCE_MATRIX.md` is the working source for requirement-by-requirement progress. Rows remain In progress until implementation and relevant test evidence are both recorded.
- Do not report 100% based on merged PR count. Use verified matrix rows divided by total in-scope rows, and separately report unresolved product decisions and pilot/production gates.


## Current implementation increment (PR #36 merged)
- PR #36 merged: Driver app subscribes to its authenticated SignalR driver group while Online, handles `RideOfferReceived`, de-duplicates at-least-once events by `offerId`, and refreshes authoritative available rides after connect/reconnect.
- PR #36 CI run 97 passed all three jobs: backend restore/build/tests, Rider Expo config/typecheck, and Driver Expo config/typecheck.
- Merge commit: `99c36552c7add12e5cf3ecb7d13bd60a11573f8f`.

## Current implementation increment (in progress)
- Branch `fix/mobile-refresh-token-single-flight`: serialize concurrent 401-triggered refreshes in both Rider and Driver apps. Without a shared in-flight refresh promise, parallel requests could submit the same rotating refresh token and cause one request to invalidate the session after another had already rotated it.
- Adds AUTH-LOGIN-021 to the test plan. CI and runtime validation are still required; do not mark the acceptance row verified yet.
- Next priority after this increment: improve mobile auth testability and cover refresh rotation/recovery, then continue durable dispatch recovery and Rider payment/receipt/support acceptance gaps.


## Rider trip history increment (merged in PR #47)
- Rider-only `GET /api/v1/rides/mine` derives ownership from the authenticated subject, returns a newest-first allow-listed summary, and omits rider/driver IDs, exact coordinates and fare.
- Rider Activity restores the latest active ride from persisted history; late responses after unmount/session change are ignored, and stale in-memory active state is cleared when no active ride remains.
- API tests cover owner isolation despite client-supplied riderId, ordering, response privacy, role separation and empty history.
- PR #47 merge commit: `32cacbf430e1443835e3bd8e4294c15d3c137fe3`; latest-head CI run 42214516012 passed.
- Cosmos cross-partition query RU/retention/performance and physical-device restoration remain unverified.

## Driver trip history increment (merged in PR #48)
- Add Driver-only `GET /api/v1/driver/rides/mine`, deriving ownership from the authenticated subject; return newest-first allow-listed trip summaries and active-location-sharing state only.
- Driver app restores the latest active assigned trip on opening Trips, displays previous assigned trips, refreshes authoritative history after SignalR reconnect, and clears stale in-memory active state on terminal-only history or session expiry.
- API tests cover owner isolation despite client-supplied driverId, ordering, private-field omission, Rider denial and empty history.
- PR #48 final head `2b19856b02b4722cd76cfb04459c01afb6a4e3ab` passed all four CI jobs in run 42215718229 and was merged as `2ec84e4eb99b5b1f66fb8b059d71fa36b01c6135`.
- Do not expose rider/driver IDs, exact coordinates or fare values in history. Real Cosmos cross-partition behaviour remains an infrastructure-backed gate.


## PLATFORM-01 CORS and health increment (merged in PR #49)
- PR #49 is open: https://github.com/rajeevp727/turnova-platform/pull/49 (branch `fix/platform-cors-health`). Implementation head `5458c7c244b3621e294cef5ae1c2ad3d24c339c7` passed all four jobs in CI run 42216452456; the final documentation-only head still needs CI before merge.
- Replace wildcard CORS with explicit `Cors:AllowedOrigins`; reject wildcards, URL paths, query/userinfo and non-HTTPS origins outside Development. Empty allow-list denies browser CORS while native Android/iOS clients remain unaffected.
- Keep `/health/live` as process liveness and `/health/ready` as the single Cosmos-aware readiness endpoint; remove the duplicate health-check route mapping.
- Add integration tests for configured/disallowed browser origins and demo-mode liveness/readiness.
- Remaining platform evidence: Cosmos-unavailable readiness failure injection, production origin configuration, secrets/logging review, metrics/traces/alerts, durable audit failure/recovery and load tests.


## PLATFORM-01 queue audit increment (merged in PR #51)
- PR #51 is open: https://github.com/rajeevp727/turnova-platform/pull/51 (branch `feat/platform-observability-audit`). Implementation head `369f5dacea92885ce78c1504bcb4f9dce9cbde04` passed all four CI jobs in run 42219053050; merge remains gated on green CI for the latest PR head.
- Existing `RideOfferState.Attempts` persists offer decisions and `DriverUnionAudit` persists union transfers, but queue membership changes were mutable state without append-only history.
- New `QueueAuditEvent` records successful join, leave, move-to-back and offer rotation events with driver, zone/service-class partition, UTC time and optional offerId; no phone/email, exact location or fare.
- `IQueueStore` exposes queue-scoped audit retrieval. In-memory adapter records only successful mutations; duplicate offer retries and failed/no-op operations do not create duplicate events.
- Cosmos adapter stores event documents in the existing `QueueEntries` container and commits queue state plus audit event in one transactional batch within the same `zoneServiceKey` partition.
- Global exception handler returns generic Problem Details with traceId and logs traceId plus exception type only; it does not log request bodies, tokens or exception messages.
- Automated in-memory event tests cover event types, partition scoping and no-op/failed mutation behaviour; API integration test verifies generic exception Problem Details with traceId and no exception message. Real Cosmos transactional-batch/ETag/restart/retention verification remains an infrastructure-backed gate.
- Durable exception audit with actor reference and safe reason/category is still outstanding; do not claim the full PLATFORM-01 audit criterion verified.
- Metrics/traces/exporters/alerts and Cosmos failure injection remain subsequent PLATFORM-01 work.

## Authoritative product scope update — Cab-only union model (2026-11-06)

This section overrides conflicting legacy rules. TurnOva MVP is CAB rides only; Scooty, Bike and Auto are out of scope for new workflows. Preserve historical records safely.

Flow: Rider enters pickup/destination/stops → discover active approved unions → show up to 2–3 approved, available, verified Cab drivers per union ordered by pickup proximity → Rider selects → call/chat if configured → direct fare negotiation → both confirm immutable agreement version → ride lifecycle → Driver reports amount received → authorised reconciliation.

No platform fare estimates, fixed/dynamic pricing, payment gateway, escrow or in-app collection. Driver-reported amount is unverified. Delta = agreed minus reported; positive shortfall, zero exact, negative excess. Do not invent customer dispute or next-ride blocking policy.

Customer-facing discovery must not rank or auto-assign by FIFO. Internal FIFO role remains unresolved. Union membership/approval, Cab/vehicle verification, union-approved selfie before public display, least-privilege oversight and audit are in-scope. Partnership terms require legal review.

Supplemental acceptance IDs: CAB-SCOPE-01, CAB-DISCOVERY-01, CAB-AGREEMENT-01, CAB-RECON-01 and CAB-UNION-01. Keep the 19-row denominator; reclassify obsolete fare estimate/payment gateway/FIFO dispatch requirements. See PRD.md §15 and docs/MVP_ACCEPTANCE_MATRIX.md.
