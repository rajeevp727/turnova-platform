# TurnOva Project Brain

> Compact working context for future development sessions. Read this before coding; update it in the same feature PR whenever project state changes.
> Full requirements remain in `PRD.md`, `docs/architecture.md`, and `docs/test-plan.md`.

**Last verified:** 2026-11-07  
**Repository:** https://github.com/rajeevp727/turnova-platform  
**Default branch:** `main`  
**Last audited main HEAD:** `b05a62990f767064c1cf333936fe8be7ab4d7ebc` (PR #75 AUTH-02 late-401 refresh race merged; final PR-head CI 235/235 backend, 12/12 shared mobile and all four jobs passed; main push CI also green; native signing/device and live provider gates remain)

## Product in one line
Cab-only, union-connected ride discovery where the Rider selects an eligible nearby Driver, both parties negotiate and confirm an immutable fare agreement, and the Driver reports direct payment received.

## Stack and app scope
- Backend: .NET 10 LTS / ASP.NET Core Web API / C# 14.
- Mobile: React Native + Expo; Android and iOS.
- Data: Azure Cosmos DB; local in-memory fallbacks exist for demo/tests.
- Realtime: SignalR.
- Authentication: TurnOva API integrates with TriSend Auth; JWT + refresh-token rotation.
- MVP apps: TurnOva Driver and TurnOva Rider.
- Phase 2: TurnOva Fleet and TurnOva Ops. No driver/rider web apps.

## Current product rules
- Cab is the only supported category for new Rider, Driver and Union MVP workflows. Legacy Scooty/Bike/Auto records remain readable for history and are not destructively migrated or silently converted.
- Rider discovery must group approved unions and show up to 2–3 eligible Cab drivers per union by driver-to-pickup proximity; the Rider selects the Driver.
- Customer-facing FIFO ranking, automatic FIFO assignment and next-driver advancement are not part of the approved Rider flow. Whether FIFO remains an internal Union fairness mechanism is unresolved; preserve legacy queue data/code until that decision is made.
- PR #52 merged Cab-only Rider/Driver/Union foundations at `6ba7b1968c1b5b2de61843cab04e85f7631e50fc`; post-merge CI run 42223126817 passed all four jobs. PR #53 merged legacy non-Cab endpoint/UI action guards at `7642168f42e277e04b76d7fba4c28ba90d3faa22`; final-head CI run 42232390997 passed all four jobs.
- Only approved drivers with verified Cab details and current Union eligibility may enter new discovery/ride workflows.
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
- PR #50 merged: authoritative Cab-only union-selected-driver business-model documentation and 19-area acceptance reclassification. Merge commit `6bd8f734e56ce35716595307cbeb9d7ab9decc59`; final-head CI run 42220682290 passed all four jobs.
- PR #52 merged CAB-SCOPE-01 foundations: Cab-only Rider/Driver UI and API gates, new Union Cab-only configuration/assignment, legacy profile and ride-history retention tests. Post-merge CI run 42223126817 passed all four jobs.
- The 19 PRD acceptance areas are tracked in `docs/MVP_ACCEPTANCE_MATRIX.md`; AI-01 is a separately approved supplemental requirement and is not counted among the 19.
- No open PR is being treated as approved/merged unless rechecked live.

## Current active increment
- PR #55 merged sequential #3 RIDER-01 / CAB-DISCOVERY-01 at `78ddb9558b453b9171bc020ea83e51087f6e5f2f`; final-head CI run `42237572056` passed all four jobs. It adds union-grouped Cab discovery, eligibility filtering, pickup-proximity ordering, approved-photo privacy and Rider local selection.
- PR #56 merged sequential #4 RIDER-02 / CAB-AGREEMENT-01 at `9d39c83d13f619bcc7a5ec575669a4167b3cc5f8`; final-head CI run `42245873224` passed all four jobs. It adds immutable agreement term versions, append-only confirmations, ETag/revision concurrency protection, Rider/Driver proposal and confirmation controls, SignalR agreement events, and start/completion gates.
- PR #57 merged sequential #5 RIDER-03 at `c5ff07843365e949b70fe1d4b9753bcd88022e10`; final synchronized PR head `8c846db3c93cfbe6f1eb574c838eb3ab0bf4fb9e` passed CI run [42254924983](https://github.com/rajeevp727/turnova-platform/actions/runs/42254924983), all four jobs green. It adds durable selected-driver bookings, route/Union snapshots, idempotent create-if-absent persistence, per-driver temporary/permanent reservation lifecycle, safe expiry/cancel/decline/completion release, Union-transfer exclusion and Driver reconnect recovery. Automated reviews [6689967817](https://github.com/rajeevp727/turnova-platform/pull/57#issuecomment-6689967817) and [6690099440](https://github.com/rajeevp727/turnova-platform/pull/57#issuecomment-6690099440) found no unresolved critical/high-risk issue.
- Sequential #6 DISPATCH-01 is implemented in PR #55 and its independent regression work merged in PR #58 at `a3a5835c66ec5f77f4b7505b4d99d5a43de18403`; final head `73db39c8cbdf79da99181688f7540e63c88cfc32` passed CI run [42258555372](https://github.com/rajeevp727/turnova-platform/actions/runs/42258555372), all four jobs. Automated review comment [6690682769](https://github.com/rajeevp727/turnova-platform/pull/58#issuecomment-6690682769) found no unresolved critical/high-risk issue. Production radius/zone approval, live Cosmos query/index and Android/iOS pickup-zone acceptance remain blockers.
- Sequential #7 DISPATCH-02 is implemented in PR #55/RIDER-03: union grouping, up-to-three cap, pickup-distance then DriverId ordering, explicit Rider selection and no FIFO dependency. PR #59 merged at `508a789246cec1a9452651c62fa08aa3908140a4`; final-head CI run `42259580145` passed all four jobs.
- PR #61 merged sequential #9 DISPATCH-04 regression coverage at `118929955d3959f078ddb59f7f5bdf3c3965d52a`; automated review found no unresolved critical/high-risk issue. PR #64 final-head CI [42272254259](https://github.com/rajeevp727/turnova-platform/actions/runs/42272254259) executed 189/189 backend tests, including this regression.

- PR #66 merged sequential #11 DRIVER-01 at `26dafc87451ba49ea4734df81c94f9e75790800d`; final-head CI [42278673796](https://github.com/rajeevp727/turnova-platform/actions/runs/42278673796) executed 191/191 backend tests and passed all four jobs. Identity-provider claim mapping, private document storage/retention and Android/iOS onboarding evidence remain.

## Highest-priority unfinished work

1. **#6 DISPATCH-01 — Blocked:** PR #55 implementation and PR #58 regression coverage are independent of the missing decision. Resume final verification only after product approval of the production pickup radius and zone boundaries, plus live Cosmos and Android/iOS pickup-zone evidence.
2. **#7 DISPATCH-02 — Implementation audit recorded:** PR #55/RIDER-03 implement the per-union cap, pickup-proximity ordering, explicit selection and no-FIFO Rider path; PR #59 adds an equal-distance deterministic tie-break regression. Live Cosmos/device evidence and the internal FIFO role decision remain, so the acceptance row stays In progress.
3. **#8 DISPATCH-03 / #9 DISPATCH-04:** selected negotiation expiry/idempotency and no-overlap protections are implemented and covered by in-memory/API tests, including PR #61's accepted-active-ride Union-transfer guard. Live Cosmos restart/ETag contention remains external.
4. **#10 REALTIME-01 — Code audit recorded:** PR #63 fixes Rider reconnect state restoration. PR #64 final-head CI [42272254259](https://github.com/rajeevp727/turnova-platform/actions/runs/42272254259) executed 189/189 backend tests, including hub ownership tests; physical Android/iOS WebSocket/reconnect acceptance remains outstanding.
5. **#11 DRIVER-01 — In progress:** PR #66 merged union-scoped selfie approval and Driver membership display. Identity-provider claim mapping, private document storage/scanning/consent/retention and Android/iOS onboarding evidence remain.
6. **#12 DRIVER-02 — In progress:** PR #67 merged the active Cab Union/document gate, reservation-safe Online/Offline and zone-exit transitions, and foreground availability-GPS refresh; live Cosmos and Android/iOS device gates remain.
7. **#13 TRIP-01 — In progress:** PR #68 merged the selected-Cab lifecycle/location safety slice; live Cosmos and Android/iOS SignalR/map/permission/background-resume acceptance remain.
8. **#14 DRIVER-03 — In progress:** PR #69 merged at `70738af97d17cb8be715410898138d3e2f10f1cf`; final-head CI [42291337082](https://github.com/rajeevp727/turnova-platform/actions/runs/42291337082) passed 196/196 backend tests, native non-container API publish, both mobile typechecks and nine shared mobile tests. Android/iOS PDF document acceptance and live Cosmos snapshot/recovery remain.
9. **#15 DRIVER-04 — Blocked:** Driver support categories, escalation owner and operational process are not approved. Do not invent them.
10. **#16 RIDER-04 — Current:** PR #70 implements the approved CAB-RECON-01 supplemental requirement: immutable Driver-reported payment reconciliation after completed Cab rides, signed delta, Driver history and claim-scoped UnionManager oversight. Code head `d73dc0c76528ca95fff39a5d38a5512b1720b941` passed CI [42295719183](https://github.com/rajeevp727/turnova-platform/actions/runs/42295719183) with 210/210 backend tests, both mobile typechecks, nine shared tests and native non-container publish; live Cosmos/device gates remain. CAB-RECON-01 stays outside the 19-row denominator.
11. **#17 SUPPORT-01 — Blocked:** Rider support categories, escalation owner and operating process remain unapproved.
12. **#18 PLATFORM-01 / #19 RELEASE-01:** code/CI increments are merged; retain live hosting, Cosmos, native signing, physical-device, provider and legal gates.
13. **Current sequential #2 AUTH-02:** secure token-pair persistence, startup restoration and offline logout failure handling; AI-01 remains separately tracked outside the 19-row denominator.

## Known constraints and migration cautions
- Cosmos DB transactions are limited to one logical partition. Do not assume atomic writes across Rides, QueueEntries, Driver profiles or other containers.
- Ride model currently includes in-memory/demo paths; Cosmos-backed persistence exists for selected resources, not proof that all ride/dispatch state is production durable.
- Development zone `hyderabad-central` uses a placeholder bounding rectangle; replace with reviewed operational boundaries before pilot.
- Existing driver/union records with legacy Scooty/Bike/Auto service classes are retained for history and are not eligible for new Cab discovery; never silently map them to Cab.
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
- PR #51 merged as `1f30ffd7e268a09ba5863f8f9f58e3457e1ef9f0`; final-head CI run 42219327319 passed all four jobs.
- Existing `RideOfferState.Attempts` persists offer decisions and `DriverUnionAudit` persists union transfers, but queue membership changes were mutable state without append-only history.
- New `QueueAuditEvent` records successful join, leave, move-to-back and offer rotation events with driver, zone/service-class partition, UTC time and optional offerId; no phone/email, exact location or fare.
- `IQueueStore` exposes queue-scoped audit retrieval. In-memory adapter records only successful mutations; duplicate offer retries and failed/no-op operations do not create duplicate events.
- Cosmos adapter stores event documents in the existing `QueueEntries` container and commits queue state plus audit event in one transactional batch within the same `zoneServiceKey` partition.
- Global exception handler returns generic Problem Details with traceId and logs traceId plus exception type only; it does not log request bodies, tokens or exception messages.
- Automated in-memory event tests cover event types, partition scoping and no-op/failed mutation behaviour; API integration test verifies generic exception Problem Details with traceId and no exception message. Real Cosmos transactional-batch/ETag/restart/retention verification remains an infrastructure-backed gate.
- Durable exception audit with actor reference and safe reason/category is still outstanding; do not claim the full PLATFORM-01 audit criterion verified.
- Metrics/traces/exporters/alerts and Cosmos failure injection remain subsequent PLATFORM-01 work.

## Authoritative product scope update — Cab-only union model (2026-11-06)
- PR #50 is reconciled with main after PR #51; final-head CI run 42220458874 passed all four jobs. Documentation scope reset is ready for merge; no feature implementation is claimed by this PR.

This section overrides conflicting legacy rules. TurnOva MVP is CAB rides only; Scooty, Bike and Auto are out of scope for new workflows. Preserve historical records safely.

Flow: Rider enters pickup/destination/stops → discover active approved unions → show up to 2–3 approved, available, verified Cab drivers per union ordered by pickup proximity → Rider selects → call/chat if configured → direct fare negotiation → both confirm immutable agreement version → ride lifecycle → Driver reports amount received → authorised reconciliation.

No platform fare estimates, fixed/dynamic pricing, payment gateway, escrow or in-app collection. Driver-reported amount is unverified. Delta = agreed minus reported; positive shortfall, zero exact, negative excess. Do not invent customer dispute or next-ride blocking policy.

Customer-facing discovery must not rank or auto-assign by FIFO. Internal FIFO role remains unresolved. Union membership/approval, Cab/vehicle verification, union-approved selfie before public display, least-privilege oversight and audit are in-scope. Partnership terms require legal review.

Supplemental acceptance IDs: CAB-SCOPE-01, CAB-DISCOVERY-01, CAB-AGREEMENT-01, CAB-RECON-01 and CAB-UNION-01. Keep the 19-row denominator; reclassify obsolete fare estimate/payment gateway/FIFO dispatch requirements. See PRD.md §15 and docs/MVP_ACCEPTANCE_MATRIX.md.


## CAB-SCOPE-01 implementation and legacy action hardening
- PR #52 merged Cab-only new-workflow validation and historical data retention. PR #64 final-head CI [42272254259](https://github.com/rajeevp727/turnova-platform/actions/runs/42272254259) executed 189/189 backend tests and passed native non-container API publish, Rider/Driver Expo config/typechecks and shared mobile tests.
- PR #53 merged legacy non-Cab selfie replacement/approval, location, queue-read, pending-offer, rejection and timeout guards; the Driver app suppresses legacy-profile queue/offer polling and SignalR subscriptions while retaining read-only history and Offline cleanup.
- PR #54 merged documentation reconciliation at `7dd1aedf56fdc9ef36b6b04add952d78687a4266`; latest-head CI run 42234802360 passed backend, Rider app, Driver app and shared mobile-auth jobs.
- PR #53 merge commit: `7642168f42e277e04b76d7fba4c28ba90d3faa22`; final-head CI run `42232390997` passed all four jobs. This is CI validation, not physical-device or production acceptance.
- The legacy FIFO/automatic-offer machinery remains for compatibility, but the Rider booking path no longer uses customer-facing FIFO ranking or automatic assignment. Whether FIFO remains an internal Union fairness mechanism is unresolved.


## RIDER-02 agreement implementation details
- Agreement terms are append-only `RideAgreementVersion` records embedded in the Rides aggregate; Rider/Driver confirmation records are append-only and version-specific.
- `AgreementStateRevision` increments on proposal/confirmation changes. Cosmos writes use the existing Rides partition and ETag replacement; concurrent proposal/confirmation or stale start/completion attempts return conflict.
- Proposal API requires party ownership, active Cab union membership, approved Cab profile, `Idempotency-Key`, positive two-decimal amount and uppercase three-letter currency. No platform fare or payment collection is introduced.
- Start and completion require both parties to confirm the current version. A fare renegotiated during the trip invalidates prior confirmations and blocks completion until both reconfirm.
- RIDER-03 adds optional stops to selected rides and agreement route snapshots; live Cosmos/device evidence remains required before the related acceptance rows can be marked Verified.


## DISPATCH-03 reservation lifecycle audit (2026-11-06)

- Main after PR #59 merge `508a789246cec1a9452651c62fa08aa3908140a4` includes selected-driver reservation lifecycle handling and regression coverage.
- Temporary leases are acquired before selected booking creation; availability and union membership are re-read while holding the lease. Mutual agreement and Driver acceptance promote the lease to permanent.
- Rider cancellation, Driver decline, completed/cancelled/expired ride reconciliation and lazy negotiation expiry release only the matching driver/ride reservation. Expired leases are not silently stolen by the store; the API first reconciles authoritative ride state. Idempotent retry can recover the same ride after an interrupted write.
- Tests in `RideApiIntegrationTests.cs` cover concurrent bookings, cancel/decline release, expiry through ride read/discovery, fail-closed missing expiry configuration, interrupted-write recovery and terminal-state cleanup. `DriverRideReservationStoreTests.cs` covers matching release, expiry retention and permanent promotion.
- Cosmos reservation store uses conditional ETag replacement/deletion. Live Cosmos multi-instance contention, restart/crash recovery and outbox validation remain external gates; no live validation is claimed.
- DISPATCH-04 audit is recorded in PR #61: accepted active rides retain the per-driver reservation and Union transfer is rejected. Live Cosmos multi-instance/restart validation remains external. Next sequential work is #10 REALTIME-01; do not introduce customer-facing FIFO ranking or automatic next-driver advancement.


## DISPATCH-04 accepted-ride transfer audit (2026-11-06)

- PR #61 merged at `118929955d3959f078ddb59f7f5bdf3c3965d52a` after an automated review found no unresolved critical/high-risk issue. PR #64 final-head CI [42272254259](https://github.com/rajeevp727/turnova-platform/actions/runs/42272254259) executed 189/189 backend tests, including its accepted-active-ride/Union-transfer regression.
- `UnionTransferIsBlockedWhileDriverHasAcceptedActiveRide` verifies a Rider-selected Cab booking proceeds through proposal, both confirmations and Driver acceptance; the permanent per-driver reservation remains held; transfer to another Union returns conflict; the original membership and reservation remain unchanged.
- The test is in-memory/API integration evidence. Live Cosmos multi-instance contention, ETag conflict behaviour under real service, process restart/crash recovery and approved negotiation-lifetime configuration remain unverified.
- PR #63 completes the #10 REALTIME-01 code/test audit; physical Android/iOS delivery and reconnect acceptance remain external. Sequential next item: #11 DRIVER-01.


## REALTIME-01 Rider reconnect audit (2026-11-06)

- PR #63 fixes the Rider's stale-state gap after a SignalR disconnect: initial connection and reconnect now reload owner-scoped ride history/current ride details and agreement versions. Rider status/location/agreement callbacks are guarded after cleanup. Driver reconnect continues to reload available requests and authoritative selected/active/terminal history.
- A shared role-specific active-ride selector is used by Rider and Driver. Automated tests cover Rider and Driver active states, terminal-only history clearing and invalid roles.
- `RideTrackingHubTests` verifies Rider and assigned Driver can join only their own ride group, and a Driver can subscribe only to their own selected-booking notification group.
- PR #63 head `d51acc55093f71efec190b10ba85b8cc738f985c` passed native non-container API publish, both mobile app config/typechecks and shared mobile tests. PR #64 final-head CI [42272254259](https://github.com/rajeevp727/turnova-platform/actions/runs/42272254259) executed 189/189 backend tests, including `RideTrackingHubTests`.
- Physical Android/iOS WebSocket delivery/reconnect, background/resume and device-to-device trip updates remain external acceptance gates. No production or device readiness is claimed.
- Next sequential work: #11 DRIVER-01.


## CI backend-test execution correction (2026-11-06)

- PR #64 merged the Release solution mappings and explicit test-project execution guard at `c90699d8fd5e9140d2daccbced106075d86118bb`.
- Final-head CI [42272254259](https://github.com/rajeevp727/turnova-platform/actions/runs/42272254259) passed all four jobs. The backend Release job executed 189/189 tests and the TRX non-zero guard passed.
- Native non-container API publish/artifact verification, Rider/Driver Expo config/typechecks and shared mobile tests passed. Live Cosmos and Android/iOS physical-device acceptance remain separate gates.


## DRIVER-01 union-scoped selfie approval increment (2026-11-07)

- PR #66 adds a signed `UnionManager` role plus one or more signed `unionId` claims. `GET /api/v1/union/drivers` returns only active Cab members of claimed active unions and only the review fields needed for vehicle/selfie checks; it excludes phone/email, exact coordinates, private chat and Rider/ride data.
- `PUT /api/v1/union/drivers/{driverId}/selfie-review` requires the current active membership, active Cab-serving union, approved Cab profile, approved driving licence and vehicle-registration documents, and a valid selfie. Only the current assigned UnionManager can approve public visibility; operations admin-key access can reject/hide but cannot approve.
- Selfie review serializes with selected-driver reservations and Union transfers, updates the profile through the ETag-aware conditional update path, and writes deterministic retry-safe audit events. A changed selfie/profile/document snapshot is rejected for refresh; duplicate approval does not duplicate audit.
- Union transfer resets selfie approval to Pending unless the selfie is already approved by the exact new union. Rider discovery continues to hide the photo until the current union approves it. The Driver app reads its own `/api/v1/driver/union` membership and displays current union/status.
- Automated API tests cover reviewer role/scope, least-privilege fields, approved-document gating, operations non-approval/rejection, active-reservation conflict, audit idempotency, transfer reset, old/new union access, Rider photo visibility and Driver membership. PR #66 merged at `26dafc87451ba49ea4734df81c94f9e75790800d`; final-head CI [42278673796](https://github.com/rajeevp727/turnova-platform/actions/runs/42278673796) passed 191/191 backend tests and all four jobs.
- Remaining DRIVER-01 gates: identity-provider issuance of the `UnionManager`/`unionId` claims; private document storage/scanning/consent/retention provider and policy; Android/iOS camera/document-permission/onboarding device acceptance; live Cosmos retry/ETag/restart validation.
- Operations-controlled union creation/activation remains the MVP approval gate for discovery. No separate Union app is introduced. Partnership terms and legal/T&C approval remain external launch gates.
- The 19-area denominator remains unchanged. Weighted progress is 42.1% (0 Verified, 16 In progress, 0 Not started, 3 Blocked; strict 0/19).


## DRIVER-02 availability/location increment (2026-11-07)

- PR #67 is the current sequential #12 increment. It serializes Online/Offline transitions and zone-exit mutations with the same per-Driver reservation lease used by selected bookings and Union transfers.
- Going Online requires an approved Cab profile, active membership in an Active Cab-serving Union covering the assigned zone, approved driving-licence and vehicle-registration documents, and valid GPS inside the assigned zone.
- The Driver app tracks location only in the foreground while Online and not in Negotiating, AgreementConfirmed or an active ride. It stops the watcher in background, refreshes GPS and restarts the watcher on resume, and attempts to go Offline if foreground permission is revoked.
- Automated API tests cover missing Union/documents and availability/location immutability during selected negotiation. PR #67 final head `a371ae9932e998ee9daaea6fd2a8fdff6e3615c1` passed CI [42286508190](https://github.com/rajeevp727/turnova-platform/actions/runs/42286508190), all four jobs and 193/193 backend tests; native API publish, both Expo typechecks and six shared mobile tests passed. Android/iOS physical-device permission/background/resume and live Cosmos ETag/race validation remain external gates.
- Existing FIFO queue code remains compatibility-only for this Rider-selected path. No customer-facing FIFO ranking or automatic assignment is added; the internal FIFO role remains unresolved.
- Weighted progress remains 42.1% (0 Verified, 16 In progress, 0 Not started, 3 Blocked; strict 0/19). Next sequential area: #13 TRIP-01.


## TRIP-01 selected-Cab lifecycle and foreground location increment (2026-11-07)

- The selected Rider booking path must be tested end-to-end from Negotiating through mutual agreement, Driver acceptance, arrival, start and completion; the legacy FIFO lifecycle test is not sufficient evidence for this path.
- Active-trip location is foreground-only on the Driver app. Backgrounding, leaving Trips, terminal state or unmounting removes the GPS watcher; late callbacks are ignored. Resume reloads authenticated Driver history, rechecks foreground permission and starts a watcher only if the same ride remains Accepted/Arrived/InProgress and the server still marks location sharing active.
- API coordinate validation rejects NaN, infinity and geographic out-of-range values for availability, pickup and active-trip updates. Only the assigned Driver may update an active ride; only its owning Rider reads the current coordinates.
- PR #68 final head `98ee431ffb686411589aec0a5305e1062a787864` passed CI [42290264457](https://github.com/rajeevp727/turnova-platform/actions/runs/42290264457): 195/195 backend tests, native non-container API publish/artifact, Rider/Driver Expo typechecks and nine shared mobile tests; all four jobs green.
- Physical Android/iOS SignalR, map, permission and background/resume tests and live Cosmos ETag/history tests remain external acceptance gates.


## DRIVER-03 work-metrics payslip increment (2026-11-07)

- The Driver payslip is a PDF-only monthly work summary. It must not calculate earnings, commission, deductions, fare, payment received or net payable; direct Rider-to-Driver payment reconciliation remains a separate unverified record.
- Only completed India-local months may be requested. The existing UTC-start comparison rejected future months but incorrectly allowed the current incomplete month; compare against the current India-local month start.
- The authenticated Driver claim is the sole source of driverId. A query parameter must not select another Driver's payslip. Missing Union assignment or a Union transfer inside the month must reject generation rather than invent split-employer allocation.
- PDF content should include only Driver/Union identity and work metrics; exclude ride/customer identifiers, route details and payment/fare values.
- Android/iOS month list and PDF save/share/accessibility checks plus live Cosmos snapshot/migration/crash-window validation remain external gates.


## RIDER-04 direct-payment reconciliation increment (2026-11-07)

- #16 adds one immutable Driver-reported payment record inside the existing ride aggregate after a Completed Cab ride and mutually confirmed current agreement. It stores Rider/Driver/Union linkage, agreement version, agreed amount, amount received, signed delta, currency, timestamp, reporting actor, unverified status and hashed idempotency key.
- Driver trip history exposes only the assigned Driver's report. `GET /api/v1/union/reconciliation` is UnionManager-only, filtered by signed unionId claims and returns minimum reconciliation fields without Rider identity, route/stops, contact details, exact location or private chat. No separate payment container or migration is introduced.
- Reports are immutable and idempotent for the same key/payload. No customer confirmation/dispute or non-zero-delta next-ride policy is inferred; completion's existing reservation release is unchanged. No payment gateway or escrow is added.
- Current weighted progress remains 42.1% (0 Verified, 16 In progress, 0 Not started, 3 Blocked; strict 0/19). Next sequential area after #16 is #17 SUPPORT-01, whose support process/owner remains unapproved.


## PLATFORM-01 bounded storage readiness (2026-11-07)

- PR #70 merged at `17c489eac64dc0f99e881a1474147b2f79fe2aac`; final-head CI [42295994210](https://github.com/rajeevp727/turnova-platform/actions/runs/42295994210) passed 210/210 backend tests and all four CI jobs. CAB-RECON-01 remains supplemental and outside the 19-area denominator.
- Current sequential area is #18 PLATFORM-01 because #17 SUPPORT-01 remains blocked by unapproved Rider support categories, escalation owner and process.
- PR [#71](https://github.com/rajeevp727/turnova-platform/pull/71) adds a three-second bounded storage readiness probe. Liveness stays independent; dependency failure/probe timeout returns generic 503; caller cancellation is propagated; diagnostics are not exposed in response/logs. Final-head CI [42303066838](https://github.com/rajeevp727/turnova-platform/actions/runs/42303066838) passed 214/214 backend tests, the Metro shared-module assertion, all four Rider/Driver Android/iOS bundle exports and three artifacts. Native signed builds and physical-device acceptance remain blocked.
- Tests use a deterministic failing probe and a cancellable delayed probe. They do not claim live Cosmos validation.
- Next sequential area after #18 is #19 RELEASE-01. Keep the 19-area weighted status at 42.1% (0 Verified, 16 In progress, 0 Not started, 3 Blocked; strict 0/19) until row evidence is fully accepted.


## RELEASE-01 production bundle validation (2026-11-07)

- PR #71 merged at `e75ed523fec4c4fd706f9b5f1024abd7598b8ed6`; its final-head CI passed 214/214 backend tests, native non-container API publish, Rider/Driver Expo config/typechecks and shared mobile tests.
- Focused PR [#72](https://github.com/rajeevp727/turnova-platform/pull/72) adds CI exports of production JavaScript bundles for Android and iOS in both Rider and Driver apps, with metadata verification and uploaded artifacts. The first CI attempt exposed Metro's inability to resolve `src/mobile/shared` from the app root; both apps now configure only `src/mobile/shared` in `watchFolders` while preserving Metro's normal hierarchical/nested dependency lookup. A subsequent CI run also exposed missing SDK 54 `expo-asset`; the compatible dependency is now added to both app manifests. A later run exposed a nested React Native dependency blocked by `disableHierarchicalLookup`; that override was removed. CI now also asserts the shared refresh module exists and is included in Metro's watch folders before export. Final-head CI [42303066838](https://github.com/rajeevp727/turnova-platform/actions/runs/42303066838) passed 214/214 backend tests, the Metro assertion, all four Rider/Driver Android/iOS bundle exports and three uploaded artifacts.
- Bundle exports are not native binaries. Android APK/AAB, iOS archive/signing, iOS bundle identifiers, store credentials and physical-device functional/security/pilot gates remain external. Do not invent identifiers or signing secrets.
- #17 SUPPORT-01 remains blocked by unapproved Rider support categories, escalation owner and operating process. PR #73 completed the AI-01 coding increment; current sequential work resumes at #1 AUTH-01 without changing the 19-row denominator.



## AI-01 — approved supplemental Rider/Driver assistance (2026-11-07)

- PR #73 merged AI-01 at `3a2e37e610e8660fa8ecca1a3e1f42e8899251ff`; its code and CI are verified. Live provider sandbox, managed-secret wiring and data-processing review remain external gates. AI-01 stays outside the 19-area denominator.
- Focused branch: `feat/ai-01-rider-intent-help`. The .NET Application layer defines `IAiAssistantProvider`, typed contracts, disabled-by-default provider fallback, strict output validation, timeout/cancellation and deterministic parsing/help content.
- Rider route values are extracted locally and never sent to a provider. The provider boundary receives fixed pickup/drop-off placeholders and allow-listed generic intent words only. Provider output cannot replace the locally parsed route or select a non-Cab category.
- Help requests are reduced to allow-listed keywords and a canonical ride state. The provider can select only an approved source ID; the API returns the exact in-repository answer. Unsupported topics require escalation.
- Rider UI exposes a human-reviewed trip draft in Book and approved help in Profile; Driver Profile exposes approved ride/queue help. Drafting does not create a booking or call fare/agreement/payment APIs.
- Authenticated API routes are Rider-only for trip-intent drafting and Rider/Driver for help. Per-caller rate limiting is enabled; logs exclude prompt and personal data.
- Automated review [6697690613](https://github.com/rajeevp727/turnova-platform/pull/73#issuecomment-6697690613) found no unresolved critical/high-risk issue. Final-head CI [42306913746](https://github.com/rajeevp727/turnova-platform/actions/runs/42306913746) passed 233/233 backend tests and all API/mobile jobs.
- Live AI provider/model/credential selection, sandbox validation and data-processing review remain post-coding infrastructure gates. Do not choose a provider or commit secrets.
- PR #72 remains merged at `43cb52f4652990d1ddc88efb765f8fef72ec0753`; PR #73 is the current main HEAD `3a2e37e610e8660fa8ecca1a3e1f42e8899251ff`. Native signed APK/AAB/IPA, physical-device acceptance and live provider validation remain separate release gates.



## AUTH-01 — duplicate registration and profile read contract (2026-11-07)

- Current sequential area: #1 AUTH-01. Focused branch: `feat/auth-01-registration-profile-contract`.
- The existing API proxy already preserves upstream TriSend status/body. New fake-TriSend integration tests assert a duplicate Rider registration preserves HTTP 409 and returns no access/refresh token.
- A new `GET /api/v1/auth/me` integration test verifies the bearer token is forwarded to TriSend and the authenticated Rider profile is returned.
- The PRD says profile management but does not define editable fields or an upstream profile-update endpoint. Do not invent a TriSend route or field contract. Record profile editing as unresolved until an approved provider contract exists.
- Mobile runtime/session and physical-device profile lifecycle evidence remain outstanding. Next ordered area after AUTH-01 is #2 AUTH-02 token lifecycle.


## AUTH-02 — late 401 refresh-token race (2026-11-07)

- Current sequential area: #2 AUTH-02. Focused branch: `fix/auth-02-late-401-refresh-race`.
- PR #74 completed the AUTH-01 duplicate-registration and authenticated profile-read contract increment; it merged at `7fa2fc3490e7d914fa8bdfea7006025e84f6739d`. CI [42307569528](https://github.com/rajeevp727/turnova-platform/actions/runs/42307569528) passed 235/235 backend tests and all four jobs.
- Both Rider and Driver now read the latest SecureStore access/refresh pair only after joining the shared in-flight refresh operation. If a delayed request's old access token receives 401 after another request has already rotated the session, the helper reuses the new access token rather than sending the stale refresh token again.
- A per-app session epoch prevents requests started under a previous login from retrying with a replacement user's token or returning stale responses after logout/session replacement.
- Shared Node tests cover one stored-token read/refresh for concurrent unauthorized requests, delayed 401 after rotation and missing refresh-token rejection. CI [42309061896](https://github.com/rajeevp727/turnova-platform/actions/runs/42309061896) passed **12/12** shared mobile tests, **235/235** backend tests, both Rider/Driver Expo config/typechecks, all four Android/iOS production JavaScript bundle exports and native non-container API publish/artifact verification.
- The Driver payslip direct-fetch path now uses the same refresh coordinator. Session epoch checks prevent old requests/responses from crossing logout or a replacement session.
- PR #75 merged at `b05a62990f767064c1cf333936fe8be7ab4d7ebc`; final PR-head CI [42309594059](https://github.com/rajeevp727/turnova-platform/actions/runs/42309594059) and main push CI [42309804461](https://github.com/rajeevp727/turnova-platform/actions/runs/42309804461) passed all four jobs.
- Remaining AUTH-02 gaps: live TriSend rotation/reuse contract, app restart/expiry/logout behavior and SecureStore read/write/delete failure behavior on Android/iOS runtimes.


## AUTH-02 — secure storage recovery and app restart (2026-11-07)

- PR #75 merged at `b05a62990f767064c1cf333936fe8be7ab4d7ebc`; final PR-head CI [42309594059](https://github.com/rajeevp727/turnova-platform/actions/runs/42309594059) and main push CI [42309804461](https://github.com/rajeevp727/turnova-platform/actions/runs/42309804461) passed all four jobs.
- Current sequential branch: `fix/auth-02-secure-storage-restore`.
- Shared `sessionTokenStore` helpers now roll back both token keys when pair persistence fails, attempt both deletes, reject/clear partial pairs, restore from `/me`, refresh only after a 401, preserve credentials on network/5xx failures, and clear local credentials without waiting for online revocation.
- Mock tests cover successful save, write rollback, delete failures, partial-pair cleanup, valid restart, expired-access refresh rotation, expired refresh cleanup, network/503 preservation, secure read failure and offline logout.
- Automated review found that an already-started token-pair save could race with logout deletion. The shared SecureStore adapter now serializes pair writes and deletes; logout clearing queues after an in-flight write, with AUTH-LOGIN-027 regression coverage. Final code/test head `10858a88a35b686b51d8a7a47efe227b652c593f` passed CI [42315482037](https://github.com/rajeevp727/turnova-platform/actions/runs/42315482037): 27/27 shared mobile tests, 235/235 backend tests, both app typechecks, four Android/iOS production bundle exports and native non-container API publish/artifact verification. No unresolved critical/high-risk issue was found in the automated review. AUTH-02 remains In progress pending live TriSend rotation/reuse and Android/iOS runtime/device gates; next sequential item after this code/test increment is #3 RIDER-01.


## RIDER-01 — discovery evidence and test-plan reconciliation (2026-11-07)

- Current sequential item: #3 RIDER-01. The selected-driver Cab discovery vertical slice is already implemented in PR #55; this increment reconciles the test plan with the approved customer-selected-driver flow and records provider/device gates. No independent feature-code gap was found in the audited discovery path.
- Automated API coverage in `RideApiIntegrationTests`: `RiderDiscoveryGroupsApprovedUnionsCapsThreeAndOrdersByPickupProximityWithoutQueueDependency`, `RiderDiscoveryExcludesReservedDriversAndLegacyFifoRiderRoutesAreRetired`, and `RiderDiscoveryFailsClosedWhenNearbyRadiusIsMissingOrInvalid`. These cover active approved Cab unions, membership and driver eligibility, maximum three per union, proximity order, current-union selfie approval, response privacy, reserved-driver exclusion, no FIFO dependency, null journey distance and fail-closed radius configuration.
- PR #55 merged at `78ddb9558b453b9171bc020ea83e51087f6e5f2f`; CI [42237572056](https://github.com/rajeevp727/turnova-platform/actions/runs/42237572056) passed all four jobs. Current main baseline after PR #76 merge, [42315870228](https://github.com/rajeevp727/turnova-platform/actions/runs/42315870228), passed 235/235 backend tests, 27/27 shared tests, Rider/Driver typechecks and all four Android/iOS production JavaScript bundle exports.
- Current Rider uses foreground device GPS as the pickup coordinate; the free-text pickup field is a landmark/Driver instruction. Custom pickup pin/geocoding, route distance, and live Call/Chat require configured providers; the UI leaves Call/Chat disabled and journey distance unavailable. Production pickup radius/zone coverage is unapproved and discovery fails closed when the radius is absent/invalid.
- Remaining external gates: approved radius/zone configuration, maps/geocoding/routing provider, Call/Chat provider, live Cosmos multi-instance behavior, and Android/iOS physical-device location permission/resume tests. No provider or credentials are invented.
- Legacy FIFO queue cases are compatibility-only and do not control Rider discovery or customer-facing ranking/assignment. The internal FIFO fairness decision remains unresolved and is not changed here.
- Next sequential item: #4 RIDER-02 — audit direct fare proposal/counterproposal, mutual confirmation and immutable agreement versioning against the API, Rider/Driver UI and tests; implement the smallest missing vertical slice.
- The 19-area score remains 42.1% (0 Verified, 16 In progress, 0 Not started, 3 Blocked; strict 0/19). The denominator is unchanged; AI-01 remains supplemental.


## RIDER-02 — agreement confirmation audit timestamps (2026-11-07)

- Current sequential item: #4 RIDER-02. The direct proposal/counterproposal, immutable version, append-only confirmation, stale-version rejection, mutual-confirmation start gate and in-trip renegotiation/completion gate are already implemented and covered by API integration tests.
- Audit gap found: `RideAgreementConfirmation` persisted actor and UTC timestamp, but `GET /api/v1/rides/{rideId}/agreements` exposed only confirmation booleans and both mobile apps displayed only Confirmed/Pending.
- This increment exposes `riderConfirmedAtUtc` and `driverConfirmedAtUtc` per version, adds API assertions for null timestamps before confirmation and ordered timestamps after both parties confirm, and displays the recorded times in Rider Activity and Driver Trips. Existing agreement versions remain immutable; no fare calculation or payment behavior is added.
- Live Cosmos ETag/multi-instance contention and Android/iOS physical-device negotiation, confirmation and in-trip renegotiation remain external gates. PR #78 code/test head `d0b29b8f60bcdecfc19a72d5aef27b94e6a74360` passed code/test-head CI [42316737841](https://github.com/rajeevp727/turnova-platform/actions/runs/42316737841): 235/235 backend tests, 27/27 shared mobile tests, both app typechecks, four Android/iOS bundle exports and native non-container API publish/artifact verification.
- Next sequential item: #5 RIDER-03 — audit selected-driver booking, cancellation/expiry, idempotency and reservation lifecycle.
- The 19-area weighted score remains 42.1% (0 Verified, 16 In progress, 0 Not started, 3 Blocked; strict 0/19). AI-01 remains supplemental and outside the denominator.


## RIDER-03 — selected-driver booking and reservation audit (2026-11-07)

- Current sequential item: #5 RIDER-03. The selected-driver booking vertical slice is already implemented in PR #57: request fingerprint/idempotency, active approved Union/Driver revalidation, per-driver temporary lease, post-lease eligibility re-read, ambiguous-write recovery, Driver SignalR notification, cancellation/decline/expiry cleanup and reservation promotion only after mutual agreement plus Driver acceptance.
- Existing API tests cover duplicate and mismatched idempotency retries, interrupted-write recovery, concurrent reservations, Union transfer exclusion, cancellation/decline, expiry reconciliation during discovery, reservation release and agreement-gated Driver acceptance. PR #64 CI [42272254259](https://github.com/rajeevp727/turnova-platform/actions/runs/42272254259) executed 189/189 backend tests. Main baseline after PR #78, CI [42317139878](https://github.com/rajeevp727/turnova-platform/actions/runs/42317139878), passed 235/235 backend, 27/27 shared mobile tests, both app typechecks and four Android/iOS production bundle exports.
- No independent feature-code gap was found in this audit. The production setting `Dispatch:SelectedDriverNegotiationLifetimeMinutes` is unset; the booking API returns 503 until the Product Owner approves the expiry lifetime. The 15-minute value in test fixtures is not a product default and must not be copied into production configuration without approval.
- Live Cosmos ETag/multi-instance/restart/ambiguous-write behavior and Android/iOS physical-device booking, cancel, expiry, reconnect and Driver notification tests remain external gates.
- Next sequential item: #6 DISPATCH-01 is Blocked because production pickup radius and zone boundaries require product approval. The checked-in `Dispatch:NearbyRadiusKm=0` intentionally fails closed; do not invent a radius. After recording this blocker, continue to #7 DISPATCH-02 and audit capped/proximity-ordered Rider discovery without FIFO customer ranking.
- The 19-area weighted score remains 42.1% (0 Verified, 16 In progress, 0 Not started, 3 Blocked; strict 0/19). AI-01 remains supplemental and outside the denominator.


## DISPATCH-01 / DISPATCH-02 — radius blocker and Rider ordering audit (2026-11-07)

- #6 DISPATCH-01 remains Blocked. `Dispatch:NearbyRadiusKm=0` is intentionally checked in so discovery fails closed; `RiderDiscoveryFailsClosedWhenNearbyRadiusIsMissingOrInvalid` covers null, zero, negative, NaN and infinity. Production radius and service-zone boundaries require Product Owner approval; live Cosmos query/index and Android/iOS pickup-zone tests remain. No radius is invented.
- Current sequential item #7 DISPATCH-02. The Rider discovery implementation already groups eligible approved Cab drivers by active approved union, caps each union at three, orders by pickup proximity and uses deterministic DriverId tie-break for equal distances. It exposes only current-union-approved selfies and selection-safe response fields. Rider selection does not depend on FIFO membership or queue position and does not auto-assign.
- Automated evidence: `RiderDiscoveryGroupsApprovedUnionsCapsThreeAndOrdersByPickupProximityWithoutQueueDependency` asserts cap, equal-distance tie-break, proximity, selfie approval and privacy; `RiderDiscoveryExcludesReservedDriversAndLegacyFifoRiderRoutesAreRetired` asserts active reservation exclusion and retired customer-facing FIFO route. Main baseline after PR #79, CI [42317507502](https://github.com/rajeevp727/turnova-platform/actions/runs/42317507502), passed 235/235 backend, 27/27 shared mobile, Rider/Driver typechecks, four Android/iOS bundle exports and native non-container API publish.
- The internal FIFO fairness/availability role remains an unresolved product decision. Preserve compatibility behavior without letting FIFO rank or assign Rider-selected drivers.
- Live Cosmos multi-instance and Android/iOS discovery/selection physical-device tests remain external gates.
- Next sequential item: #8 DISPATCH-03 — audit reservation expiry, lifecycle reconciliation, cancellation/decline/expiry release, Cosmos restart/ETag/outbox recovery and ensure no automatic next-driver advancement.
- The 19-area weighted score remains 42.1% (0 Verified, 16 In progress, 0 Not started, 3 Blocked; strict 0/19). AI-01 remains supplemental and outside the denominator.
