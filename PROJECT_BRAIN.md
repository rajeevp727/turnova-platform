# TurnOva Project Brain

> Compact working context for future development sessions. Read this before coding; update it in the same feature PR whenever project state changes.
> Full requirements remain in `PRD.md`, `docs/architecture.md`, and `docs/test-plan.md`.

**Last verified:** 2026-11-09 04:00 IST  
**Repository:** https://github.com/rajeevp727/turnova-platform  
**Default branch:** main  
**Last audited main HEAD:** `4e65b147a01230b4a303d72c1270d448071c8bd6` after supplemental AI-01 PR #123 merged. PR #123 exact-head CI [42523956938](https://github.com/rajeevp727/turnova-platform/actions/runs/42523956938) passed 8/8 jobs (330/330 backend tests; 99/99 mobile auth tests); main post-merge run [42528216521](https://github.com/rajeevp727/turnova-platform/actions/runs/42528216521) passed. Current engineering item is #14 DRIVER-03 Unicode payslip rendering on PR [#124](https://github.com/rajeevp727/turnova-platform/pull/124), branch `fix/driver-03-unicode-payslip-rendering`, latest head `ff55082d76c1ce6cfaffd78138d1d3cc14141c83`. The renderer uses SkiaSharp/HarfBuzz `AllocatePositionedTextRun` with original UTF-8 source bytes and HarfBuzz clusters; test-only PdfPig 0.1.16 asserts embedded `/FontFile2` and `/ToUnicode`, extracts Latin plus nine Indic scripts, verifies concurrent text isolation and full long-name extraction, and checks the authenticated API PDF excludes ride/customer, route and fare/payment data. Latest exact-head CI [run #843 / 42530903667](https://github.com/rajeevp727/turnova-platform/actions/runs/42530903667) is queued. Do not merge until it passes. Strict acceptance remains 0/19; 15 In Progress and 4 Blocked. No live Cosmos, physical-device or signed-production validation is claimed.

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
- The 19-area score remains 42.1% (0 Verified, 16 In progress, 0 Not started, 3 Blocked; strict 0/19). The denominator is unchanged; AI-01 remains supplemental.


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
- 19-area progress remains 42.1% (0 Verified, 16 In Progress, 0 Not Started, 3 Blocked; strict 0/19). AI-01 and CAB-RECON-01 remain supplemental outside the denominator.


## Latest sequential handoff — #18 PLATFORM-01 (2026-11-07)

- PR #101 merged at 66f217a599ef694847b2490a656120f198a970f6; exact-head CI [42373804446](https://github.com/rajeevp727/turnova-platform/actions/runs/42373804446) passed all eight jobs (252/252 backend and 53/53 shared mobile tests plus Rider/Driver typechecks/bundles, Android debug APKs, unsigned iOS Simulator apps and non-container API publish).
- #17 SUPPORT-01 is Blocked pending Product-approved Rider support categories, escalation owner and operating process. Do not invent these.
- #18 PLATFORM-01 adds .NET Meter/ActivitySource instrumentation for ride-audit write attempts and storage readiness. Tags are low-cardinality and contain no ride/user/Union identifiers or PII. Exporter, dashboards and alert routing remain infrastructure gates.
- New evidence: `CompletedSelectedRidePersistsPrivacyMinimisedAuditEventsForAgreementLifecycleAndPayment` observes metrics/spans from the actual API audit path; `ReadinessProbeEmitsBoundedMetricsAndTraceWithoutDependencyDetails` verifies readiness metrics/activity without exposing dependency details. PR #102 exact-head CI [42375960785](https://github.com/rajeevp727/turnova-platform/actions/runs/42375960785) passed all eight jobs on `1cb1a5f2cb8af18d932990f0269d1f3d63f5164f`: 254/254 backend tests, 53/53 shared mobile tests, both app typechecks/bundles, Android debug APKs, unsigned iOS Simulator apps and native non-container API publish. Automated review [6707392983](https://github.com/rajeevp727/turnova-platform/pull/102#issuecomment-6707392983) found no unresolved critical/high-risk issue.
- Next: #19 RELEASE-01 — audit native release/signing/device evidence without confusing debug APKs or unsigned simulator apps with production-signed artifacts.
- Progress remains 42.1% = (0 Verified + 16 × 0.5 + 0 Not Started + 3 Blocked) / 19; strict fully verified count 0/19. AI-01 and CAB-RECON-01 remain supplemental.


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
- 19-area progress: 39.5% = (0 Verified + 15 × 0.5 + 0 Not started + 4 Blocked) / 19; strict fully verified 0/19. Supplemental AI-01 and CAB requirements remain outside the denominator and are not fully verified.
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
- Next sequential item after DRIVER-01: DRIVER-02.

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

The baseline AI-01 implementation silently defaulted an omitted passenger count to one and included passenger count in the provider prompt contract. The current follow-up keeps passenger count local, returns it as missing for manual completion, removes family/child/accessibility and other sensitive intent tokens from the provider allow-list, and clears the Rider form's stale default so discovery/booking are blocked until explicit 1–6 entry. Branch: `fix/ai-01-no-inferred-passenger-count`. New `AiAssistanceApiIntegrationTests` role/rate-limit integration tests, shared mobile helper tests and Rider typecheck/build are pending exact-head CI. AI-01 remains supplemental and outside the 19-area denominator.


### DRIVER-03 Unicode payslip PDF rendering (active PR #124)

- Replaced ASCII-only Type1 Helvetica rendering with SkiaSharp + HarfBuzz shaping and embedded Noto Sans variable fonts for Latin, Devanagari, Bengali, Gurmukhi, Gujarati, Odia, Tamil, Telugu, Kannada and Malayalam. Fonts are SIL OFL 1.1; Fonts/OFL.txt and Fonts/THIRD_PARTY_NOTICES.md are included.
- Positioned text blobs preserve original UTF-8 bytes and HarfBuzz cluster offsets so generated PDFs retain ToUnicode/searchable text. PdfPig tests extract every bundled script from generated PDFs.
- Immutable typefaces are cached process-wide; HarfBuzz shapers are per-request. Unsupported scripts return a safe HTTP 422 rather than question marks. Snapshot IDs, metrics, amounts and compensation policy are unchanged.
- Tests: DriverPayslipPdfTests covers script extraction, embedded fonts, unsupported scripts, concurrent generation and long-name wrapping. Final-head CI pending.
- External gates: live Cosmos snapshot create-conflict visibility/restart/multi-instance; physical Android/iOS PDF render/save/share/logout/cache acceptance. Additional scripts beyond the bundled set require licensed font assets and shaping/extraction tests.
