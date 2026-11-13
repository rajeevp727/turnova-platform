# TurnOva Project Brain

> Compact working context for future development sessions. Read this before coding; update it in the same feature PR whenever project state changes.
> Full requirements remain in `PRD.md`, `docs/architecture.md`, and `docs/test-plan.md`.

**Last verified:** 2026-11-13  
**Repository:** https://github.com/rajeevp727/turnova-platform  
**Main HEAD:** `b6564c7dc2959883c2910f5b6f13134f7052566f` after PR #157 merged. PR #155 merged at `4817351616d6f0d7f0c9a51a0863cb92ce4caea4`; PR #156 at `1fc57d0d32cc04e33825dc694321dbc6719466ad`; PR #157 at `b6564c7dc2959883c2910f5b6f13134f7052566f`. Sole active feature PR [#158](https://github.com/rajeevp727/turnova-platform/pull/158), branch `fix/platform-01-storage-readiness-failure-mapping`. Exact-head CI [#1513 / run 43431035440](https://github.com/rajeevp727/turnova-platform/actions/runs/43431035440) passed **12/12 on attempt 2** at `89dc76f483ffa9a27dda1b67c5eaf50d78c362da`: backend 570/570, shared mobile/auth 179/179, Python 47/47, Rider/Driver validation and bundles, native API publish, both Android debug/release APKs, both unsigned iOS Simulator builds and all four same-run smoke tests. Rider Android passed on failed-job-only retry; all four smoke artifacts were inspected: Rider Android 14045725928, Driver Android 14041925228, Rider iOS 14043137536, Driver iOS 14041887826. Unsigned iOS, production API/authentication, live Cosmos and physical-device limitations remain explicit.
**Strict acceptance:** 0/19 (15 In Progress, 4 Blocked). This is a complete-evidence count, not an implementation-completion score. Production readiness is not established.

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

## Historical implementation milestones (merged)
- PR #55 merged sequential #3 RIDER-01 / CAB-DISCOVERY-01 at `78ddb9558b453b9171bc020ea83e51087f6e5f2f`; final-head CI run `42237572056` passed all four jobs. It adds union-grouped Cab discovery, eligibility filtering, pickup-proximity ordering, approved-photo privacy and Rider local selection.
- PR #56 merged sequential #4 RIDER-02 / CAB-AGREEMENT-01 at `9d39c83d13f619bcc7a5ec575669a4167b3cc5f8`; final-head CI run `42245873224` passed all four jobs. It adds immutable agreement term versions, append-only confirmations, ETag/revision concurrency protection, Rider/Driver proposal and confirmation controls, SignalR agreement events, and start/completion gates.
- PR #57 merged sequential #5 RIDER-03 at `c5ff07843365e949b70fe1d4b9753bcd88022e10`; final synchronized PR head `8c846db3c93cfbe6f1eb574c838eb3ab0bf4fb9e` passed CI run [42254924983](https://github.com/rajeevp727/turnova-platform/actions/runs/42254924983), all four jobs green. It adds durable selected-driver bookings, route/Union snapshots, idempotent create-if-absent persistence, per-driver temporary/permanent reservation lifecycle, safe expiry/cancel/decline/completion release, Union-transfer exclusion and Driver reconnect recovery. Automated reviews [6689967817](https://github.com/rajeevp727/turnova-platform/pull/57#issuecomment-6689967817) and [6690099440](https://github.com/rajeevp727/turnova-platform/pull/57#issuecomment-6690099440) found no unresolved critical/high-risk issue.
- Sequential #6 DISPATCH-01 is implemented in PR #55 and its independent regression work merged in PR #58 at `a3a5835c66ec5f77f4b7505b4d99d5a43de18403`; final head `73db39c8cbdf79da99181688f7540e63c88cfc32` passed CI run [42258555372](https://github.com/rajeevp727/turnova-platform/actions/runs/42258555372), all four jobs. Automated review comment [6690682769](https://github.com/rajeevp727/turnova-platform/pull/58#issuecomment-6690682769) found no unresolved critical/high-risk issue. Production radius/zone approval, live Cosmos query/index and Android/iOS pickup-zone acceptance remain blockers.
- Sequential #7 DISPATCH-02 is implemented in PR #55/RIDER-03: union grouping, up-to-three cap, pickup-distance then DriverId ordering, explicit Rider selection and no FIFO dependency. PR #59 merged at `508a789246cec1a9452651c62fa08aa3908140a4`; final-head CI run `42259580145` passed all four jobs.
- PR #61 merged sequential #9 DISPATCH-04 regression coverage at `118929955d3959f078ddb59f7f5bdf3c3965d52a`; automated review found no unresolved critical/high-risk issue. PR #64 final-head CI [42272254259](https://github.com/rajeevp727/turnova-platform/actions/runs/42272254259) executed 189/189 backend tests, including this regression.

- PR #66 merged sequential #11 DRIVER-01 at `26dafc87451ba49ea4734df81c94f9e75790800d`; final-head CI [42278673796](https://github.com/rajeevp727/turnova-platform/actions/runs/42278673796) executed 191/191 backend tests and passed all four jobs. Identity-provider claim mapping, private document storage/retention and Android/iOS onboarding evidence remain.
- PR #85 merged sequential #12 DRIVER-02 at `c9376d5f1ee9055878eeac8a44667ae1402bca73`; it removes the unapproved 120-second production GPS freshness default and fails discovery/selected booking closed when the positive approved age is missing or invalid. Final-head CI [42320526153](https://github.com/rajeevp727/turnova-platform/actions/runs/42320526153) passed 242/242 backend, 30/30 shared mobile tests, both app typechecks, four Android/iOS bundle exports and native non-container API publish. Automated review [6699933644](https://github.com/rajeevp727/turnova-platform/pull/85#issuecomment-6699933644) found no unresolved critical/high-risk issue.
- PR #86 merged sequential #13 TRIP-01 at `64c84dcf93dc38d7c53065d519bf22db69933a24`; it adds `SelectedCabLifecycleRetriesAreIdempotentAndKeepTerminalState` for idempotent arrival/start/completion retries, a single completion activity, terminal Rider history and post-completion guards. Final-head CI [42321792261](https://github.com/rajeevp727/turnova-platform/actions/runs/42321792261) passed 243/243 backend tests, 30/30 shared mobile tests, both app typechecks, four Android/iOS bundle exports and native non-container API publish. Automated review [6700051223](https://github.com/rajeevp727/turnova-platform/pull/86#issuecomment-6700051223) found no unresolved critical/high-risk issue.
- PR #87 merged DRIVER-03 India-local payslip month selection at `76057ee852e5726c6fd412d3fafca027069387c9`; final-head CI [42323626642](https://github.com/rajeevp727/turnova-platform/actions/runs/42323626642) passed 243/243 backend tests, 34/34 shared mobile tests, Rider/Driver typechecks, four Android/iOS JS bundle exports and native non-container API publish.
- PR #88 merged RIDER-04 original-Union reconciliation retention at `0e1865fdbe46138ddd05202a1c8a84221a5de237`; final-head CI [42327178578](https://github.com/rajeevp727/turnova-platform/actions/runs/42327178578) passed 244/244 backend tests and all four jobs.
- PR #89 merged PLATFORM-01 append-only ride audit events at `818c624e139f6c77c674f6466aee352e79701726`; final-head CI [42328297135](https://github.com/rajeevp727/turnova-platform/actions/runs/42328297135) passed 246/246 backend tests, 34/34 shared mobile tests and all four jobs. Automated review [6700893842](https://github.com/rajeevp727/turnova-platform/pull/89#issuecomment-6700893842) found no unresolved critical/high-risk issue.

## Historical sequential handoff (superseded by latest handoff below)

1. **#6 DISPATCH-01 — Blocked:** PR #55 implementation and PR #58 regression coverage are independent of the missing decision. Resume final verification only after product approval of the production pickup radius and zone boundaries, plus live Cosmos and Android/iOS pickup-zone evidence.
2. **#7 DISPATCH-02 — Implementation audit recorded:** PR #55/RIDER-03 implement the per-union cap, pickup-proximity ordering, explicit selection and no-FIFO Rider path; PR #59 adds an equal-distance deterministic tie-break regression. Live Cosmos/device evidence and the internal FIFO role decision remain, so the acceptance row stays In progress.
3. **#8 DISPATCH-03 / #9 DISPATCH-04:** selected negotiation expiry/idempotency and no-overlap protections are implemented and covered by in-memory/API tests, including PR #61's accepted-active-ride Union-transfer guard. Live Cosmos restart/ETag contention remains external.
4. **#10 REALTIME-01 — Code audit recorded:** PR #63 fixes Rider reconnect state restoration. PR #64 final-head CI [42272254259](https://github.com/rajeevp727/turnova-platform/actions/runs/42272254259) executed 189/189 backend tests, including hub ownership tests; physical Android/iOS WebSocket/reconnect acceptance remains outstanding.
5. **#11 DRIVER-01 — In progress:** PR #66 merged union-scoped selfie approval and Driver membership display. Identity-provider claim mapping, private document storage/scanning/consent/retention and Android/iOS onboarding evidence remain.
6. **#12 DRIVER-02 — In progress:** PR #67 merged the active Cab Union/document gate, reservation-safe Online/Offline and zone-exit transitions, and foreground availability-GPS refresh. PR #85 removed the implicit 120-second freshness policy and added missing/non-positive configuration fail-closed tests. Product Owner must approve a positive `Dispatch:NearbyLocationMaxAgeSeconds`; live Cosmos and Android/iOS permission/background/resume evidence remain.
7. **#13 TRIP-01 — In progress:** PR #68 and PR #86 merged selected-Cab lifecycle, assigned-Driver-only location and idempotent retry coverage. Final PR #86 head `e48888c626ee0ea14007c9d87dcbc3529f2539d1` passed CI [42321792261](https://github.com/rajeevp727/turnova-platform/actions/runs/42321792261): 243/243 backend tests, 30/30 shared mobile tests and all four jobs. Live Cosmos history/ETag and Android/iOS SignalR/map/permission/background-resume acceptance remain. Product Owner must decide whether a Driver may accept another ride before reporting the preceding completed trip and whether a non-zero reported delta affects eligibility; current code releases the active-ride lease at Completed and has no delta-based gate.
8. **#14 DRIVER-03 — In progress:** PR #87 merged at `76057ee852e5726c6fd412d3fafca027069387c9`; final PR head `b0cce388daabb9c0e5dd2a3f42f6752048273ca0` passed CI [42323626642](https://github.com/rajeevp727/turnova-platform/actions/runs/42323626642): 243/243 backend, 34/34 shared mobile tests, Rider/Driver typechecks, four Android/iOS production JavaScript bundle exports and native non-container API publish; all four jobs passed. Android/iOS PDF save/share/accessibility and live Cosmos snapshot/recovery plus work-activity crash-window reconciliation remain.
9. **#15 DRIVER-04 — Blocked:** Driver support categories, escalation owner and operational process are not approved. Do not invent them.
10. **#16 RIDER-04 — In progress:** PR #70 implements immutable Driver-reported payment reconciliation after completed Cab rides, signed delta, Driver history and claim-scoped UnionManager oversight. PR #88 merged original-Union retention after Driver transfer at `0e1865fdbe46138ddd05202a1c8a84221a5de237`; final-head CI [42327178578](https://github.com/rajeevp727/turnova-platform/actions/runs/42327178578) passed 244/244 backend tests and all four jobs. Live Cosmos ETag/restart and Android/iOS report-entry/history gates remain. Customer dispute/confirmation, non-zero-delta next-ride and cross-Union preceding-ride visibility decisions remain unresolved.
11. **#17 SUPPORT-01 — Blocked:** Rider support categories, escalation owner and operating process remain unapproved.
12. **#18 PLATFORM-01 — In progress:** PR #89 merged append-only ride audit events in the existing Rides aggregate at `818c624e139f6c77c674f6466aee352e79701726`; final-head CI [42328297135](https://github.com/rajeevp727/turnova-platform/actions/runs/42328297135) passed 246/246 backend tests and all four jobs. Automated review [6700893842](https://github.com/rajeevp727/turnova-platform/pull/89#issuecomment-6700893842) found no unresolved critical/high-risk issue. Live Cosmos ETag/restart/retention, telemetry/alerts, production origin and load gates remain.
13. **#19 RELEASE-01 — In progress:** PR #90 merged native Android debug APK and unsigned iOS Simulator compilation for Rider/Driver at `4608c4c05dd5d359afc8fc6ae1bead6d7e3aa3ba`; final-head CI [42332828805](https://github.com/rajeevp727/turnova-platform/actions/runs/42332828805) passed all eight jobs and uploaded both APKs and both simulator apps. Production signing, final store identifiers and physical-device functional/security/pilot acceptance remain.
14. **Current sequential #3 RIDER-01:** PR #92 completed AUTH-02 at `baa4a17140333d04a26efe4babdd92c35e81de3e`; final-head CI [42341545410](https://github.com/rajeevp727/turnova-platform/actions/runs/42341545410) passed all eight jobs, including 248/248 backend and 40/40 shared mobile tests plus Rider/Driver Android/iOS native builds. The current increment tests the foreground-only pickup permission/current-coordinate gate. #15 DRIVER-04 and #17 SUPPORT-01 remain blocked by unapproved support categories/process/owner. AI-01 and CAB-RECON-01 remain supplemental outside the 19-row denominator.

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
- Historical weighted-status scoring is retired and is not used as an engineering-completion or acceptance measure.


## DRIVER-02 availability/location increment (2026-11-07)

- PR #67 is the current sequential #12 increment. It serializes Online/Offline transitions and zone-exit mutations with the same per-Driver reservation lease used by selected bookings and Union transfers.
- Going Online requires an approved Cab profile, active membership in an Active Cab-serving Union covering the assigned zone, approved driving-licence and vehicle-registration documents, and valid GPS inside the assigned zone.
- The Driver app tracks location only in the foreground while Online and not in Negotiating, AgreementConfirmed or an active ride. It stops the watcher in background, refreshes GPS and restarts the watcher on resume, and attempts to go Offline if foreground permission is revoked.
- Automated API tests cover missing Union/documents and availability/location immutability during selected negotiation. PR #67 final head `a371ae9932e998ee9daaea6fd2a8fdff6e3615c1` passed CI [42286508190](https://github.com/rajeevp727/turnova-platform/actions/runs/42286508190), all four jobs and 193/193 backend tests; native API publish, both Expo typechecks and six shared mobile tests passed. Android/iOS physical-device permission/background/resume and live Cosmos ETag/race validation remain external gates.
- Existing FIFO queue code remains compatibility-only for this Rider-selected path. No customer-facing FIFO ranking or automatic assignment is added; the internal FIFO role remains unresolved.


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
- Historical reconciliation remains attached to the Union recorded on the ride. The new regression test transfers the Driver after report submission and proves the original UnionManager retains visibility and Driver history keeps the original Union linkage. Whether a new Union receives a minimum preceding-ride reconciliation summary for next-ride eligibility remains a product decision; no cross-Union disclosure policy is inferred.


## PLATFORM-01 bounded storage readiness (2026-11-07)

- PR #70 merged at `17c489eac64dc0f99e881a1474147b2f79fe2aac`; final-head CI [42295994210](https://github.com/rajeevp727/turnova-platform/actions/runs/42295994210) passed 210/210 backend tests and all four CI jobs. CAB-RECON-01 remains supplemental and outside the 19-area denominator.
- PR #71 completed the bounded readiness sub-increment; current sequential #18 work is the append-only ride audit trail described below. #17 SUPPORT-01 remains blocked by unapproved Rider support categories, escalation owner and process.
- PR [#71](https://github.com/rajeevp727/turnova-platform/pull/71) adds a three-second bounded storage readiness probe. Liveness stays independent; dependency failure/probe timeout returns generic 503; caller cancellation is propagated; diagnostics are not exposed in response/logs. Final-head CI [42301719963](https://github.com/rajeevp727/turnova-platform/actions/runs/42301719963) passed 214/214 backend tests, 9/9 shared mobile tests, Rider/Driver typechecks and the native non-container API publish artifact. Native signed builds and physical-device acceptance remain blocked.
- Tests use a deterministic failing probe and a cancellable delayed probe. They do not claim live Cosmos validation.


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
- PR #72 and PR #73 remain merged; PR #90 merged native Android/iOS compilation at `4608c4c05dd5d359afc8fc6ae1bead6d7e3aa3ba`, final-head CI [42332828805](https://github.com/rajeevp727/turnova-platform/actions/runs/42332828805) passed all eight jobs and uploaded Rider/Driver debug APKs and unsigned iOS Simulator apps. Production signed AAB/IPA, physical-device acceptance and live provider validation remain separate release gates.



## AUTH-01 — registration, profile read and app-role isolation (2026-11-07)

- Current sequential area: #1 AUTH-01. PR #74 added fake-TriSend duplicate Rider registration HTTP 409 and authenticated `GET /api/v1/auth/me` profile-read integration tests.
- PR #91 adds a shared `requireAppRole` guard used by both Rider and Driver apps before a new token pair is persisted or authenticated profile state is set; restored `/auth/me` profiles are also checked against the app's expected role.
- Shared mobile tests cover valid Rider/Driver profiles, cross-app role mismatch, missing profile/role and unsupported app role. API integration tests add Rider registration server-owned role enforcement and Rider login with a Driver account returning 403 while revoking the returned session.
- Refresh responses that rotate tokens without a profile no longer clear the current in-memory user; startup restore after a token-only refresh remains in the next AUTH-02 audit.
- The PRD says profile management but does not define editable fields or an upstream profile-update endpoint. Do not invent a TriSend route or field contract. Profile editing remains unresolved until an approved provider contract exists.
- PR #91 final-head CI and automated review are pending. Physical Android/iOS registration/login/profile-read acceptance remains outstanding. Next ordered area after AUTH-01 is #2 AUTH-02 token lifecycle.


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


## RIDER-02 — agreement confirmation audit timestamps (2026-11-07)

- Current sequential item: #4 RIDER-02. The direct proposal/counterproposal, immutable version, append-only confirmation, stale-version rejection, mutual-confirmation start gate and in-trip renegotiation/completion gate are already implemented and covered by API integration tests.
- Audit gap found: `RideAgreementConfirmation` persisted actor and UTC timestamp, but `GET /api/v1/rides/{rideId}/agreements` exposed only confirmation booleans and both mobile apps displayed only Confirmed/Pending.
- This increment exposes `riderConfirmedAtUtc` and `driverConfirmedAtUtc` per version, adds API assertions for null timestamps before confirmation and ordered timestamps after both parties confirm, and displays the recorded times in Rider Activity and Driver Trips. Existing agreement versions remain immutable; no fare calculation or payment behavior is added.
- Live Cosmos ETag/multi-instance contention and Android/iOS physical-device negotiation, confirmation and in-trip renegotiation remain external gates. PR #78 code/test head `d0b29b8f60bcdecfc19a72d5aef27b94e6a74360` passed code/test-head CI [42316737841](https://github.com/rajeevp727/turnova-platform/actions/runs/42316737841): 235/235 backend tests, 27/27 shared mobile tests, both app typechecks, four Android/iOS bundle exports and native non-container API publish/artifact verification.
- Next sequential item: #5 RIDER-03 — audit selected-driver booking, cancellation/expiry, idempotency and reservation lifecycle.


## RIDER-03 — selected-driver booking and reservation audit (2026-11-07)

- Current sequential item: #5 RIDER-03. The selected-driver booking vertical slice is already implemented in PR #57: request fingerprint/idempotency, active approved Union/Driver revalidation, per-driver temporary lease, post-lease eligibility re-read, ambiguous-write recovery, Driver SignalR notification, cancellation/decline/expiry cleanup and reservation promotion only after mutual agreement plus Driver acceptance.
- Existing API tests cover duplicate and mismatched idempotency retries, interrupted-write recovery, concurrent reservations, Union transfer exclusion, cancellation/decline, expiry reconciliation during discovery, reservation release and agreement-gated Driver acceptance. PR #64 CI [42272254259](https://github.com/rajeevp727/turnova-platform/actions/runs/42272254259) executed 189/189 backend tests. Main baseline after PR #78, CI [42317139878](https://github.com/rajeevp727/turnova-platform/actions/runs/42317139878), passed 235/235 backend, 27/27 shared mobile tests, both app typechecks and four Android/iOS production bundle exports.
- No independent feature-code gap was found in this audit. The production setting `Dispatch:SelectedDriverNegotiationLifetimeMinutes` is unset; the booking API returns 503 until the Product Owner approves the expiry lifetime. The 15-minute value in test fixtures is not a product default and must not be copied into production configuration without approval.
- Live Cosmos ETag/multi-instance/restart/ambiguous-write behavior and Android/iOS physical-device booking, cancel, expiry, reconnect and Driver notification tests remain external gates.
- Next sequential item: #6 DISPATCH-01 is Blocked because production pickup radius and zone boundaries require product approval. The checked-in `Dispatch:NearbyRadiusKm=0` intentionally fails closed; do not invent a radius. After recording this blocker, continue to #7 DISPATCH-02 and audit capped/proximity-ordered Rider discovery without FIFO customer ranking.


## DISPATCH-01 / DISPATCH-02 — radius blocker and Rider ordering audit (2026-11-07)

- #6 DISPATCH-01 remains Blocked. `Dispatch:NearbyRadiusKm=0` is intentionally checked in so discovery fails closed; `RiderDiscoveryFailsClosedWhenNearbyRadiusIsMissingOrInvalid` covers null, zero, negative, NaN and infinity. Production radius and service-zone boundaries require Product Owner approval; live Cosmos query/index and Android/iOS pickup-zone tests remain. No radius is invented.
- Current sequential item #7 DISPATCH-02. The Rider discovery implementation already groups eligible approved Cab drivers by active approved union, caps each union at three, orders by pickup proximity and uses deterministic DriverId tie-break for equal distances. It exposes only current-union-approved selfies and selection-safe response fields. Rider selection does not depend on FIFO membership or queue position and does not auto-assign.
- Automated evidence: `RiderDiscoveryGroupsApprovedUnionsCapsThreeAndOrdersByPickupProximityWithoutQueueDependency` asserts cap, equal-distance tie-break, proximity, selfie approval and privacy; `RiderDiscoveryExcludesReservedDriversAndLegacyFifoRiderRoutesAreRetired` asserts active reservation exclusion and retired customer-facing FIFO route. Main baseline after PR #79, CI [42317507502](https://github.com/rajeevp727/turnova-platform/actions/runs/42317507502), passed 235/235 backend, 27/27 shared mobile, Rider/Driver typechecks, four Android/iOS bundle exports and native non-container API publish.
- The internal FIFO fairness/availability role remains an unresolved product decision. Preserve compatibility behavior without letting FIFO rank or assign Rider-selected drivers.
- Live Cosmos multi-instance and Android/iOS discovery/selection physical-device tests remain external gates.
- Next sequential item: #8 DISPATCH-03 — audit reservation expiry, lifecycle reconciliation, cancellation/decline/expiry release, Cosmos restart/ETag/outbox recovery and ensure no automatic next-driver advancement.


## DISPATCH-03 — selected-driver reservation expiry audit (2026-11-07)

- Current sequential item: #8 DISPATCH-03. The selected-booking lifecycle already uses conditional, server-owned expiry; no independent feature-code gap was found in this audit.
- `FindRideAsync` lazily expires only Negotiating/AgreementConfirmed rides after the configured deadline. `TryTransitionAsync` uses the shared ride replacement guard: in-memory compare-and-swap via `ConcurrentDictionary.TryUpdate`, Cosmos snapshot comparison followed by `IfMatchEtag` replacement. A successful expiry transition releases only the exact Driver/ride reservation and publishes a ride status event.
- `HasActiveDriverRideAsync` reconciles terminal linked rides and expired orphan leases before permitting another booking. Cosmos reservation store uses per-driver partitioning, create-if-absent acquisition, ETag-guarded mutation/deletion and deliberately returns expired leases to the API instead of stealing them. The in-memory store uses atomic compare/update/remove.
- Tests cover `ExpiredSelectedNegotiationBecomesTerminalAndReleasesDriverReservation`, `DiscoveryExpiresAnExpiredNegotiationBeforeReleasingItsDriverReservation`, `DiscoveryReleasesPermanentReservationWhenRideIsAlreadyTerminal`, `SelectedBookingReservationPreventsConcurrentBookingsAndRiderCancelReleasesIt`, `DriverDecliningSelectedBookingCancelsRideAndReleasesReservation`, `ExpiredTemporaryReservationRemainsHeldUntilRideLifecycleReconciliation` and `ReleaseOnlyRemovesMatchingRide`.
- Selected booking does not invoke `IRideOfferAdvancer`. The hosted FIFO expiry processor is isolated to the legacy offer/queue stores; it is not the selected-booking expiry path and must not advance a Rider-selected Driver.
- PR #80 final-head CI [42317693913](https://github.com/rajeevp727/turnova-platform/actions/runs/42317693913) passed all four jobs: 235/235 backend tests, 27/27 shared mobile tests, Rider/Driver typechecks, four Android/iOS production JavaScript bundle exports and native non-container API publish. Automated review [6699538432](https://github.com/rajeevp727/turnova-platform/pull/80#issuecomment-6699538432) found no unresolved critical/high-risk issue.
- External gates: live Cosmos multi-instance/ETag/restart and ambiguous-write recovery; Android/iOS expiry/cancel/reconnect physical-device tests; and Product Owner approval of `Dispatch:SelectedDriverNegotiationLifetimeMinutes`. The setting remains unset and booking fails closed with 503; the test-only 15-minute lifetime is not a product default.
- Next sequential item: #9 DISPATCH-04 — audit exclusive reservation behavior across concurrent selected negotiations, accepted/in-progress rides, availability mutations and Union transfers, including live Cosmos conditional-write validation.


## DISPATCH-04 — exclusive per-Driver reservation audit (2026-11-07)

- Current sequential item: #9 DISPATCH-04. The API uses one `IDriverRideReservationStore` per Driver across selected booking, availability changes, zone-exit transitions and Union transfer. Selected bookings acquire a temporary lease and promote it to permanent only after mutual agreement plus Driver acceptance; terminal ride transitions release the matching reservation.
- Availability and Union transfer first reconcile any existing ride lease, then acquire the same per-Driver lease and re-read the authoritative profile/membership before mutation. If booking wins the lease, these operations conflict; if a technical mutation wins, booking fails its post-lease eligibility re-read or lease acquisition. An accepted ride's permanent lease blocks another booking and Offline transition.
- Added API-level concurrency regressions: `ConcurrentSelectedBookingAndDriverOfflineTransitionCannotBothSucceed`, `ConcurrentSelectedBookingAndUnionTransferCannotBothSucceed`, and `AcceptedSelectedRideBlocksAnotherBookingAndDriverAvailabilityChange`. Existing coverage includes `AvailabilityAndLocationRemainUnchangedDuringSelectedDriverNegotiation`, `UnionTransferIsBlockedWhileDriverHasSelectedNegotiation`, `UnionTransferIsBlockedWhileDriverHasAcceptedActiveRide`, and in-memory store atomic acquisition/release tests.
- PR #81 final-head CI [42318343530](https://github.com/rajeevp727/turnova-platform/actions/runs/42318343530) passed all four jobs: 235/235 backend tests, 27/27 shared tests, both app typechecks, four Android/iOS bundle exports and native non-container API publish. PR #82 adds three API concurrency tests; its final-head CI must pass before merge.
- Live Cosmos multi-instance create-if-absent contention, ETag races, process-restart/crash recovery and Android/iOS concurrent selection/availability/Union-transfer physical-device tests remain external gates. The production negotiation lifetime is still unset pending Product Owner approval.
- Next sequential item: #10 REALTIME-01 — audit live negotiation/agreement/ride status delivery, reconnect restoration and foreground/background recovery across Rider and Driver apps.


## REALTIME-01 — resilient SignalR reconnect audit (2026-11-07)

- Current sequential item: #10 REALTIME-01. Server events include `RideSelected` for the selected Driver notification group, `RideStatusChanged` for ride lifecycle transitions, `RideAgreementChanged` for proposal/confirmation and `DriverLocationUpdated` for assigned Driver location.
- `RideTrackingHub.cs` restricts Rider group membership to the ride owner, Driver ride-group membership to the assigned Driver and Driver notification membership to the authenticated Driver's own group. UnionManager/other roles are denied.
- Rider Activity starts a ride connection and reloads owner-scoped ride history plus agreement versions on initial connect/reconnect. Driver selected-booking notification reconnect reloads available requests and own ride history; Driver ride/agreement connections reload authoritative status/agreement. Driver foreground-resume location logic reloads authoritative history before sharing resumes.
- Audit found two reconnect gaps: default `withAutomaticReconnect()` has a finite retry schedule, and a failed initial `connection.start()` is not covered by automatic reconnect. PR #83 adds `src/mobile/shared/signalRConnection.js` with bounded exponential retry delays (1s, 2s, 4s, ... capped at 30s) and a cancellable initial-start retry loop. All Rider and Driver ride, agreement and selected-booking notification connections use the shared policy. Retries stop when the owning effect is disposed.
- New `signalRConnection.test.cjs` tests retry sequence/cap, invalid counts, initial start retry and cancellation. Existing `rideRecovery.test.cjs`, `tripLocationSharing.test.cjs` and `RideTrackingHubTests` cover role-specific state recovery, terminal clearing, foreground-only location and group authorization.
- PR #82 final-head CI [42318889871](https://github.com/rajeevp727/turnova-platform/actions/runs/42318889871) passed all four jobs: 238/238 backend tests, 27/27 shared mobile tests, both app typechecks, four Android/iOS bundle exports and native non-container API publish. PR #83 final-head CI must validate the new helper and both app imports before merge.
- Remaining external gates: live SignalR WebSocket delivery and long-outage reconnect; device-to-device agreement/status/location updates; Android/iOS background/resume, permission and token-refresh behavior. No physical-device or live WebSocket validation is claimed.
- Next sequential item: #11 DRIVER-01 — audit Cab-only Driver onboarding, approved identity/licence/vehicle documents, Union membership, selfie approval and transfer re-review across API/mobile.


## DRIVER-01 — Cab onboarding and Union approval audit (2026-11-07)

- Current sequential item: #11 DRIVER-01. Driver onboarding accepts Cab only, requires a valid JPEG selfie and opaque private document references, and creates a Pending profile. Driver eligibility requires operations approval, approved DrivingLicence and VehicleRegistration documents, active Cab Union assignment, pickup-zone compatibility and current-Union selfie approval.
- Operations-admin endpoints create/activate unions, approve/reject/suspend Driver profiles, assign/transfer Cab Drivers and retain assignment audit history. Current behavior treats operations-admin creation as Union activation; no separate Pending-union state exists. A distinct partner-contract approval lifecycle is a Product/Legal decision and is not inferred.
- UnionManager access requires signed `role=UnionManager` and matching signed `unionId` claims. The API does not trust caller-supplied union IDs. Member listing omits phone/email, exact location, document storage references, Rider identity and private chat; it exposes document statuses and the selfie needed for the current Union to review. Only the current active Union can approve public selfie visibility. Operations may reject/hide but cannot approve.
- Selfie review acquires the shared per-Driver reservation lease, re-reads assignment/profile/documents while holding it, and records audit. Union transfer is serialized, resets selfie approval and hides the photo from Rider discovery until the new Union approves it.
- Automated tests: `DriverMustSubmitOnboardingAndBeApprovedBeforeJoiningQueue`, `DriverCannotGoOnlineWithoutActiveCabUnionAndApprovedDocuments`, `OperationsCanCreateUnionAssignDriverTransferAndReadAuditHistory`, `UnionAssignmentRejectsPendingAndZoneIncompatibleDrivers`, `CabOnlyScopeRejectsNonCabNewWorkflowsAndPreservesLegacyProfiles`, `UnionManagerCanReviewOnlyCurrentAssignedCabDriverSelfiesAndReceivesMinimalData`, `UnionTransferResetsSelfieApprovalUntilTheNewUnionApprovesIt`, and `DriverOnboardingRejectsNonCabWithoutCreatingOrOverwritingProfile`.
- PR #83 final-head CI [42319522116](https://github.com/rajeevp727/turnova-platform/actions/runs/42319522116) passed all four jobs: 238/238 backend tests, 30/30 shared tests, Rider/Driver typechecks, four Android/iOS bundle exports and native non-container API publish.
- External gates: actual private document upload/object storage and access controls, malware/content scanning, consent/retention policy, signed UnionManager/unionId identity-provider claim issuance, legal partnership approval, Android/iOS camera/document permission and onboarding physical-device tests. No live provider/device/legal validation is claimed.
- Next sequential item: #12 DRIVER-02 — audit Cab Driver availability/location eligibility, reservation-aware Online/Offline transitions, pickup-zone enforcement and selected-driver negotiation entry.


## DRIVER-02 — availability and location freshness policy audit (2026-11-07)

- Current sequential item: #12 DRIVER-02. Online eligibility requires approved Cab Driver profile, active Cab Union assignment, approved DrivingLicence/VehicleRegistration, valid GPS and assigned-zone match. Online/Offline and zone-exit transitions share the same per-Driver lease as selected booking and Union transfer.
- Driver app sends availability location while Online and foregrounded (15-second/50-metre watcher); backgrounding stops the watcher, and resume rechecks permission/current GPS before restarting. Negotiating/AgreementConfirmed/active ride pauses the availability watcher; active-trip location has a separate assigned-Driver-only foreground watcher. Zone exit takes the Driver Offline and clears coordinates.
- Audit found an unapproved product default: production `appsettings.json` set `Dispatch:NearbyLocationMaxAgeSeconds=120`, while PRD/business instructions do not specify a freshness threshold. PR #85 removes that default and the implicit 30–600 second clamp. Rider discovery and selected booking now fail closed with 503 when the setting is missing or non-positive. A positive value must be explicitly approved/configured; the 120-second value in `TurnOvaApiFactory` is test-only.
- Added `RiderDiscoveryFailsClosedWhenLocationFreshnessPolicyIsNotConfigured`, `RiderDiscoveryFailsClosedWhenLocationFreshnessPolicyIsNonPositive` and `SelectedBookingFailsClosedWhenLocationFreshnessPolicyIsNotConfigured`. Existing coverage verifies Cab/Union/document gating, valid coordinates, zone exit, foreground resume and reservation-aware availability changes.
- External decision: Product Owner must approve the maximum age of an availability GPS update. Do not infer the test fixture's 120 seconds as a product recommendation. Internal FIFO fairness/availability role remains unresolved; Rider discovery stays queue-independent.
- PR #84 final-head CI [42319967469](https://github.com/rajeevp727/turnova-platform/actions/runs/42319967469) passed 238/238 backend tests, 30/30 shared mobile tests, both app typechecks, four Android/iOS bundle exports and native non-container API publish. PR #85 final-head CI must pass before merge.
- Next sequential item: #13 TRIP-01 — audit mutual-agreement start, legal server-side transitions, assigned-Driver-only location sharing, Rider live progress/history and terminal cleanup.


## PLATFORM-01 ride audit trail increment (2026-11-07)

- The existing `DemoRide` aggregate now contains an optional `AuditEvents` list. Existing documents without the property remain readable; no new container or migration is required.
- `RideAuditEvent` records cover selected ride creation, agreement proposal, each party confirmation, lifecycle status changes and Driver-reported/unverified payment reporting. Each event records ride/Union linkage, actor ID/role, UTC timestamp, previous/new state and agreement/amount fields only where relevant.
- Creation events are written with the initial ride document; subsequent events are appended in the same `TryReplaceRideAsync` operation as the mutation. The existing Rides partition and ETag remain authoritative; snapshot comparison includes events and an append-only prefix guard prevents old events from being removed, reordered or rewritten.
- Event payloads omit route/stops, precise coordinates, contact data, KYC/document references, tokens and private chat. No general-purpose admin audit endpoint or cross-Union preceding-ride disclosure policy is added.
- Integration tests verify event order and actors across the selected Cab lifecycle, cancellation, Driver decline, System expiry, agreement confirmation retries, payment report retries and non-audit location updates. Live Cosmos ETag/multi-instance/restart/retention remains unverified.


## AUTH-02 — authoritative profile restore after token rotation (2026-11-07)

- Main after PR #91 is `2e5aa128794378c30819d347c40f6e4239cb2815`; AUTH-01 role-isolation final-head CI [42335030964](https://github.com/rajeevp727/turnova-platform/actions/runs/42335030964) passed all eight jobs: 248/248 backend tests, 38/38 shared mobile tests, Rider/Driver typechecks and bundles, both Android debug APKs, both unsigned iOS Simulator apps and native non-container API publish.
- AUTH-02 audit found that startup restore called `/api/v1/auth/me`, refreshed after a 401, persisted the rotated token pair, then returned `result.user` from the refresh response. That profile is optional; when TriSend returns only tokens, the app-role guard rejects the missing profile and the session cannot be restored.
- The fix always calls `/api/v1/auth/me` with the newly rotated access token after securely persisting the new pair. The refreshed profile is authoritative; an optional profile in the refresh payload is not trusted as a substitute.
- A transient post-rotation `/me` failure preserves the newly rotated pair and surfaces a recoverable error. If the new access token is rejected with 401, both stored keys are cleared and no authenticated session is restored.
- Regression tests cover token-only refresh, new-token profile lookup, transient profile failure with rotated-pair retention, and rejection/clearing after refresh. The PR conversation records automated review and the final-head CI merge gate; no local test/build result is claimed.
- Live TriSend refresh-token rotation/reuse behavior and Android/iOS SecureStore/runtime restart/expiry/logout acceptance remain external gates. The 19-area denominator and.


## RIDER-01 — foreground pickup permission and coordinate guard (2026-11-07)

- Audit confirmed the existing Rider flow uses foreground device GPS as the discovery pickup coordinate; editable pickup/drop-off text remains a landmark or Driver instruction, not a geocoded pin. No background permission or last-known location fallback is part of discovery.
- Test gap found: the app requested foreground permission before reading GPS and calling `/api/v1/rider/discovery`, but this critical mobile gate had no deterministic shared test. The new `foregroundPickupLocation` helper centralizes the existing behavior without changing the product flow.
- The helper validates finite latitude/longitude within geographic bounds, uses balanced current-position accuracy, and throws before any discovery request when permission is denied or coordinates are invalid. Selected-driver booking reuses the same coordinate validation when it has no prior discovery coordinates.
- Shared mobile tests cover denied permission (zero position reads and zero discovery calls), successful current GPS lookup and passenger query, invalid/out-of-range coordinates (zero discovery calls), and foreground-only permission usage.
- PR #93 implementation commit `f5dacbbe12ca459efe7ae2305bfec94d892d8ea1` passed all eight CI jobs in [42345777887](https://github.com/rajeevp727/turnova-platform/actions/runs/42345777887). Documentation-evidence commit `c94482f3c90d9346b6783fa1af60e1733cebe067` also passed all eight jobs in [42349929671](https://github.com/rajeevp727/turnova-platform/actions/runs/42349929671): 248/248 backend, 47/47 shared mobile tests, Rider/Driver typechecks and bundles, Android debug APKs, unsigned iOS Simulator apps and native non-container API publish. Automated review [6703214201](https://github.com/rajeevp727/turnova-platform/pull/93#issuecomment-6703214201) found no unresolved critical/high-risk issue; the latest documentation-only head must also pass before merge.
- This test slice does not replace Android/iOS physical-device location permission, accuracy, app resume or map acceptance. Production radius/zone approval, custom pickup geocoding/routing, live Call/Chat and live Cosmos remain external gates.
- Next sequential item after this RIDER-01 increment: #4 RIDER-02 — audit immutable direct-fare agreement versioning, both-party confirmation and start/completion gates.


## RIDER-02 confirmation display consistency (2026-11-07)

- PR #93 completed the RIDER-01 foreground pickup permission/coordinate test increment and merged at `17cb46017b215b0e29f26b5a68be6399ec7885f9`; final-head CI [42350780382](https://github.com/rajeevp727/turnova-platform/actions/runs/42350780382) passed all eight jobs.
- The agreement API and both app screens already support per-party UTC confirmation timestamps. The remaining deterministic mobile-test gap is shared label formatting for Pending, Confirmed with timestamp, and older/malformed timestamp values.
- Current focused increment extracts the existing formatting into `agreementConfirmationLabel`, used by Rider Activity and Driver Trips, with shared Node tests. It does not alter agreement state or timestamps.
- Live Cosmos ETag/multi-instance and Android/iOS physical-device negotiation, confirmation, reconnect and in-trip renegotiation remain external gates.
- Next sequential item after this increment: #5 RIDER-03 — audit selected-driver booking idempotency, cancellation/expiry and reservation release.

## RIDER-03 history-triggered expiry reconciliation (2026-11-07)

- PR #94 merged at `16e075730556ea9fdab77ddfc740c5c05ea872aa`; final-head CI [42352025790](https://github.com/rajeevp727/turnova-platform/actions/runs/42352025790) passed 248/248 backend, 51/51 shared mobile, both app typechecks/bundles, both Android debug APKs, both unsigned iOS Simulator builds and native non-container API publish.
- The Rider and Driver history endpoints both call `FindRideAsync` for each returned ride, but explicit expiry coverage previously exercised direct ride reads and discovery rather than history. Current increment adds one integration test for both history routes, asserting one System Expired event and matching lease release.
- Production `Dispatch:SelectedDriverNegotiationLifetimeMinutes` remains unset. No expiry default is inferred; the API returns 503 until the Product Owner approves the lifetime. Live Cosmos ETag/multi-instance/restart and Android/iOS booking/cancel/expiry device acceptance remain external gates.

## DISPATCH-02 audit and DISPATCH-03 handoff (2026-11-07)

- PR #95 merged at `1b2a881f574cbfac0c8f1be6db3ac3116a114982`; exact-head CI [42352928382](https://github.com/rajeevp727/turnova-platform/actions/runs/42352928382) passed all eight jobs and executed 249/249 backend tests. The added Rider/Driver history integration test proves expired selected negotiations transition once to Expired, append one System Expired audit event and release only the matching Driver reservation.
- #6 DISPATCH-01 remains Blocked because production pickup radius/zone boundaries require Product Owner approval; checked-in radius 0 intentionally fails closed. Live Cosmos query/index and Android/iOS pickup-zone evidence remain external.
- #7 DISPATCH-02 was audited on main commit `1b2a881f574cbfac0c8f1be6db3ac3116a114982`. The API already groups eligible approved Cab drivers by union, caps at three, orders by unrounded pickup proximity with deterministic DriverId tie-break, enforces current-union selfie approval and response privacy; Rider UI renders those groups/order without FIFO ranking. Existing API tests cover the deterministic acceptance cases. No additional code change was justified; live Cosmos/device gates and the internal FIFO role decision remain.
- Current sequential item is #8 DISPATCH-03. Re-audit the existing lazy expiry, exact lease release, history/discovery/availability reconciliation and isolation from the legacy FIFO offer-expiry worker. Only implement a concrete independent gap; production negotiation lifetime remains unset until product approval.


## DISPATCH-03 selected-expiry FIFO isolation (2026-11-07)

- PR #96 merged at `fce4e58c4f9c21b6270328d82771a3eeb5c87d63`; exact-head CI [42354011278](https://github.com/rajeevp727/turnova-platform/actions/runs/42354011278) passed all eight jobs and executed 249/249 backend tests.
- The selected-booking `FindRideAsync` path performs the authoritative conditional expiry transition, releases only the matching Driver reservation and emits the ride-status event without depending on `IRideOfferAdvancer`. Existing tests proved terminal state and lease release, but did not directly guard this boundary against a future accidental FIFO call.
- PR #97 adds `ExpiredSelectedNegotiationDoesNotAdvanceLegacyFifoOffer`, injecting a recording advancer into the API test host and asserting zero calls after selected negotiation expiry. The existing legacy offer-expiry positive-control test remains unchanged.
- PR #97 commit [`f8774f4`](https://github.com/rajeevp727/turnova-platform/commit/f8774f40b53b8f6d389c260adbc8f60fef8b313b) is open; exact-head CI and automated review are pending. No production behavior or FIFO policy changes. Production `Dispatch:SelectedDriverNegotiationLifetimeMinutes` remains unset and fails closed until approved.
- Next sequential item after this increment: #9 DISPATCH-04 — re-audit exclusive Driver reservation across concurrent selected bookings, availability transitions and Union transfer; implement only a concrete remaining gap.


## Latest autonomous audit and handoff — 2026-11-07

- PR #97 merged at `722e022b17f16e206e865d0d8c0e1d606b26f28d`. CI [42357835735](https://github.com/rajeevp727/turnova-platform/actions/runs/42357835735) passed all eight jobs, including 250/250 backend tests and Rider/Driver Android debug APK plus unsigned iOS Simulator builds. The selected-negotiation expiry regression proves zero calls to the legacy FIFO offer advancer. Duplicate PR #98 was closed; its branch and commits were retained.
- #9 DISPATCH-04 audit: PR #82 already covers simultaneous selected booking versus Offline and Union transfer, plus active-ride exclusion; CI [42318889871](https://github.com/rajeevp727/turnova-platform/actions/runs/42318889871) passed. Live Cosmos multi-instance ETag/restart and physical-device concurrency remain external gates.
- #10 REALTIME-01 audit: PR #83 already applies capped exponential reconnect and initial-start retry/cancellation to Rider/Driver SignalR connections; CI [42319522116](https://github.com/rajeevp727/turnova-platform/actions/runs/42319522116) passed. Live WebSocket and background/resume device validation remain.
- #11 DRIVER-01 audit: PR #66 already covers Cab onboarding, approved licence/vehicle requirements, signed UnionManager union scoping, least-privilege member data, selfie approval, audit idempotency and transfer reapproval; CI [42278673796](https://github.com/rajeevp727/turnova-platform/actions/runs/42278673796) passed 191/191 backend tests. Identity-provider claim issuance, private document storage/retention and physical-device/legal gates remain.
- #12 DRIVER-02 audit: PR #85 already removes the invented 120-second location freshness default and fails discovery/booking closed until a positive Product-approved value is configured; CI [42320526153](https://github.com/rajeevp727/turnova-platform/actions/runs/42320526153) passed 242/242 backend and 30/30 shared tests. GPS age approval, Cosmos and device gates remain.
- #13 TRIP-01 finding: the completion handler returned immediately when the ride was already `Completed`, so a retry could not repair a process interruption between the ride write and the separate work-activity/reservation-release writes. PR #99 makes the deterministic activity create-if-absent and exact reservation release retryable, including a lost compare-and-swap when another completion request already won. The new in-memory regression simulates that interruption and checks no duplicate activity.
- **Next sequential item:** #14 DRIVER-03 — audit completed-month work-metrics payslips, authenticated Driver isolation, Union-period rules, PDF content privacy and Android/iOS save/share.


## DRIVER-03 — selected-ride payslip audit recovery (2026-11-07)

- Main after PR #99: `1863586eebe210eaae78b041f8567f6e9bf85be0`; exact-head CI [42362228646](https://github.com/rajeevp727/turnova-platform/actions/runs/42362228646) passed all eight jobs and 251/251 backend tests.
- A selected Cab ride's state transition and append-only ride audit are persisted together, but the `DriverWorkActivities` append occurs afterward in a separate container. The first immutable payslip snapshot previously read only the activity container, so a process interruption could omit Accepted/Completed/Driver-decline metrics permanently from that snapshot.
- The current #14 increment replays missing selected-ride Driver events from the ride audit before first snapshot creation, using deterministic create-if-absent activity IDs. Selected-ride detection uses non-null Union linkage rather than requiring an agreement version, because a Driver may decline during initial Negotiating state before any proposal. Completed rides also retry the exact completion activity and reservation release. No snapshot is rewritten after creation; no compensation or monetary policy is added.
- Live Cosmos cross-container query/create-conflict/restart/ETag and physical Android/iOS PDF save/share/accessibility remain external verification gates.
- Next sequential item: #15 DRIVER-04 (blocked pending Product Owner approval of Driver support categories, escalation owner and process); next actionable row is #16 RIDER-04 if the blocker remains.


## DRIVER-03 selected-ride payslip read-repair verification (2026-11-07)

- PR #100 final head `d4e4f9c79f305034027340b4d211984180e1dcf4` passed exact-head CI [42368222460](https://github.com/rajeevp727/turnova-platform/actions/runs/42368222460): all eight jobs, 252/252 backend tests, Rider/Driver typechecks and bundles, Android debug APKs, unsigned iOS Simulator apps and native non-container API publish.
- Automated review follow-up [6706246591](https://github.com/rajeevp727/turnova-platform/pull/100#issuecomment-6706246591) found no unresolved critical/high-risk issue in the reviewed head. The correction explicitly covers a selected Driver decline before any agreement version exists.
- DRIVER-03 remains In progress: live Cosmos cross-container restart/ETag recovery and physical Android/iOS PDF save/share/accessibility are not represented by the in-memory integration test or simulator compilation.
- Next sequential row #15 DRIVER-04 remains Blocked until Product approves Driver support categories, escalation owner and operating process. If unresolved, proceed to #16 RIDER-04 and continue independent acceptance work.


## Current sequential handoff — #16 RIDER-04 (2026-11-07)

- #14 DRIVER-03 PR #100 merged at `fd4e008b63da50be5c26602403850105e9fc1b4b`; final-head CI [42368222460](https://github.com/rajeevp727/turnova-platform/actions/runs/42368222460) passed all eight jobs and 252/252 backend tests. Live Cosmos cross-container recovery and physical Android/iOS payslip save/share/accessibility remain.
- #15 DRIVER-04 is Blocked pending Product approval of Driver-support categories, escalation owner and operating process. Do not invent these.
- #16 RIDER-04 audit found the completed-ride Driver report input hard-coded INR although the agreement/report model carries its currency. PR #101 adds only current agreement version/amount/currency to authenticated Driver history, uses that currency in the report form, and tests that Rider history remains unchanged. Final-head CI [42373100229](https://github.com/rajeevp727/turnova-platform/actions/runs/42373100229) passed all eight jobs, including 252/252 backend tests and 53/53 shared mobile tests.
- Existing report immutability, signed delta, idempotency, ride audit and signed-unionId UnionManager privacy are preserved. Customer dispute, non-zero-delta next-ride gating and cross-Union preceding-ride disclosure remain unresolved product decisions.
- After #16, #17 SUPPORT-01 remains Blocked pending approved Rider-support categories, escalation owner and process; continue to actionable #18 PLATFORM-01.


## Latest sequential handoff — #18 PLATFORM-01 (2026-11-07)

- PR #101 merged at 66f217a599ef694847b2490a656120f198a970f6; exact-head CI [42373804446](https://github.com/rajeevp727/turnova-platform/actions/runs/42373804446) passed all eight jobs (252/252 backend and 53/53 shared mobile tests plus Rider/Driver typechecks/bundles, Android debug APKs, unsigned iOS Simulator apps and non-container API publish).
- #17 SUPPORT-01 is Blocked pending Product-approved Rider support categories, escalation owner and operating process. Do not invent these.
- #18 PLATFORM-01 adds .NET Meter/ActivitySource instrumentation for ride-audit write attempts and storage readiness. Tags are low-cardinality and contain no ride/user/Union identifiers or PII. Exporter, dashboards and alert routing remain infrastructure gates.
- New evidence: `CompletedSelectedRidePersistsPrivacyMinimisedAuditEventsForAgreementLifecycleAndPayment` observes metrics/spans from the actual API audit path; `ReadinessProbeEmitsBoundedMetricsAndTraceWithoutDependencyDetails` verifies readiness metrics/activity without exposing dependency details. PR #102 exact-head CI [42375960785](https://github.com/rajeevp727/turnova-platform/actions/runs/42375960785) passed all eight jobs on `1cb1a5f2cb8af18d932990f0269d1f3d63f5164f`: 254/254 backend tests, 53/53 shared mobile tests, both app typechecks/bundles, Android debug APKs, unsigned iOS Simulator apps and native non-container API publish. Automated review [6707392983](https://github.com/rajeevp727/turnova-platform/pull/102#issuecomment-6707392983) found no unresolved critical/high-risk issue.
- Next: #19 RELEASE-01 — audit native release/signing/device evidence without confusing debug APKs or unsigned simulator apps with production-signed artifacts.


## Latest sequential handoff — #19 RELEASE-01 (2026-11-07)

- Main after PR #102 is `0bedcf5f7ec6c85ba7dff25a8fc09a66a43733ee`; no open PRs were present at the start of the release audit.
- PR #102 final-head CI [42380032068](https://github.com/rajeevp727/turnova-platform/actions/runs/42380032068) passed 8/8 jobs on `7b4320bfdad460511280251963e1b6c4ffc51c80`: 254/254 backend, 53/53 shared mobile, Rider/Driver typechecks and production JS bundles, both Android debug APKs, both unsigned iOS Simulator apps and non-container .NET API publish. Seven expected artifacts were uploaded.
- Automated native compilation is evidenced; signed Android AAB/iOS IPA, final iOS store identifiers/certificates/profiles, physical-device install/runtime and pilot acceptance are not. Do not claim RELEASE-01 Verified or production-ready.
- No independent code-only release gap was found. RELEASE-01 is Blocked pending signing/store inputs and physical-device acceptance.
- Next: final end-to-end gap audit across all 19 rows and approved supplemental requirements; make no feature changes unless a concrete code/test gap is found.


## Latest sequential handoff — final MVP gap audit (2026-11-07)

- PR #103 merged at `7222c5e4dccc2ca0f804eae92105455df89d3fb9`; final-head CI [42382259118](https://github.com/rajeevp727/turnova-platform/actions/runs/42382259118) passed 8/8 jobs: 254/254 backend tests, 53/53 shared mobile tests, Rider/Driver typechecks and bundles, Rider/Driver Android debug APKs, Rider/Driver unsigned iOS Simulator apps and non-container API publish.
- The final audit rechecked PRD sections 15/16, architecture, test plan, all 19 matrix rows, AI-01 and the five Cab supplemental acceptance areas. It found stale CAB-DISCOVERY-01/CAB-AGREEMENT-01 evidence and a legacy Payments adapter entry in the architecture diagram; this follow-up reconciles those documents. No additional safe code-only implementation gap was found.
- The architecture now states the MVP has no payment gateway/escrow adapter; payment is direct between Rider and Driver, with only Driver-reported/unverified reconciliation stored. Call/Chat remains an optional provider placeholder.
- Current sequential #19 RELEASE-01 is Blocked: signed Android AAB/iOS IPA, final iOS store identifiers/certificates/profiles, physical-device install/runtime and pilot acceptance remain. Other blockers include approved pickup radius/zone, Driver location-freshness age, selected-negotiation lifetime, Driver/Rider support categories/process/owner, live Cosmos/TriSend/maps/Call/Chat/push/telemetry, document storage/retention, legal partnership/T&C, production API hosting and load tests.
- **Next:** between runs. Re-read main on the next scheduled run and resume the first item whose external blocker has cleared, or implement a concrete newly evidenced code/test gap. Do not invent product policy or request infrastructure credentials before the coding milestone.


## Latest execution handoff (2026-11-08)
- Current sequential item: #3 RIDER-01. PR #106 is the only active feature PR; do not start another feature PR until it is merged or formally blocked.
- PR #106 automated review comment: [6711746199](https://github.com/rajeevp727/turnova-platform/pull/106#issuecomment-6711746199); no critical/high-risk findings.
- PR #106 adds 14 invalid booking-input cases and one positive maximum-boundary test. It asserts invalid requests do not create a driver reservation or idempotency record.
- After the documentation commit, rerun and inspect all required checks on the exact latest PR head before squash merge.
- Next sequential task (#4 RIDER-02): fix agreement-proposal idempotency payload mismatch. Current API lookup replays an existing agreement for the same actor-scoped Idempotency-Key without comparing the new amount/currency; the ETag-conflict retry path also replays without comparing. Add an optional backward-compatible RequestHash to RideAgreementVersion, compare the amount/currency fingerprint on both paths, return 409 for changed payloads, and add same-key retry plus concurrent changed-payload regression tests.
- Do not change approved Cab-only discovery, direct negotiation, no-payment-gateway model, FIFO guard or no-container policy. Live Cosmos and physical-device checks remain external gates.


## Latest sequential handoff — #4 RIDER-02 (2026-11-08)

- PR #106 completed #3 RIDER-01 and merged at `1cfe9818190800777686516ce394d492988879ad`; exact-head CI [42404990956](https://github.com/rajeevp727/turnova-platform/actions/runs/42404990956) passed 8/8 jobs and 272/272 backend tests.
- #4 fixes an idempotency correctness gap in `POST /api/v1/rides/{rideId}/agreements`: a replay with the same actor-scoped key but a changed amount/currency must return HTTP 409 instead of replaying the original agreement as success.
- The optional `RideAgreementVersion.RequestHash` stores SHA-256 over invariant canonical two-decimal amount plus currency. Existing persisted versions without the field reconstruct the hash from immutable stored terms; no migration or rewrite is required.
- Both the initial idempotency lookup and the ETag-conflict concurrent retry path compare the request fingerprint before returning the prior version.
- New deterministic tests cover changed amount, changed currency, legacy records without a fingerprint, concurrent same-key requests with matching and mismatched payloads, and equivalent decimal-scale normalization. PR [#107](https://github.com/rajeevp727/turnova-platform/pull/107) is open; exact-head CI is pending.
- **Next:** #5 RIDER-03 — audit selected booking/reservation expiry, cancellation, release and history-triggered recovery for any remaining independent engineering gaps.
- Live Cosmos ETag/multi-instance/restart and physical Android/iOS negotiation/device acceptance remain external gates. No credentials or product defaults are invented.


## Latest sequential execution — #5 RIDER-03 (2026-11-08)

- PR #107 completed #4 RIDER-02 and merged at 0d315b3d96729ed2d0129fd962de170879ebf743; exact-head CI [42409103030](https://github.com/rajeevp727/turnova-platform/actions/runs/42409103030) passed all eight backend/API/mobile/native jobs.
- The #5 RIDER-03 re-audit found a concrete interrupted-write recovery gap: the deterministic ride ID and original temporary reservation were reused, but NegotiationExpiresAtUtc was recomputed from retry time. This could extend the ride deadline beyond its original reservation lease.
- PR [#108](https://github.com/rajeevp727/turnova-platform/pull/108), branch fix/rider-03-preserve-negotiation-expiry-on-recovery, code commit 18f09c83ee56911436d21acbdc8d715efe864496, preserves the idempotency record original CreatedAtUtc for ride RequestedAt, RideCreated audit and negotiation expiry. The existing recovery integration test now asserts the ride deadline equals the still-held reservation expiry exactly.
- PR #108 merged at `f0e60ca7e1aa986b994d1a6d1240911402afee62`; exact-head CI [42414571838](https://github.com/rajeevp727/turnova-platform/actions/runs/42414571838) passed 8/8 jobs and 278/278 backend tests. Automated review found no unresolved critical/high-risk issue. Live Cosmos ETag/multi-instance/restart and physical Android/iOS booking/expiry/reconnect/notification validation remain external gates; the approved negotiation lifetime remains unset and booking fails closed until Product supplies it.
- Strict acceptance remains 0/19 Verified (16 In Progress, 3 Blocked). This does not mean the implementation is empty; extensive merged code and automated evidence are recorded per row.
- Next sequential row after #5 is #6 DISPATCH-01. Product-approved radius and zone boundaries remain external; keep the fail-closed API and complete independent test/audit work before proceeding to #7 DISPATCH-02.


## Latest sequential execution — #6 DISPATCH-01 (2026-11-08)

- #5 RIDER-03 PR #108 merged at `f0e60ca7e1aa986b994d1a6d1240911402afee62`; exact-head CI [42414571838](https://github.com/rajeevp727/turnova-platform/actions/runs/42414571838) passed all 8/8 jobs and 278/278 backend tests, Rider/Driver typechecks and production bundles, both Android debug APKs, both unsigned iOS Simulator apps, shared mobile-auth tests and native non-container API publish.
- Audit of #6 found `ResolveDispatchZone` silently used the first configured rectangle containing a coordinate. Overlap could therefore assign a pickup to the wrong zone based on configuration order.
- PR [#109](https://github.com/rajeevp727/turnova-platform/pull/109), branch `fix/dispatch-01-reject-ambiguous-zones`, accepts only exactly one matching zone rectangle. No match or multiple matches (including inclusive shared boundaries) returns no zone; discovery and selected booking fail closed, and Driver location reconciliation takes the Driver Offline.
- Twelve deterministic API integration cases exercise overlapping interiors and shared inclusive boundaries in both zone configuration orders across Rider discovery, selected booking with zero ride/idempotency/reservation side effects, and Driver offline reconciliation. PR #109 merged at `203b8843741248d8648345f462609d3102fd6715`; exact-head CI [42418661118](https://github.com/rajeevp727/turnova-platform/actions/runs/42418661118) passed all 8/8 jobs and 290/290 backend tests. Automated engineering review was a COMMENT, not an approval, and found no unresolved critical/high-risk issue.
- Rider app audit: `discoverNearbyCabDrivers` requests foreground GPS and calls the server discovery endpoint; the UI renders server union grouping, approved photo, Cab details and pickup proximity, keeping journey distance separate. Driver app audit: availability and foreground location are server-validated; the app handles the zone-exit conflict by setting Offline. No client-side zone selection is added.
- DISPATCH-01 remains Blocked for strict acceptance until Product approves a positive radius and non-overlapping zone boundaries, and live Cosmos query/index and Android/iOS pickup-zone evidence is recorded. No default is inferred.
- Next sequential item after PR #109 merged: #7 DISPATCH-02 — audit per-Union driver cap/order, explicit Rider selection, privacy, and no FIFO/automatic assignment.



## Latest sequential execution — #7 DISPATCH-02 (2026-11-08)

- #6 DISPATCH-01 PR #109 merged at `203b8843741248d8648345f462609d3102fd6715`; exact-head CI [42418661118](https://github.com/rajeevp727/turnova-platform/actions/runs/42418661118) passed 8/8 jobs and 290/290 backend tests, Rider/Driver typechecks and production bundles, both Android debug APKs, both unsigned iOS Simulator apps, shared mobile-auth tests and native non-container API publish.
- Existing #7 API and UI evidence covers the per-Union three-driver cap, Haversine pickup-proximity ordering and deterministic DriverId tie-break, current-Union selfie approval, response privacy, reservation exclusion, explicit Rider selection and no FIFO dependency/automatic assignment.
- Concrete gap: selected booking reused discovery-time GPS even if the Rider moved while reviewing candidates. Code commit `bdfd232a53ecb257aaa56c3ab857f7889246936d` in PR [#110](https://github.com/rajeevp727/turnova-platform/pull/110) obtains a fresh foreground fix on the first booking attempt, then reuses that exact pair with the same Idempotency-Key for retries. Route/passenger/stop/Driver changes reset the attempt; concurrent reads are shared, denied permission is not cached and stale results are fenced.
- Reviewed PR head `37308fee3c231c5eb4d2ef8815cf8090271b974d` passed exact-head CI [42424122943](https://github.com/rajeevp727/turnova-platform/actions/runs/42424122943): all 8/8 jobs, 290/290 backend tests and 57/57 shared mobile tests; Rider/Driver typechecks and production JS bundles, both Android debug APKs, both unsigned iOS Simulator apps and native non-container API publish/artifact verification passed.
- Automated engineering review is a COMMENT, not approval; it found no unresolved critical/high-risk security, privacy, authorization, concurrency, idempotency or data-loss issue. PR #110 is the only feature PR. The documentation evidence sync is a follow-up commit and its new final head must pass CI before merge.
- Precise coordinates and the Idempotency-Key are not persisted across app-process restart. Live Cosmos and physical Android/iOS acceptance are not claimed.
- **Next sequential item:** #8 DISPATCH-03 — re-audit terminal ride/reservation recovery. Specifically, if a prior instance persisted a terminal ride status but stopped before releasing its Driver reservation, a later direct ride/history read should retry release of only that exact Driver/ride pair, including the ETag-race reread path. Add deterministic tests for already-terminal Expired, Cancelled and Completed rides; never release a newer reservation or advance legacy FIFO.
- Acceptance accounting: 0 Verified, 15 In Progress, 4 Blocked; strict fully verified count 0/19.


## Latest sequential handoff — #8 DISPATCH-03 (2026-11-08)

- #7 DISPATCH-02 PR #110 merged at `9782bb08b51ccccc4aa076f6b9cb946c54853819`; exact-head CI [42427667726](https://github.com/rajeevp727/turnova-platform/actions/runs/42427667726) passed all 8/8 jobs, 290/290 backend tests and 57/57 shared mobile tests, Rider/Driver typechecks/bundles, Android debug APKs, unsigned iOS Simulator apps and native non-container API publish.
- #8 audit: `FindRideAsync` returned an already-terminal ride without retrying the selected Driver reservation release. `HasActiveDriverRideAsync` happened to release it during discovery, but direct ride reads and Rider/Driver history could leave an interrupted terminal cleanup unrepaired. The ETag-conflict reread path had the same gap.
- PR [#111](https://github.com/rajeevp727/turnova-platform/pull/111), branch `fix/dispatch-03-repair-terminal-reservations-on-read`, adds `ReleaseTerminalSelectedRideReservationAsync`. It repairs only Union-linked selected Cab rides in Completed/Cancelled/Expired state, matching Cab service class, DriverId and RideId; this leaves legacy non-Union/non-Cab rides and a newer reservation untouched. Direct Rider reads check ownership before any reconciliation side effect. The ETag/snapshot race path re-reads the latest ride and invokes the same guarded helper.
- New deterministic API tests cover direct reads for all three terminal statuses, Rider and Driver history reads, preservation of a newer reservation for the same Driver, concurrent expiry reads with one terminal audit event, unrelated-Rider isolation, non-Cab history, and Cab history with null/empty/whitespace UnionId. A final review changed the guard from null-only to non-whitespace UnionId so legacy records cannot be mistaken for Union-linked selected rides. Existing `ExpiredSelectedNegotiationDoesNotAdvanceLegacyFifoOffer` continues to protect the no-FIFO-advancement rule.
- PR #111 final head `3ae3bc1daf1c3f8b40834722e23576ca6c6cd929` passed exact-head CI [42441104485](https://github.com/rajeevp727/turnova-platform/actions/runs/42441104485), all 8/8 jobs and 301/301 backend tests. Reviews are COMMENT-only, not approvals; no unresolved threads or critical/high-risk findings remain. No live Cosmos or physical-device validation is claimed.
- Next exact action: recheck PR #111 head `3ae3bc1daf1c3f8b40834722e23576ca6c6cd929` against main and merge only if GitHub confirms all repository requirements without bypass or self-approval. If independent approval is required, leave it open. After merge, re-read main and proceed to #9 DISPATCH-04, starting with expired-orphan reconciliation before UnionManager selfie-review lease acquisition.

## Latest sequential handoff — #9 DISPATCH-04 (2026-11-08)

- #8 PR #111 merged at `b983b6b1079fb95c7b3adac1951f4381ebab9d07`; final-head CI [42442111771](https://github.com/rajeevp727/turnova-platform/actions/runs/42442111771) passed 8/8 jobs. Its code-bearing CI [42441104485](https://github.com/rajeevp727/turnova-platform/actions/runs/42441104485) passed 301/301 backend and 57/57 shared mobile tests.
- #9 audit found the UnionManager selfie-review route acquired a temporary per-Driver lease without first running the shared authoritative reservation reconciliation. Other API temporary-lease acquisition paths (selected booking, Driver availability, zone exit and Union transfer) already performed this precheck.
- PR [#112](https://github.com/rajeevp727/turnova-platform/pull/112) adds `HasActiveDriverRideAsync` after current UnionManager assignment/claim authorization and before the selfie-review lease. It repairs only expired orphan or terminal reservations by exact DriverId+RideId; an expired lease tied to an active negotiation/ride remains held. The subsequent create-if-absent lease and finally exact-release preserve concurrency with booking, availability, transfer and another review.
- Tests `UnionManagerSelfieReviewReconcilesExpiredOrphanReservationBeforeAcquiringReviewLease` and `UnionManagerSelfieReviewKeepsExpiredReservationForActiveNegotiation` cover both branches. Initial CI found missing Driver fixture initialization; corrected fixtures passed final code-head CI [42444975803](https://github.com/rajeevp727/turnova-platform/actions/runs/42444975803), 8/8 jobs and 303/303 backend tests, 57/57 shared mobile tests, both app typechecks/bundles, Android debug APKs, unsigned iOS Simulator apps and native non-container API publish.
- Engineering review is COMMENT-only, not approval; no unresolved review threads or critical/high-risk finding remains. Docs evidence sync is in the PR and needs final-head CI before merge.
- External gates remain live Cosmos multi-instance ETag/create-if-absent/restart/crash recovery, physical Android/iOS concurrency/availability/transfer acceptance and Product-approved selected-negotiation lifetime. Strict acceptance remains 0/19 Verified, 15 In progress, 4 Blocked.
- **Next:** verify PR #112 final docs-sync head CI and merge only if GitHub permits with all checks green; then re-read main and start #10 REALTIME-01 audit.

 
## Latest sequential handoff — #10 REALTIME-01 (2026-11-08)

- Main after #9 is `68695ee3f8e488f9ac8d84397d056fcf02c046ee`; PR #112 DISPATCH-04 merged with CI [42444975803](https://github.com/rajeevp727/turnova-platform/actions/runs/42444975803), 8/8 jobs and 303/303 backend tests.
- Single active feature PR: [#113](https://github.com/rajeevp727/turnova-platform/pull/113), branch `fix/realtime-01-refresh-history-on-terminal-status`. It fences stale/mismatched Rider/Driver `RideStatusChanged` events and tests actual SignalR hub group-add/abort behavior for owner Rider, assigned Driver and self-only Driver notification group.
- Driver active-card/history projection fix originally developed in PR #114 is integrated into #113: shared immutable `applyRideStatusUpdate`, matching ride only, terminal location-sharing shutdown, unrelated history rows preserved, and explicit event-handler cleanup. Tests and TRACK-017 are added.
- Prior standalone heads: PR #113 `f909c56f0d32ca2dbee4c78fb16f4dfd82837e5a` passed 8/8 CI [42449557146](https://github.com/rajeevp727/turnova-platform/actions/runs/42449557146), 305/305 backend tests; PR #114 feature and final docs heads passed 8/8 CI [42451754665](https://github.com/rajeevp727/turnova-platform/actions/runs/42451754665) and [42452140890](https://github.com/rajeevp727/turnova-platform/actions/runs/42452140890). Integrated #113 final-head CI is pending. PR #114 is closed as superseded after all code and acceptance evidence were integrated into #113.
- Do not self-approve. Merge #113 only on green exact-final-head CI, resolved review threads and GitHub/repository-policy permission; no bypass.
- REALTIME-01 external gates: live WebSocket/device-to-device agreement/status/location delivery, long-outage reconnect, Android/iOS background/resume and physical-device acceptance. No live SignalR/device validation is claimed.
- Next #11 DRIVER-01: conditionalize stale blind upserts in Driver selfie update, onboarding submit and Operations document review using existing `TryUpdateDriverProfileAsync` compare-and-swap / Cosmos ETag retry support. Preserve unrelated latest profile fields and reject changed review-document snapshots; add deterministic race/retry tests. External gates: private document upload/storage/scanning/consent/retention, signed UnionManager/unionId claims, physical camera/document-permission/onboarding tests, live Cosmos multi-instance/restart, legal partnership approval.
- Completion accounting: engineering implementation/evidence remains partial across rows; strict acceptance 0/19 Verified (15 In Progress, 4 Blocked); production readiness not established. AI-01 and Cab supplemental work are outside the 19-row denominator.


## DRIVER-01 profile concurrency increment (in progress)

- Current branch: `fix/driver-01-profile-cas-onboarding-review`; base is main after REALTIME-01 PR #113 merge `120c00dba0deed0d3357f822fe8ca8143ea16888`.
- Removed the blind Driver profile Upsert path from selfie replacement, onboarding resubmission and Operations document review.
- Selfie replacement now mutates only selfie fields through `TryUpdateDriverProfileAsync`; the latest availability/location and other unrelated fields are retained, while public selfie approval is reset to Pending.
- First onboarding uses create-if-absent (in-memory TryAdd / Cosmos CreateItemAsync). A create collision re-reads and retries conditionally. Resubmission uses the latest CAS/ETag snapshot, refuses legacy non-Cab and Approved/Suspended profiles, updates submitted identity/vehicle/doc/selfie fields and preserves unrelated latest availability/location fields and the original SubmittedAtUtc.
- Operations document review uses CAS/ETag replacement and `DriverProfileReviewGuard` to reject a stale snapshot when submitted document references/status, vehicle identity, service class, profile status or prior review state changed. Rejection/suspension queue cleanup uses the successfully persisted latest profile.
- Added integration tests for onboarding/selfie profile-field preservation and unit tests for stale document/vehicle review snapshots plus unrelated availability/location changes.
- Bounded retries fail closed with HTTP 409 rather than blindly overwriting newer state. CI is pending; no live Cosmos concurrency/restart evidence is claimed.
- External gates remain private document upload/storage/scanning/consent/retention, signed UnionManager/unionId claims, Android/iOS camera/document permissions and physical onboarding, live Cosmos multi-instance/restart/ETag, and legal partnership approval.
- DRIVER-02 is already implemented/merged (PR #117); after PR #150 merges, the next ordered row is #13 TRIP-01, followed by #14 DRIVER-03.

## Latest execution handoff — DRIVER-01 merged; DRIVER-02 in progress (2026-11-08)

- PR [#115](https://github.com/rajeevp727/turnova-platform/pull/115) DRIVER-01 merged at `435a62e8dba1c93b630efc7a8e5f02bd93734a94`; exact-head CI [42455925931](https://github.com/rajeevp727/turnova-platform/actions/runs/42455925931) passed 8/8 jobs, including 311/311 backend tests and both Android/iOS native build jobs.
- Next sequential row is #12 DRIVER-02. Branch `fix/driver-02-fence-out-of-order-location` is based on the merge commit above.
- Concrete gap: Driver foreground GPS callbacks can complete out of order. Server receipt-time stamping previously allowed an older delayed fix to overwrite a newer location and look fresh.
- Current implementation adds a persisted Driver location capture watermark, sends Expo GPS capture timestamps from the Driver app, rejects future and non-advancing capture times through availability/location CAS paths, retains the watermark across ordinary Offline transitions and advances it on zone exit, and uses capture time for Rider discovery and selected-booking freshness checks (including the post-lease re-read). Legacy records fall back to receipt time; no approved max-age default is invented.
- Deterministic API regressions cover stale in-zone and out-of-zone updates, stale Online transitions after Offline, discovery/booking rejection when server receipt is recent but GPS capture is stale, and future capture timestamps beyond the one-minute clock-skew allowance being rejected without profile/queue mutation. An earlier superseded backend run [42459347697](https://github.com/rajeevp727/turnova-platform/actions/runs/42459347697) exposed fixture-only failures; the current final test setup creates Driver fixtures and seeds availability from Offline before asserting queue membership. Docs/test plan/acceptance matrix are synchronized. Latest pre-sync PR tip `d13751865940b13a603d433f45ecd50bc96f191e` has exact-head CI [42466304752](https://github.com/rajeevp727/turnova-platform/actions/runs/42466304752): Rider/Driver mobile and shared mobile-auth passed; both Android native builds failed before compilation after three Gradle 8.14.3 distribution-download attempts returned HTTP 504 through the GitHub release redirect; backend passed build but 1 of 317 tests failed (`StartupRejectsMissingJwtConfiguration` for whitespace issuer returned `ObjectDisposedException` instead of the expected validation message); both iOS native builds remain queued. The workflow now configures a 120-second wrapper timeout and retries only transient HTTP 5xx/socket-timeout distribution failures up to three times. A fresh exact-head CI run is required after this documentation-only synchronization.
- DRIVER-02 external gates remain live Cosmos multi-instance/restart/ETag and physical Android/iOS location permission, foreground/resume and location-update acceptance. The approved positive `Dispatch:NearbyLocationMaxAgeSeconds` is still a Product input; missing configuration must fail closed.
- PR #116 (`fix/driver-02-reconcile-server-availability`) was closed as superseded so only one DRIVER-02 feature PR remains active. Its Driver availability recovery helper, six tests and UI integration have now been integrated into PR #117 alongside capture-time ordering; #116 itself remains unmerged.
- Do not merge until fresh exact final-head CI is green, all review threads are resolved and GitHub/repository policy permits the merge without bypass or self-approval. The observed Android failure was a Gradle distribution HTTP 504 before compilation; retry after the current workflow completes. After merge, re-read main and continue sequentially to #13 TRIP-01. Read-only TRIP-01 preparation found that Rider Activity receives live Driver marker coordinates but the MapView uses `initialRegion` only; evaluate follow-camera/fit-to-driver-and-pickup behavior and deterministic helper tests before treating map progress as complete.


## Latest execution handoff — DRIVER-02 CI green; workflow hardening sync pending (2026-11-08)

- Main remains `435a62e8dba1c93b630efc7a8e5f02bd93734a94`; only active feature PR is [#117](https://github.com/rajeevp727/turnova-platform/pull/117), `fix/driver-02-fence-out-of-order-location`.
- Exact-head CI [42472744096](https://github.com/rajeevp727/turnova-platform/actions/runs/42472744096) passed **8/8 jobs** on `91959a7056251687f030a7abfbd7d08078b3d5bf`. Driver iOS simulator build took about 21m25s; Rider iOS about 31m55s; Android builds about 6–8m; backend 1m26s; mobile JS/typecheck jobs 52–61s. These are first-run baseline timings, not proven cache savings.
- CI currently caches NuGet, npm download cache through setup-node, Gradle dependencies/distribution and CocoaPods downloads; uses PR-scoped cancellation, macOS Intel native iOS runners and native job timeouts. This sync removes mutable `node_modules` cache (no lockfiles are committed), restores npm audit and avoids caching generated `Pods` without a lockfile. Fresh CI is required for the sync head.
- DRIVER-02 implementation/test evidence: persisted monotonic GPS capture watermark, future/out-of-order rejection via CAS, capture-time discovery/booking freshness, and six shared tests for authoritative availability/ride-history conflict recovery. Product-approved freshness and zone/radius, live Cosmos multi-instance/ETag/restart, physical Android/iOS permission/foreground/resume/GPS and internal FIFO policy remain external gates.
- Strict acceptance: **0/19 Verified**, 15 In Progress, 4 Blocked; this is not an implementation-completion percentage. Production readiness is not established.
- **Next:** validate the new exact-head CI, merge #117 only if all required checks pass and GitHub permits without self-approval/bypass; then re-read main and proceed to #13 TRIP-01.
- TRIP-01 preparation: Driver active-trip location POST currently lacks a GPS capture timestamp, so delayed callbacks can reorder trip coordinates; Rider Activity receives live Driver coordinates but uses `MapView.initialRegion` only. Next code increment should add active-trip capture-time ordering/CAS and stale SignalR event protection, plus deterministic Rider map-camera follow/recenter helper tests. Physical-device/map/SignalR acceptance remains external.


## Latest execution handoff — TRIP-01 active-trip GPS ordering and Rider map follow (2026-11-08)

- PR [#117](https://github.com/rajeevp727/turnova-platform/pull/117) DRIVER-02 merged at `5c9a97ce6798ca0df3451c1c4f781407baa6babf`; exact-head CI [42483895777](https://github.com/rajeevp727/turnova-platform/actions/runs/42483895777) passed 8/8 jobs and 317/317 backend tests. Main was re-read at that merge commit; there are no other open feature PRs.
- Sole active feature PR is [#118](https://github.com/rajeevp727/turnova-platform/pull/118), branch `fix/trip-01-fence-active-trip-location`. Current implementation changes the active-trip location request to accept `capturedAtUtc`, persists `DriverLocationCapturedAtUtc` on the existing `DemoRide` aggregate, rejects >1-minute future and non-advancing capture times, includes the watermark in `IsSameRideSnapshot`, and emits capture + receipt timestamps to SignalR.
- Driver Expo active-trip watcher sends `LocationObject.timestamp`. Rider event handler validates same ride, finite coordinates and strictly newer capture timestamps (legacy receipt-time comparison remains supported). Rider Activity map fits valid Driver+pickup points only after the matching ride-keyed native map reports `onMapReady`, follows accepted updates, pauses on manual pan and provides a Follow driver control.
- API regression extends `SelectedCabBookingUsesOnlyAssignedDriverLocationAndLifecycleGuards` to cover legacy timestamp fallback, newer capture acceptance, stale 409/no mutation and future 400/no mutation. Shared tests cover monotonic event ordering, mismatched ride/invalid data, legacy event ordering, legacy-to-capture upgrade and map viewport coordinates.
- Test plan adds TRIP-LOC-001..007 and TRIP-MAP-001..004. Acceptance matrix and progress handoff identify #13 TRIP-01 as current; strict count remains 0/19 (15 In Progress, 4 Blocked).
- Run #745 [42489359916](https://github.com/rajeevp727/turnova-platform/actions/runs/42489359916) passed 8/8 jobs on `100d2bf0f50bc1cd23eeff8a39229c529e7e71e8`. The map-ready refinement adds a ride-keyed MapView, waits for its onMapReady callback before camera fitting, and tests that follow is enabled only for the ready current ride. The new exact head must pass all checks before merge. Merge only when required checks are green, no unresolved critical/high risk exists and GitHub permits without self-approval/bypass. After merge re-read main and continue to #14 DRIVER-03.
- External gates: live Cosmos multi-instance ETag/restart; Android/iOS foreground GPS and map pan/follow; device-to-device SignalR/reconnect; unresolved next-ride-after-completion and non-zero reconciliation-delta eligibility policies. No production/device/live Cosmos validation is claimed.

- Final review refinement: the API serializes the persisted `DemoRide.DriverLocationCapturedAtUtc` field as `driverLocationCapturedAtUtc`, not `locationCapturedAtUtc`. Rider's helper now reads both the event field and the persisted API field; a deterministic regression proves an older SignalR event is rejected after reconnect from an API snapshot. Malformed capture values are rejected even if a receipt timestamp is present, and legacy receipt-only events cannot downgrade a snapshot once its capture watermark exists.
- CI run #733 failed one test only because the response assertion still expected the coordinate from the earlier accepted fix after a newer fix had correctly replaced it. The test was corrected to assert the newer coordinates and serialized watermark. API build succeeded in that run. The latest exact-head run after docs synchronization must pass before merge.

- A second stale-data path was found in Rider reconnect/history recovery: an API response started before a newer SignalR event could finish afterward and overwrite the newer marker. `mergeTripLocationSnapshot` now preserves the newer location/capture watermark while still merging non-location ride state; Rider `loadRideHistory` uses it. Deterministic shared test covers stale and newer API snapshots. Test plan now includes TRIP-LOC-009.

- Run #742 surfaced one Rider typecheck issue only: the optional-chain ternary did not narrow `Ride | null` for `mergeTripLocationSnapshot`. The Rider refresh path now uses an explicit non-null guard before calling the helper. Backend build/tests and shared auth job passed in that attempt; exact-head CI after this correction is required.


## Latest execution handoff — #13 TRIP-01 exact-head validation (2026-11-08)

- PR [#117](https://github.com/rajeevp727/turnova-platform/pull/117) DRIVER-02 is merged at `5c9a97ce6798ca0df3451c1c4f781407baa6babf`; its exact-head CI [42483895777](https://github.com/rajeevp727/turnova-platform/actions/runs/42483895777) passed 8/8 jobs and 317/317 backend tests.
- Sole active feature PR remains [#118](https://github.com/rajeevp727/turnova-platform/pull/118), branch `fix/trip-01-fence-active-trip-location`, head `a13a8b706ddd0ee46eb635cbc6cd6ad6a48390a2`.
- Exact-head CI [#746 / 42491279009](https://github.com/rajeevp727/turnova-platform/actions/runs/42491279009) completed **8/8 successfully**: 317/317 backend tests, Rider/Driver typechecks and JS bundles, shared mobile tests, Android Rider/Driver debug APKs, unsigned iOS Rider/Driver Simulator apps, and native non-container API publish. Artifacts include both native app builds, production JS bundles, API publish, npm audit reports and generated npm lockfiles.
- Both app npm audits reported 0 critical and 0 high vulnerabilities; 11 moderate and 1 low remain in the Expo SDK 54 dependency tree. Audit reports remain available as CI artifacts. Generated lockfiles are review artifacts and are not committed; dependency reproducibility remains a follow-up.
- Review: three COMMENT-only engineering reviews, zero unresolved inline threads, no self-approval. PR is open and mergeable; merge only after verifying current head and GitHub policy, with no bypass.
- TRIP-01 code evidence covers active-trip capture watermark/CAS, future and out-of-order rejection, SignalR capture timestamp, stale event and reconnect/history snapshot fencing, ride-keyed MapView readiness, camera follow/manual-pan pause and explicit Follow action.
- Strict acceptance remains **0/19 Verified** (15 In Progress, 4 Blocked); this is not an implementation-completion percentage. Production readiness is not established.
- External gates: live Cosmos multi-instance ETag/restart and mixed-version API validation; physical Android/iOS foreground GPS, manual map pan/follow and device-to-device SignalR/reconnect; Product decisions on accepting a next ride before prior reconciliation and non-zero-delta eligibility.
- **Next:** verify PR #118 still points to this head and no required check changed, merge only if GitHub permits without self-approval/bypass, re-read main, then begin #14 DRIVER-03. Its next audit will address canonical persisted payslip snapshot read-after-create-conflict behavior and clearing cached payslip PDFs on logout/session expiry. Unicode PDF rendering remains a known gap pending a legally approved embedded font/shaping approach.


## Latest sequential handoff — #14 DRIVER-03 canonical payslip snapshot and cache privacy (2026-11-08)

- PR [#118](https://github.com/rajeevp727/turnova-platform/pull/118) TRIP-01 merged at `4a58ea6d5e4d0a516754f3500d8ef60a3ea48829`; exact-head CI [42492757245](https://github.com/rajeevp727/turnova-platform/actions/runs/42492757245) passed 8/8 jobs. Main post-merge CI [42494068834](https://github.com/rajeevp727/turnova-platform/actions/runs/42494068834) completed successfully with 8/8 jobs. No live Cosmos or physical-device validation is claimed.
- Current sole feature PR is [#119](https://github.com/rajeevp727/turnova-platform/pull/119), branch `fix/driver-03-canonical-payslip-cache-cleanup`, based on main `4a58ea6d5e4d0a516754f3500d8ef60a3ea48829`.
- API payslip persistence now returns the in-memory `ConcurrentDictionary.GetOrAdd` winner. For Cosmos CreateItem 409, it point-reads the canonical item up to three times with 50 ms and 100 ms bounded delays; if the canonical snapshot remains missing or the read fails, the request fails closed rather than returning locally calculated metrics. Added `DriverPayslipSnapshotRecoveryTests` for immediate/delayed reads, bounded fail-closed, read error and cancellation.
- Driver app cache helper only recognizes exact `TurnOva-Payslip-YYYY-MM.pdf` names. Cleanup runs on logout, detected session expiry, no-session startup and before new login/register. Unrelated cache files are preserved. Session-epoch checks before/after write remove a generated PDF if logout/expiry wins during an in-flight download; cleanup failures are surfaced and prevent a new account from signing in until old-account cache cleanup succeeds. Shared `driverPayslipCache.test.cjs` covers allow-list, path safety, partial failures and write/logout races.
- Documentation updated in `docs/MVP_ACCEPTANCE_MATRIX.md`, `docs/MVP_PROGRESS.md`, `docs/test-plan.md` and `docs/architecture.md`. The row remains In progress pending exact-head CI and external validation.
- Known PDF gap: Type1 Helvetica is ASCII-only and current renderer substitutes `?` for non-ASCII Driver/Union names. Do not select/embed a font without Legal/Product approving its font and shaping-library license. Add deterministic PDF extraction/rendering tests after that approval. No monetary compensation policy is inferred; `DaysWorked` remains distinct India-local dates with at least one completed activity.
- External gates: Platform/Cloud development Cosmos create-conflict point-read visibility, multi-instance/restart/consistency; Mobile QA physical Android/iOS PDF save/share/logout/session-expiry/cache/accessibility; Legal/Product font license and shaping approach; Product confirmation of the documented `DaysWorked` definition if a different metric is intended.
- Strict acceptance remains **0/19 Verified** (15 In Progress, 4 Blocked); this does not mean features are unimplemented. Production readiness is not established.
- Next: complete PR #119 exact-head CI, inspect the full diff for snapshot consistency and account-isolation risks, submit COMMENT-only review, fix only failed jobs where possible, and merge only if all required checks pass and GitHub policy permits. After merge, re-read main and proceed sequentially to the next acceptance row.


## Latest sequential handoff — #16 RIDER-04 (2026-11-08)

- DRIVER-03 PR #119 merged at `5c7902e7e5ebbfd67f477f8fd9dc936d607ffb64`; exact-head CI [42495908059](https://github.com/rajeevp727/turnova-platform/actions/runs/42495908059) passed 8/8 jobs and 322/322 backend tests, including both unsigned iOS Simulator builds. Main post-merge CI [42497520855](https://github.com/rajeevp727/turnova-platform/actions/runs/42497520855) was still running at the last poll; native builds were in progress.
- #15 DRIVER-04 remains blocked by unapproved Product/Operations support categories, intake fields, priority/severity, escalation owner/contact channel, support hours/response targets, attachment handling, privacy/retention and operational handoff. Do not invent these inputs or a support API contract.
- #16 RIDER-04 branch `fix/rider-04-minimize-union-reconciliation-projection` replaces the UnionManager reconciliation Cosmos `SELECT *` read with a flat scalar projection of only minimum report fields. The API still rechecks Cab, ride/report Union equality and signed `unionId` claims before returning the same response shape. New tests enforce the DTO allow-list and SQL projection shape; existing integration coverage verifies signed-union response isolation and no Rider/route/contact/location/chat disclosure.
- The projection does not change report persistence, Driver idempotency, CAS, signed delta, unverified status, or historical original-Union ownership. No customer dispute/confirmation, non-zero-delta next-ride policy, payment gateway or cross-Union disclosure is added.
- External gates: live Cosmos query/partition/index and multi-instance/restart validation; physical Android/iOS reconciliation history/report-entry acceptance; unresolved customer dispute/confirmation and non-zero-delta next-ride/cross-Union policy decisions.
- Strict acceptance remains **0/19 Verified** (15 In Progress, 4 Blocked); production readiness is not established.


## Latest execution handoff — #16 RIDER-04 (2026-11-08)

- Active PR #120 `fix/rider-04-minimize-union-reconciliation-projection`, head `26e458a416c708880293eaad3a585a4c3598c160`.
- Exact-head CI [run #766 / 42502454708](https://github.com/rajeevp727/turnova-platform/actions/runs/42502454708) passed all 8 jobs; backend executed 324/324 tests with zero failures. Both Android debug APKs, both unsigned iOS Simulator builds, Rider/Driver typechecks/bundles, shared auth tests and native non-container API publish passed.
- Automated review is COMMENT-only and found no unresolved critical/high-risk issue; there are no unresolved inline review threads. Branch-protection settings could not be read with the connected GitHub permission. Merge must be attempted with the exact expected head so GitHub enforces all required policy; no bypass or self-approval.
- #17 DRIVER-04 remains blocked by Product/Operations support categories, required intake fields, priority/severity, escalation owner/channel, hours/response targets, attachment handling, privacy/retention and operational handoff. Do not invent a workflow.
- Next actionable item after #120: #18 PLATFORM-01. Re-read main and test-plan `PLATFORM-AUDIT-006`; add only the documented dispatch-exception audit gap if still absent. Preserve existing Rides/QueueEntries storage; no new container or telemetry vendor.
- Strict acceptance remains 0/19 Verified (15 In Progress, 4 Blocked); production readiness is not established. No live Cosmos, provider, physical-device, signing or legal acceptance is claimed.


## Latest sequential handoff — #18 PLATFORM-01 (2026-11-09)

- PR #120 RIDER-04 merged at `3e20aee9d244b6e90988669b5658824f01926e3a`; final-head CI [42508275522](https://github.com/rajeevp727/turnova-platform/actions/runs/42508275522) passed 8/8 jobs and 324/324 backend tests. Main post-merge run #768 is in progress.
- PR #121 adds safe, separately typed dispatch exception audit records to the existing `QueueEntries` partition. It records only server trace ID, claim-derived Driver actor reference, allow-listed `QueueReadFailed` category and UTC timestamp for unexpected non-cancellation queue-read exceptions during authenticated legacy Driver acceptance.
- The exception record is distinct from queue mutation audit. Successful queue mutations remain transactional with their audit event; no-op/conflict/cancellation outcomes do not create mutation or exception events. Rider discovery and selected-driver assignment remain queue-independent.
- New deterministic API and store tests verify authenticated actor scope, same trace ID, generic 500 response, unchanged ride state, safe metadata, Cab-only category allow-list and cancellation behaviour. PR #121 exact-head CI is pending.
- #17 DRIVER-04 remains blocked by Product/Operations support-policy decisions; no support workflow or SLA is invented.
- Next after PR #121 merges: #19 RELEASE-01 — audit signed Android/iOS artifacts, device acceptance and release gates. Simulator compilation is not store signing or pilot readiness.
- Strict acceptance remains 0/19 Verified (15 In progress, 4 Blocked); this is not an implementation-completion score. Production readiness is not established.


### PLATFORM-01 selected-driver exception coverage

PR #121 also records unexpected non-cancellation failures from the active Cab selected-driver reservation-store promotion (`TryMakePermanentAsync`) as `DispatchException` in the existing zone/service audit partition. The record contains only server trace ID, claim-derived Driver actor reference, allow-listed `DriverReservationFailed` or `DriverReservationPromotionRecovered` category and UTC timestamp. An unrecovered failure is rethrown through the generic 500 handler with the `AgreementConfirmed` ride and temporary reservation unchanged. If a re-read proves the same ride's permanent lease committed before an ambiguous timeout, the recovered exception is audited and the normal acceptance transition continues. Deterministic failure injection covers the selected booking path as well as the legacy queue-read compatibility path. No Rider FIFO ranking/assignment is introduced.


### PLATFORM-01 ambiguous reservation promotion

Selected-driver reservation promotion now handles an ambiguous storage outcome idempotently: after a non-cancellation exception from `TryMakePermanentAsync`, the API point-reads the reservation. If the same ride already holds a permanent lease, the accept flow appends `DriverReservationPromotionRecovered` and continues; otherwise a safe `DriverReservationFailed` event is appended and the exception is rethrown before ride transition. Deterministic tests cover both pre-commit failure (ride and temporary lease unchanged) and post-commit timeout (re-read recovers success without a duplicate exception event).


Dispatch exception event IDs are deterministic from the server-generated trace ID. Repeated writes for the same trace are idempotent in the in-memory adapter and Cosmos store; Cosmos 409 is accepted only after reading back an event with matching actor, trace and reason category. Trace IDs are constrained to a bounded Cosmos-safe character set.


## RELEASE-01 release configuration preflight (2026-11-09)

- PR #122 adds `scripts/release-preflight.cjs` and deterministic fixtures in `src/mobile/shared/releasePreflight.test.cjs`.
- Each Rider/Driver mobile CI job runs the preflight after npm postinstall icon generation and uploads a per-app `release-readiness.json` artifact.
- Structural errors fail CI: app identity/version, established Android package, icon path containment/existence, iOS device-family setting, EAS CLI minimum and development/preview/production profile shape.
- The `.env.example` API URL remains a placeholder and GitHub CI cannot inspect EAS's remote environment; the approved HTTPS production endpoint is therefore unverified. Final iOS bundle ID, EAS project ID and build-number increment policy are also explicit configuration gates. Signing credentials, physical-device/pilot acceptance and store submission remain external gates.
- Current native CI artifacts remain Android debug APKs and unsigned iOS Simulator apps; no signed AAB/IPA or production readiness is claimed.
- #15 DRIVER-04 and #17 SUPPORT-01 remain blocked by Product/Operations support-policy decisions. #6 DISPATCH-01 remains blocked by production zone/radius approval.


### RELEASE-01 preflight trust-boundary refinement (2026-11-09)

- The release preflight rejects private/reserved IPv4 and non-global/documentation IPv6 literals, plus known internal hostname suffixes and single-label hosts. It accepts syntactically public HTTPS hosts but does not perform DNS resolution or prove EAS remote environment configuration.
- A syntactically valid iOS bundle identifier remains gated until Product/App Owner verifies Apple registration. A UUID-shaped EAS project ID remains gated until Release Engineering verifies the approved remote project linkage.
- CI-visible build-number configuration requires explicit `cli.appVersionSource` (`local` or `remote`) and production `autoIncrement=true`; Release Engineering must still approve the policy and verify an actual EAS build.
- The preflight checks the configured iOS icon path independently from the general Expo icon. Structural mistakes fail CI; remote ownership, DNS/TLS, signing, device/pilot and store evidence remain external.

- Release-preflight report schema is version 2; CI-visible configuration fields are distinct from the external approval/remote-verification gates.

- Release-preflight schema-v2 tests assert that CI-visible configuration remains distinct from external approval gates and reserved example domains are rejected.

- A CI failure in RELEASE-01 exposed a placeholder bundle-ID validation escape caused by an over-escaped regex; the check now uses explicit normalized string comparisons and has exact/subdomain placeholder fixtures.


### AI-01 passenger-count and provider-privacy correction

The baseline AI-01 implementation silently defaulted an omitted passenger count to one and included passenger count in the provider prompt contract. The current follow-up keeps passenger count local, returns it as missing for manual completion, removes family/child/accessibility and other sensitive intent tokens from the provider allow-list, and clears the Rider form's stale default so discovery/booking are blocked until explicit 1–6 entry. Branch: `fix/ai-01-no-inferred-passenger-count`. AI-01 is merged through PR #123; exact-head CI [run #819](https://github.com/rajeevp727/turnova-platform/actions/runs/42523956938) passed 330/330 backend tests and 99/99 shared mobile tests. Provider sandbox, managed-secret wiring and data-processing review remain external. AI-01 is supplemental and outside the 19-area denominator.


### DRIVER-03 Unicode payslip PDF rendering (active PR #124)

- Visible glyphs use SkiaSharp/HarfBuzz raw positioned glyph runs. A separate PDFsharp 6.2.4 (MIT) layer appends low-visibility searchable Unicode text with embedded Noto fonts and ToUnicode mappings for Latin plus nine Indic scripts.
- The separate PDFsharp layer is intentional: a Skia-only positioned-text experiment did not embed a Unicode font program or preserve searchable long-name text (two backend tests failed). PdfSharpCore was removed because its transitive ImageSharp 1.0.4 dependency triggered high-severity GIF-decoder advisories; PDFsharp 6.2.4 has no ImageSharp dependency. Its transitive Pkcs 8.0.1 was high-vulnerable and is pinned to patched 10.0.13.
- Immutable Skia typefaces are process-cached; HarfBuzz shapers/placements are request-local; PDFsharp's process-wide resolver/cache append is serialized. The bundled fonts include SIL OFL 1.1 and per-family attribution notices.
- Tests cover all bundled scripts, embedded font/ToUnicode, unsupported-script 422, concurrent request isolation, long-name wrapping, immutable snapshots and API privacy. PAY-023–PAY-027 document the new rendering, fail-closed, concurrency and native-publish cases.
- The Linux x64 validation publish checks both Skia/HarfBuzz native libraries, rejects non-Linux runtime directories and optional PdfSharp.WPFonts.dll, and enforces a 120 MiB uncompressed ceiling. The check is isolated in scripts/verify-api-publish.sh and guarded by bash -n.
- External gates: live Cosmos snapshot create-conflict visibility/restart/multi-instance; physical Android/iOS PDF visual/save/share/screen-reader acceptance; Product confirmation of DaysWorked semantics; Arabic/Sinhala/emoji and other unbundled scripts remain unsupported and return 422.


## Latest handoff — DRIVER-03 Unicode payslip and current-head CI blocker (2026-11-09)

- Active PR #124, branch fix/driver-03-unicode-payslip-rendering; current head before this docs sync is f2bf1aa279c565c17a2e02b91a852090d17f796b.
- Run #876 attempt 1 on superseded head ebcb889c9a8ac8ab8089cad4cc243b7af7a94f33: backend 340/341 with one AuthApiIntegrationTests startup-validation ObjectDisposedException during WebApplicationFactory teardown. Only the failed backend job was rerun; attempt 2 passed, so run #876 is 8/8 on that old head. Android and both iOS builds passed. AuthApiIntegrationTests are now in a non-parallel xUnit collection.
- Push runs #878–#882 had zero jobs because the default-branch workflow still filters pushes to main; the fix/feat/chore filter in this PR applies after merge. API-authored close/reopen did not trigger a pull_request run. The current head requires an owner-originated PR event or manual CI run before merge; no second PR is created.
- Native publish verification is in scripts/verify-api-publish.sh. scripts/test-api-publish-verifier.sh validates a valid Linux-x64 fixture and rejects missing Skia/API assemblies, non-Linux runtime directories, PdfSharp.WPFonts.dll and output over 120 MiB. Both scripts pass bash -n and the fixture suite passed locally.
- The PDF uses SkiaSharp/HarfBuzz visible glyph shaping and a separate PDFsharp 6.2.4 searchable Unicode layer with embedded Noto SIL OFL fonts and ToUnicode. PAY-023–PAY-027 document supported scripts, fail-closed unsupported scripts, concurrent isolation, long-name wrapping and publish artifact validation.
- Review history is COMMENT-only; no self-approval. No unresolved inline threads were found at the prior review checkpoint; recheck after the latest commit.
- Live Cosmos snapshot recovery, physical Android/iOS PDF visual/save/share/accessibility, Product confirmation of DaysWorked and additional script requirements remain external.
- Strict acceptance remains 0/19 Verified (15 In Progress, 4 Blocked). After #14, #15 DRIVER-04 remains blocked on support policy; continue to #16 RIDER-04 (query-level Cab-only Cosmos filter + deterministic SQL assertion), #17 SUPPORT-01 (same support-policy blocker), #18 PLATFORM-01 (mocked Cosmos 409/read-back idempotency tests) and #19 RELEASE-01 external release gates.



## Latest execution handoff — #14 DRIVER-03 (2026-11-09)

- Sole active PR #124, branch `fix/driver-03-unicode-payslip-rendering`, head `9467838172c27b896075c72373c0852352f76fb8`. Exact-head CI #901 / 42545087180 passed all 8 jobs, including 341/341 backend tests, 102/102 shared auth tests, Android debug APKs, unsigned iOS Simulator apps, mobile checks and native Linux x64 API publish plus verifier fixtures.
- Reviews are COMMENT-only; no unresolved inline threads. Recheck exact head and repository policy before merge; do not self-approve or bypass.
- After merge, re-read main and continue sequentially: #15 DRIVER-04 is blocked on approved support workflow inputs; #16 RIDER-04's next safe increment is a query-level case-insensitive Cab predicate plus SQL test; #17 SUPPORT-01 remains blocked; #18 PLATFORM-01 and #19 RELEASE-01 are merged and must be re-audited for any new deterministic gaps.
- Strict acceptance is 0/19 Verified, 15 In Progress and 4 Blocked. Production readiness is not established; live Cosmos, physical-device, signing/store, provider, legal and hosting gates remain external.


## Latest sequential handoff — #16 RIDER-04 Cab-only reconciliation query filter (2026-11-09)

- DRIVER-03 PR #124 merged at `e9a86abfcff9d947f0fb26864d74fb20a57875fe`. Exact-head CI #902 / 42546820729 passed 8/8 on `87b7c96e175709b639c58f33c57b4b1185ad8209`, including 341/341 backend tests and 102/102 shared mobile auth tests; both Android and iOS Simulator builds, mobile typechecks/exports and native Linux x64 API publish/verifier passed.
- #15 DRIVER-04 is Blocked until Product/Operations approve issue categories, required fields, priority/severity, escalation owner/channel, support hours/targets, attachments, privacy/retention and handoff. Do not invent a support workflow or API contract.
- #16 current branch `fix/rider-04-cab-filter-reconciliation-query` is based on main `e9a86abfcff9d947f0fb26864d74fb20a57875fe`. It adds `LOWER(c.serviceClass) = 'cab'` to the Cosmos UnionManager reconciliation query while retaining signed ride/report Union predicates and the API post-filter. Projection, response, immutable report, idempotency and signed-delta semantics are unchanged. Test plan adds CAB-REC-013 and a deterministic query-contract regression; exact-head CI pending.
- #17 SUPPORT-01 remains Blocked by the corresponding Rider help/issue category, intake, escalation, service-hours, attachment, privacy/retention, audit and operational handoff decisions.
- #18 PLATFORM-01 already has safe dispatch exception auditing and ambiguous reservation-promotion recovery. Next independent code gap after #16: deterministic Cosmos 409/create-conflict read-back/idempotency tests for matching versus mismatched actor/trace/reason; preserve the existing QueueEntries partition.
- #19 RELEASE-01 preflight is merged; signed Android/iOS store artifacts, production API/EAS configuration, signing credentials, physical-device/pilot acceptance and store submission remain external.
- Strict acceptance remains 0/19 Verified (15 In Progress, 4 Blocked). This is not an implementation-completion percentage. No live Cosmos, physical-device, signed-production or production-hosting validation is claimed.


## Latest sequential handoff — #18 PLATFORM-01 dispatch-exception conflict recovery (2026-11-09)

- Latest main base: `8368b0ef518bda956442e99f5e9cc343a332bba8`; PR #133 removed the prohibited container deployment files and duplicate Android workflow. Native non-container .NET publish remains the only CI hosting artifact; no API deployment is claimed.
- #16 RIDER-04 Cab-only reconciliation query filtering merged in PR #125 at `552dadea76215fa4a1f67c545e04a79070b6d44f`. #15 DRIVER-04 and #17 SUPPORT-01 remain blocked by explicit Product/Operations support policy inputs; continue with independent engineering work.
- Current #18 branch: `feat/platform-01-idempotent-dispatch-exception-audit`. A narrow persistence interface and Cosmos adapter now isolate deterministic dispatch-exception writes. Conflict retries require same-partition canonical read-back matching event ID, event type, partition, actor, trace, reason and no offer ID; missing/mismatched items fail closed. No new Cosmos container or provider is introduced.
- Eleven deterministic writer tests cover successful create, exact matching read-back despite retry timestamp drift, seven mismatch variants, missing read-back and cancellation. Exact-head CI/review pending; no local .NET run or live Cosmos validation is claimed.
- External #18 gates: live Cosmos 409 visibility/read-after-write consistency, partition isolation, multi-instance/restart/retention, telemetry export/alerts, production CORS origins, load testing and device acceptance.
- #19 RELEASE-01 remains next after #18: re-audit Android/iOS native artifact/signing readiness and physical-device/pilot evidence. Signed store artifacts, production API/EAS linkage, signing credentials, physical-device acceptance and store submission remain external.
- Strict acceptance remains **0/19 Verified** (15 In Progress, 4 Blocked). This is not a measure of implementation completion. Production readiness is not established.

## Latest TurnOva handoff — #19 RELEASE-01 native artifact identity (2026-11-09)

- PLATFORM-01 PR #134 merged at 5f6f9cf3d25ac0fb04739b197c65fa0260d05639; exact head 37fe05de79ca8e5f97a49bfcf8ed48d7332164f0 passed CI run 42562772947, 8/8 jobs. No self-approval; main was unprotected and no rulesets were present. Live Cosmos 409/read-after-write validation remains external.
- Main was re-read at 5f6f9cf3d25ac0fb04739b197c65fa0260d05639; no open PRs remained. Active branch is fix/release-01-native-artifact-identity-verification.
- RELEASE-01 increment adds actual Android APK manifest package-ID verification using aapt, actual iOS Simulator Info.plist bundle-ID verification using plistlib, and 12 deterministic Python tests. The Android ID is checked against expo.android.package. The iOS ID is the CI-only value passed to xcodebuild and is not treated as Product approval or Apple registration.
- CI runs the Python verifier tests and both native artifact checks. Code-bearing head `1827b98fb64773f8ebdc75edbdda2f86ca90d4fb` passed run [#42565547874](https://github.com/rajeevp727/turnova-platform/actions/runs/42565547874), 8/8, including 12 deterministic tests, both Android release-variant APK identity checks and both iOS Simulator Info.plist identity checks. Subsequent documentation-only sync is tracked on the current PR #135 head; its exact-head result is available in the PR Checks tab. Release docs and test plan keep signed AAB/IPA, EAS, API URL, signing, physical-device, pilot and store gates external.
- Strict acceptance remains 0/19 Verified (15 In Progress, 4 Blocked); implementation evidence and production readiness are reported separately.
- Next: recheck the latest PR #135 head, its final-head CI and COMMENT-only review; merge only if all required checks are green and GitHub policy permits without self-approval. Then re-read main and continue sequential re-audit. No containers or production deployments.

- RELEASE-01 implementation is tracked in [PR #135](https://github.com/rajeevp727/turnova-platform/pull/135), branch fix/release-01-native-artifact-identity-verification; initial code-bearing head was 9d855b870d4212e1240d6ab8073d87021ccb31e1. Later documentation-only commits do not change the native verifier implementation; use the live PR head for final CI.

- #16 RIDER-04 Cab-only reconciliation query filter merged in PR #125 at 552dadea76215fa4a1f67c545e04a79070b6d44f. Its initial push run #42552962718 failed before job creation (zero jobs); later main CI [42561935666](https://github.com/rajeevp727/turnova-platform/actions/runs/42561935666) passed 8/8 on 8368b0ef518bda956442e99f5e9cc343a332bba8 and includes the merged query change. Live Cosmos SQL/index/partition validation remains external.


## Latest sequential handoff — supplemental AI-01 help output bounds (2026-11-09)

- RELEASE-01 PR #135 merged at `b999288babb058f12935e857c19fcbd7a9a81f92`; exact final-head CI [42568085512](https://github.com/rajeevp727/turnova-platform/actions/runs/42568085512) passed all 8/8 jobs on `287699591c6b81c135fce38274bd88047da7605b`. The run verifies both standalone Android release APK package IDs against Expo config, both iOS Simulator built `Info.plist` identifiers against CI-only IDs, 12 deterministic Python tests, backend and mobile validation/builds.
- RELEASE-01 remains externally Blocked pending production signing, Apple registration, EAS project/production API configuration, approved build-number policy, physical-device/pilot acceptance and store submission.
- AI-01 supplemental work now bounds every help response. Known ride-state explanations and provider-selected approved content that exceed `MaxOutputCharacters` are replaced by a short deterministic escalation response; unsupported fallback is also bounded. Invalid direct service configuration that cannot fit the fallback is rejected. Four deterministic service tests and AI-022–AI-025 test-plan cases were added. Code-bearing exact-head CI [42570362124](https://github.com/rajeevp727/turnova-platform/actions/runs/42570362124) passed all 8 jobs, including 357/357 backend tests.
- PR [#136](https://github.com/rajeevp727/turnova-platform/pull/136) is open from branch `fix/ai-01-bound-help-output`, based on `b999288babb058f12935e857c19fcbd7a9a81f92`. Implementation/test commits include `6a39e6e487631f7ae5383cf10974477bac16e0c1`, `23fd76acffd6537483aaac75b30ceee1f696e3cb`, `917a0a047cd59d13414611901d3bd0614fcec0e9` and `ab595b0fbf2d58ef7aa37b29b219aad129e383dd`; documentation sync is part of the live PR head. COMMENT-only review was submitted on code-bearing head `36350ab0bed8e729b7b8b18853a1f50f1b8b1338`; exact-head CI [42570362124](https://github.com/rajeevp727/turnova-platform/actions/runs/42570362124) passed 8/8 jobs, including 357/357 backend tests. A later documentation-only sync is on the live PR head; final-head CI is pending.
- #15 DRIVER-04 and #17 SUPPORT-01 remain blocked on Product/Operations-approved support intake and escalation policy. Continue independent safe code/test work; do not invent categories, SLA, contacts or data retention.
- Strict acceptance remains 0/19 Verified (15 In Progress, 4 Blocked). AI-01 is supplemental and excluded from the denominator. No live provider/Cosmos, physical-device, signing, legal or production-hosting evidence is claimed.


## Latest execution handoff — #16 RIDER-04 fallback legacy non-Cab isolation (2026-11-09)

- Main base: `beb552dcc08c7b9a8b48e1c981465c1bbdfb35ca`; supplemental AI-01 PR #136 is merged. Its exact final-head CI [42571952145](https://github.com/rajeevp727/turnova-platform/actions/runs/42571952145) passed 8/8, including 357/357 backend tests and both Android standalone release APKs.
- #16 RIDER-04 [PR #137](https://github.com/rajeevp727/turnova-platform/pull/137) already has the Cosmos UnionManager reconciliation query filter `LOWER(c.serviceClass) = 'cab'` before the minimum-data projection and retains the API case-insensitive post-filter. The current increment adds deterministic in-memory regressions for uppercase `CAB`, retained `Scooty` history isolation and rejection of direct-payment reporting for historical non-Cab rides without mutating the legacy record.
- #15 DRIVER-04 and #17 SUPPORT-01 remain blocked by Product/Operations support-policy inputs; do not invent categories, fields, SLAs, channels or retention rules.
- External #16 gates remain live Cosmos SQL/index/partition/multi-instance/restart and physical-device reconciliation acceptance, plus unresolved customer dispute/confirmation and non-zero-delta next-ride policy. No production validation is claimed.
- Strict acceptance remains 0/19 Verified (15 In Progress, 4 Blocked); this is not an implementation-completion measure.


## Latest sequential handoff — #18 PLATFORM-01 reservation audit (2026-11-09)

- Main HEAD is `a638e6f2ccc10e3a5aed9d067bf5827873aadcfe`; RIDER-04 PR #137 merged with exact-head CI #42578080973 passing 8/8 and 359/359 backend tests.
- #17 SUPPORT-01 was audited across PRD, Rider/Driver app screens and API endpoints. Approved-content help and unsupported-question escalation exist; no issue-report intake/persistence/attachments/escalation route exists. Product/Operations must approve categories, required fields, severity, escalation owner/channel, hours/targets, attachment rules, consent/privacy/retention, audit and handoff. Keep Blocked and do not invent a generic free-text intake or contact route.
- #18 PLATFORM-01 PR #138 adds reservation lifecycle audit facts to the existing DriverRideReservations container in the existing /driverId partition. Reservation create, ETag replace and ETag delete are paired with audit-event creation in a single transactional batch. The in-memory implementation uses one CAS state containing the active reservation and immutable event history. CI and live Cosmos gates remain pending.
- Next: inspect PR #138 complete diff and exact-head CI; fix any compile/test/review issues; merge only if exact-head checks pass, no unresolved high-risk review issue exists, and repository policy permits without self-approval or bypass. After merge, re-read main and proceed to #19 RELEASE-01 audit.


## Latest sequential handoff — #18 PLATFORM-01 concurrent dispatch-exception audit retries (2026-11-09)

- Latest main HEAD: `a95c6219fffc164213b284e023d10019e12cb900`; PR #138 reservation lifecycle audit is merged. Exact-head CI [#42581364351](https://github.com/rajeevp727/turnova-platform/actions/runs/42581364351) passed 8/8, including 361/361 backend tests and Rider/Driver native build evidence.
- Current PR #139 adds PLAT-AUDIT-012–014: concurrent identical trace writes create one canonical event and verify the conflict read-back; same-trace/different-actor reuse fails closed; cancellation during canonical conflict read-back propagates. The barrier-controlled fake is deterministic unit evidence only, not live Cosmos validation.
- Code-bearing head `e2e6637fea7d64b49c5419f821dcdd0012c323db` passed exact-head CI [#42593042733](https://github.com/rajeevp727/turnova-platform/actions/runs/42593042733), 8/8 including 362/362 backend tests. Previous docs-only head `0e0fa7d82787cfb4656b9c5a94e29d2826a4e660` passed [#42594918895](https://github.com/rajeevp727/turnova-platform/actions/runs/42594918895), 8/8. This commit reconciles docs with PR #138's merge; require fresh exact-head CI and a final COMMENT review before merge.
- #15 DRIVER-04 and #17 SUPPORT-01 remain blocked on Product/Operations-approved categories, intake fields, priority/severity, escalation owner/channel, hours/targets, attachment rules, consent/privacy/retention and operational handoff. No support schema or SLA is invented.
- #16 RIDER-04 has deterministic Cab-only reconciliation, historical non-Cab preservation and no-mutation rejection evidence; live Cosmos SQL/index/partition/multi-instance/restart, Android/iOS report-entry/history acceptance and unresolved dispute/next-ride/cross-Union policies remain.
- #18 PLATFORM-01 external gates remain live Cosmos 409/read-after-write and transactional-batch/ETag/partition/multi-instance/restart/retention, telemetry export/alerts, production CORS origins, load and device acceptance.
- Next sequential row after #139 merges: #19 RELEASE-01 — add Android emulator install/launch smoke for both standalone release APKs. Signed AAB/IPA, EAS/API production configuration, Apple registration, signing credentials, physical-device/pilot acceptance and store submission remain external.
- Strict acceptance remains **0/19 Verified** (15 In Progress, 4 Blocked); implementation evidence is separate and production readiness is not established.


## Latest sequential handoff — #18 PLATFORM-01 final concurrency-test refinement (2026-11-09)

- Main HEAD: `a95c6219fffc164213b284e023d10019e12cb900`; PR #138 reservation lifecycle audit is merged and main exact-head CI [#42581364351](https://github.com/rajeevp727/turnova-platform/actions/runs/42581364351) passed 8/8.
- Active PR #139: `test/platform-01-dispatch-audit-concurrency`. It adds deterministic tests for concurrent identical dispatch-exception writes, same-trace/different-actor fail-closed behaviour, and cancellation during canonical conflict read-back. Code-bearing CI [#42593042733](https://github.com/rajeevp727/turnova-platform/actions/runs/42593042733) passed 8/8 (362/362 backend tests); branch-sync head CI [#42597528464](https://github.com/rajeevp727/turnova-platform/actions/runs/42597528464) passed 8/8.
- The latest test refinement now cancels the real token source from inside the fake canonical `ReadAsync` and asserts the same token propagates. It is test-only; no runtime/storage/authorization/dispatch behaviour changes. The latest branch head must pass exact-head CI and receive a final COMMENT-only review before merge.
- After PR #139 merges, re-read main and proceed to #19 RELEASE-01. Next safe task: install and launch both standalone release APKs on Android emulators in CI, with deterministic package/activity/startup assertions. Do not treat emulator smoke as physical-device or production acceptance.
- #18 external gates: live Cosmos transactional batch/ETag, 409 read-after-write, partition isolation, multi-instance/restart/retention, telemetry export/alerts, production CORS origins, load and physical-device acceptance.
- #15 DRIVER-04 and #17 SUPPORT-01 remain blocked pending Product/Operations-approved support categories, required intake fields, severity, escalation owner/channel, hours/targets, attachment rules, consent/privacy/retention and handoff. No support schema/SLA is invented.
- Strict acceptance remains 0/19 Verified (15 In Progress, 4 Blocked); implementation evidence is separate and production readiness is not established.


## Latest sequential handoff — #19 RELEASE-01 native install/launch smoke (2026-11-09)

- Main HEAD is `c9d7276c1af5b6b726add2f0d158dbba295282a7` after PLATFORM-01 PR #139 merged. Exact PR-head CI [#42601098085](https://github.com/rajeevp727/turnova-platform/actions/runs/42601098085) passed 8/8 on `77e7bbd7408e7e77464edb610b988da563d936e1`, including 364/364 backend tests.
- Current #19 branch is `test/release-01-native-launch-smoke` (PR #140 in preparation). It adds native install/launch checks for both standalone Android release APKs in an API 35 emulator and both unsigned iOS Simulator .app artifacts in an iPhone Simulator. Checks cover expected package/bundle, install, launch activity/process survival, startup crash markers and screenshot/diagnostic artifacts.
- The simulator tests consume the exact artifacts built earlier in the same CI run. They deliberately do not invoke production APIs, authentication providers, maps, SignalR, push or any credentialed external service.
- Exact-head CI for PR #140 is pending. Do not merge until all 12 jobs pass on the exact final SHA and a COMMENT-only review finds no critical/high-risk issue; do not self-approve or bypass repository policy.
- #19 remains Blocked pending signed AAB/IPA, approved and Apple-registered iOS bundle IDs, EAS project linkage, production HTTPS API environment/DNS/TLS/CORS/readiness, approved build-number strategy, signing credentials, physical-device functional/security/pilot acceptance and store submission. Emulator/Simulator smoke is not physical-device or production acceptance.
- #18 PLATFORM-01 still needs live Cosmos transactional-batch/ETag/409 read-after-write/partition/multi-instance/restart/retention, telemetry export/alerts, production CORS, load and device evidence. #15 DRIVER-04 and #17 SUPPORT-01 remain blocked on Product/Operations support-policy inputs.
- Strict acceptance remains 0/19 Verified (15 In Progress, 4 Blocked). This is a strict external-evidence count, not an implementation-completion percentage; production readiness is not established.


## Latest sequential handoff — #19 RELEASE-01 Android AVD bootstrap correction (2026-11-09)

- Main HEAD remains `c9d7276c1af5b6b726add2f0d158dbba295282a7`; PLATFORM-01 PR #139 is merged. Main exact-head CI [#42601098085](https://github.com/rajeevp727/turnova-platform/actions/runs/42601098085) passed 8/8, including 364/364 backend tests and Rider/Driver native build evidence.
- PR #140 initial head `fe8f9e52d696a141422271a7d94e5ce9dbc1e700` ran CI [#42606662115](https://github.com/rajeevp727/turnova-platform/actions/runs/42606662115). The original eight backend/mobile/native-build/auth jobs passed; both Android emulator smoke jobs failed before ADB device registration. Their uploaded `emulator.log` showed `Unknown AVD name [turnova-release-smoke]` because the script did not explicitly pin the AVD home. The iOS Simulator jobs were still running at the last poll; no iOS smoke result is claimed from that run.
- The Android script now exports `ANDROID_AVD_HOME` explicitly before AVD creation and launch, verifies the expected `.ini` and `.avd` exist, captures AVD-manager diagnostics, detects an emulator process that exits before ADB registration, and emits emulator logs on readiness timeout. Startup crash checks are scoped to the target app/package and process to avoid unrelated system-log false positives.
- Code-bearing correction head `90df49fd4c28d59a308d92f937d3e597c37ff2a7`; a synchronized docs commit follows. Fresh exact-head CI is required. Do not merge unless all 12 jobs pass on the final head and a COMMENT-only review finds no critical/high-risk issue; no self-approval or bypass.
- #19 remains Blocked pending signed AAB/IPA, approved and Apple-registered iOS bundle IDs, EAS project linkage, production HTTPS API/DNS/TLS/CORS/readiness, approved build-number strategy, signing credentials, physical-device functional/security/pilot acceptance and store submission. Simulator smoke is engineering evidence only.
- #18 PLATFORM-01 retains live Cosmos transactional-batch/ETag/409 read-after-write/partition/multi-instance/restart/retention, telemetry export/alerts, production CORS, load and device gates. #15 DRIVER-04 and #17 SUPPORT-01 remain blocked on Product/Operations-approved support-policy inputs.
- Strict acceptance remains **0/19 Verified** (15 In Progress, 4 Blocked). This is a strict external-evidence count, not an implementation-completion percentage; production readiness is not established.


## RELEASE-01 deterministic Android emulator harness (2026-11-09)

- The first PR #140 run [#42606662115](https://github.com/rajeevp727/turnova-platform/actions/runs/42606662115) passed the original eight jobs but both Android smoke jobs failed before ADB registration because the emulator could not find the AVD. The log recorded `Unknown AVD name [turnova-release-smoke]`.
- The Android script now exports `ANDROID_AVD_HOME` before both `avdmanager` and `emulator`, checks the named `.ini` and `.avd` files, records AVD-manager output, detects early process exit and scopes startup crash checks to the target package/process.
- A new standard-library fake-SDK test exercises a non-default AVD home, AVD creation, fake install/activity launch/process stability, result JSON/screenshot and malformed package rejection without a real emulator or network. CI now discovers all `scripts/tests/test_*.py` tests.
- The corrected exact-head 12-job CI run must pass before merge. This deterministic harness does not replace actual emulator smoke or physical-device acceptance.

### RELEASE-01 fake-device test correction

- The first fake-SDK execution exposed that awk exits zero by default even when no React Native marker matches. The Android script now records a match and explicitly exits nonzero on no match, so the enclosing `if` fails only for a target-process startup marker.
- The fake ADB emits an unhandled marker under unrelated PID 1111 for the passing case and under target PID 4242 for a negative case. The harness asserts that unrelated logs are ignored and target-app failures are rejected.

## RELEASE-01 login UI assertion increment (2026-11-09)

- Android emulator smoke now dumps the fresh-install UI hierarchy after launch and asserts the login title, email/password hints and sign-in action. The screenshot and hierarchy are retained as CI artifacts.
- iOS Simulator smoke runs a local Swift/Vision OCR checker against the captured screenshot and asserts the same login-surface text. It does not enter credentials or call API/authentication services.
- These UI checks complement process-stability, package/bundle identity and existing functional/security test evidence. They do not establish physical-device, pilot or store readiness.


### RELEASE-01 UI hierarchy fake-device fixture correction

- The first run after adding Android login UI assertions failed in the fake-SDK test because its mock ADB did not implement `uiautomator dump` or `cat`. The mock now returns deterministic XML nodes for the login title, email/password hints and sign-in action, and the harness asserts them.
- The actual Android emulator job must still prove that the platform UI hierarchy command works and the app renders the login screen. The iOS job uses local Vision OCR against the captured simulator screenshot.


### RELEASE-01 hosted Android KVM permission correction (2026-11-09)

- Exact-head run [#42611168422](https://github.com/rajeevp727/turnova-platform/actions/runs/42611168422) passed the eight backend/mobile/native-build/auth jobs; both Android emulator jobs failed before app install because the runner lacked read/write access to `/dev/kvm`. The uploaded emulator diagnostics explicitly reported the KVM group permission issue. The iOS Simulator smoke jobs were still running at last poll.
- CI workflow commit `eed87f2b58272eadf8d71b9d3fc6465ecac0268d` adds a fail-closed KVM preflight; follow-up commit `e6e1dcf426c9f0d934372e6383a82811d9aa6a1a` assigns `/dev/kvm` to the ephemeral runner user with owner-only mode `0600` and rechecks read/write permissions before starting the emulator. This is not a production infrastructure change and does not count as app launch evidence.
- Require a fresh exact-head 12-job run, inspect Android Rider/Driver login UI hierarchy and iOS Vision OCR artifacts, then a COMMENT-only final review and normal policy-compliant merge. No self-approval/bypass.
- #19 remains externally Blocked on signed AAB/IPA, approved bundle identifiers/EAS linkage, production API environment, signing, physical-device/pilot and store gates. Strict acceptance remains 0/19 (15 In Progress, 4 Blocked); no production readiness is claimed.


## Latest sequential handoff — #19 RELEASE-01 clean simulator state (2026-11-09)

- Main HEAD before PR #141: `4521c7e9a1448f85208ca744cd4824fd2a02596f`, after PR #140 merged. Exact-head CI [#42613834167](https://github.com/rajeevp727/turnova-platform/actions/runs/42613834167) passed 12/12; backend 364/364. All four simulator smoke jobs passed install/launch/process checks and required login UI text.
- Artifact inspection found Android Rider/Driver screenshots clean. iOS Rider/Driver screenshots both visibly show `Unable to restore your session. Check your connection or secure storage and retry.` The prior script installed into an available simulator without erasing its device/keychain state. Persistent Keychain data is a plausible cause but is not yet proven.
- PR #141 branch `test/release-01-clean-simulator-state` resets the iOS simulator before install, rejects stale session/secure-storage error text through Vision OCR, adds the equivalent Android UI hierarchy negative assertion, and adds deterministic fake-SDK coverage. No app business/runtime code or production configuration is changed.
- Exact-head CI must prove the clean-state result on both iOS apps; if the banner remains after erase, investigate the actual SecureStore restore path instead of weakening the assertion.
- #19 remains externally Blocked pending signed AAB/IPA, Apple-approved identifiers, EAS linkage, production HTTPS API/DNS/TLS/CORS/readiness, build-number policy, signing credentials, physical-device/pilot acceptance and store submission. #18 live Cosmos/telemetry/CORS/load/device gates remain. #15 DRIVER-04 and #17 SUPPORT-01 remain blocked on Product/Operations-approved support policy.
- Strict acceptance remains 0/19 Verified (15 In Progress, 4 Blocked); production readiness is not established.


### RELEASE-01 OCR line-wrap regression (2026-11-09)

Apple Vision may split a visible session-restore banner across multiple recognized lines. The iOS smoke checker normalizes whitespace before matching required and forbidden text; the fake-SDK test emits the stale-session phrase across a line break and must fail. PR #141's updated exact-head 12-job CI is the merge gate. No app runtime or production configuration changed.


### RELEASE-01 SecureStore Keychain test signing (2026-11-09)

The iOS clean-state smoke continued to show `Unable to restore session` after `simctl erase`. The native simulator artifact is deliberately built with `CODE_SIGNING_ALLOWED=NO`; Expo SecureStore accesses the Keychain, so the unsigned app cannot validate the empty-Keychain path. PR #141 now ad-hoc signs only the ephemeral smoke copy with bundle-scoped application/keychain entitlements and verifies the embedded values before install. The original unsigned artifact remains the compile/identity artifact. This is not Apple Developer signing or production readiness. Keep the stale-session assertion fail-closed; investigate further if the banner persists.


## Latest sequential handoff — #19 RELEASE-01 cold simulator boot bound (2026-11-09)

- Latest main/base HEAD remains `4521c7e9a1448f85208ca744cd4824fd2a02596f`; PR #141 branch head is `dc5d21327a843d46ed507603e5eed605af1b3f6a`. Main is 0 commits ahead/behind the PR base; no parallel feature PR is open.
- Prior run [#42628714272](https://github.com/rajeevp727/turnova-platform/actions/runs/42628714272) showed Driver iOS Simulator first-boot Data Migration/LaunchServicesMigrator waiting and a nonzero bootstatus exit before app installation; Rider iOS was still in progress when superseded. Both Android emulator smoke jobs and all backend/mobile/native build jobs completed successfully.
- The smoke script now runs `xcrun simctl bootstatus <UDID> -b` under Python `subprocess.run(timeout=300)`, with a validated 1–600 second override. The fake-SDK suite asserts exact bootstatus args, nonzero exit fail-closed behaviour and a real 1-second timeout using a sleeping fake executable.
- Exact-head CI [#1035 / 42633933073](https://github.com/rajeevp727/turnova-platform/actions/runs/42633933073) is pending on `dc5d21327a843d46ed507603e5eed605af1b3f6a`. Next: poll every job; inspect Rider/Driver iOS smoke artifacts and Vision OCR; if boot still times out, capture simulator state/boot logs and use a runner-only recovery or increase the bounded timeout based on evidence, not by weakening the stale-session assertion. Submit a new COMMENT-only review against the final SHA, then merge only if all required checks pass and repository policy permits.
- External gates: signed AAB/IPA, Apple-approved bundle IDs/provisioning, EAS linkage, production HTTPS API/DNS/TLS/CORS/readiness, build-number policy, physical Android/iOS devices, pilot acceptance and store submission. Other rows retain live Cosmos/telemetry/CORS, Product dispatch radius/zones/freshness and Product/Operations support-policy gates.
- Strict acceptance remains 0/19 (15 In Progress, 4 Blocked); no production readiness is claimed.


## Latest sequential handoff — #19 RELEASE-01 iOS smoke diagnostics (2026-11-09)

- Main/base HEAD: `4521c7e9a1448f85208ca744cd4824fd2a02596f`; one active PR only: #141, branch `test/release-01-clean-simulator-state`.
- Exact-head run [#1038 / 42634056917](https://github.com/rajeevp727/turnova-platform/actions/runs/42634056917), head `03602ad2ad1348a17559213b10476ab205458361`: 10/12 jobs passed, including backend 364/364, Rider/Driver mobile, shared tests, four native builds and two Android emulator smokes.
- Rider iOS timed out after 300 seconds in first-boot simulator readiness before app installation (exit 124). Driver iOS bootstatus reached Finished, then the smoke exited 1 before producing launch/screenshot artifacts. The script's command-substitution + `set -e` path hid the failing install/container/launch output; the exact Driver stage is not yet known.
- The active branch now raises the default boot bound to 600 seconds (still validated 1–600), prints captured install/container/launch command output, and adds deterministic fake-SDK tests for a slow default boot and install/launch failures. Stale-session OCR remains fail-closed.
- Next exact action: poll the new final-head CI, inspect both iOS smoke artifacts/screenshots, confirm required login text and absence of stale-session/storage banners, then COMMENT-only review the final SHA. Merge only if every required check passes and repository policy permits; no self-approval/bypass.
- External RELEASE-01 gates: production-signed AAB/IPA, approved/registered identifiers, EAS linkage, HTTPS production API configuration, physical Android/iOS testing, pilot acceptance and store submission. Other external gates remain Product-approved dispatch radius/zones/freshness, support policy, live Cosmos ETag/multi-instance/restart, telemetry export/alerts, production CORS, maps/realtime providers, private document controls and legal partnership terms.
- Strict acceptance remains 0/19 (15 In Progress, 4 Blocked); no production readiness is claimed.


## Latest sequential handoff — #19 RELEASE-01 iOS SpringBoard launch denial (2026-11-09)

- Active PR #141: `test/release-01-clean-simulator-state`; current head before diagnostic increment `1223e5989e8bc360c575f4498a6aa65da13332c5`.
- Exact-head CI [#1041 / 42639619625](https://github.com/rajeevp727/turnova-platform/actions/runs/42639619625) passed 10/12 jobs. Backend 364/364, native publish, both mobile validation/bundles, shared tests, all native builds and both Android emulator smokes passed.
- Both iOS simulator jobs passed ad-hoc signing and embedded-entitlement validation, clean erase/boot, install and app-container lookup. `simctl launch` failed with SpringBoard `SBMainWorkspace` denial (`FBSOpenApplicationServiceDomain` code 1); no screenshot was produced. This is not a SecureStore result. The prior unsigned-Keychain hypothesis is unconfirmed.
- Next increment adds best-effort failure-only simulator unified logs, launchctl state, installed-app inventory, signature metadata and embedded entitlements, covered by a deterministic fake-SDK test. Inspect actual logs before changing entitlement/signing behaviour. Keep stale-session OCR fail-closed.
- External gates remain: signed AAB/IPA, approved identifiers/EAS, production HTTPS API/DNS/TLS/CORS/readiness, physical Android/iOS testing, pilot and store acceptance; other product/infra gates remain recorded in the matrix/progress docs.
- Strict acceptance remains 0/19 (15 In Progress, 4 Blocked); production readiness is not established.


### RELEASE-01 diagnostic harness correction (2026-11-09)

Run [#1045 / 42644957570](https://github.com/rajeevp727/turnova-platform/actions/runs/42644957570) exposed a test-only assertion defect: the fake xcrun log records the full `spawn ... log show --last 5m --style compact --predicate ...` invocation, while the test expected an exact shorter string. The command executed and the diagnostic capture path ran; the test failed before checking the files. The assertion now matches the command prefix, and fake codesign emits deterministic signature/entitlement content which is asserted from the output files. All 102 shared JavaScript tests and 22/23 Python tests passed in that attempt; the remaining jobs were cancelled after a later branch update. Require fresh exact-head CI before counting this increment.

## Latest sequential handoff — #19 RELEASE-01 unsigned iOS Simulator boundary (2026-11-09)

- Main/base HEAD remains `4521c7e9a1448f85208ca744cd4824fd2a02596f`; one active feature PR only: #141, branch `test/release-01-clean-simulator-state`. Pre-fix branch head was `a6de23bef4891420a991d764398b137b9b4d23c8`.
- Exact-head CI [#1049 / 42645314471](https://github.com/rajeevp727/turnova-platform/actions/runs/42645314471) passed 10/12 jobs: backend 364/364, both mobile validation/bundle jobs, shared JavaScript tests 102/102, Python fake-SDK/native artifact tests 23/23, all four native builds and both Android emulator smoke jobs. Both iOS simulator smoke jobs failed at launch; no iOS screenshots were produced.
- Inspected failure artifacts [Rider 13688114910](https://github.com/rajeevp727/turnova-platform/actions/runs/42645314471/artifacts/13688114910) and [Driver 13688154719](https://github.com/rajeevp727/turnova-platform/actions/runs/42645314471/artifacts/13688154719). Their unified logs explicitly report `runningboardd ... spawn failed, error=162: Codesigning issue`; the ephemeral ad-hoc signatures show `TeamIdentifier=not set`. This confirms the ad-hoc signing attempt caused launchd rejection. It is not a SecureStore runtime result.
- The original native iOS artifacts are built unsigned. The previously successful unsigned simulator run launched both apps and displayed `Unable to restore your session. Check your connection or secure storage and retry.`. In this fresh, tokenless CI path, `restoreStoredSession` reads SecureStore before any API call; this banner is the expected fail-closed result of unavailable simulator Keychain access.
- Current increment removes ad-hoc signing, rejects signed copies, uses the original unsigned artifact, and changes Vision OCR to require the exact SecureStore-unavailable banner while still rejecting expired-session, token-cleanup and other unexpected storage errors. Deterministic fake-SDK tests cover the expected and missing banner paths. This is startup/fail-closed evidence only; it does not claim Keychain success, persistent token storage or clean-session restoration.
- Exact-head CI after this correction is pending. Next: poll all 12 jobs on the new SHA; inspect both iOS screenshots/OCR and confirm unsigned install/launch/process stability plus expected banner; COMMENT-only review the final SHA; merge only if all required checks pass, no high-risk issue remains and repository policy permits. Never self-approve or bypass.
- #19 RELEASE-01 remains externally Blocked pending Apple-approved bundle IDs, EAS project linkage/build-number policy, production HTTPS API/DNS/TLS/CORS/readiness, production signing credentials, signed physical-device testing, pilot acceptance and store submission. #18 live Cosmos ETag/transactional-batch/multi-instance/restart validation, telemetry export/alerts and production CORS remain external. #6 needs Product-approved radius/zones/location freshness. #15/#17 need Product/Operations support-policy inputs. Other maps/realtime/document-storage/legal gates remain recorded.
- Engineering implementation/evidence exists across the 19 areas; strict acceptance remains 0/19 Verified (15 In Progress, 4 Blocked). This count is not an implementation-completion percentage. Production readiness is not established.


## Latest sequential handoff — #19 RELEASE-01 Pixel Launcher ANR recovery (2026-11-10)

- Main/base HEAD: `4521c7e9a1448f85208ca744cd4824fd2a02596f`; only active feature PR: #141, branch `test/release-01-clean-simulator-state`, latest code head before docs sync `917a0757f958ffce4933afa8bea2c21bab9aa7e8`.
- Run [#1070 / 42663289638](https://github.com/rajeevp727/turnova-platform/actions/runs/42663289638) exposed a Pixel Launcher system ANR modal over the still-top-resumed Rider app. Do not treat this as a TurnOva process crash or bypass the UI check.
- Run [#1072 / 42669755886](https://github.com/rajeevp727/turnova-platform/actions/runs/42669755886) recorded 11 successful jobs but omitted the Driver Android emulator-smoke job/artifact and concluded failure. Rider Android passed; Rider/Driver iOS simulator smokes passed. Inspect artifacts 13698723012, 13698668818 and 13699018200 before counting the evidence; production API/authentication and Keychain persistence remain untested.
- Current code requires the exact Pixel Launcher dialog title and an enabled/clickable Wait control, parses the control bounds, attempts recovery at most twice, and fails closed for disabled/persistent/other ANR states. Fake-SDK coverage verifies transient recovery, exhaustion, disabled-control no-tap and a separate TurnOva app ANR that fails without tapping or emitting success.
- Immediate next action: poll fresh exact-head CI after the current code/doc sync, confirm all 12 expected jobs are present and green, inspect the Driver Android artifact and both iOS artifacts, then submit a COMMENT-only review anchored to the final SHA. Merge only if all exact-head checks pass and repository policy permits; no self-approval/bypass.
- RELEASE-01 external gates remain signed AAB/IPA, approved identifiers/EAS, production HTTPS API, physical-device testing, pilot acceptance and store submission. Other product/infra gates remain in the acceptance matrix.
- Strict acceptance remains 0/19 (15 In Progress, 4 Blocked); production readiness is not established.


## Latest sequential handoff — #19 RELEASE-01 all-platform smoke passed (2026-11-10)

- Main/base HEAD: 4521c7e9a1448f85208ca744cd4824fd2a02596f; active PR #141, branch test/release-01-clean-simulator-state, code head before documentation sync 51dbb81b6baf3a6e832889132b63f15b1923b310.
- Run [#1079 / 42679676632](https://github.com/rajeevp727/turnova-platform/actions/runs/42679676632) passed all 12/12 expected jobs. Exact evidence: backend 364/364; shared JS 102/102; Python fake-SDK/native-artifact 28/28; Rider/Driver mobile validation/bundles; native Android Rider/Driver; native iOS Rider/Driver; Android emulator Rider/Driver; iOS Simulator Rider/Driver.
- Android release-APK smoke artifacts [Rider 13702114989](https://github.com/rajeevp727/turnova-platform/actions/runs/42679676632/artifacts/13702114989) and [Driver 13702836717](https://github.com/rajeevp727/turnova-platform/actions/runs/42679676632/artifacts/13702836717) show correct package IDs, install/launch, stable process, login screen and no stale-session error. No Pixel Launcher ANR occurred in this run; both report zero Wait attempts. The deterministic fake-SDK suite separately covers transient ANR recovery, persistent/disabled/other ANR fail-closed behaviour and corrupt SDK archive retry.
- iOS smoke artifacts [Rider 13705430717](https://github.com/rajeevp727/turnova-platform/actions/runs/42679676632/artifacts/13705430717) and [Driver 13704571529](https://github.com/rajeevp727/turnova-platform/actions/runs/42679676632/artifacts/13704571529) show simulator erase, install/launch, stable process, login screen, expected unsigned SecureStore-unavailable banner and no stale-session expired error. The artifacts explicitly report no production API/authentication and no available Keychain access group. This is unsigned simulator startup evidence only, not token persistence or signed-device validation.
- The docs-only synchronization changes the branch head. Run fresh exact-head CI on that final SHA, inspect all 12 checks, submit COMMENT-only review, then merge only if repository policy permits. Do not self-approve or bypass.
- Next after PR #141: continue AI-01 supplemental engineering. Add tests for transient HttpRequestException/TimeoutException fallback, in-flight cancellation, and cancellation taking precedence over a racing provider HTTP failure. Fix the current unconditional HttpRequestException catch to re-check caller cancellation before returning fallback.
- Strict acceptance: 0/19 Verified, 15 In Progress, 4 Blocked. This is not an implementation-completion percentage. Production readiness is not established.


## Latest sequential handoff — #19 RELEASE-01 final exact-head simulator evidence (2026-11-10)

- Main/base HEAD: 4521c7e9a1448f85208ca744cd4824fd2a02596f; active PR #141 branch test/release-01-clean-simulator-state, final head 6d7e6776e27ad34d926eb454856d41c293776a2e. PR #141 remains the only open feature PR.
- Exact-head CI [#1080 / 42686782043](https://github.com/rajeevp727/turnova-platform/actions/runs/42686782043) completed successfully with 12/12 jobs: backend 364/364; shared JS 102/102; Python fake-SDK/native-artifact 28/28; Rider/Driver mobile validation/bundles; four native builds; both Android emulator smokes; both iOS Simulator smokes.
- Inspected run #1080 artifacts: Rider Android [13706635203](https://github.com/rajeevp727/turnova-platform/actions/runs/42686782043/artifacts/13706635203), Driver Android [13704609925](https://github.com/rajeevp727/turnova-platform/actions/runs/42686782043/artifacts/13704609925), Rider iOS [13707380822](https://github.com/rajeevp727/turnova-platform/actions/runs/42686782043/artifacts/13707380822), Driver iOS [13706204614](https://github.com/rajeevp727/turnova-platform/actions/runs/42686782043/artifacts/13706204614). Android APKs install/launch and show the correct role-specific login UI with stable processes and no stale-session error; both report zero Pixel Launcher Wait attempts. Unsigned iOS apps install/launch after simulator erase, show login UI and the expected SecureStore-unavailable banner, and do not show a stale-session expiry error. No production API/authentication or Keychain persistence is exercised.
- Documentation sync must be committed as one tree update and followed by fresh exact-head 12-job CI. Submit a COMMENT-only review on the resulting final SHA. Merge only if every required check passes, PR is clean/mergeable, no critical/high-risk issue remains and repository policy permits. Main currently reports unprotected/no required status checks and repository rulesets are empty; never self-approve or bypass.
- #19 RELEASE-01 remains externally Blocked on approved app IDs/EAS, production signing, HTTPS API hosting/configuration, physical-device testing, pilot acceptance and store submission. #18 live Cosmos ETag/transactional-batch/multi-instance/restart/retention, telemetry export/alerts, production CORS/load; #6 Product radius/zones/location freshness; #15/#17 Product/Operations support-policy inputs; and maps/realtime/document/legal gates remain external.
- Strict acceptance remains 0/19 Verified (15 In Progress, 4 Blocked). This is not an implementation-completion percentage; substantial implementation and deterministic automated evidence exist. Production readiness is not established.
- After PR #141 merges, continue AI-01 supplemental work: make caller cancellation take precedence over provider transport-failure fallback and add deterministic tests for transient HttpRequestException/TimeoutException, in-flight cancellation and cancellation racing with HTTP failure. AI-01 is outside the 19-row denominator.

## Latest sequential handoff — #19 RELEASE-01 exact-head simulator evidence (2026-11-10)

- Main/base HEAD remains `4521c7e9a1448f85208ca744cd4824fd2a02596f`; sole active feature PR is [#141](https://github.com/rajeevp727/turnova-platform/pull/141), branch `test/release-01-clean-simulator-state`. Code/evidence head `076b37dd2c0d71524bb8424c639399ece494d6af` passed [CI #1085 / 42708832985](https://github.com/rajeevp727/turnova-platform/actions/runs/42708832985) with 12/12 jobs green.
- Exact-head CI [#1085 / 42708832985](https://github.com/rajeevp727/turnova-platform/actions/runs/42708832985) passed all 12/12 jobs on code/evidence head `076b37dd2c0d71524bb8424c639399ece494d6af`: .NET 10 backend 364/364; shared JavaScript 102/102; Python fake-SDK/native-artifact tests 30/30; Rider and Driver mobile configuration/typecheck/production bundle validation; all four Android/iOS native builds; both Android standalone release-APK emulator smokes; and both unsigned iOS Simulator smokes. The four smoke artifacts were downloaded and inspected: Rider Android [13716265571](https://github.com/rajeevp727/turnova-platform/actions/runs/42708832985/artifacts/13716265571), Driver Android [13714823999](https://github.com/rajeevp727/turnova-platform/actions/runs/42708832985/artifacts/13714823999), Rider iOS [13717028524](https://github.com/rajeevp727/turnova-platform/actions/runs/42708832985/artifacts/13717028524), Driver iOS [13714969794](https://github.com/rajeevp727/turnova-platform/actions/runs/42708832985/artifacts/13714969794). Both Android apps installed/launched, remained process-stable, displayed the correct role-specific login screen, had no stale-session error, and needed zero Pixel Launcher Wait attempts. Both iOS simulators were erased before install; both apps installed/launched, remained process-stable, displayed the correct login screen and the expected unsigned-Simulator SecureStore-unavailable banner, with no expired-session error. The iOS result JSON explicitly reports `secureStoreAvailable=false`, no Keychain access group and `productionApiOrAuthentication=not exercised`. This is CI-only startup evidence; it does not prove Keychain persistence, production auth/API, signed release distribution, physical-device behaviour, pilot or store readiness.
- The documentation synchronization is a new commit on top of the tested code/evidence head and must receive its own exact-head CI before merge. Submit a final COMMENT-only review anchored to the resulting head. Merge only if the new exact-head run passes, the PR remains clean/mergeable, no critical/high-risk issue or unresolved review thread remains, and GitHub's merge endpoint permits it. Do not self-approve or bypass protections. The repository rulesets endpoint returned an empty list; the branch-protection endpoint is not readable by the current integration, so the merge API remains the authoritative policy check.
- RELEASE-01 remains externally Blocked pending approved/registered app identifiers, EAS linkage, production-signed AAB/IPA, production HTTPS API/DNS/TLS/CORS/readiness, approved build-number/signing strategy, physical Android/iOS testing, pilot acceptance and store submission. #18 PLATFORM-01 retains live Cosmos transactional-batch/ETag/409 read-after-write/partition/multi-instance/restart/retention, telemetry export/alerts, production CORS and load gates. #6 requires Product-approved positive radius/non-overlapping zones/location freshness. #15 DRIVER-04 and #17 SUPPORT-01 require Product/Operations-approved support categories, intake fields, priority/severity, escalation owner/channel, hours/targets, attachment/privacy/retention rules and operational handoff. Maps/realtime providers, private document controls and legal partnership review remain external.
- Strict acceptance remains **0/19 Verified** (15 In Progress, 4 Blocked). This is a strict evidence count, not an implementation-completion percentage; substantial implementation and automated evidence exist. Production readiness is not established.
- After PR #141 merges, continue AI-01 supplemental engineering: make caller cancellation take precedence over a racing `HttpRequestException`, map explicit provider `TimeoutException` to deterministic fallback unless caller cancellation is set, and add deterministic tests for transient transport failure, explicit timeout, in-flight cancellation and cancellation-versus-HTTP-failure precedence. AI-01 is outside the 19-row denominator.

## Latest RELEASE-01 handoff — 2026-11-10

PR #141 `test/release-01-clean-simulator-state` latest tested code head: `98cd89ff0a74a6c4bc5feedda29c4b44b189b0f2`. Exact-head GitHub Actions [run #42717698786](https://github.com/rajeevp727/turnova-platform/actions/runs/42717698786) passed **12/12 jobs**: backend 364/364, shared JS 102/102, Python fake-SDK/native-artifact 30/30, Rider/Driver mobile validation and bundle exports, four native Android/iOS builds and four same-run simulator smokes. Android Rider/Driver APKs installed/launched with stable processes and role-specific login UI; unsigned iOS Rider/Driver apps installed/launched after erase with stable processes and login UI plus the expected SecureStore-unavailable banner. Do not infer iOS Keychain persistence, production authentication or physical-device acceptance.

Latest code-only change adds a deterministic assertion that global `adb emu kill` is never issued; AVD ownership and cleanup are scoped to an isolated temporary home and invocation-specific name. Artifacts and strict acceptance accounting are recorded in `docs/MVP_PROGRESS.md`, `docs/MVP_ACCEPTANCE_MATRIX.md` and `docs/test-plan.md`. RELEASE-01 remains Blocked until approved identifiers/EAS, production signing, HTTPS API configuration, physical-device/pilot acceptance and store submission.

Next: submit a COMMENT-only review anchored to the final documentation-synchronized head, inspect final-head CI and same-run smoke artifacts, and merge PR #141 only if all required checks pass and GitHub repository policy permits without self-approval or bypass. After merge, re-read main and continue the approved AI-01 provider timeout/cancellation/fallback audit. AI-01 is supplemental and excluded from the 19-row denominator.


## Latest sequential handoff — #19 RELEASE-01 code/evidence head passed (2026-11-10)

- PR #141 branch test/release-01-clean-simulator-state; code/evidence head 3316980ec59d1e865eb2bfbd2c9d0be55015fa09; exact-head [CI run #42726122385](https://github.com/rajeevp727/turnova-platform/actions/runs/42726122385) (run #1095) passed **12/12 jobs**: backend 364/364, shared JS 102/102, Python 32/32, Rider/Driver mobile validation and bundles, all four native Android/iOS builds and all four same-run simulator smokes.
- Android Rider/Driver smoke artifacts [13722214388](https://github.com/rajeevp727/turnova-platform/actions/runs/42726122385/artifacts/13722214388) and [13722738316](https://github.com/rajeevp727/turnova-platform/actions/runs/42726122385/artifacts/13722738316) passed install/launch and process checks using same-run standalone APKs. iOS Rider/Driver [13724815604](https://github.com/rajeevp727/turnova-platform/actions/runs/42726122385/artifacts/13724815604) and [13724351483](https://github.com/rajeevp727/turnova-platform/actions/runs/42726122385/artifacts/13724351483) passed unsigned Simulator install/launch and expected SecureStore-unavailable banner checks; no Keychain persistence is claimed.
- The latest harness changes preserve complete/incomplete pre-existing Android system-image directories, clean only partial packages created by the current invocation, isolate the AVD under an invocation-owned temporary home/name, avoid global emulator shutdown, and require CI=true before Android SDK mutation or iOS simulator erasure. The 32/32 Python tests exercise these boundaries.
- Five documentation files are being synchronized to this code/evidence head. The docs-only commit requires a new exact-head CI run and a final COMMENT-only review. Merge only after all 12 jobs pass, no critical/high-risk issue or unresolved review thread remains, and GitHub policy permits; never self-approve or bypass.
- Strict acceptance remains **0/19 Verified** (15 In Progress, 4 Blocked), not an implementation-completion percentage. RELEASE-01 is still externally Blocked on approved identifiers/EAS, production signing, HTTPS API configuration, physical-device/pilot acceptance and store submission. #18 live Cosmos/telemetry/CORS, #6 Product radius/zones/freshness, #15/#17 Product/Operations support-policy, maps/realtime, private document, reconciliation and legal gates remain external. Production readiness is not established.
- After PR #141 merges, re-read main and continue approved AI-01 timeout/cancellation/fallback work; AI-01 is supplemental and outside the 19-row denominator.


## Latest sequential handoff — #19 RELEASE-01 exact-head all-platform evidence (2026-11-10)

- Main/base HEAD `4521c7e9a1448f85208ca744cd4824fd2a02596f`; only active feature PR [#141](https://github.com/rajeevp727/turnova-platform/pull/141), branch `test/release-01-clean-simulator-state`, tested code head `a1494f906696b6e28c2aa688534cd069a52601ec`.
- Exact-head CI [#1097 / 42736524343](https://github.com/rajeevp727/turnova-platform/actions/runs/42736524343) passed 12/12 jobs: backend 364/364, shared JS 102/102, Python 35/35, Rider/Driver mobile validation and bundles, four native builds, and four simulator smokes.
- Same-run artifacts inspected: Rider Android [13728123213](https://github.com/rajeevp727/turnova-platform/actions/runs/42736524343/artifacts/13728123213), Driver Android [13727234634](https://github.com/rajeevp727/turnova-platform/actions/runs/42736524343/artifacts/13727234634), Rider iOS [13728118398](https://github.com/rajeevp727/turnova-platform/actions/runs/42736524343/artifacts/13728118398), Driver iOS [13729585235](https://github.com/rajeevp727/turnova-platform/actions/runs/42736524343/artifacts/13729585235). Both Android apps installed/launched, remained stable and showed role-specific login UI without stale-session errors; both had zero Pixel Launcher Wait attempts. Both erased iOS simulators installed/launched, remained stable and showed role-specific login UI plus expected unsigned SecureStore-unavailable banner. iOS explicitly records `secureStoreAvailable=false`, no Keychain access group and production API/authentication not exercised.
- Commit `a1494f9` adds bounded `simctl` operations (default 180 seconds, allowed 1–300), separate first-boot `bootstatus` bound (600 seconds), and deterministic fail-closed timeout tests. This is CI-only simulator harness work; no app runtime, API, JWT, Cosmos, dispatch, fare or payment behaviour changed.
- PR #141 remains open pending fresh exact-head CI after docs synchronization and final COMMENT-only review. No self-approval or bypass.
- Strict acceptance remains **0/19 Verified** (15 In Progress, 4 Blocked); implementation evidence is substantial and separately recorded. Production readiness is not established. External gates remain approved dispatch radius/zones/location freshness; Product/Operations support policy; live Cosmos ETag/transactional batch/multi-instance/restart/retention, telemetry/alerts and production CORS; approved app IDs/EAS, signed AAB/IPA, HTTPS production API, physical-device/pilot/store acceptance; maps/realtime providers, private document storage/scanning/consent/retention, reconciliation policy and legal partnership review.
- After merge and re-reading main, continue approved supplemental AI-01 provider timeout/cancellation/fallback hardening. AI-01 is outside the 19-row denominator; do not select paid provider/model/credentials.


## Latest sequential handoff — #19 RELEASE-01 iOS OCR cold-start correction (2026-11-10)

- Main/base HEAD: `4521c7e9a1448f85208ca744cd4824fd2a02596f`; sole active sequential PR: [#141](https://github.com/rajeevp727/turnova-platform/pull/141), branch `test/release-01-clean-simulator-state`, tested head `1f6220134853c6feba8ac1044ca0b35905a6973a`.
- Exact-head CI [#42746353993](https://github.com/rajeevp727/turnova-platform/actions/runs/42746353993) completed with **11/12 jobs successful**: backend 364/364; shared JavaScript 102/102; Python fake-SDK/native-artifact tests 38/38; Rider/Driver validation and bundles; all four native builds; both Android emulator smokes; Rider iOS Simulator smoke. Driver iOS Simulator smoke failed at login OCR. No merge is permitted on this head.
- Same-run Android Rider [artifact 13732336266](https://github.com/rajeevp727/turnova-platform/actions/runs/42746353993/artifacts/13732336266) and Driver [artifact 13732590850](https://github.com/rajeevp727/turnova-platform/actions/runs/42746353993/artifacts/13732590850) were downloaded and inspected; both result JSONs confirm standalone APK install/launch, stable process, correct role-specific login UI and no stale-session error. Rider iOS [artifact 13731889702](https://github.com/rajeevp727/turnova-platform/actions/runs/42746353993/artifacts/13731889702) confirms erased simulator, install/launch, stable process, correct login UI and expected unsigned SecureStore-unavailable banner. Driver iOS [artifact 13731849848](https://github.com/rajeevp727/turnova-platform/actions/runs/42746353993/artifacts/13731849848) contains a SpringBoard home-screen screenshot and no success result; install and process launch succeeded, but login UI was not verified.
- Driver iOS log chronology: `simctl launch` returned PID 10975 at 03:22:02Z; screenshot was written at 03:23:00Z; SpringBoard reported the Driver scene ready at 03:23:02Z; the 90-second login-readiness check failed at 03:25:44Z. The script invokes the Swift Vision checker through the `swift` interpreter inside the readiness deadline. The first interpreter compile is therefore the leading explanation for consuming the first poll budget before a second screenshot; this is a test-harness timing diagnosis, not a confirmed app-runtime defect. The same log also contains unsigned-bundle SplashBoard validation warnings; unsigned simulator artifacts do not prove Keychain persistence.
- Follow-up code compiles `ios-simulator-screen-text-check.swift` once to a temporary executable with a separately bounded `TURNOVA_IOS_OCR_COMPILE_TIMEOUT_SECONDS` (default 300, allowed 1–600) before the first simulator enumeration. Readiness polls reuse the compiled executable. Deterministic tests cover one compile across repeated OCR polls, compile failure and timeout before any `simctl` access, invalid timeout values and valid lower/upper boundaries. New test-plan case: RELEASE-SMOKE-025.
- This remains #19 RELEASE-01 CI/test-harness work only; no Rider/Driver runtime, API, JWT, Cosmos, dispatch, fare or payment behaviour changed. Require fresh exact-head CI and inspect all four same-run simulator artifacts before considering merge. No self-approval or bypass.
- Strict acceptance remains **0/19 Verified** (15 In Progress, 4 Blocked). This is a full-evidence count, not an implementation-completion percentage; substantial implementation and deterministic test/build evidence already exist. Production readiness is not established. External gates remain approved production IDs/EAS/signing, HTTPS API, physical-device/pilot/store acceptance, Product dispatch radius/zones/location freshness, Product/Operations support policy, live Cosmos ETag/transactional-batch/multi-instance/restart/retention, telemetry/alerts/CORS, maps/realtime providers, private document controls, reconciliation policy and legal partnership review.
- Next exact action: commit this OCR compile-once code/test change and synchronized docs to PR #141, run fresh exact-head CI, inspect Rider/Driver Android and iOS smoke artifacts, submit a COMMENT-only review on the final head, and merge only if all required checks pass and repository policy permits. After merge, re-read main and continue approved AI-01 provider timeout/cancellation/fallback hardening; AI-01 remains outside the 19-row denominator.


## Latest sequential handoff — #19 RELEASE-01 exact-head simulator verification (2026-11-10)

- Main/base HEAD: `4521c7e9a1448f85208ca744cd4824fd2a02596f`; sole active PR: [#141](https://github.com/rajeevp727/turnova-platform/pull/141), branch `test/release-01-clean-simulator-state`; tested code/evidence head `ce88ade0f6b265de72d9bb520e8e866ea71e88d1`.
- Exact-head [CI #1101 / 42750283649](https://github.com/rajeevp727/turnova-platform/actions/runs/42750283649) completed successfully: **12/12 jobs green**. Backend: **364/364 .NET tests**. Shared mobile JavaScript: **102/102 tests**. Deterministic fake-SDK/native-artifact suite: **40/40 Python tests**. Rider/Driver configuration, typecheck and production JS bundles passed; all four native Android/iOS builds passed; both Android emulator and both iOS Simulator smoke jobs passed.
- Same-run smoke artifacts were downloaded, result JSONs inspected and screenshots visually checked: Rider Android [13734170935](https://github.com/rajeevp727/turnova-platform/actions/runs/42750283649/artifacts/13734170935), Driver Android [13732909631](https://github.com/rajeevp727/turnova-platform/actions/runs/42750283649/artifacts/13732909631), Rider iOS [13733574238](https://github.com/rajeevp727/turnova-platform/actions/runs/42750283649/artifacts/13733574238), Driver iOS [13733349134](https://github.com/rajeevp727/turnova-platform/actions/runs/42750283649/artifacts/13733349134).
- Android Rider/Driver: correct package and role-specific login screen; install and launch passed; process remained stable; `loginScreenTextVerified=true`, `staleSessionErrorAbsent=true`; zero Pixel Launcher Wait attempts. Smoke used the same-run standalone release APKs. Production API/authentication was not exercised.
- iOS Rider/Driver: simulator erased before install; correct bundle; install/launch passed; process stable; role-specific login screen verified. Both result JSONs report `secureStoreAvailable=false`, `secureStoreUnavailableBannerVerified=true`, `staleSessionExpiredErrorAbsent=true`, `productionApiOrAuthentication=not exercised`, unsigned CI simulator artifact and no Keychain access group. The screenshots visibly include the generic “Unable to restore your session. Check your connection or secure storage and retry.” warning. This is the expected unsigned-Simulator SecureStore-unavailable boundary detected by the smoke, **not** successful Keychain persistence and not proof of signed-device authentication. The expired-session assertion separately passed.
- The OCR checker is compiled once with bounded timeout before simulator access and reused during readiness polling; deterministic tests cover compile-once reuse, compile failure/timeout before simulator access and timeout boundaries (RELEASE-SMOKE-025).
- This is CI-only engineering/build/simulator evidence. It does not prove signed production AAB/IPA, physical-device behaviour, production auth/API, live Cosmos, GPS/maps/realtime providers, pilot acceptance or store readiness. No Rider/Driver runtime, JWT, API, Cosmos, dispatch, fare, payment or production deployment behaviour changed in this PR.
- PR #141 remains open pending a documentation-synchronization commit, fresh exact-head 12-job CI, final COMMENT-only review anchored to that new SHA and a policy-permitted merge. Do not self-approve or bypass protections.
- Strict acceptance remains **0/19 Verified** (15 In Progress, 4 Blocked). This is the full-evidence count, not an implementation-completion percentage. Production readiness is not established.
- External gates: Product-approved dispatch radius/non-overlapping zones/location freshness; Product/Operations support categories/intake/severity/escalation owner/channel/hours/targets/attachment/privacy/retention/handoff; live Cosmos ETag/transactional-batch/partition/multi-instance/restart/retention; telemetry/alerts/production CORS/load; approved app IDs/EAS, signed AAB/IPA, HTTPS API/DNS/TLS, physical-device/pilot/store acceptance; maps/realtime providers; private document storage/scanning/consent/retention; reconciliation dispute and non-zero-delta next-ride policy; legal partnership review.
- After merge, re-read main and continue approved supplemental AI-01 provider timeout/cancellation/fallback hardening. AI-01 remains outside the 19-row denominator; do not choose a paid provider/model or credentials.

## Latest sequential handoff — AI-01 provider resilience (2026-11-10)

The approved AI-01 supplemental scope remains outside the 19-area denominator. The current branch adds independent `Task.WaitAsync` timeout enforcement so an uncooperative provider cannot hold the request beyond `RequestTimeoutSeconds`; the linked token still asks the provider to stop. Synchronous/asynchronous `TimeoutException` and `HttpRequestException` return deterministic fallback. Caller cancellation takes precedence over provider failure and propagates. Late provider-task faults are observed without logging exception text or provider content.

Deterministic tests cover ignored cancellation tokens, late faults, synchronous and asynchronous timeout/HTTP failures, in-flight cancellation and cancellation-versus-HTTP-failure precedence. Branch code/test commits: `80cb9942b1a980face5496b5f20af15c96de7322`, `065a75a131fc04fbc12557531cf79b545c1d57eb`, `aa1a936411eb2be83a41d5b3bdd66c55bf845605`. Code-head exact-head CI #1105 / 42759995138 passed 12/12 after a targeted Rider Android smoke retry (backend 371/371, shared JS 102/102, Python 40/40, four native builds and four simulator smokes). The documentation synchronization creates a new head that still requires exact-head CI before merge.

Do not choose a paid provider/model or credentials. Live provider sandbox, managed-secret wiring and data-processing terms remain external. AI must not book, set fares, approve KYC, change eligibility/dispatch/ride state or make safety/legal decisions.


## Latest AI-01 provider-resilience CI evidence — 2026-11-10

PR [#142](https://github.com/rajeevp727/turnova-platform/pull/142) code head `05d20e176abf3c97c360650d3daa5e426a8a253a` passed all 12/12 jobs in [CI run #42759995138 (run 1105)](https://github.com/rajeevp727/turnova-platform/actions/runs/42759995138) after a targeted retry of the Rider Android emulator smoke. The run executed .NET 10 backend tests 371/371, shared JavaScript tests 102/102, Python fake-SDK/native tests 40/40, Rider/Driver mobile validation and production bundle exports, all four native Android/iOS builds, and all four same-run simulator smokes.

The first Rider Android smoke attempt failed because the Pixel Launcher ANR dialog persisted after two bounded Wait attempts; the app's launch command had succeeded, but the harness correctly failed closed. Only that failed job was rerun. The retry passed and its inspected artifact [13739865249](https://github.com/rajeevp727/turnova-platform/actions/runs/42759995138/artifacts/13739865249) reports install/launch passed, stable Rider process, login text verified, no stale-session error and zero Pixel Launcher ANR Wait attempts. The Driver Android artifact [13737179773](https://github.com/rajeevp727/turnova-platform/actions/runs/42759995138/artifacts/13737179773) reports the same successful app evidence and zero ANR attempts. Rider iOS [13738776886](https://github.com/rajeevp727/turnova-platform/actions/runs/42759995138/artifacts/13738776886) and Driver iOS [13738339007](https://github.com/rajeevp727/turnova-platform/actions/runs/42759995138/artifacts/13738339007) both report install/launch passed, stable process and role-specific login screen; both explicitly report unsigned simulator artifact, `secureStoreAvailable=false`, SecureStore-unavailable banner verified, no Keychain access group and production API/authentication not exercised. All four screenshots were downloaded and visually inspected. This does not establish Keychain persistence, signed-device authentication, physical-device or production readiness.

The documentation synchronization is a new PR head and requires a fresh exact-head CI run plus a COMMENT-only review anchored to that SHA before merge. PR #142 remains open; no self-approval or bypass. Strict acceptance remains 0/19 (15 In Progress, 4 Blocked), and production readiness is not established. AI-01 remains supplemental and outside the 19-row denominator.


### AI-01 provider-boundary hardening — 2026-11-10

The provider invocation boundary independently bounds waits with `Task.WaitAsync`, sends cancellation to cooperative providers, observes late task faults, gives caller cancellation precedence, and returns deterministic fallback for timeout, HTTP and unexpected provider-adapter exceptions. Provider exception messages and prompt content are not logged or returned. AI-030 tests unexpected synchronous/asynchronous failures. AI-01 remains supplemental/outside the 19-row denominator; live provider and managed-secret validation remain external.


## Latest exact-head evidence — AI-01 (2026-11-10)

PR [#142](https://github.com/rajeevp727/turnova-platform/pull/142) head `79c473661d7804f761bedebbfe871ecd9b0e94e2` passed all 12/12 jobs in [CI #1110 / 42769495825](https://github.com/rajeevp727/turnova-platform/actions/runs/42769495825). Backend 373/373, shared JavaScript 102/102, Python fake-SDK/native 40/40; both app validation/bundles, four native builds and four simulator smokes passed. Same-run artifacts 13742280744 (Rider Android), 13742285530 (Driver Android), 13741469890 (Rider iOS), and 13742567031 (Driver iOS) were downloaded; all result JSONs and screenshots were inspected. Android login surfaces passed with stable processes and no stale-session errors. Unsigned iOS login surfaces passed with SecureStore unavailable, no Keychain access group and production authentication not exercised.

AI-01 provider calls are independently timeout-bounded with `Task.WaitAsync`; cooperative cancellation is signalled, caller cancellation takes precedence, late task faults are observed, and synchronous/asynchronous timeout, HTTP and unexpected adapter exceptions use deterministic local fallback. Tests AI-026–AI-030 cover the failure/cancellation cases. AI-01 remains supplemental/outside the 19-row denominator; no paid provider/model/credential is selected. Live provider sandbox, managed-secret wiring and data-processing terms remain external.

After PR #142 merges and main is re-read, continue AUTH-02 refresh/logout concurrency hardening. AUTH-01 editable profile fields/routes remain undefined upstream; do not invent them.


## Current sequential handoff — AUTH-02 (2026-11-10)

PR [#143](https://github.com/rajeevp727/turnova-platform/pull/143) adds epoch-aware serialized token-pair persistence to both Rider and Driver. Shared `saveSessionTokensIfCurrent` checks the captured session epoch before writing and after each secure key; if the session changes during persistence, the stale/partial pair is cleared inside the same storage mutation queue. Refresh handlers only update in-memory state when the captured epoch remains current. New login, logout, Rider/Driver sign-in start and refresh-expiry cleanup invalidate prior refresh work and reset the single-flight reference. `isAuthenticatedSessionCurrent` binds the in-memory token pair to the accepted epoch; protected API calls and Driver payslip downloads fail closed immediately after logout/account-switch/expiry advances the session epoch. AUTH-LOGIN-031–035 cover guarded storage and stale-refresh/login/logout races; AUTH-LOGIN-036–038 cover authenticated-epoch matching and invalidation. Existing offline-safe logout and pair-write rollback remain.

PR #142 AI-01 merged at `6f0dc4821ae6408c7057f6b34aa4990ea01d948d` after exact documentation-head CI #1113 / 42774588830 passed 12/12. AI-01 is supplemental/outside the denominator; live provider sandbox, managed-secret wiring and data-processing terms remain external.

AUTH-02 remains In Progress pending exact-head CI on `97ffdbb0819dab51046980698035cd364fce3bb2`, live TriSend refresh rotation/reuse validation, and signed Android/iOS physical-device restore/expiry/logout/SecureStore failure acceptance. Do not claim production Keychain persistence from unsigned simulator builds. After PR #143 merges, re-read `main` and continue the next actionable acceptance row in order.


## Current sequential handoff — AUTH-02 authenticated-epoch request fencing (2026-11-10)

PR [#143](https://github.com/rajeevp727/turnova-platform/pull/143) is the sole active feature PR on `fix/auth-02-refresh-logout-epoch-race`. Current code/test head: `97ffdbb0819dab51046980698035cd364fce3bb2`. The narrower duplicate PR #144 was closed and its documentation state consolidated here.

The new `isAuthenticatedSessionCurrent` predicate ensures the token pair in React state is usable only when its accepted epoch matches the active session epoch. Both apps set the authenticated epoch only after guarded token persistence or successful startup restore. Protected API calls reject before network dispatch when logout/account-switch/expiry has advanced the active epoch. Driver payslip downloads use the same guard. Three deterministic tests cover matching/mismatched epochs, immediate invalidation after epoch advance, and invalid/missing authenticated epochs; test-plan IDs AUTH-LOGIN-036–038.

Previous code head dc5daf passed [CI #1120 / 42782377492](https://github.com/rajeevp727/turnova-platform/actions/runs/42782377492) 12/12: backend 373/373, shared JS 109/109, Python native-artifact tests, both mobile validation/bundles, four native builds and four simulator smokes. Same-run simulator artifacts were inspected; unsigned iOS SecureStore limitations remain. Current head `97ffdbb0819dab51046980698035cd364fce3bb2` requires fresh exact-head CI and artifact inspection before merge.



## Latest sequential handoff — AUTH-02 realtime session invalidation (2026-11-10)

PR [#143](https://github.com/rajeevp727/turnova-platform/pull/143) remains the sole active feature PR. In addition to epoch-aware token-pair persistence, stale async completion fencing and protected REST request guards, the latest review found that an open SignalR connection could remain active during asynchronous logout cleanup. The follow-up adds `readAccessTokenIfCurrent` so SignalR token factories return an empty token when the captured authenticated epoch is stale (including when the epoch changes during SecureStore read); `isSessionEpochCurrent` for captured connection epochs; and `stopRegisteredConnections` to stop active Rider/Driver transports immediately when logout, account switch or refresh expiry advances the session epoch. Rider location/status/agreement callbacks and Driver offer/selection/agreement/status callbacks plus reconnect recovery require the captured authenticated epoch. Protected REST response bodies are checked again after parsing. Deterministic tests and AUTH-LOGIN-039–042 cover stale token reads, captured epochs, connection teardown and late response rejection. Fresh exact-head CI and all four same-run simulator artifacts are required before merge. AUTH-02 remains In Progress pending live TriSend rotation/reuse semantics and signed Android/iOS physical-device SecureStore/Keychain acceptance. After merge, re-read `main` and continue sequentially to RIDER-01, then RIDER-02.


## Latest sequential handoff — AUTH-02 complete session-resource invalidation (2026-11-10)

PR [#143](https://github.com/rajeevp727/turnova-platform/pull/143) remains the sole active feature PR. The latest AUTH-02 increment adds a shared `readJsonResponseIfCurrent` guard used by Rider/Driver protected API helpers and Driver payslip error parsing, with deterministic tests for a response body that completes after logout. The active session-stop registry now includes Rider/Driver SignalR connections and the Driver foreground location watcher. Logout/account switch/refresh expiry advances the session epoch and stops those resources immediately; location permission/watch continuations, history/agreement loaders, SignalR token factories, event callbacks and reconnect recovery all require the captured authenticated epoch. `startSignalRConnection` rechecks its owner after successful start and stops a transport if invalidation happened while `start()` was pending. AUTH-LOGIN-039–044 cover stale token reads, captured epochs, resource teardown, late response bodies, location watcher stop and in-flight SignalR start. Fresh exact-head CI and all four same-run simulator artifacts are required before merge. AUTH-02 remains In Progress pending live TriSend rotation/reuse semantics and signed Android/iOS physical-device SecureStore/Keychain acceptance. After merge, re-read `main` and continue sequentially to RIDER-01, then RIDER-02.


## AUTH-02 startup restore race — 2026-11-10

A final concurrency audit found startup restore was not bound to the session epoch. If a Rider/Driver began a replacement sign-in while the old /auth/me or /auth/refresh request was pending, the old restore could later publish its profile/tokens or clear the new token pair. This is fixed in the active PR #143:

- restoreStoredSession(storage, request, isCurrent) checks the captured epoch before and after token reads, /auth/me, /auth/refresh, guarded token persistence and the final profile read.
- Startup token rotation uses saveSessionTokensIfCurrent; stale results do not write.
- Stale 400/401 cleanup is serialized, re-reads the pair and deletes only when it still matches the pair owned by that restore.
- Partial-token cleanup also runs under the mutation queue and rechecks the exact observed pair, preventing a delayed stale read from deleting a replacement login.
- Rider and Driver mount effects fence restore state, errors and loading completion. The successful restore adopts the new epoch it creates; a sign-in that advances the epoch first wins.

AUTH-LOGIN-045–048 add deterministic deferred-promise tests for delayed startup profile, delayed refresh, stale rejection cleanup and partial-pair cleanup. These are code/test evidence only; live TriSend rotation/reuse and signed-device SecureStore/Keychain behavior remain external. Active PR #143 remains the only feature PR; do not open a parallel PR.


## AUTH-02 startup restore test refinements — 2026-11-10

AUTH-LOGIN-048 now deterministically exercises the serialized snapshot recheck: a token-pair write pauses after the access key, a concurrent read observes a partial pair, the refresh-key write completes, and queued cleanup must preserve the now-complete pair because it no longer matches the observed snapshot. AUTH-LOGIN-049 holds the final /auth/me after token rotation while a replacement login writes a new pair; the stale profile result must be ignored and the new pair preserved. The exact-head CI for the combined PR #143 change must pass before merge.


## Latest verified handoff — AUTH-02 (2026-11-10)

PR [#143](https://github.com/rajeevp727/turnova-platform/pull/143) is the sole active feature PR; PR #144 was closed as a superseded narrower duplicate. Exact code/test head `de2c8779aa196b7f82e6c4453e08041eea9f1892` passed [CI #1143 / 42796791632](https://github.com/rajeevp727/turnova-platform/actions/runs/42796791632) 12/12: .NET backend 373/373, shared JavaScript 129/129, Python native-artifact tests 40/40, Rider/Driver typechecks and Android/iOS bundles, four native Android/iOS builds and four simulator smoke jobs. All four same-run artifacts were downloaded and visually inspected: Rider Android `13754982878`, Driver Android `13753564082`, Rider iOS `13754729815`, Driver iOS `13755816466`. Both Android login screens passed. Both unsigned iOS login screens passed with SecureStore unavailable and no Keychain access group; production authentication and signed-device persistence are not claimed.

The 21-file PR diff was reviewed for token-pair mutation ordering, stale refresh and startup-restore races, epoch invalidation, authenticated request gating, SignalR token factories/callbacks/reconnects, Driver location watcher teardown, protected response parsing, late completion and data/privacy exposure. Deterministic AUTH-LOGIN-031–049 coverage passed within the 129 shared mobile tests. Final COMMENT-only review is on exact head; zero unresolved inline threads and no critical/high-risk issue identified. AUTH-02 remains In Progress pending live TriSend refresh rotation/reuse semantics and signed Android/iOS physical-device restore, expiry, logout and SecureStore read/write/delete acceptance.

**Next sequential action:** merge PR #143 only if GitHub permits with expected head unchanged; re-read `main`; proceed to #3 RIDER-01 for a fresh API/Rider/Driver/persistence/privacy/test audit. Strict acceptance remains 0/19 (15 In Progress, 4 Blocked); this is not an implementation-completion percentage. Production readiness is not established.


## Latest handoff — AUTH-02 SignalR expiry-aware token factory (2026-11-10)

The active PR #143 uses getSignalRAccessToken for the Rider selected-ride hub and all three Driver hubs. A finite future JWT exp is used directly. Expired or locally unverifiable tokens go through the existing epoch-guarded apiFetch('/api/v1/auth/me') path, which owns refresh and session invalidation; the helper re-reads SecureStore with no stale in-memory fallback and rechecks the captured epoch. AUTH-LOGIN-050–056 add seven deterministic cases, including overflow expiry and logout during SecureStore read or server validation.

CI #1145 / 42805017563 failed 11/12 because Rider iOS simctl install exceeded the 180-second bound after a 2m41 first-boot migration. The simulator harness now defaults individual operations to 300 seconds (maximum remains 300) while first-boot bootstatus remains separately bounded at 600 seconds. RELEASE-SMOKE-026 verifies the default. Expected suite totals after this increment: shared JS 136, Python 41; backend remains 373. Exact-head CI and all four same-run simulator artifact inspections are pending.

Do not merge PR #143 until exact-head CI is green, all four simulator artifacts are inspected, and final review/policy checks pass. Live TriSend rotation/reuse and signed-device SecureStore/Keychain acceptance remain external.


## Latest handoff — SignalR helper TypeScript declaration (2026-11-10)

CI #1147 / 42813653333 passed backend but exposed TS2305 in Driver mobile typecheck because signalRConnection.d.ts did not export getSignalRAccessToken. Rider mobile typecheck was cancelled after the same missing declaration. The current PR commit adds SignalRAccessTokenOptions and declarations for getJwtExpiryMilliseconds/getSignalRAccessToken. Fresh exact-head CI is mandatory. Expected backend/shared/Python totals: 373/136/41. Do not merge until all checks and four same-run simulator artifacts pass. Live TriSend rotation/reuse and signed-device Keychain/SecureStore remain external.


## Latest CI correction — RELEASE-SMOKE-026 assertion (2026-11-10)

CI #1148 / 42813974342 passed backend 373/373 and both Rider/Driver mobile typecheck/bundle jobs. Python ran 41 tests; 40 passed and the new RELEASE-SMOKE-026 test failed because its exact command string omitted the app path. The test now checks the recorded command prefix `install TEST-DEVICE `, preserving the install-invocation assertion while allowing the expected temporary app path. CI #1148 is not green; fresh exact-head CI must pass all 12 jobs and all four same-run simulator artifacts must be inspected before merge.


## Latest handoff — AUTH-02 exact-head verification (2026-11-10)

PR #143 remains the sole active feature PR on `fix/auth-02-refresh-logout-epoch-race`. Verified source/test head `65a1ea8eca5087a6ad64d88b47dce602938b3c94` passed [CI #1149 / 42814411667](https://github.com/rajeevp727/turnova-platform/actions/runs/42814411667) 12/12 (backend 373, shared JS 136, Python 41, both app typechecks/bundles, four native builds and four simulator smokes). All four same-run result JSON files and screenshots were inspected. iOS evidence is unsigned simulator only; SecureStore is unavailable and Keychain persistence is not verified.

Current documentation synchronization creates a new PR head and must receive fresh exact-head CI. After that run and final COMMENT-only review, merge only if every required check passes and GitHub permits. Then re-read `main` and continue sequentially to #3 RIDER-01; after its audit, proceed to #4 RIDER-02 proposal idempotency. The next RIDER-02 audit found that Rider and Driver proposal handlers generate a new random Idempotency-Key on every button press/retry, despite backend same-key/same-payload replay and changed-payload conflict semantics. Implement a shared stable proposal-attempt key with an in-flight guard, retaining the key after ambiguous failures and resetting only after a confirmed proposal success or payload/ride change. Do not open a second feature PR while #143 is active.

Strict acceptance remains 0/19 (15 In Progress, 4 Blocked); this is a complete-evidence count, not an implementation-completion percentage. Production readiness is not established. Live TriSend rotation/reuse, signed-device SecureStore/Keychain, live Cosmos, maps/realtime providers, support policies, reconciliation decisions, legal review, signing, hosting and pilot/store acceptance remain external gates.


## Latest AUTH-02 handoff — session-state privacy (2026-11-10)

Source/test commit `b9f02acdc0d67ca5ecf70d4796ef40f933a90677` adds shared role-specific reset helpers. Rider refresh expiry previously cleared tokens/user but retained ride/history/discovery/agreement/route/help state; Driver expiry retained some queue, membership, onboarding, vehicle/document and selfie state. The new reset helpers clear those session-scoped values before replacement login, after refresh expiry, and after successful logout (only after SecureStore deletion succeeds). AUTH-LOGIN-057/058 plus two additional shared tests verify complete reset, setter preflight and fresh collection instances.

CI #1151 / 42831865877 is pending on the exact source/test head. Documentation synchronization is on top and requires a fresh final-head CI. CI #1150 / 42819171585 is still in progress on superseded documentation head `c10b19f...` after the Rider iOS artifact-upload DNS retry; do not merge based on it.

After AUTH-02 merges, re-read main and continue sequentially: #3 RIDER-01 audit; #4 RIDER-02 stable proposal Idempotency-Key reuse for double-tap/ambiguous retries; #5 RIDER-03 server-side single-active Rider booking reservation across different keys. Do not open another feature PR while #143 is active.

Strict acceptance remains 0/19 (15 In Progress, 4 Blocked); this is not an implementation-completion percentage. Production readiness is not established. Live TriSend rotation/reuse, signed-device SecureStore/Keychain, live Cosmos ETag/multi-instance/restart, product-approved dispatch radius/zones/location freshness and negotiation lifetime, maps/realtime providers, support policies, reconciliation decisions, legal review, signing, hosting and pilot/store acceptance remain external.


## Latest exact-head verification — AUTH-02 PR #143 (2026-11-10)

The documentation head `97c8528f56596537c9e46aec0277a4a83b4286a1` passed [CI #1153 / 42832334281](https://github.com/rajeevp727/turnova-platform/actions/runs/42832334281) with **12/12 jobs successful**. Exact-head results: .NET 10 backend **373/373**; shared mobile JavaScript **140/140**; Python native-artifact/simulator harness **41/41**; Rider and Driver TypeScript validation and production JS bundles; both Android native builds; both unsigned iOS Simulator native builds; and all four Android/iOS simulator smoke jobs.

Same-run artifacts were downloaded and inspected, including each result JSON and screenshot:
- Rider Android: `13773461057` — `com.turnova.rider`, install/launch passed, process stable, Rider login screen verified, no stale-session error, zero Pixel Launcher ANR recovery attempts.
- Driver Android: `13773895577` — `com.turnova.driver`, install/launch passed, process stable, Driver login screen verified, no stale-session error, zero Pixel Launcher ANR recovery attempts.
- Rider iOS: `13772979189` — `com.turnova.rider`, clean simulator data erased, install/launch passed, process stable, Rider login screen verified.
- Driver iOS: `13775055975` — `com.turnova.driver`, clean simulator data erased, install/launch passed, process stable, Driver login screen verified.

Both iOS result JSONs report `secureStoreAvailable=false`, the expected SecureStore-unavailable banner verified, no Keychain access group, unsigned simulator artifacts, and production API/authentication not exercised. The screenshots visibly show the expected warning. This is simulator startup/UI evidence only; it does not establish signed-device Keychain persistence, production authentication, or physical-device acceptance.

CI #1153 and its four inspected simulator artifacts belong to pre-documentation head `97c8528f56596537c9e46aec0277a4a83b4286a1`. Subsequent commits update documentation only and do not change runtime or test source. The current PR head is recorded in PR #143 metadata; exact-head CI #42841628009 is pending. The prior review is COMMENT-only on the pre-documentation head; a fresh exact-head COMMENT review and current thread check are required after CI completes. The repository rulesets endpoint returned an empty list, while branch-protection details were not readable by the connected integration (403). The expected-head merge operation must enforce any remaining GitHub policy; do not self-approve or bypass protections.

AUTH-02 engineering implementation and deterministic evidence are complete for this increment. Strict acceptance remains **0/19** (15 In Progress, 4 Blocked); this is a complete-evidence count, not an implementation-completion score. Production readiness is not established. External AUTH-02 gates remain live TriSend refresh rotation/reuse semantics and signed Android/iOS physical-device restore, expiry, logout, and SecureStore/Keychain failure acceptance.

Next sequential work after merge: re-read `main`, finish the #3 RIDER-01 audit, implement #4 RIDER-02 stable same-payload proposal Idempotency-Key reuse for double-tap/ambiguous retries in both apps, then #5 RIDER-03 server-side single-active Rider booking reservation across different keys. Maintain one active feature PR.


## AUTH-02 latest privacy correction — Driver payslip expiry (2026-11-10)

The separate Driver payslip-download 401/refresh-failure branch previously cleared only a subset of state. It now calls `clearDriverSessionState()` after secure-token and cached-payslip cleanup, ensuring queue/membership, availability, onboarding, Union, vehicle/document/selfie and Help state are cleared too. AUTH-LOGIN-059 is a source-integration regression for the wiring. Current PR head requires fresh exact-head CI; CI #1153 passed before this follow-up and must not be reused as its evidence.


## Latest AUTH-02 handoff — auth form and Rider pickup cache (2026-11-10)

PR #143 remains the sole active feature PR. Current source/test head is dd5b61efde3f730cddce434c3371c859b4f19001. Previous source/test head 639e6c1 passed [CI #1159 / 42842677468](https://github.com/rajeevp727/turnova-platform/actions/runs/42842677468) 12/12: backend 373/373, shared JavaScript 141/141, Python 41/41, both mobile validation/bundles, four native builds and four simulator smoke jobs. Same-run artifacts were inspected: Rider Android 13777386979, Driver Android 13778300986, Rider iOS 13777927561 and Driver iOS 13778222906. The iOS app artifacts 13778820239 and 13778236148 contain the expected com.turnova.rider and com.turnova.driver identifiers. Both iOS smoke result JSONs/screenshots show the expected SecureStore-unavailable warning; no Keychain access group or production auth was exercised.

The current follow-up extends role-specific reset helpers to clear old email/password/name/mode/error state. Replacement-login flows preserve only the current attempt's input, and successful login clears the password. Rider reset invalidates cached pickup GPS coordinates, rotates the booking idempotency key and does not release bookingInFlight while an older request remains unresolved. New deterministic tests AUTH-LOGIN-060/061 bring the expected shared suite to 143/143; these changes are not yet covered by CI. Run exact-head CI and inspect all four same-run simulator artifacts before final review/merge.

After AUTH-02 merges, re-read main, finish #3 RIDER-01 audit, then implement #4 RIDER-02 stable proposal idempotency and #5 RIDER-03 single-active Rider booking reservation. Keep one active feature PR.

Strict acceptance remains 0/19 (15 In Progress, 4 Blocked); substantial implementation/evidence exists separately. External gates include live TriSend refresh rotation/reuse, signed-device SecureStore/Keychain, live Cosmos ETag/multi-instance/restart, Product dispatch radius/zones/location freshness, support policies, reconciliation policy, maps/realtime providers, private document controls, legal partnership review, signing, hosting, physical-device/pilot and store acceptance. Production readiness is not established.


## Latest AUTH-02 handoff — 2026-11-10

Driver `clearDriverSessionState()` now clears the session-scoped `receivedOfferIds` Set used to deduplicate at-least-once SignalR offers. This prevents a prior account's seen-offer IDs from suppressing a new account's offer. The existing 500-entry cap remains. AUTH-LOGIN-062 covers the source integration. Source/test head 87129ce909ae39c1b3a42e0b175a93436201ee58; fresh exact-head CI is required. Preceding head fe3c029 passed CI #1163 / 42852250391 12/12; all four same-run simulator smoke artifacts were inspected. Next sequential work after AUTH-02 merge: re-read main, complete #3 RIDER-01 audit, then implement #4 RIDER-02 stable same-payload proposal idempotency and #5 RIDER-03 Rider-scoped active-booking reservation.

## Latest sequential handoff — RIDER-02 proposal-attempt idempotency (2026-11-10)

PR #143 AUTH-02 merged at `a3e701f7acb3793d70d6656e5f7ac8b55791a1a4` after [CI #1166 / 42861806486](https://github.com/rajeevp727/turnova-platform/actions/runs/42861806486) passed 12/12. All four same-run simulator artifacts were downloaded and inspected; unsigned iOS SecureStore-unavailable behavior is expected, and signed-device Keychain/production authentication is not claimed.

PR #145 is the sole active feature PR on `fix/rider-02-proposal-idempotency`. It adds shared client proposal-attempt idempotency in Rider and Driver: stable key for identical retry payloads, new key on ride/actor/amount/currency changes, double-tap guard, disabled amount input, retained key after ambiguous POST/history refresh failure, expected-key clear and session-reset cleanup. Backend actor-scoped immutable-version replay and HTTP 409 mismatch remain authoritative. Code head `ca5df7792e3962c542e5c5b129b3a0cc52e0c016` CI #1168 / 42872267649 passed 12/12; backend 373/373, shared JS 151/151, Python 41/41, both app validation/bundles, four native builds and four simulator smokes passed. All four same-run simulator artifacts were downloaded and inspected. A new actor/currency fingerprint regression was added at `927657d1917fd7cb1d32cc234b3bb4051979ba27`; documentation synchronization on this PR still requires fresh exact-head CI; CI #1168 predates this test. COMMENT-only review 5847336885 found no critical/high issue. Pending attempt persistence across process death is not proven.

Fresh #3 RIDER-01 audit found no additional independent source gap: Cab-only union-grouped discovery, max-three pickup-proximity ordering, union-approved selfie/verified Cab details, location freshness, distance separation and privacy filtering are implemented and covered by deterministic tests. Product-approved radius/zones/freshness, maps/routing, Call/Chat, live Cosmos and physical-device location acceptance remain external.

Next sequential work after PR #145 merges: #5 RIDER-03. The selected-booking API protects each Driver with a reservation but has no Rider-scoped active-booking reservation across different Idempotency-Keys. Add durable Rider reservation using existing `RideIdempotency` container partitioned by `/riderId` (or equivalent), create-if-absent/ETag protection, expiry/orphan reconciliation, promotion on Driver acceptance, matching terminal release and concurrent different-key tests. Production negotiation lifetime remains unset and booking fails closed with 503.

Strict acceptance remains 0/19 (15 In Progress, 4 Blocked); production readiness is not established.


## Latest sequential handoff — RIDER-02 proposal submission ownership hardening (2026-11-11)

PR #145 remains the sole active feature PR on `fix/rider-02-proposal-idempotency`. Current source/test head: `56a363621029fbfb16e2ef7c72d14f1faad48c8d`.

A deeper review found that the original boolean proposal in-flight guard could remain set after account reset/ride change until an old network promise settled. Its unconditional `finally` could also release a newer submission. Both Rider and Driver now use an attempt-key-owned lock. `finishProposalSubmission(ref, expectedKey)` only clears a matching owner; session reset and ride identity change clear the old attempt/lock and set submitting UI state false. The old finalizer is harmless if a replacement attempt owns the lock.

New deterministic coverage:
- `a stale proposal completion cannot release a newer in-flight submission`.
- Rider/Driver source-integration assertions verify lock ownership, session reset cleanup and ride-change cleanup.
- New test-plan cases CAB-AGR-029 and CAB-AGR-030.
- Expected shared JavaScript suite: 153 tests.

Earlier CI #1168 passed 12/12 before the actor/currency and lock-ownership additions. CI #1171 / 42879001921 was on superseded head `0a7bb547...`; it must not be reused. Fresh exact-head CI on `56a363621029fbfb16e2ef7c72d14f1faad48c8d` and all same-run native/simulator artifact inspection are required. PR #145 is not merged.

The pending proposal attempt remains in-memory; replay across process death is not proven. Live Cosmos ETag/multi-instance and physical-device negotiation/reconnect remain external gates.

Next after PR #145 merges: re-read main and implement #5 RIDER-03 Rider-scoped active-booking reservation across different Idempotency-Keys, using the existing rider-partitioned `RideIdempotency` storage only after verifying partition/ETag/transaction semantics. Strict acceptance remains 0/19 (15 In Progress, 4 Blocked); production readiness is not established.


## Latest verified handoff — RIDER-02 (2026-11-11)

PR #145 source/test head `56a363621029fbfb16e2ef7c72d14f1faad48c8d` and documentation head `9fbca10d57334167aa41018a9104096f9f85e877` passed [CI #1180 / 42882071110](https://github.com/rajeevp727/turnova-platform/actions/runs/42882071110) with **12/12 jobs successful**. Results: .NET 10 backend **373/373**, shared mobile JavaScript **153/153**, Python native-artifact/simulator harness **41/41**, Rider/Driver TypeScript validation and production bundles, both Android native builds, both iOS Simulator native builds, and all four Android/iOS simulator smoke jobs. The Driver Android smoke initially encountered a Pixel Launcher ANR; only that failed job was rerun, and the retry passed without app changes. The final workflow conclusion is success.

Same-run smoke artifacts were downloaded and inspected, including result JSON and screenshots:
- Rider Android `13795890654`: `com.turnova.rider`, install/launch passed, process stable, Rider login text and screenshot verified, stale-session error absent.
- Driver Android `13798054264`: `com.turnova.driver`, targeted retry after the first artifact `13795995296` recorded a Pixel Launcher ANR; retry install/launch passed, process stable, Driver login text and screenshot verified, stale-session error absent, zero launcher recovery attempts on the successful retry.
- Rider iOS `13796097620`: `com.turnova.rider`, clean simulator data erased, install/launch passed, process stable, Rider login screen verified.
- Driver iOS `13795844308`: `com.turnova.driver`, clean simulator data erased, install/launch passed, process stable, Driver login screen verified.

Both iOS result JSONs report `secureStoreAvailable=false`, the expected SecureStore-unavailable banner, no Keychain access group, unsigned simulator artifacts, and production API/authentication not exercised. These artifacts prove simulator install/launch/login-surface checks only; they do not establish signed-device Keychain persistence, production authentication, live Cosmos, or physical-device acceptance. Final-head COMMENT-only review [5847757020](https://github.com/rajeevp727/turnova-platform/pull/145#pullrequestreview-5847757020) found no critical/high issue; unresolved review threads: **0**. The subsequent documentation-only synchronization requires fresh exact-head CI before merge.

PR #145 adds stable client proposal-attempt idempotency in Rider and Driver: same ride/actor/amount/currency reuses the same actor-prefixed key after ambiguous POST or history-refresh failure; a changed fingerprint creates a new key; double taps are blocked; the amount field is disabled while submitting; and an attempt is cleared only after the agreement history refresh succeeds or the session/ride resets. The in-flight lock is owned by the attempt key, so a stale request finalizer cannot release a newer account/ride submission. Deterministic coverage includes actor/currency fingerprinting, stale-finalizer ownership and Rider/Driver session/ride cleanup. The pending attempt remains in an in-memory ref; replay across process death is not proven.

Fresh #3 RIDER-01 audit found no additional independent source-code gap. Product-approved dispatch radius/zones/location freshness, routing/maps, live Call/Chat, live Cosmos and physical-device location acceptance remain external. Next sequential item after PR #145 merges is #5 RIDER-03: add a durable Rider-scoped active-booking reservation across different Idempotency-Keys. The current route has per-Driver reservation only; verify the existing RideIdempotency container migration/partition and transaction semantics before adding create-if-absent/ETag-safe Rider reservation, terminal/orphan reconciliation, matching release and concurrent different-key tests. Production negotiation lifetime remains unset and booking fails closed with 503.

Strict acceptance remains **0/19** (15 In Progress, 4 Blocked); this is a complete-evidence count, not an implementation-completion score. Production readiness is not established.


## Current RIDER-03 handoff — 2026-11-11

Main HEAD: `ab988b02b01d89394e5213586f9ee24f0e78c6b1` (RIDER-02 PR #145 merged). Active draft PR #146: `feat/rider-03-single-active-booking`, head `7e49cce79698fe5c107d8d8014e5eca61dd54d56`; exact-head CI #1199 is pending.

### Rider-scoped reservation design
- Reuse the existing Cosmos `RideIdempotency` container, partition key `/riderId`; reservation document ID is `active-selected-booking`. No new container or migration.
- Create-if-absent reservation and privacy-minimised audit event are written in one same-partition transactional batch. Permanent promotion and matching release are ETag-conditional transactional batches with append-only audit.
- In-memory implementation is atomic and uses an injectable clock for deterministic expiry tests. It never steals an expired reservation; the API reconciles the authoritative ride first.
- The booking route reserves the Rider before acquiring the selected Driver lease and persisting the ride. Same RideId/request-hash retries are idempotent; a different active RideId returns HTTP 409.
- Negotiating/AgreementConfirmed leases remain temporary. Driver acceptance promotes the Rider reservation to permanent. Cancellation, Driver decline, expiry and completion release only the matching RiderId+RideId. Terminal read-repair handles interrupted release.
- Same-key replay repairs a missing reservation and returns the existing ride. Acceptance can repair a pre-deployment selected ride only if no different Rider reservation exists and the request fingerprint reconstructed from the ride matches.
- Rider app loads server-owned active ride state after sign-in, redirects to Activity and blocks Book/discovery/request until terminal state. Backend enforcement is authoritative.

### Deterministic evidence added (pending CI)
- Store unit tests: atomic different-Ride acquisition, same-Ride/hash replay, independent Rider partitions, permanent promotion, exact/stale release, expired no-steal and audit privacy.
- API integration tests: one Rider/two Drivers/different keys, independent Riders, cancellation/rebooking, accepted promotion and same-key replay, completion release, expired-negotiation reconciliation and Driver-decline release.
- Mobile shared tests: active status helper and source integration for sign-in hydration, Book tab guard, discovery guard and request guard.

### External gates
Live Cosmos transactional-batch/ETag/multi-instance/restart validation and Android/iOS physical-device booking/cancel/expiry acceptance remain unverified. Production `Dispatch:SelectedDriverNegotiationLifetimeMinutes` is unset; selected booking correctly returns 503 until Product approves/configures the value. The 15-minute test fixture is not a product default.

Next after merge: re-read main, audit #6 DISPATCH-01 in order, implement all safe gaps, then proceed to DISPATCH-02. Do not open a second feature PR while this one is active.


## Latest engineering handoff — 2026-11-11 (RIDER-03)

- Main HEAD: `ab988b02b01d89394e5213586f9ee24f0e78c6b1`; PR #145 RIDER-02 is merged. Sole active feature PR: draft #146 `feat/rider-03-single-active-booking`.
- Source/test HEAD before this documentation sync: `726d1b4356231d4993aa6292c09d5e5a33f635de`. The code adds a durable Rider-scoped reservation in the existing `RideIdempotency` `/riderId` partition, transactional audit, ETag promotion/release, legacy active-ride reconciliation, and fail-closed Rider app hydration. An explicit `allowExpiredForAuthoritativeActiveRide` option repairs only a lease attached to a ride the API has re-read as Accepted/Arrived/InProgress; default expired promotion remains rejected.
- Test plan CAB-BOOK-001–005 covers UI hydration/reset and explicit expired-lease repair. Previous tested head `18b8651d23970030f111310ad510103aea02957e` passed workflow `42904988755` 12/12 (backend 388/388, shared mobile 167/167, Python 41/41, four native builds and four simulator smokes); all four same-run artifacts were inspected. New source/test head and this docs sync require fresh exact-head CI; no result is claimed yet.
- External gates: Product-approved `Dispatch:SelectedDriverNegotiationLifetimeMinutes` is unset; live Cosmos ETag/transactional-batch/multi-instance/restart validation; physical Android/iOS booking/cancel/expiry acceptance. Strict acceptance remains 0/19 (15 In Progress, 4 Blocked); production readiness is not established.
- Next: finish exact-head CI and review/merge PR #146 only if policy permits; re-read main; continue to #6 DISPATCH-01 in order. Do not open a parallel feature PR.


## RIDER-03 final source/test hardening — 2026-11-11

- Source/test head: `d8a052a2ccad3ee1a733b099505d2d5c9433eaee`.
- Added fail-closed Rider booking hydration and explicit authoritative expired-lease promotion. The shared mobile source-integration test now asserts that `clearRiderSessionState` resets `activeRideStateLoaded`, `loadRideHistory` clears it before the server history read, and it becomes true only after the active-ride detail read completes. This directly covers CAB-BOOK-004 rather than merely finding a reset call elsewhere in the file.
- The prior docs head `80c4676dca6907093a03dc0e8e6af1d7c2a1e7de` had backend 389/389, shared mobile 170/170, Python 41/41 and both app validation jobs passed; native/simulator jobs were superseded before completion by this final test assertion. Fresh exact-head CI is required.


## Previous verified handoff — RIDER-03 exact-head CI #1214 (2026-11-11)

PR #146 was merged to main at `b1ea22f4e2d19e76f5b182b93e527960b110ad7a`. Its CI-verified code/test evidence is CI [#1214 / 42920911797](https://github.com/rajeevp727/turnova-platform/actions/runs/42920911797), 12/12: backend 403/403, shared mobile 171/171, Python 41/41, both app validation/bundles, four native builds and four simulator smokes. Same-run artifacts 13811579024, 13813150972, 13812929395 and 13813746404 were inspected. The next sequential task is DISPATCH-01 selected-booking fail-closed tests; see the latest handoff below.

## Latest handoff — DISPATCH-01 exact-head CI #1222 (2026-11-11)

- Main HEAD: `b1ea22f4e2d19e76f5b182b93e527960b110ad7a`. Sole active feature PR: [#147](https://github.com/rajeevp727/turnova-platform/pull/147), branch `test/dispatch-01-selected-booking-config-guards`, source/test HEAD `8cca1958f0e9d67d57ee69ee5327fd4449d9651a`, base `main`; PR is Draft pending docs-synchronized exact-head CI.
- Added nine executed API theory cases: six radius cases (missing, zero, negative, NaN, positive/negative infinity) and three location-freshness cases (missing, zero, negative). This is eight net additional backend tests because one existing Fact expanded to three cases. All assert HTTP 503 before ride, idempotency, Driver-reservation or Rider-reservation writes; Driver profile remains Online and unchanged.
- Exact-head CI [#1222 / 42929505358](https://github.com/rajeevp727/turnova-platform/actions/runs/42929505358), run attempt 2, passed **12/12**: backend **411/411**, shared mobile **171/171**, Python **41/41**, Rider/Driver TypeScript and bundles, four native builds and four simulator smokes. Initial Rider iOS simulator attempt hit the bounded 300-second `simctl install` timeout after a slow clean-simulator boot; only that failed job was retried, and the retry passed without code changes.
- Same-run artifacts inspected: Rider Android smoke `13817215681`, Driver Android smoke `13816635972`, Rider iOS smoke `13818882794`, Driver iOS smoke `13818155109`; Rider iOS app `13817225157`, Driver iOS app `13816243464`. Both Android apps installed/launched and displayed correct role-specific login screens with stable processes, no stale-session errors and zero Pixel Launcher ANR recovery attempts. Both unsigned iOS apps installed/launched, remained stable and displayed the expected SecureStore-unavailable banner. iOS bundle IDs: `com.turnova.rider`, `com.turnova.driver`. No provisioning profile, signed-device Keychain or production authentication is claimed.
- Final source-head COMMENT-only review [5851552534](https://github.com/rajeevp727/turnova-platform/pull/147) found no critical/high issue; unresolved inline review threads: **0**. Rulesets API returned no rulesets; branch-protection API was inaccessible (403). Never self-approve or bypass; rely on GitHub merge enforcement and expected-head SHA.
- DISPATCH-02 audit: discovery cap/order/privacy, foreground GPS capture and same-process stable retry are implemented. Rider post-auth hydration reads server-owned history before enabling Book; RIDER-03's durable Rider reservation prevents a different-key overlapping booking after process restart. Exact same-response replay after process death is not guaranteed, and precise GPS is not persisted locally. No additional code gap identified.
- DISPATCH-03 next safe regression: prove active selected-driver decline and Driver reject of an already-expired selected negotiation never invoke the legacy FIFO advancer. The current route reads/reconciles the selected ride and returns before the legacy queue/advancer branch; existing FIFO regression exercises direct Rider read only.
- Next: complete docs sync, run fresh exact-head CI, submit final COMMENT-only review, mark PR #147 ready and merge only if all required checks pass and GitHub permits. Re-read main; continue sequentially through DISPATCH-02 evidence and DISPATCH-03 FIFO-isolation regression. Keep one feature PR active.
- Strict acceptance remains **0/19** (15 In Progress, 4 Blocked); this is a complete-evidence count, not an implementation-completion score. Product-approved radius/zones/freshness, live Cosmos, physical-device, signing, maps/realtime, support-policy, reconciliation-policy, legal and production-hosting gates remain. Production readiness is not established.


## Latest exact-head verification — DISPATCH-01 documentation CI #1225 (2026-11-11)

- PR #147 remains the sole active feature PR on `test/dispatch-01-selected-booking-config-guards`; source/test head `8cca1958f0e9d67d57ee69ee5327fd4449d9651a`, documentation head verified by CI `7bcb81cb9596fc9f2877bb6f79a7c502665f8e80`, base main HEAD `b1ea22f4e2d19e76f5b182b93e527960b110ad7a`.
- Documentation-synchronized CI [#1225 / 42938055684](https://github.com/rajeevp727/turnova-platform/actions/runs/42938055684) completed **12/12 jobs successfully**: .NET backend **411/411**, shared mobile **171/171**, Python native-artifact/simulator tests **41/41**, Rider/Driver TypeScript validation and production bundles, four native Android/iOS builds, and four Android/iOS simulator smoke jobs.
- Same-run simulator artifacts were downloaded and inspected: Rider Android `13818804661`, Driver Android `13819773601`, Rider iOS `13820856185`, Driver iOS `13821041213`. Both Android apps installed/launched, remained stable and displayed their role-specific login screens; stale-session errors were absent and Pixel Launcher ANR recovery attempts were zero. Both unsigned iOS apps installed/launched, remained stable and displayed role-specific login screens plus the expected SecureStore-unavailable banner. Their result JSONs report `secureStoreAvailable=false`, no Keychain access group, unsigned simulator artifacts and production API/authentication not exercised. This is not signed-device Keychain, production authentication or physical-device acceptance.
- Backend job steps confirm native .NET 10 restore/build/test and non-container Linux-x64 publish succeeded. No Docker/container build or deployment was added.
- Final review must be COMMENT-only on the latest post-sync head; the existing review [5851552534](https://github.com/rajeevp727/turnova-platform/pull/147) is anchored to the source/test head, not this documentation head. Recheck review threads and exact-head CI before marking ready. Merge only if GitHub permits the expected-head merge.
- DISPATCH-02 audit remains: capped three-per-union discovery, raw pickup-distance ordering, union-approved selfie/verified Cab projection, foreground GPS capture and same-process retry are implemented. Exact response replay across process death is not guaranteed; precise GPS is not persisted locally. No independent source gap identified.
- DISPATCH-03 next regression increment: prove active selected-driver decline and Driver rejection of an already-expired selected negotiation never invoke the legacy FIFO advancer. Preserve legacy FIFO compatibility; it must not rank, auto-assign or advance the customer-selected Rider flow.
- Strict acceptance remains **0/19** (15 In Progress, 4 Blocked); this is the full-evidence count, not an implementation-completion score. Product-approved radius/zones/freshness, live Cosmos, physical-device, signing, maps/realtime, support, reconciliation, legal and production-hosting gates remain. Production readiness is not established.


## Latest exact-head handoff — DISPATCH-03 CI #1231 passed (2026-11-11)

- Main HEAD at PR #148 base: `2f99083b4ac57bca7a6a1cefaa1a212f1c82a80e`. Sole active feature PR [#148](https://github.com/rajeevp727/turnova-platform/pull/148), branch `test/dispatch-03-selected-reject-fifo-isolation`, source/test commit `c7a9d4649dfa5343f27515e12780e0cbdab166fc`, tested documentation head `3ef0e10c953a4538e25162fcc015aea36d513541`; PR remains open and Draft. No parallel feature PR exists.
- Exact-head CI [#1231 / 42950871464](https://github.com/rajeevp727/turnova-platform/actions/runs/42950871464), run attempt 3, passed **12/12 jobs**. Backend **413/413** (18 warnings, zero errors); shared mobile **171/171**; Python **41/41**; Rider/Driver validation and bundles; four native Android/iOS builds; four simulator smokes.
- Same-run smoke artifacts inspected: Rider Android `13825815457`, Driver Android retry `13826556864`, Rider iOS `13826002670`, Driver iOS retry `13826194724`. Both Android apps installed/launched, remained stable and displayed the correct role-specific login screens. Both unsigned iOS apps installed/launched and displayed role-specific login screens plus the expected SecureStore-unavailable banner. Native iOS app artifacts Rider `13825272195` and Driver `13825786705` confirm `com.turnova.rider` / `com.turnova.driver`, display names, version 0.1.0 and minimum iOS 15.1; no code signature or provisioning profile is present.
- Initial Driver Android smoke failed because a Pixel Launcher ANR dialog persisted after bounded recovery; its one targeted retry passed. Initial Driver iOS smoke timed out during `simctl install` after 300 seconds waiting for System App; its one targeted retry passed. No smoke check was waived. Production API/authentication, live Cosmos and physical-device behaviour were not exercised.
- New DISPATCH-03 tests: `DriverDecliningSelectedBookingDoesNotAdvanceLegacyFifoOffer` asserts HTTP 204, Negotiating→Cancelled, Driver-owned audit, exact Driver/Rider reservation release and zero FIFO advancer calls. `DriverRejectingExpiredSelectedNegotiationDoesNotAdvanceLegacyFifoOffer` asserts authoritative Negotiating→Expired System audit before stale Driver reject returns 409, exact reservation release and zero FIFO calls. Existing Rider-read expiry test and legacy FIFO positive control remain. No runtime FIFO semantics changed.
- Next: finish documentation synchronization, run fresh exact-head CI, submit final COMMENT-only review, mark PR #148 ready, and merge only if all checks pass, latest SHA is verified, no critical/high issue or unresolved review thread remains, and GitHub permits the expected-head merge. Re-read main after merge.
- Next sequential item #10 REALTIME-01: `RideTrackingHubAuthorization.CanJoinRideGroup` currently checks role/ownership but does not require `ride.RideId` to equal the requested query/group ID. Add an ordinal exact-ID check and an `OnConnectedAsync` regression proving a mismatched stored aggregate aborts before any group membership. Then continue to #11 DRIVER-01.
- DISPATCH-04 audit confirmed existing exclusive per-Driver leases, booking-vs-Offline/Union-transfer concurrency protection, accepted-active-ride exclusion and UnionManager selfie-review reservation reconciliation; no additional independent source gap found. Live Cosmos ETag/transactional-batch/multi-instance/restart, Product-approved negotiation lifetime, Android/iOS physical-device acceptance, signed-device SecureStore/Keychain, maps/realtime providers, support/escalation policies, private document controls, reconciliation policy, legal partnership review, production hosting/signing/pilot/store gates remain external.
- Strict acceptance remains **0/19** (15 In Progress, 4 Blocked); this is a complete-evidence count, not an implementation-completion score. Production readiness is not established.


## Latest exact handoff — DRIVER-01 phone validation (2026-11-11)

- REALTIME-01 PR [#149](https://github.com/rajeevp727/turnova-platform/pull/149) merged at `9129b2bfbf174e8df07a9713f7e99f51d232cb45`. Exact-head CI [#1239 / 42963719244](https://github.com/rajeevp727/turnova-platform/actions/runs/42963719244), attempt 2, passed 12/12: backend 416/416, shared mobile 171/171, Python 41/41, Rider/Driver validation and bundles, four native builds and four simulator smokes. All four same-run simulator artifacts were inspected. No live WebSocket/backplane, production auth, live Cosmos or physical-device acceptance is claimed.
- Sole active feature PR [#150](https://github.com/rajeevp727/turnova-platform/pull/150), branch `fix/driver-01-phone-validation`, based on main `9129b2bfbf174e8df07a9713f7e99f51d232cb45`. Latest source/test HEAD `e2c665a248b816f4e29de109e01ad430c4e5cfdb`; runtime commit `6adf12d2ebaad3c532fa68a606ad95ade89b7987`; CI #1244 passed on the superseded source/test head `b1da63bb5987d27f26455119bd4e0e280a4c7428`; expanded boundary/profile-preservation tests are now on `e2c665a248b816f4e29de109e01ad430c4e5cfdb` and require fresh exact-head CI.
- DRIVER-01 runtime validation accepts an optional single leading '+' followed by 10–15 ASCII digits. Nine malformed inputs return HTTP 400 without profile creation; four 10/15-digit boundary values with/without '+' are accepted; malformed resubmission preserves the existing profile. Test-plan ID `DRIVER-01-ONBOARD-014`. This validates syntax only; no phone ownership, OTP/SMS or identity-provider assurance is claimed.
- Exact-head CI [#1244 / 42971085313](https://github.com/rajeevp727/turnova-platform/actions/runs/42971085313) passed **12/12 jobs** on `b1da63bb5987d27f26455119bd4e0e280a4c7428`: backend **423/423**, shared mobile **171/171**, Python **41/41**, Rider/Driver TypeScript validation and production JS bundles, native API publish, four Android/iOS native builds and four simulator smoke jobs. Same-run simulator artifacts inspected: Rider Android `13833549494`, Driver Android `13833473650`, Rider iOS `13834578010`, Driver iOS `13834996824`. Android results report install/launch passed, stable processes, role-specific login text, no stale-session error and zero Pixel Launcher ANR recovery attempts. Unsigned iOS simulator results report install/launch passed, stable processes, role-specific login text and the expected SecureStore-unavailable banner; `secureStoreAvailable=false`, no Keychain access group, and production API/authentication not exercised. These are simulator/build checks, not signed-device, live-provider or production acceptance.
- Release-readiness JSONs still report engineering checks passed and production readiness blocked. Both mobile dependency audits report 11 moderate, 1 low, 0 high and 0 critical advisories. The available complete remediation path moves Expo SDK 54 to SDK 58, a major compatibility upgrade; it is intentionally not mixed into this focused PR and requires a separate dependency/native verification increment.
- External gates include phone ownership/OTP and upstream identity-provider validation; private document storage/upload/scanning/consent/retention; signed UnionManager/current-union claim issuance; live Cosmos ETag/multi-instance/restart; physical-device camera/document permissions; production API URL, EAS linkage, build-number policy, signing, pilot and store acceptance; legal partnership approval.
- DRIVER-02 is already implemented and merged through PR #117; its remaining Product-approved GPS freshness/zone policy and live Cosmos/device checks are external. Next sequential acceptance area after #11 DRIVER-01 is **#13 DRIVER-03** (Driver work metrics and document export), not a second DRIVER-02 implementation.
- Strict acceptance remains **0/19** (15 In Progress, 4 Blocked); this is the complete-evidence count, not an implementation-completion score. Production readiness is not established.


## Latest exact-head handoff — DRIVER-01 CI #1248 passed (2026-11-11)

- Last audited main/base HEAD: `9129b2bfbf174e8df07a9713f7e99f51d232cb45`. Sole active feature PR [#150](https://github.com/rajeevp727/turnova-platform/pull/150), branch `fix/driver-01-phone-validation`; runtime commit `6adf12d2ebaad3c532fa68a606ad95ade89b7987`, expanded source/test commit `e2c665a248b816f4e29de109e01ad430c4e5cfdb`, documentation/test-plan head `09f7be0c91ba64741c680ce477438920fdda9828`.
- Exact-head GitHub Actions [CI #1248 / 42977122031](https://github.com/rajeevp727/turnova-platform/actions/runs/42977122031) completed **12/12 jobs successfully** on `09f7be0c91ba64741c680ce477438920fdda9828`: .NET backend **430/430** (0 failed, 0 skipped), shared mobile **171/171**, Python native-artifact/simulator harness **41/41**, Rider/Driver TypeScript validation and production bundles, native API publish, two Android and two iOS native builds, and four Android/iOS simulator smoke jobs.
- Same-run simulator artifacts downloaded and inspected: Rider Android `13836830858`, Driver Android `13836218302`, Rider iOS `13837860133`, Driver iOS `13837290357`. Android result JSONs report install/launch passed, stable processes, correct role-specific login text, no stale-session error and zero Pixel Launcher ANR recovery attempts. Unsigned iOS result JSONs report install/launch passed, stable processes, correct role-specific login text, expected SecureStore-unavailable banner, `secureStoreAvailable=false`, no Keychain access group and production API/authentication not exercised. All four screenshots were visually inspected. This is simulator/build evidence only, not signed-device, live-provider or production acceptance.
- Release-readiness artifacts Rider `13836376099` and Driver `13835742296` report engineering checks passed but production readiness blocked. Both apps lack registered iOS bundle IDs, EAS project IDs, build-number policy and production HTTPS API URL; no signed AAB/IPA is produced. Release Owner, Product/App Owner, Platform/Infrastructure and QA must supply/verify those inputs, physical-device acceptance and store submission. Never commit signing secrets.
- DRIVER-01 phone validation accepts an optional single leading '+' followed by 10–15 ASCII digits. Nine malformed inputs (including misplaced/repeated plus, whitespace, letters, 9/16-digit values and Arabic-Indic digits) return HTTP 400 before profile creation; four valid 10/15-digit boundary forms with/without '+' are accepted; malformed resubmission preserves the complete existing profile. Test-plan IDs `DRIVER-01-ONBOARD-014/015`. This is syntax validation only; phone ownership, OTP/SMS and identity-provider assurance remain external.
- The iOS simulator applications are unsigned and SecureStore is unavailable in this environment. No signed-device Keychain, production authentication, live Cosmos, private document-storage, legal, or physical-device behaviour is claimed.
- Dependency audit artifacts remain a separate follow-up: the previously inspected Rider/Driver reports show 11 moderate, 1 low, 0 high and 0 critical advisories per app. Expo SDK 54 to SDK 58 is a major compatibility upgrade and is not mixed into this focused PR.
- Ordered acceptance rows: #12 DRIVER-02 is already implemented/merged (PR #117); next is #13 TRIP-01 (reconcile the progress/brain evidence with merged PR #118, then re-audit), followed by #14 DRIVER-03. Do not skip or renumber the matrix.
- Strict acceptance remains **0/19** (15 In Progress, 4 Blocked); this is a complete-evidence count, not an implementation-completion score. Production readiness is not established.

## Latest sequential handoff — DRIVER-01 final-head CI #1255 (2026-11-11)

- Last audited main/base HEAD: `9129b2bfbf174e8df07a9713f7e99f51d232cb45`. Sole active feature PR [#150](https://github.com/rajeevp727/turnova-platform/pull/150), branch `fix/driver-01-phone-validation`; source/test commit `e2c665a248b816f4e29de109e01ad430c4e5cfdb`; documentation head verified by CI #1255: `af9548dadde101776d1568de6900ece53739aceb`.
- Exact-head CI [#1255 / 42985442306](https://github.com/rajeevp727/turnova-platform/actions/runs/42985442306) passed 12/12: backend 430/430, shared mobile 171/171, Python 41/41, Rider/Driver validation/bundles, native API publish, four native builds and four simulator smokes.
- Same-run artifacts inspected: Rider Android `13839539421`, Driver Android `13839718872`, Rider iOS `13840987499`, Driver iOS `13840917053`. Android apps installed/launched, remained stable and displayed correct role-specific login screens; no stale-session errors and zero Pixel Launcher ANR recovery attempts. Unsigned iOS apps installed/launched and displayed role-specific login screens plus the expected SecureStore-unavailable banner; `secureStoreAvailable=false`, no Keychain access group, and production API/authentication not exercised.
- Release-readiness artifacts Rider `13839790635` and Driver `13839381740` report engineering checks passed but production readiness blocked: iOS bundle registration, EAS linkage, build-number policy, production HTTPS API URL, signing credentials, signed physical-device acceptance and store submission remain.
- The PR review is COMMENT-only, not approval; there are zero unresolved review threads. No self-approval or branch-protection bypass is permitted. The latest PR head is documentation-only relative to source/test commit `e2c665a`; fresh CI is required after any further docs synchronization.
- PR #150 merged at `f7114f6c3df896b53b3ab4f502dce17c97982d6b`. #12 DRIVER-02 is already implemented/merged (PR #117); #13 TRIP-01 evidence reconciliation is the current step, followed by #14 DRIVER-03.
- Strict acceptance remains 0/19 (15 In Progress, 4 Blocked); this is the full-evidence count, not an implementation-completion score. Production readiness is not established.


## DRIVER-01 latest exact-head verification — CI #1259 (2026-11-11)

- PR [#150](https://github.com/rajeevp727/turnova-platform/pull/150), branch `fix/driver-01-phone-validation`, remains the sole active feature PR. Main/base HEAD is `9129b2bfbf174e8df07a9713f7e99f51d232cb45`. Runtime commit `6adf12d2ebaad3c532fa68a606ad95ade89b7987` and source/test commit `e2c665a248b816f4e29de109e01ad430c4e5cfdb` are included in the exact documentation-head CI `a4a21d73be69301ccd8f8a71cf5fd1f6a6c1c14e`.
- GitHub Actions [CI #1259 / 42991278634](https://github.com/rajeevp727/turnova-platform/actions/runs/42991278634) completed successfully: **12/12 jobs**, backend **430/430** (0 failed, 0 skipped), shared mobile **171/171**, Python **41/41**, Rider/Driver TypeScript validation and production bundles, native non-container API publish, two Android and two iOS native builds, and all four simulator smoke jobs.
- Same-run artifacts downloaded and inspected: Rider Android smoke `13842058610`, Driver Android smoke `13843415264`, Rider iOS smoke `13844475295`, Driver iOS smoke `13843986871`. Both Android apps installed/launched, remained stable, displayed the correct role-specific login screen, had no stale-session error and zero Pixel Launcher ANR recovery attempts. Both unsigned iOS apps installed/launched, remained stable and displayed their role-specific login screens plus the expected SecureStore-unavailable banner; `secureStoreAvailable=false`, no Keychain access group, and production API/authentication were not exercised. All four screenshots and result JSONs were inspected.
- Release-readiness artifacts Rider `13841173754` and Driver `13840739230` report engineering checks passed but production readiness blocked. Both lack configured iOS bundle registration/EAS linkage/build-number policy/production API URL in the CI-visible configuration; no signed AAB or IPA was produced. Product/App Owner, Release Engineering, Platform/Infrastructure, QA and Release Owner/Legal must complete the listed inputs.
- The phone validator remains syntax-only: optional one leading '+' followed by 10–15 ASCII digits. Nine malformed values are rejected before profile creation, four valid boundary forms are accepted, and malformed resubmission preserves the full existing profile. Phone ownership/OTP and identity-provider assurance remain external.
- No source/test changes occurred after the verified source/test commit. This documentation synchronization is based on the completed CI #1259 evidence; the new documentation commit itself requires fresh exact-head CI before PR merge.
- Strict acceptance remains **0/19** (15 In Progress, 4 Blocked); this is the complete-evidence count, not an implementation-completion score. Production readiness is not established.


## Latest engineering handoff — TRIP-01 evidence reconciliation (2026-11-11)

- Main HEAD: `f7114f6c3df896b53b3ab4f502dce17c97982d6b`. DRIVER-01 PR #150 merged after exact-head CI #1260 / 43000036463 passed 12/12 on `da576ba4d4d63067c638a9d55a13118221c85cee` (backend 430/430, shared mobile 171/171, Python 41/41, Rider/Driver validation/bundles, native API publish, four native builds and four simulator smokes).
- TRIP-01 PR #118 merged at `4a58ea6d5e4d0a516754f3500d8ef60a3ea48829`; source head `31d79773ea14115bb0f6f822f45814b271426204`; exact-head CI #747 / 42492757245 passed 8/8, backend 317/317. Code covers capture watermark/CAS, future/out-of-order rejection, SignalR timestamp, Rider stale-event/history fencing, map-ready follow/recenter/manual-pan pause and Driver foreground-only GPS watcher.
- Fresh main audit found no additional independent TRIP-01 runtime defect. External gates: live Cosmos ETag/multi-instance/restart; live SignalR/backplane/reconnect; maps/routing; physical Android/iOS foreground GPS, map pan/follow and resume; Product decision on next-ride-before-payment-report and non-zero-delta eligibility.
- Next row #14 DRIVER-03. Preserve the existing IST month-boundary tests; the identified code increment is session-safe payslip loading cleanup so an old request finalizer cannot clear a newer authenticated session's loading state.
- Strict acceptance remains 0/19 (15 In Progress, 4 Blocked); production readiness is not established.


## Latest engineering handoff — DRIVER-03 exact-head CI #1269 (2026-11-11)

- Main HEAD `d73c54186052450012ae30d0b93dd431c922dc95`; active PR [#152](https://github.com/rajeevp727/turnova-platform/pull/152), branch `fix/driver-03-payslip-session-loading`, source/test head `d083d9425261e5432bac788ccb55f6d26931c2ab`, latest documentation-only commits (fresh exact-head CI pending).
- Exact-head CI [#1269 / 43017499605](https://github.com/rajeevp727/turnova-platform/actions/runs/43017499605) passed **12/12** after one targeted Rider Android emulator retry: backend **430/430**, shared mobile **176/176**, Python **41/41**, both mobile validation/bundles, native API publish, four native builds and four simulator smoke jobs. Artifacts inspected: Rider Android `13857476146`, Driver Android `13854167151`, Rider iOS `13856305163`, Driver iOS `13855407139`; all four result JSONs and screenshots were inspected. Unsigned iOS results explicitly report SecureStore unavailable, no Keychain access group and production API/authentication not exercised.
- PR #152 fixes stale payslip loading finalization using the captured session epoch; Driver session reset clears loading; failed logout cleanup releases loading only if the same logout epoch remains current. Five deterministic tests (`PAY-028`) cover stale/current finalizer, reset, stale logout and App.tsx wiring. No API, database, PDF, cache deletion, payment, ride-state or token-format change.
- COMMENT-only review `5858442999` is on exact head; zero unresolved review threads; no critical/high-risk finding; no self-approval. PR remains Draft until the documentation-head CI is green.
- Separate unguarded Driver loading finalizers remain in availability, queue-leave, payment-report and onboarding operations. They are not included in this focused PR and must be revisited in their owning acceptance rows.
- Next ordered row #15 DRIVER-04 is blocked pending Product/Operations decisions on categories, required fields, severity, escalation owner/channel, hours/response targets, attachment handling, privacy/retention and operational handoff. No support API or policy is invented. After recording the blocker, proceed to #16 RIDER-04.
- Strict acceptance remains 0/19 (15 In Progress, 4 Blocked); production readiness is not established.


## Latest sequential engineering handoff — RIDER-04 (2026-11-11)

- Main HEAD: `09e8f90eb64cf0112999e4b874511eb201e4158b`. DRIVER-03 PR [#152](https://github.com/rajeevp727/turnova-platform/pull/152) merged at this SHA after exact documentation-head CI [#1276 / 43026958247](https://github.com/rajeevp727/turnova-platform/actions/runs/43026958247) passed 12/12 on `9e7df3eede1126062d302d1127a4817abb365f5f`: backend 430/430, shared mobile 176/176, Python 41/41, Rider/Driver validation and bundles, native API publish, four Android/iOS native builds and four simulator smoke jobs. CI #1269 source/test artifacts were inspected; iOS simulator builds remain unsigned with SecureStore unavailable/no Keychain access group. Production API/authentication was not exercised.
- #15 DRIVER-04 remains Blocked pending Product/Operations approval of issue categories, required intake fields, priority/severity, escalation owner/channel, support hours/response targets, attachment handling, consent/privacy/retention, audit and operational handoff. Do not invent support categories, SLA, contact details, attachment policy or API contract. Continue independent work at #16 RIDER-04.
- Sole active feature PR [#153](https://github.com/rajeevp727/turnova-platform/pull/153), branch `fix/rider-04-reconciliation-identity-session-guard`, based on main `09e8f90eb64cf0112999e4b874511eb201e4158b`. Source/test head: `61f5cf783bd1f8acad2b8951d017bdb418667297`; documentation head: `a7e58603ad0a342e20e44c385b96d32ebaccb450`. CI #1278 and final documentation-head CI #1279 both passed 12/12; see the exact-head evidence below.
- Backend correction: UnionManager reconciliation Cosmos SQL now requires `c.rideId = c.paymentReconciliation.rideId`; in-memory fallback applies the same ordinal exact-ID predicate. Existing signed Union, report-Union, Cab-only and allow-listed projection checks remain unchanged. Deterministic tests add `CAB-REC-016`, covering mismatched embedded report identity while preserving a valid matching report and no mutation.
- Driver mobile correction: `reportPaymentReceived` captures the authenticated session epoch, passes the same current-session predicate into `loadDriverRideHistory`, and suppresses stale history/input/notice/error/loading mutations. Existing payment idempotency key, direct-payment report semantics and unverified wording remain unchanged. Deterministic shared tests add `PAY-029` for delayed stale/current completion and App.tsx source wiring.
- Complete diff review found no critical/high-risk issue; latest COMMENT-only review is anchored to the documentation head, with zero unresolved review threads. CI #1279 passed all 12 jobs. Mark PR #153 ready, then merge only with the expected head SHA if GitHub's normal policy permits. After merge, re-read main; record #17 SUPPORT-01 as blocked on Product/Operations policy inputs, then continue #18 PLATFORM-01 and #19 RELEASE-01.
- External RIDER-04 gates remain live Cosmos SQL/index/partition/multi-instance/restart validation; Android/iOS reconciliation history/report-entry acceptance; customer dispute/confirmation, non-zero-delta next-ride and cross-Union visibility policies. No payment gateway, escrow, customer confirmation or delta-based eligibility rule is introduced.
- Strict acceptance remains **0/19** (15 In Progress, 4 Blocked); this is the complete-evidence count, not an implementation-completion score. Production readiness is not established.


## RIDER-04 exact-head verification — CI #1278 (2026-11-11)

- Active PR [#153](https://github.com/rajeevp727/turnova-platform/pull/153), branch `fix/rider-04-reconciliation-identity-session-guard`, base main `09e8f90eb64cf0112999e4b874511eb201e4158b`, source/test head `61f5cf783bd1f8acad2b8951d017bdb418667297`, documentation head tested by CI `877dffc559aba3e628445f824d0acabb159a5cf1`.
- Exact-head GitHub Actions [CI #1278 / 43034715352](https://github.com/rajeevp727/turnova-platform/actions/runs/43034715352) passed **12/12 jobs**: backend **431/431**, shared mobile **179/179**, Python harness **41/41**, Rider/Driver validation and production JS bundles, native non-container API publish, two Android native builds, two unsigned iOS Simulator builds, two Android emulator smoke jobs and two iOS Simulator smoke jobs.
- Same-run smoke artifacts downloaded and inspected: Rider Android `13863178192`, Driver Android `13863650955`, Rider iOS `13863523984`, Driver iOS `13864551988`. All four result JSONs report install/launch passed, stable process, role-specific login screen, no stale-session error and successful smoke completion. Both Android results report zero Pixel Launcher ANR recovery attempts. All four screenshots were visually inspected.
- Same-run native iOS app artifacts Rider `13864200268` and Driver `13863825959` were inspected: bundle IDs `com.turnova.rider` / `com.turnova.driver`, display names, version 0.1.0, build 1, minimum iOS 15.1. Both are unsigned simulator apps with no provisioning profile or app CodeSignature. Both iOS smoke results explicitly report SecureStore unavailable, expected secure-storage restore warning visible, no Keychain access group and production API/authentication not exercised.
- Release-readiness artifacts Rider `13862961522` and Driver `13862916835` report engineering checks passed but production readiness blocked: approved iOS bundle IDs, EAS project IDs, build-number policy, production HTTPS API URL/DNS/TLS/CORS/readiness, secure signing credentials, signed physical-device acceptance and store submission remain external. No signed AAB or IPA was produced.
- Same-run npm audit artifacts Rider `13862028118` and Driver `13862647074` each report 11 moderate, 1 low, 0 high and 0 critical advisories. The available Expo remediation resolves through Expo 57.0.32 and is a major SDK upgrade from the current app baseline; keep it as a separate compatibility/native-build hardening increment rather than mixing it into this PR.
- The backend build emitted an existing nullable warning at `Program.cs:1396` in the Operations selfie-rejection audit path; it is not introduced by PR #153 and is queued for the PLATFORM-01/Union privacy audit. No test/build failure resulted.
- PR #153's deterministic regression coverage includes `CAB-REC-016` (mismatched embedded report RideId excluded by Cosmos query contract and in-memory fallback without mutation) and `PAY-029` (stale/current Driver payment-report session completion plus App.tsx guard wiring). The existing idempotency key, Driver-reported/unverified label, signed delta, direct-payment semantics and privacy-minimized Union projection remain unchanged.
- Documentation-only synchronization was verified by exact-head CI #1279 (12/12) on `a7e58603ad0a342e20e44c385b96d32ebaccb450`; no source/test changes were introduced by that synchronization.
- External RIDER-04 gates remain live Cosmos SQL/index/partition/multi-instance/restart validation; Android/iOS reconciliation history/report-entry acceptance; customer dispute/confirmation, non-zero-delta next-ride and cross-Union visibility decisions. No gateway, escrow, customer confirmation or delta-based eligibility policy is introduced.
- Strict acceptance remains **0/19** (15 In Progress, 4 Blocked); this is a complete-evidence count, not an implementation-completion score. Production readiness is not established.


## RIDER-04 final documentation-head verification — CI #1279 (2026-11-11)

- Exact documentation-head CI [#1279 / 43039769965](https://github.com/rajeevp727/turnova-platform/actions/runs/43039769965) passed **12/12** on `a7e58603ad0a342e20e44c385b96d32ebaccb450`: backend **431/431**, shared mobile **179/179**, Python **41/41**, Rider/Driver validation and production bundles, native non-container API publish, two Android native builds, two unsigned iOS Simulator builds, two Android emulator smoke jobs and two iOS Simulator smoke jobs.
- Same-run smoke artifacts: Rider Android `13865940928`, Driver Android `13865142765`, Rider iOS `13865972836` and Driver iOS `13867025038`; all four result JSONs and screenshots were downloaded and inspected. Both Android apps installed/launched, displayed their correct role login screens, remained stable and recorded zero Pixel Launcher ANR recovery attempts. Both iOS simulator apps installed/launched and displayed the correct role login; SecureStore was unavailable as expected in unsigned simulator builds, so no signed-device Keychain or production-auth claim is made.
- Release-readiness artifacts continue to report production readiness blocked: approved app identifiers/EAS linkage/build-number policy, production HTTPS API/DNS/TLS/CORS/readiness, signing credentials, signed physical-device acceptance and store submission remain external. No signed AAB/IPA, production API/authentication, live Cosmos or physical-device validation is claimed.
- PR #153 remains the sole active PR until the expected-head merge succeeds. Strict acceptance remains **0/19** (15 In Progress, 4 Blocked); production readiness is not established.


## RIDER-04 final exact-head verification — CI #1280 (2026-11-11)

- Sole active PR [#153](https://github.com/rajeevp727/turnova-platform/pull/153), branch `fix/rider-04-reconciliation-identity-session-guard`, base main `09e8f90eb64cf0112999e4b874511eb201e4158b`, exact final documentation head `b7aae701904b6cc33b88d66a43c1aecae98180a7`.
- GitHub Actions [CI #1280 / 43054989137](https://github.com/rajeevp727/turnova-platform/actions/runs/43054989137) completed with **12/12 jobs successful** on that exact head: backend **431/431**, shared mobile **179/179**, Python **41/41**, Rider/Driver validation and production JS bundles, native non-container API publish, two Android native builds, two unsigned iOS Simulator builds, two Android emulator smoke jobs and two iOS Simulator smoke jobs.
- Same-run Android smoke artifacts Rider `13871921817` and Driver `13871363809`; both result JSONs and screenshots were inspected. Both installed/launched, remained stable, displayed the correct role-specific login, had no stale-session-expired error, and recorded zero Pixel Launcher ANR recovery attempts.
- Same-run iOS Simulator smoke artifacts Rider `13871979097` and Driver `13871899500`; both ZIPs, result JSONs, UI text and screenshots were inspected. Both report install/launch passed, stable process, simulator data erased, role-specific login verified and stale-session-expired error absent. Both explicitly report unsigned CI simulator artifacts, SecureStore unavailable and the expected recoverable secure-storage banner; production API/authentication was not exercised and no signed-device Keychain access group exists.
- Rider/Driver validation jobs also generated production Android/iOS JavaScript bundles; native iOS jobs compiled unsigned simulator apps. This does not establish signed Android AAB/iOS IPA or physical-device acceptance.
- Existing release gates remain: approved app identifiers/EAS linkage/build-number policy, production HTTPS API/DNS/TLS/CORS/readiness, signing credentials, signed physical-device testing, pilot acceptance and store submission. Live Cosmos SQL/index/partition/multi-instance/restart, reconciliation policy and legal gates remain external.
- No payment gateway, escrow, in-app collection, customer dispute/confirmation or delta-based next-ride policy is introduced. Strict acceptance remains **0/19** (15 In Progress, 4 Blocked); this is a complete-evidence count, not an implementation-completion score. Production readiness is not established.


## RIDER-04 final documentation-head verification — CI #1282 (2026-11-11)

- Sole active PR [#153](https://github.com/rajeevp727/turnova-platform/pull/153), branch `fix/rider-04-reconciliation-identity-session-guard`, base main `09e8f90eb64cf0112999e4b874511eb201e4158b`, final documentation head `6c3c529b04067046bf43533cff34f3c4f2419e89`.
- GitHub Actions [CI #1282 / 43063792555](https://github.com/rajeevp727/turnova-platform/actions/runs/43063792555) completed **12/12 jobs successfully on the exact final documentation head**: backend **431/431**, shared mobile **179/179**, Python **41/41**, Rider/Driver typecheck and production JS bundles, native non-container API publish, two Android native builds, two unsigned iOS Simulator builds, two Android emulator smoke jobs and two iOS Simulator smoke jobs. This is a documentation-only head after the fully verified source/test head; it introduces no runtime/test changes.
- Same-run smoke artifacts inspected: Rider Android `13875307910`, Driver Android `13876316269`, Rider iOS `13875853423`, Driver iOS `13876811434`. All four result JSONs and screenshots were downloaded and inspected. Both Android apps installed/launched, displayed their correct role-specific login, remained stable, had no stale-session-expired error and recorded zero Pixel Launcher ANR recovery attempts. Both unsigned iOS simulator apps installed/launched, displayed their correct role-specific login, remained stable, had simulator data erased, and showed the expected recoverable SecureStore-unavailable banner; `secureStoreAvailable=false`, no Keychain access group, and production API/authentication was not exercised.
- Release-readiness artifacts Rider `13875760194` and Driver `13874462726` report engineering checks passed but production readiness blocked. Neither has an approved iOS bundle identifier, EAS project ID, build-number policy or production API URL in CI-visible configuration; no signed Android AAB or iOS IPA was produced. Product/App Owner, Release Engineering, Platform/Infrastructure, QA and Release Owner/Legal own the outstanding inputs and acceptance.
- Same-run dependency audits Rider `13873704726` and Driver `13875265447` each report 11 moderate, 1 low, 0 high and 0 critical advisories. The automated full remediation points to Expo `58.0.24`, a major SDK upgrade; keep it as a separate compatibility/native-build hardening increment, not an unreviewed override.
- CI #1280 / 43054989137 remains the source/test verification on `b7aae701904b6cc33b88d66a43c1aecae98180a7`; CI #1282 verifies the final documentation head above. Live Cosmos SQL/index/partition/multi-instance/restart, signed-device Keychain, physical-device, production API/authentication and legal/product-policy gates remain external.
- No payment gateway, escrow, in-app collection, customer dispute/confirmation or delta-based next-ride policy is introduced. Strict acceptance remains **0/19** (15 In Progress, 4 Blocked); this is a complete-evidence count, not an implementation-completion score. Production readiness is not established.


## RIDER-04 exact documentation-head verification — CI #1285 (2026-11-12)

- Active PR [#153](https://github.com/rajeevp727/turnova-platform/pull/153), base main `09e8f90eb64cf0112999e4b874511eb201e4158b`, exact documentation head `ecb526ae524be443df4eace659d3ad64a694da4c`.
- GitHub Actions [CI #1285 / 43073753705](https://github.com/rajeevp727/turnova-platform/actions/runs/43073753705) passed **12/12 jobs on exact documentation head `ecb526ae524be443df4eace659d3ad64a694da4c`**: backend 431/431, shared mobile 179/179, Python 41/41, Rider/Driver typechecks and production JS bundles, native non-container API publish, two Android native builds, two unsigned iOS Simulator builds, two Android emulator smoke tests and two iOS Simulator smoke tests.
- Same-run smoke artifacts inspected: Rider Android `13882271982`, Driver Android `13879934066`, Rider iOS `13882698962`, Driver iOS `13883585816`. Both Android apps installed/launched, displayed their role-specific login screens, remained stable, had no stale-session error and recorded zero Pixel Launcher ANR recovery attempts. Both unsigned iOS simulator apps installed/launched and displayed the correct role-specific login; simulator data was erased, SecureStore was unavailable and the expected recoverable secure-storage banner appeared. Production API/authentication was not exercised; no signed-device Keychain claim is made.
- Release-readiness artifacts Rider `13878284537` and Driver `13877944043` remain production-blocked; unsigned simulator apps and standalone APKs do not constitute signed store deliverables. Dependency audit artifacts Rider `13878659001` and Driver `13879028586` are retained for the separate Expo compatibility hardening audit.
- This run's follow-up source audit found a DISPATCH-04 overlap-safety gap: `HasActiveDriverRideAsync` returns false immediately when the reservation store has no active lease, without querying historical active ride aggregates. The matrix now records this as a reopened engineering follow-up; planned cases are `DISPATCH-04-LEGACY-001` through `004`. Fix this before proceeding to PLATFORM-01.
- Strict acceptance remains **0/19** (15 In Progress, 4 Blocked); production readiness is not established. No payment gateway, escrow, customer confirmation/dispute or delta-based next-ride policy is introduced.


## RIDER-04 exact-head verification — CI #1286 (2026-11-12)

- PR [#153](https://github.com/rajeevp727/turnova-platform/pull/153), branch `fix/rider-04-reconciliation-identity-session-guard`, exact tested head `ba33caadfd33a87aed809c0ba4e148d9f213ba2d`, base main `09e8f90eb64cf0112999e4b874511eb201e4158b`.
- GitHub Actions [CI #1286 / 43084057288](https://github.com/rajeevp727/turnova-platform/actions/runs/43084057288) passed **12/12 jobs on exact head `ba33caadfd33a87aed809c0ba4e148d9f213ba2d`**. Backend **431/431**, shared mobile **179/179**, Python **41/41**, Rider/Driver typechecks and production JS bundles, native non-container API publish, two Android standalone release APK builds, two unsigned iOS Simulator builds, two Android emulator smoke tests and two iOS Simulator smoke tests all passed.
- Same-run smoke artifacts downloaded and inspected: Rider Android `13885114690`, Driver Android `13885967692`, Rider iOS `13886558695`, Driver iOS `13886339344`. Both Android apps installed/launched, displayed the correct role-specific login screen, remained stable, had no stale-session error and recorded zero Pixel Launcher ANR recovery attempts. Both unsigned iOS simulator apps installed/launched, displayed the correct role-specific login screen and remained stable; simulator data was erased, SecureStore was unavailable and the expected recoverable secure-storage warning was visible. Production API/authentication was not exercised; signed-device Keychain success is not claimed.
- Release-readiness artifacts Rider `13884494776` and Driver `13885995696` report engineering checks passed but production readiness blocked. Dependency audit artifacts Rider `13884244938` and Driver `13885747350` remain a separate Expo compatibility hardening follow-up.
- The separate DISPATCH-04 historical active-ride fallback finding remains open and is not implemented by PR #153. Planned cases `DISPATCH-04-LEGACY-001` through `004` remain the next safety follow-up after this PR merges.
- Strict acceptance remains **0/19** (15 In Progress, 4 Blocked); this is a complete-evidence count, not an implementation-completion score. Production readiness is not established.

## RIDER-04 exact documentation-head verification — CI #1288 (2026-11-12)

- PR [#153](https://github.com/rajeevp727/turnova-platform/pull/153), branch `fix/rider-04-reconciliation-identity-session-guard`, exact tested head `0b7576b2c6da8a13eeac889c314f1e4a31e3d453`, base main `09e8f90eb64cf0112999e4b874511eb201e4158b`.
- GitHub Actions [CI #1288 / 43093306250](https://github.com/rajeevp727/turnova-platform/actions/runs/43093306250) completed **12/12 jobs successfully** on the exact head: backend **431/431**, shared mobile **179/179**, Python **41/41**, Rider/Driver validation and production JS bundles, native non-container API publish, two Android native builds, two unsigned iOS Simulator builds, two Android emulator smoke tests and two iOS Simulator smoke tests. All four native jobs succeeded after the failed-job retry; both Android release APKs and both unsigned iOS simulator apps were uploaded successfully.
- Same-run smoke artifacts downloaded, extracted and inspected: Rider Android `13892642047`, Driver Android `13893215862`, Rider iOS `13892604439`, Driver iOS `13893277507`. Android result JSONs show correct package IDs, install/launch passed, stable processes, role-specific login, stale-session error absent and zero Pixel Launcher ANR recovery attempts. Unsigned iOS result JSONs show correct bundle IDs, install/launch passed, stable processes, simulator data erased and role-specific login; SecureStore is unavailable, the expected recoverable warning is visible, no Keychain access group exists, and production API/authentication was not exercised. All four screenshots were visually inspected.
- Same-run release-readiness artifacts Rider `13890031208` and Driver `13888833914` report engineering checks passed but production readiness blocked. Required owners/inputs: Product/App Owner must approve/register the iOS bundle IDs; Release Engineering must configure EAS project IDs and build-number ownership/auto-increment; Platform/Infrastructure must configure the production HTTPS API URL and verify DNS/TLS/CORS/readiness; Release Owner must configure secure signing credentials; QA/Product must complete signed physical-device acceptance; Release Owner/Legal must complete store metadata, privacy disclosures and submission/approval. No signed Android AAB or iOS IPA was produced.
- Same-run npm audit artifacts Rider `13889986207` and Driver `13888604434` each report **12 advisories: 11 moderate, 1 low, 0 high, 0 critical**. The complete automated remediation points to Expo `58.0.24` and is a major SDK upgrade; keep it as a separately reviewed compatibility/native-build hardening increment, not an unreviewed override.
- The earlier CI #1288 artifact-upload transient is superseded by the successful final attempt: all 12 jobs and all four smoke artifacts are now complete. No production API/authentication, live Cosmos, signed-device Keychain or physical-device acceptance is claimed.
- Strict acceptance remains **0/19** (15 In Progress, 4 Blocked); this is a complete-evidence count, not an implementation-completion score. Production readiness is not established.



## RIDER-04 final documentation-head verification — CI #1290 (2026-11-12)

- GitHub Actions [CI #1290 / 43108530927](https://github.com/rajeevp727/turnova-platform/actions/runs/43108530927) completed **12/12 jobs successfully** on exact documentation head `40421acf92f030c9737b83d987eb30d2b7e3eabe`: backend **431/431**, shared mobile **179/179**, Python **41/41**, Rider/Driver validation and production bundles, native non-container API publish, two Android standalone release APK builds, two unsigned iOS Simulator builds, two Android emulator smoke tests and two iOS Simulator smoke tests.
- Same-run smoke artifacts downloaded, extracted and inspected: Rider Android `13895439225`, Driver Android `13895109413`, Rider iOS `13897805633`, Driver iOS `13896847179`. All four result JSONs and screenshots were inspected. Android install/launch passed, package identity and role-specific login were verified, processes remained stable, stale-session errors were absent, and Pixel Launcher ANR recovery attempts were zero. Unsigned iOS simulator install/launch passed, bundle identity and role-specific login were verified, processes remained stable, simulator data was erased, SecureStore was unavailable, the expected recoverable secure-storage warning was visible, and stale-session-expiry errors were absent. Production API/authentication was not exercised; no signed-device Keychain access group is available.
- Same-run native iOS app artifacts Rider `13896092051` and Driver `13897190127` were inspected. Their Info.plists identify `com.turnova.rider` / `com.turnova.driver`, display names TurnOva Rider / TurnOva Driver, version `0.1.0`, build `1`, minimum iOS `15.1`, and `iPhoneSimulator` platform. These are unsigned simulator apps, not signed distribution IPAs.
- Release-readiness artifacts Rider `13895037964` and Driver `13895092535` report engineering checks passed but production readiness blocked. Both have no approved iOS bundle identifier, EAS project ID, build-number policy or production API URL in CI-visible configuration; neither signed Android AAB nor signed iOS IPA was produced. Owners/inputs: Product/App Owner (approve/register iOS bundle IDs); Release Engineering (EAS project IDs and build-number ownership/auto-increment); Platform/Infrastructure (production HTTPS API URL and DNS/TLS/CORS/readiness); Release Owner (secure signing credentials); QA/Product (signed physical-device functional/security/pilot acceptance); Release Owner/Legal (store metadata, privacy disclosures and submission).
- Same-run npm audit artifacts Rider `13894024579` and Driver `13893769604` each report **12 advisories: 11 moderate, 1 low, 0 high, 0 critical**. Full automated remediation requires the major Expo upgrade to `58.0.24` (with related Expo package alignment); keep this as a separate compatibility/native-build hardening increment, not an unreviewed override.
- CI #1290 verifies the documentation head that records CI #1288. This section itself is a subsequent documentation-only update and therefore requires a fresh exact-head CI and final COMMENT-only review before PR #153 can merge. RIDER-04 source/test code is unchanged from the previously verified head. The DISPATCH-04 historical active-ride fallback remains a separate P0 follow-up and is not implemented by PR #153.
- Strict acceptance remains **0/19** (15 In Progress, 4 Blocked); this is a complete-evidence count, not an implementation-completion score. Production readiness is not established. No payment gateway, escrow, in-app collection, customer dispute/confirmation or delta-based next-ride policy is introduced.


## RIDER-04 exact documentation-head verification — CI #1296 (2026-11-12)

- Active PR [#153](https://github.com/rajeevp727/turnova-platform/pull/153), branch `fix/rider-04-reconciliation-identity-session-guard`, base main `09e8f90eb64cf0112999e4b874511eb201e4158b`, exact tested head `885962b4a91d4e0f29fa2cb8734a1eafe80101b5`.
- GitHub Actions [CI #1296 / 43125266797](https://github.com/rajeevp727/turnova-platform/actions/runs/43125266797) completed **12/12 jobs successfully** on that exact head. Backend **431/431** (0 failed, 0 skipped); shared mobile-auth **179/179** (0 failed); Python harness **41/41**; Rider/Driver validation and production JS bundles; Android native builds 2/2; unsigned iOS Simulator native builds 2/2; Android emulator smoke 2/2; iOS Simulator smoke 2/2. All 12 jobs have conclusion `success`.
- Same-run smoke artifacts downloaded, extracted and inspected: Rider Android `13903496824`, Driver Android `13903208782`, Rider iOS `13904072953`, Driver iOS `13904651022`. All four result JSONs and screenshots were inspected. Android install/launch, package identity, role-specific login and process stability passed; stale-session error absent and Pixel Launcher ANR recovery attempts zero. Unsigned iOS install/launch, bundle identity, role-specific login and process stability passed; simulator data erased, SecureStore unavailable and expected recoverable secure-storage warning visible; production API/authentication was not exercised and signed-device Keychain is not established.
- Driver iOS native app artifact `13904265254` was extracted and inspected. It contains both `Frameworks/React.framework/React` and `Frameworks/ReactNativeDependencies.framework/ReactNativeDependencies`; the Driver iOS smoke launched PID 5857 and verified the Driver Partner login screen. The earlier CI #1295 missing-framework/dyld failure is superseded by this successful exact-head rebuild; no source change was made to produce the passing build. Treat the earlier incident as an intermittent native packaging/cache observation and add a separate fail-closed framework-presence assertion to the iOS build workflow when convenient; do not claim a root-cause fix from the successful retry alone.
- Same-run release-readiness artifacts Rider `13902422810` and Driver `13902703051` report engineering checks passed but production readiness blocked. No approved iOS bundle IDs, EAS project IDs, build-number policy or production API URL are configured in CI-visible settings; no signed Android AAB or iOS IPA was produced. Owners: Product/App Owner (approve/register bundle IDs); Release Engineering (EAS project IDs and build-number policy); Platform/Infrastructure (production HTTPS API URL, DNS/TLS/CORS/readiness); Release Owner (secure signing credentials); QA/Product (signed-device acceptance); Release Owner/Legal (store metadata, disclosures and submission).
- Same-run npm audit artifacts Rider `13901719495` and Driver `13902653265` each report **12 advisories: 11 moderate, 1 low, 0 high, 0 critical**. Complete automated remediation requires major Expo upgrade `58.0.24`; keep it as a separate compatibility/native-build hardening increment rather than applying dependency overrides.
- RIDER-04 source/test changes remain CAB-REC-016 and PAY-029; no payment gateway, escrow, in-app collection, customer dispute/confirmation or delta-based next-ride policy is introduced. No production API/authentication, live Cosmos, signed-device Keychain or physical-device acceptance is claimed.
- DISPATCH-04 historical active-ride fallback remains a separate P0 follow-up and is not implemented by PR #153. Eight planned cases DISPATCH-04-LEGACY-001–008 are documented but not yet implemented/passed.
- Strict acceptance remains **0/19** (15 In Progress, 4 Blocked); this is a complete-evidence count, not an implementation-completion score. Production readiness is not established.


## RIDER-04 exact-head verification — CI #1306 (2026-11-12)

- PR [#153](https://github.com/rajeevp727/turnova-platform/pull/153), exact head `4d1d2804f171a8b070458a0e9bfb9f5ef4dc3e0e`, base main `09e8f90eb64cf0112999e4b874511eb201e4158b`; [CI #1306 / 43152744526](https://github.com/rajeevp727/turnova-platform/actions/runs/43152744526) passed **12/12**: backend 431/431, shared mobile 179/179, Python 46/46, both app typechecks/bundles, native API publish, two Android standalone release APKs, two unsigned iOS simulator apps and four emulator/simulator smoke jobs.
- APK artifacts: Rider `13915310843`, Driver `13915140697`; Android package IDs verified as `com.turnova.rider` and `com.turnova.driver`. Smoke artifacts Rider Android `13915031736`, Driver Android `13914589438`, Rider iOS `13915593606`, Driver iOS `13915623097`; all four result JSONs/screenshots inspected. Android login/install/launch and process stability passed; unsigned iOS login/install/launch passed with the expected SecureStore-unavailable warning. Production API/authentication and signed-device Keychain were not exercised.
- Release readiness remains blocked: no production API base URL, approved iOS bundle/EAS project/build-number configuration, signed AAB/IPA, signing credentials, signed-device QA or store approval. Owners remain Product/App Owner, Release Engineering, Platform/Infrastructure, Release Owner, QA/Product and Legal as detailed in `docs/MVP_PROGRESS.md`.
- Next engineering task: P0 DISPATCH-04 historical active-ride fallback, including eight specified deterministic cases and bounded batched Rider discovery lookup. Strict acceptance remains 0/19 (15 In Progress, 4 Blocked); production readiness is not established.


## Latest sequential handoff — DISPATCH-04 historical active-ride fallback (2026-11-12)

- Base main before PR #154: 3b267fc1d1f7b1eb5e200fb73d9bb5e6c6b643fa. PR #154 code-bearing head 02fbfa9121b62acf5ef8ec20287cc9dc0ea94db5; exact-head CI #1330 / 43178946882 passed 12/12 (backend 477/477, shared mobile 179/179, Python 46/46, both app validations/bundles, native API publish, two Android release APKs, two unsigned iOS Simulator builds and four emulator/simulator smokes).
- Implementation covers historical active ride aggregates even when reservations are absent; all known active states including legacy Matching/Offered; terminal/orphan reservation cleanup followed by a second active-ride check; same-ride idempotency with a separate active ride; Driver acceptance; batched discovery and reservation reads (100 IDs per query); exact Cosmos item ID/embedded RideId checks; owner-bound Rider/Driver history; and authoritative expiry reconciliation that re-reads after ETag/CAS conflict and fails closed on missing/mismatched/unknown latest state.
- Test-plan cases DISPATCH-04-LEGACY-001–008 are implemented. Query contracts verify the minimal identity/status/expiry projection and the reservation-document ID predicate that excludes append-only audit records. No new customer-facing FIFO, auto-assignment, non-Cab workflow, fare/payment, schema migration, secret or container change.
- Final documentation-only sync must pass exact-head CI before merge. The source/test CI result is recorded separately so a docs-only head is not misrepresented as a new source test run.
- External gates: live Cosmos cross-partition query/index/RU/partition behaviour, multi-instance ETag/CAS contention and restart/crash recovery; Product-approved negotiation lifetime/radius/zones/location freshness; Android/iOS concurrent booking/availability/transfer acceptance. After PR #154 integration, continue to #18 PLATFORM-01; do not stop because #15 DRIVER-04 or #17 SUPPORT-01 require Product/Operations decisions.


## Latest sequential handoff — PLATFORM-01 reservation audit privacy contract (2026-11-12)

- DISPATCH-04 PR #154 merged at `09a807e1024bf386450713c0645221ff9002f040` after exact documentation-head CI #1339 / 43188534570 passed 12/12 on `209ccc219a3ac63a74ff6e37fdbb0b159e9c8399`. Code-bearing CI #1330 passed backend 477/477, shared mobile 179/179 and Python 46/46 plus native/bundle/simulator checks.
- Current focused branch: `fix/platform-01-reservation-audit-privacy-contract`. Enforce exactly four Driver reservation audit event types; reject unknown/malformed values before event creation. Redact numeric-only phone-like actor references with allowed separators (-, ., :, _) when at least eight digits are present; preserve opaque IDs containing letters.
- Add deterministic tests for formatted phone-like IDs, opaque IDs, all allowed event types and invalid event types. Bound Operations selfie-rejection reasons to one normalized 1–300-character value. Extend the existing API integration test to prove null/blank/301-character rejection leaves profile/audit unchanged and a padded valid reason is identical in response, profile and append-only Union audit. Test-plan cases PLATFORM-AUDIT-015–020 are recorded; the exact code/test head is `3beb13e042820e88cb83e18a0d1a83f2d45424f8`. Intermediate CI #1345 was cancelled after supersession. Exact final-head CI [#1349 / 43209115012](https://github.com/rajeevp727/turnova-platform/actions/runs/43209115012) passed **12/12 jobs on `1636e2731ca44dccf769a2f80aae8a7eb0279d69`**: backend **500/500**, shared mobile **179/179**, Python harness **46/46**, Rider/Driver validation and production JavaScript bundles, native non-container .NET API publish, two Android debug APKs and two standalone release APK builds, two unsigned iOS Simulator builds, and four Android emulator/iOS Simulator smoke jobs. Inspected smoke artifacts: Rider Android `13941620437`, Driver Android `13941890263`, Rider iOS `13942945816`, Driver iOS `13943110047`. Android package identity/login/install/launch/process stability passed with no stale-session error and zero launcher ANR recovery attempts; unsigned iOS bundle identity/login/install/launch/process stability passed, simulator data was erased, SecureStore was unavailable and the expected recoverable banner was visible. Production API/authentication was not exercised; signed-device Keychain is not established. Matrix/progress/test-plan/architecture and this project brain record the implementation and external gates.
- External gates remain live Cosmos transactional-batch/ETag/partition/409-readback/multi-instance/restart/retention, telemetry export/alerts, production CORS origins, load testing and physical-device acceptance. Strict acceptance remains 0/19 (15 In Progress, 4 Blocked); production readiness is not established.


### PLATFORM-01 final documentation-head CI — 2026-11-12

- The latest documentation head `98f7b81a29a74b8bc5e8493da608266593906260` was verified by GitHub Actions [CI #1354 / 43215907622](https://github.com/rajeevp727/turnova-platform/actions/runs/43215907622), which completed **12/12 jobs successfully on that exact commit**: backend, mobile-auth, Rider/Driver validation and bundles, four native Android/iOS builds, and four Android emulator/iOS Simulator smoke jobs. No job remained pending or failed.
- The source/test head remains `1636e2731ca44dccf769a2f80aae8a7eb0279d69`; its code-bearing CI [#1349 / 43209115012](https://github.com/rajeevp727/turnova-platform/actions/runs/43209115012) passed **12/12**, including backend **500/500**, shared mobile **179/179**, Python harness **46/46**, native non-container API publish, Android release APK builds, unsigned iOS Simulator builds and four platform smoke jobs. CI #1354 is a documentation-head rerun, not a new source change.
- The inspected smoke evidence remains limited to unsigned simulator/emulator workflows. Production API/authentication, live Cosmos transactional-batch/ETag/retention/multi-instance/restart, signed-device Keychain, physical-device acceptance, telemetry export/alerts, production CORS and load testing are not claimed and remain external gates. Strict acceptance remains **0/19** (15 In Progress, 4 Blocked); production readiness is not established.

## PLATFORM-01 PR #155 final documentation-head verification — 2026-11-12

- Latest documentation head `ecbf08f3e9217ee08ac0abb633a23eb174ec6cdd` passed GitHub Actions [CI #1356 / 43222822397](https://github.com/rajeevp727/turnova-platform/actions/runs/43222822397) **12/12 jobs**: backend, Rider/Driver mobile validation and bundles, mobile-auth tests, four Android/iOS native builds and four Android emulator/iOS Simulator smoke jobs. All jobs completed successfully; no pending or failed job remains.
- CI #1356 is documentation-head verification. The code-bearing head remains `3beb13e042820e88cb83e18a0d1a83f2d45424f8`, with exact source/test evidence from CI #1349: backend 500/500, shared mobile 179/179 and Python harness 46/46, plus native publish/build and four platform smoke jobs. No runtime or test source changed after that code-bearing head.
- A fresh COMMENT-only final-head review was recorded against `ecbf08f3e9217ee08ac0abb633a23eb174ec6cdd`; no critical/high-risk finding was identified. PR #155 was marked ready for review. Main remains `09a807e1024bf386450713c0645221ff9002f040`; branch protection is disabled and the repository rulesets collection is empty, so there are no configured required status checks or review approvals to bypass. Merge must still use the expected head SHA.
- External PLATFORM-01 gates remain live Cosmos transactional-batch/ETag/409 read-after-write/partition/multi-instance/restart/retention validation, telemetry export/alerts, production CORS origins, load testing, signed-device Keychain and physical-device acceptance. Strict acceptance remains 0/19 (15 In Progress, 4 Blocked); production readiness is not established.
- Next independent PLATFORM-01 work after PR #155 integrates: deterministic Operations selfie-rejection audit identity, retry-stable review timestamp, same-partition point-read verification after duplicate/409 writes, fail-closed mismatch handling and opaque JWT subject actor identity. Preserve the separate profile/audit persistence limitation; do not claim cross-container atomicity.


## Latest sequential handoff — PLATFORM-01 Union audit recovery (2026-11-12)

- Main HEAD: `4817351616d6f0d7f0c9a51a0863cb92ce4caea4`. PR [#155](https://github.com/rajeevp727/turnova-platform/pull/155) merged after exact documentation-head CI [#1358 / 43233749785](https://github.com/rajeevp727/turnova-platform/actions/runs/43233749785) passed 12/12 on `72836c5ec4497663f5c700af7cd8d211259e1212`: backend 500/500, shared mobile 179/179, Python 46/46, Rider/Driver bundle checks, native non-container API publish, Android debug/release APKs, unsigned iOS Simulator apps and four simulator/emulator smoke jobs.
- Active branch: `fix/platform-01-union-audit-idempotent-recovery`. It introduces deterministic selfie-rejection event IDs, same-review timestamp preservation, recovery after profile success/audit failure, exact same-partition canonical read-back after duplicate/409, mismatch/missing fail-closed behaviour and opaque JWT subject actor resolution.
- Added tests: `DriverUnionAuditSnapshotRecoveryTests` (identity stability/privacy, subject actor selection, matching/missing/mismatched canonical reads and cancellation) plus the Operations API integration retry scenario. Test-plan cases PLATFORM-AUDIT-021–027 are recorded. This branch has not yet run CI; do not claim its tests passed until exact-head CI succeeds.
- Profile and Union audit remain separate Cosmos containers; the implementation does not claim cross-container atomicity. Live Cosmos 409 visibility, ETag/partition, multi-instance/restart/retention, telemetry export/alerts, production CORS, load and physical-device acceptance remain external gates.
- Strict acceptance remains 0/19 (15 In Progress, 4 Blocked); production readiness is not established. After this increment, continue remaining PLATFORM-01 engineering and then RELEASE-01; do not pause for external gates.


## PLATFORM-01 PR #156 exact-head handoff — 2026-11-12

- Main HEAD: `4817351616d6f0d7f0c9a51a0863cb92ce4caea4`; PR #155 merged after exact-head CI #1358 passed 12/12 on `72836c5ec4497663f5c700af7cd8d211259e1212`.
- Active PR [#156](https://github.com/rajeevp727/turnova-platform/pull/156), branch `fix/platform-01-union-audit-idempotent-recovery`, latest head `1404306aa5a05de97ffd8a7b88baaec572312241`. It adds deterministic Operations selfie-rejection event identity, stable review timestamp on identical retries, profile-success/audit-failure recovery, exact same-partition canonical point-read verification after duplicate/409, fail-closed mismatch/missing handling and opaque JWT subject actor attribution.
- Follow-up guard treats any non-null Operations review marker as present, including empty/whitespace values; incomplete snapshots fail closed instead of being cleared. Unit and API regression tests cover blank markers (PLATFORM-AUDIT-035).
- UnionManager individual-driver detail now omits driver phone/email, exact GPS, private document storage references, Rider identity and chat data; the existing integration test covers both member-list and detail projections (PLATFORM-AUDIT-036).
- Earlier exact-head CI #1388 on `0456a9c8a0307e6620eb57da93a7a562c5b362ee` passed backend **523/523**, shared mobile **179/179**, Python harness **46/46**, Rider/Driver validation and JS bundles, and native API publish. Native Android/iOS builds were still running when the additional UnionManager privacy changes superseded that head. This is not final-head 12/12 evidence.
- The exact latest head `1404306aa5a05de97ffd8a7b88baaec572312241` requires fresh CI; do not merge until every job and downstream smoke succeeds on that SHA, the full diff is reviewed, and GitHub accepts an expected-head merge. PR #156 remains Draft/unmerged; no self-approval or bypass.
- Driver profile and Union audit remain separate Cosmos containers; no cross-container atomicity is claimed. Live Cosmos 409/ETag/partition/multi-instance/restart/retention, telemetry export/alerts, production CORS/load and physical-device validation remain external.
- Next sequential work after #156 merges: re-read main, finish remaining safe PLATFORM-01 health/readiness/diagnostics/CORS/native-publish/audit-path work, then continue #19 RELEASE-01 engineering.
- Strict acceptance remains **0/19** (15 In Progress, 4 Blocked); production readiness is not established.


## PLATFORM-01 PR #156 exact-head handoff — 2026-11-12

- Main HEAD: `4817351616d6f0d7f0c9a51a0863cb92ce4caea4`; PR #155 merged after exact-head CI #1358 passed 12/12 on `72836c5ec4497663f5c700af7cd8d211259e1212`.
- Active PR [#156](https://github.com/rajeevp727/turnova-platform/pull/156), branch `fix/platform-01-union-audit-idempotent-recovery`, latest head `0c8e49a2f5828f118cd30f41cdddf788e2e97090`. Operations selfie-rejection audit identity is deterministic; identical retries preserve review timestamp; profile-success/audit-failure recovery uses bounded exact-ID/Driver-partition canonical point reads after duplicate/409 and fails closed on mismatched/missing content. Ops actor uses a safe opaque JWT `sub`.
- Blank-but-present Operations review markers now count as metadata and fail closed rather than being cleared (PLATFORM-AUDIT-035). UnionManager individual-driver detail omits phone/email, exact GPS, private document storage references, Rider identity and chat data (PLATFORM-AUDIT-036).
- UnionManager audit actors now use a role-bound opaque JWT `sub` or fixed `UnionManager`. For an identical UnionManager selfie-review event, the first persisted actor remains canonical on duplicate retry; all other canonical event fields must still match (PLATFORM-AUDIT-037). A profile-success/audit-failure retry where the audit is absent still needs the original UnionManager actor snapshot persisted in the profile; that is the next follow-up, not claimed complete here.
- CI #1388 on prior head `0456a9c8a0307e6620eb57da93a7a562c5b362ee` passed backend 523/523, shared mobile 179/179, Python 46/46, both mobile validation/bundles and native API publish; native jobs were superseded by later changes. CI #1394 on `1c39da93cf64f2008503acba4d2f311ea6b29f9f` was cancelled after the UnionManager actor/test changes. Fresh exact-head CI is required for `0c8e49a2f5828f118cd30f41cdddf788e2e97090`.
- PR #156 remains Draft/unmerged. Merge only after all final-head jobs and downstream smokes pass, full diff review is complete, and GitHub accepts an expected-head merge. No self-approval or bypass.
- Driver profile and Union audit remain separate Cosmos containers; no cross-container atomicity is claimed. Live Cosmos 409/ETag/partition/multi-instance/restart/retention, telemetry export/alerts, production CORS/load and physical-device validation remain external.
- Next sequential work after #156 merges: re-read main, finish remaining safe PLATFORM-01 health/readiness/diagnostics/CORS/native-publish/audit-path work, then continue #19 RELEASE-01 engineering.
- Strict acceptance remains **0/19** (15 In Progress, 4 Blocked); production readiness is not established.


## PLATFORM-01 PR #156 exact-head handoff — 2026-11-12

- Main HEAD: `4817351616d6f0d7f0c9a51a0863cb92ce4caea4`; PR #155 merged after exact-head CI #1358 passed 12/12.
- Active PR [#156](https://github.com/rajeevp727/turnova-platform/pull/156), branch `fix/platform-01-union-audit-idempotent-recovery`; latest code/test head `7828b5bf7d09e9a143d2b1dbb88f8e50926a341b`. Operations selfie-rejection audit identity is deterministic; identical retries preserve review timestamp; profile-success/audit-failure recovery uses bounded exact-ID/Driver-partition canonical point reads after duplicate/409 and fails closed on mismatched/missing content. Ops actor uses a safe opaque JWT `sub`.
- Blank-but-present Operations review markers fail closed (PLATFORM-AUDIT-035). UnionManager individual-driver detail omits phone/email, exact GPS, private document storage references, Rider identity and chat data (PLATFORM-AUDIT-036).
- UnionManager selfie-review actors use a role-bound opaque JWT `sub`; a duplicate same-review event preserves the first persisted actor and compares all other canonical fields (PLATFORM-AUDIT-037). Operations assignment/transfer audit also uses the safe Ops subject resolver and never persists email-like `Identity.Name` (PLATFORM-AUDIT-038).
- CI #1388 on prior head `0456a9c8a0307e6620eb57da93a7a562c5b362ee` passed backend 523/523, shared mobile 179/179, Python 46/46, both mobile validation/bundles and native API publish; native jobs were superseded by later changes. CI #1394 and #1402 were superseded by additional audit-actor changes before final completion. Fresh exact-head CI is required.
- PR #156 remains Draft/unmerged. Merge only after all final-head jobs and downstream smokes pass, full diff review is complete, and GitHub accepts an expected-head merge. No self-approval or bypass.
- Driver profile and Union audit remain separate Cosmos containers; no cross-container atomicity is claimed. Live Cosmos 409/ETag/partition/multi-instance/restart/retention, telemetry export/alerts, production CORS/load and physical-device validation remain external.
- Next sequential work after #156 merges: re-read main, finish remaining safe PLATFORM-01 health/readiness/diagnostics/CORS/native-publish/audit-path work, then continue #19 RELEASE-01 engineering.
- Strict acceptance remains **0/19** (15 In Progress, 4 Blocked); production readiness is not established.


## PLATFORM-01 PR #156 exact-head handoff — 2026-11-12

- Main HEAD: `4817351616d6f0d7f0c9a51a0863cb92ce4caea4`; PR #155 merged after exact-head CI #1358 passed 12/12.
- Active PR [#156](https://github.com/rajeevp727/turnova-platform/pull/156), branch `fix/platform-01-union-audit-idempotent-recovery`; latest code/test head `198c472383eba9c6aa11e13cb165c307043f67ad`. Operations selfie-rejection audit identity is deterministic; identical retries preserve review timestamp; profile-success/audit-failure recovery uses bounded exact-ID/Driver-partition canonical point reads after duplicate/409 and fails closed on mismatched/missing content. Ops actor uses a safe opaque JWT `sub`.
- Blank-but-present Operations review markers fail closed (PLATFORM-AUDIT-035). The existing UnionManager member-list projection is allow-listed and omits phone/email, exact GPS, private document storage references, Rider identity and chat data (PLATFORM-AUDIT-036); no new UnionManager detail endpoint was added. Privileged Operations profile/contact views remain unchanged.
- UnionManager selfie-review actors use a role-bound opaque JWT `sub`; a duplicate same-review event preserves the first persisted actor and compares all other canonical fields (PLATFORM-AUDIT-037). Operations assignment/transfer audit also uses the safe Ops subject resolver and never persists email-like `Identity.Name` (PLATFORM-AUDIT-038).
- CI #1388 on prior head `0456a9c8a0307e6620eb57da93a7a562c5b362ee` passed backend 523/523, shared mobile 179/179, Python 46/46, both mobile validation/bundles and native API publish; native jobs were superseded by later changes. CI #1394, #1402 and #1405 were superseded before final completion. Fresh exact-head CI is required.
- PR #156 remains Draft/unmerged. Merge only after all final-head jobs and downstream smokes pass, full diff review is complete, and GitHub accepts an expected-head merge. No self-approval or bypass.
- Driver profile and Union audit remain separate Cosmos containers; no cross-container atomicity is claimed. Live Cosmos 409/ETag/partition/multi-instance/restart/retention, telemetry export/alerts, production CORS/load and physical-device validation remain external.
- Next sequential work after #156 merges: re-read main, finish remaining safe PLATFORM-01 health/readiness/diagnostics/CORS/native-publish/audit-path work, then continue #19 RELEASE-01 engineering.
- Strict acceptance remains **0/19** (15 In Progress, 4 Blocked); production readiness is not established.


## Latest PLATFORM-01 handoff — 2026-11-13

- PR #156 is the only active feature PR, base main `4817351616d6f0d7f0c9a51a0863cb92ce4caea4`, current head `c806f0e5c420c2b77db9fb6f6410251ada24cbde`. It adds deterministic selfie/assignment audit recovery, original actor equality, bounded canonical read-back, same-target/reason idempotency, history repair, Driver response redaction and contact-safe reason validation/redaction.
- CI #1423 on intermediate head `98da5d580ae859e68a36f1bc86206ca4e2e727ad` passed backend 544/544, shared mobile 179/179, Python 46/46 and both app validation/bundles; Android debug builds passed. Native release/iOS/smokes were cancelled after the branch advanced. Exact-head CI #1436 / 43296647856 is pending on current head.
- The previous compile failure was a duplicate `AuditSnapshotPending` initializer and was fixed. Completed assignment records retain `LastAssignmentOperationKey` for idempotent replay; the Driver projection redacts that hash and `AuditSnapshotPending`.
- New `DriverUnionAuditReason` rejects obvious email/phone-like reasons before Operations/UnionManager selfie review or assignment mutation/audit creation, and hides legacy contact-like reasons in UnionManager member projections. This is a conservative check, not a general PII scanner. PLATFORM-AUDIT-043–044 are in the test plan.
- PR #156 remains Draft/unmerged. Review final docs head, wait for all exact-head CI/smokes, then merge only with expected-head SHA and repository policy. No self-approval/bypass. No cross-container atomicity or live Cosmos validation.
- Strict acceptance remains 0/19 (15 In Progress, 4 Blocked); production readiness is not established. External gates include live Cosmos 409/ETag/multi-instance/restart/retention, telemetry export/alerts, production CORS/load, auth-provider migration, signed-device Keychain and physical-device acceptance.
- After merge, complete remaining #18 diagnostics/CORS/native-publish/audit-path engineering, then continue #19 RELEASE-01. DRIVER-04 and SUPPORT-01 remain blocked on Product/Operations support-policy approval.


## Latest PLATFORM-01 handoff — 2026-11-13

- PR #156 is the only active feature PR, base main \`4817351616d6f0d7f0c9a51a0863cb92ce4caea4\`, source/test head \`e8337b4d682809a4c9bc17811ae4754410e070e1\`. It adds deterministic selfie/assignment audit recovery, original actor equality, bounded canonical read-back, same-target/reason idempotency, history repair, Driver response redaction and contact-safe reason validation/redaction.
- CI #1423 on intermediate head \`98da5d580ae859e68a36f1bc86206ca4e2e727ad\` passed backend 544/544, shared mobile 179/179, Python 46/46 and Rider/Driver validation/bundles; Android debug builds passed. Native release/iOS/smokes were cancelled after the branch advanced. CI #1436/#1444 were superseded by later source/docs commits; the final exact-head CI is pending.
- The previous compile failure was a duplicate \`AuditSnapshotPending\` initializer and was fixed. Completed assignment records retain \`LastAssignmentOperationKey\` for idempotent replay; the Driver projection redacts that hash and \`AuditSnapshotPending\`.
- \`DriverUnionAuditReason\` rejects obvious email/phone-like reasons before Operations/UnionManager selfie review or assignment mutation/audit creation, preserves ordinary ISO date/time text, and hides legacy contact-like reasons in UnionManager member projections. This is a conservative check, not a general PII scanner. PLATFORM-AUDIT-043–044 are in the test plan.
- PR #156 remains Draft/unmerged. Review the final docs head, wait for all exact-head CI/smokes, then merge only with expected-head SHA and repository policy. No self-approval/bypass. No cross-container atomicity or live Cosmos validation.
- Strict acceptance remains 0/19 (15 In Progress, 4 Blocked); production readiness is not established. External gates include live Cosmos 409/ETag/multi-instance/restart/retention, telemetry export/alerts, production CORS/load, auth-provider migration, signed-device Keychain and physical-device acceptance.
- After merge, complete remaining #18 diagnostics/CORS/native-publish/audit-path engineering, then continue #19 RELEASE-01. DRIVER-04 and SUPPORT-01 remain blocked on Product/Operations support-policy approval.


## PLATFORM-01 PR #156 final exact-head evidence — 2026-11-13

- Main/base HEAD before merge: `4817351616d6f0d7f0c9a51a0863cb92ce4caea4`. PR [#156](https://github.com/rajeevp727/turnova-platform/pull/156) is the sole active feature PR on `fix/platform-01-union-audit-idempotent-recovery`.
- Source/test commit `966a1ec89bd88489522d4760f7f8042fa2fc83b0` passed backend **558/558** (0 failed, 0 skipped), shared mobile/auth **179/179**, and deterministic Python harness **46/46**. Rider and Driver Expo configuration/typecheck/production Android+iOS JS bundle validation passed. Native non-container .NET 10 Linux-x64 API publish passed and produced a 43,110,451-byte framework-dependent artifact with required native rendering assets.
- Exact documentation-head commit `33908e1f5d17a71346a1eb8653914c5175bfe1a2` passed GitHub Actions [CI #1453 / run 43307889289](https://github.com/rajeevp727/turnova-platform/actions/runs/43307889289) **12/12 jobs**. The run is completed/success on that exact SHA. Both Rider and Driver Android debug and standalone release APKs passed artifact verification; both unsigned iOS Simulator apps compiled; Rider/Driver Android emulator installation/launch and Rider/Driver iOS Simulator installation/launch smoke jobs passed. Artifacts include the API publish, both Android release APKs, both iOS Simulator apps and all four smoke reports.
- Backend log confirms: `Passed! Failed: 0, Passed: 558, Skipped: 0, Total: 558`. The earlier 554/554 and intermediate counts are superseded. No production API/authentication, live Cosmos, signed-device Keychain or physical-device acceptance is claimed.
- The final COMMENT-only security/privacy review is anchored to `33908e1f5d17a71346a1eb8653914c5175bfe1a2`; no unresolved critical/high-risk finding was identified. Inline review threads are empty. The review is not an approval. No self-approval or bypass was used.
- Implementation includes deterministic Operations/UnionManager selfie and assignment/transfer audit identities and retry snapshots, original actor/canonical-field equality, bounded exact Driver-partition point-read recovery after duplicate/409, per-Driver serialization, history read-repair, Driver response redaction, allow-listed UnionManager projections, and conservative contact/control-character/line-separator reason and actor validation. PLATFORM-AUDIT-021–047 are documented. Driver profile, assignment and audit remain separate Cosmos containers; no cross-container atomicity is claimed.
- External gates remain live Cosmos 409/ETag/partition/multi-instance/restart/retention validation, production telemetry export/alerts, approved production CORS origins and load testing, production auth-provider/Ops identity migration, signed-device Keychain, Android/iOS physical-device acceptance, legal review and remaining product/Operations policy decisions. Strict acceptance remains **0/19** (15 In Progress, 4 Blocked); this is complete-evidence accounting, not an implementation score. Production readiness is not established.
- After PR #156 merges, re-read main and continue remaining safe #18 PLATFORM-01 health/readiness/diagnostics/CORS/native-publish/audit-path work, then proceed to #19 RELEASE-01. Do not pause for external gates.


## Latest PLATFORM-01 handoff — 2026-11-13

- Main HEAD remains `4817351616d6f0d7f0c9a51a0863cb92ce4caea4`; PR #156 is the sole active feature PR at `2fc5772734c00ca86b5fc9dffee0b20ac6b589d5`.
- The latest regression makes `DriverUnionAssignmentAuditSnapshot.CreateExpectedEvent` validate stored assignment reasons with `DriverUnionAuditReason.IsNormalizedSafe` before operation-key reconstruction. Contact-like or unnormalized persisted reasons now throw `InvalidOperationException`, preserving the Operations history route's HTTP 409 fail-closed behavior. Test case PLATFORM-AUDIT-050 is recorded.
- Exact-head CI #1467 is pending. Predecessor #1466 passed backend 562/562, mobile 179/179, Python 46/46, Rider/Driver app validation/bundles, native API publish, both Android native builds and Android emulator smokes; iOS native/smoke jobs were still running when superseded. No complete exact-head result is claimed yet.
- After #156 merges, re-read main. Next concrete #18 increment: minimize public `/`, `/health` and `/health/ready` metadata (`mode`, environment, storage type and database name) while preserving generic liveness/readiness status, 503 behavior and privacy-safe server-side failure-kind logging. Add integration tests for all public response projections. Then continue remaining PLATFORM-01 work and #19 RELEASE-01.
- External gates remain live Cosmos 409/ETag/partition/multi-instance/restart/retention, telemetry export/alerts, approved production CORS/load, production Ops identity, signed-device Keychain, physical-device acceptance and legal/product/Operations decisions. Strict acceptance remains 0/19 (15 In Progress, 4 Blocked); production readiness is not established.


## Latest PLATFORM-01 handoff — 2026-11-13

- Main HEAD remains `4817351616d6f0d7f0c9a51a0863cb92ce4caea4`; PR #156 is the sole active feature PR at `0c783b729b3f9b4ff8c3406075cd8d06b17eef83`.
- Added an Operations union-history integration theory for contact-like and unnormalized persisted assignment reasons. Both return HTTP 409 without partial history. Expected backend count is 564; exact-head CI is pending.
- After #156 merges, re-read main. Next #18 increment: remove public `mode`, environment, storage type and database name from `/`, `/health` and `/health/ready`, while preserving generic status, readiness check, 503 behavior and privacy-safe server-side failure-kind logging. Add integration tests for public response projections. Then continue remaining PLATFORM-01 work and #19 RELEASE-01.
- External gates: live Cosmos 409/ETag/partition/multi-instance/restart/retention, telemetry export/alerts, approved production CORS/load, production Ops identity, signed-device Keychain, physical-device acceptance and legal/product/Operations decisions. Strict acceptance remains 0/19 (15 In Progress, 4 Blocked); production readiness is not established.


## Latest repository state — 2026-11-13 exact-head verification

- Main HEAD: `4817351616d6f0d7f0c9a51a0863cb92ce4caea4` (PR #155 merged). Sole active feature PR: [#156](https://github.com/rajeevp727/turnova-platform/pull/156), branch `fix/platform-01-union-audit-idempotent-recovery`, source/test head `0c783b729b3f9b4ff8c3406075cd8d06b17eef83`, documentation head verified by CI #1471 `8a0199de41770c8e61421f12aa75d10950836084`.
- Exact-head CI [#1471 / 43325447712](https://github.com/rajeevp727/turnova-platform/actions/runs/43325447712) passed 12/12: backend 564/564, shared mobile/auth 179/179, Python 46/46, Rider/Driver app validation and production JS bundles, native non-container .NET API publish, both Android native builds, both unsigned iOS Simulator builds and all four same-run emulator/simulator smoke jobs.
- Same-run smoke artifacts inspected: Rider Android `13992094524`, Driver Android `13992717692`, Rider iOS `13994131365`, Driver iOS `13994001982`. Android apps installed/launched, remained stable, showed login screens, had no stale-session error and required no system ANR recovery. Unsigned iOS apps installed/launched, remained stable, showed login screens and no stale-session-expired error; SecureStore was unavailable in simulator and the expected recoverable banner was verified. Production API/authentication was not exercised; no signed-device Keychain or physical-device acceptance is claimed.
- PR #156 remains Draft/unmerged pending a documentation-only follow-up commit, its exact-head CI, final COMMENT-only review and GitHub merge enforcement. Profile, assignment and audit are separate Cosmos containers; retry recovery is not cross-container atomicity.
- Strict acceptance remains **0/19** (15 In Progress, 4 Blocked). This is complete-evidence accounting, not an implementation score. Production readiness is not established.
- Next: complete the documentation-only exact-head CI and merge PR #156 only with the expected current HEAD and no bypass. Re-read main; implement public root, /health and /health/ready metadata minimization with deterministic API integration tests, preserving generic liveness/readiness status, 503 behavior and privacy-safe server-side failure-kind logging. Continue remaining PLATFORM-01 engineering and then #19 RELEASE-01.
- External gates remain live Cosmos 409/ETag/partition/multi-instance/restart/retention; telemetry export/alerts; approved production CORS/load; production auth/Ops identity; signed-device Keychain; physical-device acceptance; support/escalation and reconciliation policies; Union legal approval; product-approved dispatch radius/freshness; production hosting/deployment.


## Latest engineering handoff — 2026-11-13

- PR #157 merged at `b6564c7dc2959883c2910f5b6f13134f7052566f`; public root/health/readiness metadata is minimized while actual readiness probing and generic HTTP 503 remain.
- PR #158 source/test head `40b7e373dd525b6b3985be0c36b72956ca280e9f` passed CI #1479 12/12 after retrying only Driver Android emulator smoke job `131699581542`. The first Driver smoke artifact showed successful install/launch and the expected login UI, but exited 255 before result JSON. No app fatal exception, ANR or React Native unhandled exception was found. The targeted retry passed and produced a complete stable-process/login-screen report. All four same-run smoke artifacts were inspected.
- The current PR branch has a documentation synchronization commit in progress; run exact-head CI before marking ready. Review is COMMENT-only, with no self-approval.
- After PR #158 merges, continue #18 remaining diagnostics/CORS/native-publish/audit-path code work; then move sequentially to #19 RELEASE-01. Do not start a second feature PR before #158 is merged.
- External gates remain: live Cosmos ETag/multi-instance/restart and partition validation; production auth/Operations identity; telemetry export/alerts; product-approved dispatch radius/location freshness; maps/routing and Call/Chat/push providers; private document storage/scanning/retention/consent; support/reconciliation policies; Union legal approval; signed Android/iOS builds, physical devices, production API hosting and deployment.


## Latest exact-head handoff — PR #158 / CI #1483 (2026-11-13)

- Main remains `b6564c7dc2959883c2910f5b6f13134f7052566f`; PR #158 source/test head is `40b7e373dd525b6b3985be0c36b72956ca280e9f`; documentation head `ff339ff068d09f7ca6d6a17278344ea138dd68c2` passed workflow [#1483 / 43369222847](https://github.com/rajeevp727/turnova-platform/actions/runs/43369222847) 12/12.
- Backend 567/567, shared mobile/auth 179/179, Python 46/46; Rider/Driver validation/bundles, native API publish, both Android debug/release builds, both unsigned iOS Simulator builds and all four emulator/simulator smokes passed. Smoke artifact IDs: Rider Android 14011503947, Driver Android 14012825007, Rider iOS 14013400018, Driver iOS 14012094464. Unsigned iOS SecureStore-unavailable banners are expected; no signed Keychain, physical-device, production API/auth or live Cosmos acceptance is claimed.
- This new docs evidence commit requires its own exact-head CI. PR #158 remains Draft/unmerged pending that CI and final expected-head/review checks. After merge, re-read main and continue #18 diagnostics/CORS/native-publish/audit-path work, then #19 RELEASE-01. Strict acceptance remains 0/19 (15 In Progress, 4 Blocked); production readiness is not established.


## Latest exact-head handoff — PR #158 / CI #1487 (2026-11-13)

- PR #157 is merged at `b6564c7dc2959883c2910f5b6f13134f7052566f`. Sole active feature PR is [#158](https://github.com/rajeevp727/turnova-platform/pull/158), branch `fix/platform-01-storage-readiness-failure-mapping`, source/test HEAD `e0997a8aad7d1d8a88722acf65d716016e6e30e5`. Exact source/test workflow [#1487 / 43376966539](https://github.com/rajeevp727/turnova-platform/actions/runs/43376966539) passed 12/12 jobs.
- Evidence: backend 570/570; shared mobile/auth 179/179; Python harness 46/46; Rider/Driver validation and bundles; native API publish; both Android debug/release builds; both unsigned iOS Simulator builds; all four same-run smoke jobs. Artifacts inspected: Rider Android 14015208959, Driver Android 14015880431, Rider iOS 14016166262, Driver iOS 14016177184.
- Android smoke confirms install/launch, stable process and login-screen text. Unsigned iOS smoke confirms install/launch, stable process, login-screen text and expected SecureStore-unavailable recovery banner. No signed Keychain, physical-device, production API/authentication or live Cosmos acceptance is claimed.
- PR #158 remains Draft pending a new exact-head CI after this documentation synchronization, then final review/head/base and expected-SHA merge checks. No self-approval or bypass.
- Next sequential work after merge: continue PLATFORM-01 CORS validation, Union audit observability and remaining diagnostics/native-publish/audit-path hardening, then #19 RELEASE-01. Strict acceptance remains 0/19 (15 In Progress, 4 Blocked); production readiness is not established.


## Latest exact-head handoff — PR #158 / CI #1493 (2026-11-13)

- Pre-sync documentation HEAD `c9695ea97d299db8cedea54600f8befcdab11884`; [CI #1493 / 43384473766](https://github.com/rajeevp727/turnova-platform/actions/runs/43384473766) completed 12/12 after retrying only Driver Android emulator smoke job `131754149564` as `131767500587`.
- Backend 570/570, shared mobile/auth 179/179, Python 46/46; Rider/Driver validation and production bundles, native API publish, Android debug/release APKs, unsigned iOS Simulator builds and all four same-run smoke jobs passed.
- Same-run smoke artifacts inspected: Rider Android `14018841676`, Driver Android retry `14020282957`, Rider iOS `14019186476`, Driver iOS `14019121356`. Initial Driver artifact `14018592304` showed successful install/launch and login UI but exited 255 before result JSON; no app fatal exception, ANR or React Native startup exception was found. Exact failing ADB command remains unknown. The targeted retry passed with stable-process/login-screen evidence.
- PR #158 remains Draft/unmerged pending a fresh exact-head CI for this documentation synchronization and final expected-head/review checks. No self-approval or bypass.
- Strict acceptance remains 0/19 (15 In Progress, 4 Blocked); production readiness is not established. Unsigned iOS does not prove signed Keychain; simulator/emulator smoke does not prove physical-device or end-to-end ride acceptance.

## Latest exact-head handoff — PR #158 / CI #1498 (2026-11-13)

- Main remains `b6564c7dc2959883c2910f5b6f13134f7052566f`. Sole active feature PR [#158](https://github.com/rajeevp727/turnova-platform/pull/158), branch `fix/platform-01-storage-readiness-failure-mapping`; exact documentation HEAD `6c67c98b192df5ea8210dd47640eee76816ea578` passed [CI #1498 / 43391119590](https://github.com/rajeevp727/turnova-platform/actions/runs/43391119590) **12/12 jobs**.
- Evidence: backend **570/570**, shared mobile/auth **179/179**, Python **46/46**, Rider/Driver validation and production JS bundles, native non-container API publish, both Android debug/release APKs, both unsigned iOS Simulator builds and all four same-run emulator/simulator smoke jobs. Smoke artifacts inspected: Rider Android `14021706924`, Driver Android `14020793191`, Rider iOS `14022022857`, Driver iOS `14022750045`.
- Android artifacts confirm install/launch, stable process and role-specific login UI. Unsigned iOS artifacts confirm install/launch, stable process, role-specific login UI and expected SecureStore-unavailable banner. No signed Keychain, physical-device, production API/authentication, live Cosmos or end-to-end ride acceptance is claimed.
- PR #158 remains Draft/unmerged. The current evidence synchronization creates a new documentation head that must pass its own exact-head CI; recheck main/base, HEAD and review threads before merge. No self-approval or bypass.
- Next after #158 merges: re-read main; continue #18 PLATFORM-01 CORS wildcard validation, Union audit observability, diagnostics/native-publish/audit-path work; then proceed sequentially to #19 RELEASE-01. Keep one feature PR active.
- Strict acceptance remains **0/19** (15 In Progress, 4 Blocked); this is complete-evidence accounting, not an implementation score. Production readiness is not established. External gates remain live Cosmos ETag/transaction/partition/multi-instance/restart/retention; production auth/Ops identity; telemetry export/alerts; approved dispatch radius/zones/location freshness; maps/routing and realtime providers; private document storage/scanning/consent/retention; support/reconciliation policy; Union legal approval; signed mobile builds/physical devices; production hosting and deployment.

## Latest exact handoff — PR #158 Android smoke harness correction (2026-11-13)

- Main HEAD remains `b6564c7dc2959883c2910f5b6f13134f7052566f`; sole active feature PR [#158](https://github.com/rajeevp727/turnova-platform/pull/158), branch `fix/platform-01-storage-readiness-failure-mapping`. Latest source/test branch HEAD after harness correction: `36bcc8d608596fde5d74641412f9d1f9fecb333e` (additional evidence-documentation commits may advance it; always fetch live PR head).
- Storage readiness implementation at `e0997a8aad7d1d8a88722acf65d716016e6e30e5` passed CI #1487 12/12: backend 570/570, shared mobile/auth 179/179, Python 46/46, native API/mobile builds and four smoke jobs. Exact earlier docs head `6c67c98b192df5ea8210dd47640eee76816ea578` passed CI #1498 12/12.
- CI #1502 / 43398973091 on `6f5c2c105f530b23ac116adec168748cbd9bbf67` completed 11/12; only Driver Android smoke failed. Log proves install and ActivityManager launch (`Status: ok`) succeeded; line 242 then failed because `printf | grep -Fq` triggered Broken pipe under `set -euo pipefail`. No app fatal error/ANR observed. Artifact `14024159021` records the launch output. Corrected the shell check to use a Bash substring condition; added Python regression invariant and `PLATFORM-NATIVE-ANDROID-001` test-plan case. Commits: `2ba1a20ce31baefa65a76a5b1cadb9a2554f8fa0`, `fe193763adca3633e0cc202142f323f0188bf1bf`, `61be9329a3e8ad2fc14fe9333eee7703a5bec1d2`, `36bcc8d608596fde5d74641412f9d1f9fecb333e`.
- Require a fresh exact-head CI for the corrected smoke harness and latest documentation. Do not mark PR ready or merge on #1502. Recheck current main/base, exact head, all required jobs and unresolved review threads. No self-approval or bypass.
- Next after #158 merges: re-read main, explicitly reject wildcard CORS origins and add deterministic configuration/integration tests; then add low-cardinality privacy-safe Union audit metrics/ActivitySource tracing and finish remaining PLATFORM-01 work before RELEASE-01.
- Strict acceptance remains 0/19 (15 In Progress, 4 Blocked); this is complete-evidence accounting, not an implementation score. Production readiness is not established. Live Cosmos, production auth/Ops identity, telemetry export/alerts, dispatch policy/freshness, maps/realtime providers, private document storage, support/reconciliation policies, Union legal approval, signed mobile builds/physical devices and production hosting remain external gates.


## Latest exact-head verification — PR #158 / CI #1508 (2026-11-13)

- Main/base HEAD: `b6564c7dc2959883c2910f5b6f13134f7052566f`. PR #158 branch `fix/platform-01-storage-readiness-failure-mapping`; exact tested HEAD `115d1914cb19e0c7a13df6765409521d31a4024b`.
- GitHub Actions [CI #1508 / run 43406713092](https://github.com/rajeevp727/turnova-platform/actions/runs/43406713092) completed successfully: **12/12 jobs passed**. Backend **570/570** (0 failed, 0 skipped); shared mobile/auth **179/179**; deterministic Python harness **47/47**, including `test_launch_status_check_avoids_pipefail_sigpipe_false_failure`; Rider/Driver Expo configuration, typechecks and production JavaScript bundles; native non-container .NET API publish; both Android debug and standalone release APKs; both unsigned iOS Simulator apps; all four same-run emulator/simulator smoke jobs.
- Driver iOS smoke initially hit a 300-second `xcrun simctl list devices available -j` timeout before app installation. Only that job was retried as job `131837472857`; the retry completed successfully. This is transient Simulator infrastructure failure followed by a successful targeted retry, not an application failure.
- Same-run smoke artifacts: Rider Android `14027824488`, Driver Android `14027884362`, Rider iOS `14029062725`, Driver iOS retry `14030386392`. Job logs confirm successful install/launch and role-specific login UI; unsigned iOS reports the expected SecureStore-unavailable recovery banner and does not claim Keychain success. Android release APKs: Rider `14028800834`, Driver `14028640774`; unsigned iOS app artifacts: Rider `14029435192`, Driver `14028438409`.
- The Android smoke harness correction is covered by the 47/47 Python suite: use a Bash substring check instead of `printf | grep -Fq` under `set -euo pipefail`, preventing a SIGPIPE false negative after successful ActivityManager launch. Test-plan case `PLATFORM-NATIVE-ANDROID-001`.
- This verifies CI builds plus emulator/simulator install, launch and login-screen presentation only. It does not establish production API/authentication, live Cosmos, signed iOS Keychain persistence, physical-device acceptance, store signing or end-to-end ride acceptance.
- This evidence-documentation commit creates a new PR head and requires its own exact-head CI. Keep PR #158 Draft until all required jobs pass on that exact new head and the latest base/head, review threads and GitHub merge enforcement are rechecked. No self-approval or bypass.
- Strict acceptance remains **0/19** (15 In Progress, 4 Blocked); this is complete-evidence accounting, not an implementation score. Production readiness is not established.


## Latest exact-head verification — PR #158 / CI #1509 (2026-11-13)

- Main/base HEAD: `b6564c7dc2959883c2910f5b6f13134f7052566f`. PR #158 branch `fix/platform-01-storage-readiness-failure-mapping`; exact tested HEAD `9fe2b884f9e0c2a566dd6e06b1b0fca731f65f82`.
- GitHub Actions [CI #1509 / run 43414466125](https://github.com/rajeevp727/turnova-platform/actions/runs/43414466125) completed successfully: **12/12 jobs passed**. Backend **570/570** (0 failed, 0 skipped); shared mobile/auth **179/179**; deterministic Python harness **47/47**; Rider/Driver Expo configuration, typechecks and production JavaScript bundles; native non-container .NET API publish; both Android debug and standalone release APKs; both unsigned iOS Simulator apps; all four same-run emulator/simulator smoke jobs.
- Same-run smoke artifacts were downloaded and inspected: Rider Android `14030984936`, Driver Android `14031119728`, Rider iOS Simulator `14033460518`, Driver iOS Simulator `14032367500`. Android results confirm install/launch, stable foreground process, role-specific login screen, no stale-session error and no system-UI recovery. iOS results confirm erased simulator data, install/launch, stable process, role-specific login screen, stale-session error absent and the expected SecureStore-unavailable recovery banner.
- Native artifacts: Rider Android release APK `14031468247`, Driver Android release APK `14030834941`, Rider unsigned iOS Simulator app `14032345534`, Driver unsigned iOS Simulator app `14031683932`, native API publish `14031511255`.
- These are CI build and emulator/simulator install/launch/login-screen results only. Unsigned iOS does not establish signed-device Keychain persistence. Production API/authentication, live Cosmos, physical-device acceptance, store signing or end-to-end ride acceptance are not claimed.
- This evidence synchronization creates a new PR head and requires its own exact-head CI before PR #158 is marked ready or merged. Recheck main/base, exact head, all required jobs, review threads and GitHub merge enforcement. No self-approval or bypass.
- Strict acceptance remains **0/19** (15 In Progress, 4 Blocked); this is complete-evidence accounting, not an implementation score. Production readiness is not established. Next after #158 merges: re-read main, implement explicit CORS wildcard-origin validation with deterministic configuration tests, then Union audit observability and remaining PLATFORM-01 work before RELEASE-01.


## Latest exact-head verification — PR #158 / CI #1512 (2026-11-13)

- Exact documentation HEAD `56f5f5573083f354ac1a3cc198f9b672655efcae`; GitHub Actions [CI #1512 / run 43421477453](https://github.com/rajeevp727/turnova-platform/actions/runs/43421477453) completed successfully: **12/12 jobs passed**.
- Backend: **570/570** (0 failed, 0 skipped). Shared mobile/auth: **179/179**. Deterministic Python harness: **47/47**. Rider and Driver Expo configuration, typechecks and Android/iOS production JavaScript bundles passed. Native non-container .NET API publish passed.
- Native builds: both Rider/Driver Android debug APKs and standalone release APKs passed; both unsigned iOS Simulator apps compiled. All four same-run emulator/simulator smoke jobs passed.
- Downloaded and inspected same-run smoke artifacts and result JSON/screenshots:
  - Rider Android emulator: `14035271929` — install/launch passed, stable foreground process, Rider login screen verified, no stale-session error, no system-UI recovery.
  - Driver Android emulator: `14034763708` — install/launch passed, stable foreground process, Driver login screen verified, no stale-session error, no system-UI recovery.
  - Rider iOS Simulator: `14036036290` — simulator data erased, install/launch passed, stable process, Rider login screen verified, SecureStore-unavailable recovery banner verified, stale-session error absent.
  - Driver iOS Simulator: `14037970012` — simulator data erased, install/launch passed, stable process, Driver login screen verified, SecureStore-unavailable recovery banner verified, stale-session error absent.
- Native build artifacts: Rider Android debug `14034092527`, Driver Android debug `14033948751`, Rider Android release APK `14034467813`, Driver Android release APK `14034093247`, Rider unsigned iOS Simulator app `14033739564`, Driver unsigned iOS Simulator app `14035391612`, native API publish `14034013011`.
- The screenshots confirm the expected role-specific login surfaces. The iOS apps are unsigned and have no Keychain access group; the SecureStore recovery banner is expected in this CI environment. This does not establish signed-device Keychain persistence.
- CI verifies build, install, launch and login-screen presentation only. Production API/authentication was not exercised; no live Cosmos, physical-device, signed-release, end-to-end ride or production-readiness claim is made.
- PR #158 remains the sole active feature PR. This evidence-only synchronization creates a new head and requires its own exact-head CI. Recheck current main/base, head SHA, review threads and GitHub merge enforcement; no self-approval or bypass.
- Strict acceptance remains **0/19** (15 In Progress, 4 Blocked); this is complete-evidence accounting, not an implementation score. Production readiness is not established.
- Next after PR #158 merges: re-read main, implement explicit CORS wildcard-origin validation with deterministic configuration tests, then privacy-safe Union audit metrics/ActivitySource tracing and remaining PLATFORM-01 diagnostics/native-publish/audit-path work before #19 RELEASE-01.


## Latest exact-head verification — PR #158 / CI #1513 (2026-11-13)

- Main/base HEAD: `b6564c7dc2959883c2910f5b6f13134f7052566f`. PR #158 branch `fix/platform-01-storage-readiness-failure-mapping`; tested documentation HEAD `89dc76f483ffa9a27dda1b67c5eaf50d78c362da`.
- GitHub Actions [CI #1513 / run 43431035440](https://github.com/rajeevp727/turnova-platform/actions/runs/43431035440) completed successfully on **attempt 2: 12/12 jobs passed**. Backend **570/570** (0 failed, 0 skipped); shared mobile/auth **179/179**; deterministic Python harness **47/47**; Rider/Driver Expo validation, typechecks and production JS bundles; native non-container .NET API publish; both Android debug/release APKs; both unsigned iOS Simulator builds; all four same-run Android/iOS smoke tests.
- The first attempt failed only Rider Android smoke job `131909886413` with exit 255 after APK installation and ActivityManager `Status: ok`. Its artifact `14040178925` contained the expected Rider login UI/foreground activity and no app fatal exception or ANR, but no final result JSON. GitHub's failed-jobs-only retry ran job `131930156233` and passed. Retry artifact `14045725928` confirms install/launch, stable foreground process, Rider login text, no stale-session error and no system-UI recovery.
- All four final smoke artifacts were downloaded and inspected:
  - Rider Android retry: `14045725928`.
  - Driver Android: `14041925228`.
  - Rider iOS Simulator: `14043137536`.
  - Driver iOS Simulator: `14041887826`.
- Android reports confirm successful install/launch, stable foreground process, role-specific login screen, no stale-session error and no system-UI recovery. iOS reports confirm simulator data erasure, install/launch, stable process, role-specific login screen, no stale-session-expired error and the expected SecureStore-unavailable recovery banner. Native artifacts: Rider Android debug `14038763284`, Driver Android debug `14039628739`, Rider Android release APK `14038654645`, Driver Android release APK `14039349859`, Rider unsigned iOS Simulator app `14040388207`, Driver unsigned iOS Simulator app `14040736292`, API publish `14039892373`.
- These are CI build and emulator/simulator install/launch/login-screen results only. Unsigned iOS does not establish signed-device Keychain persistence. Production API/authentication, live Cosmos, physical-device, signed-store release and end-to-end ride acceptance were not exercised.
- This evidence synchronization creates a new PR head and requires fresh exact-head CI before PR #158 is marked ready or merged. Recheck main/base, exact HEAD, all required jobs, review threads and GitHub merge enforcement. No self-approval or bypass.
- Strict acceptance remains **0/19** (15 In Progress, 4 Blocked); this is complete-evidence accounting, not an implementation score. Production readiness is not established.
- Next after PR #158 merges: re-read main, implement explicit CORS wildcard-origin validation with deterministic configuration tests, then privacy-safe Union audit metrics/ActivitySource tracing and remaining PLATFORM-01 diagnostics/native-publish/audit-path work before #19 RELEASE-01.
