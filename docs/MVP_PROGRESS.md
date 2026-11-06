# TurnOva MVP Progress

**Last updated:** 2026-11-06  
**Scope:** 19 agreed Rider/Driver MVP acceptance areas. Fleet/Ops Phase 2 is excluded.

## 1. Current step

**#11 DRIVER-01: Audit Cab-only Driver onboarding, required identity/licence/Cab vehicle verification, Union approval and union-approved selfie visibility; implement only the next missing in-scope vertical slice.**

- PR #52 merged Cab-only foundations at `6ba7b1968c1b5b2de61843cab04e85f7631e50fc`.
- PR #53 merged legacy non-Cab endpoint and mobile action guards at `7642168f42e277e04b76d7fba4c28ba90d3faa22`; final-head CI run [42232390997](https://github.com/rajeevp727/turnova-platform/actions/runs/42232390997) passed all four jobs.
- CAB-SCOPE-01 is implemented and CI-validated for the covered API/mobile paths. Physical-device and production acceptance remain outstanding.
- PR #54 merged tracker reconciliation at `7dd1aedf56fdc9ef36b6b04add952d78687a4266`; its final-head CI run 42234802360 passed all four jobs.
- PR #55 merged at `78ddb9558b453b9171bc020ea83e51087f6e5f2f`; final-head CI run [42237572056](https://github.com/rajeevp727/turnova-platform/actions/runs/42237572056) passed all four jobs. It implements Cab union discovery, radius filtering, proximity ordering and photo privacy.
- PR #57 merged at `c5ff07843365e949b70fe1d4b9753bcd88022e10`; final synchronized PR head `8c846db3c93cfbe6f1eb574c838eb3ab0bf4fb9e` passed CI run [42254924983](https://github.com/rajeevp727/turnova-platform/actions/runs/42254924983), all four jobs. Automated reviews [6689967817](https://github.com/rajeevp727/turnova-platform/pull/57#issuecomment-6689967817) and [6690099440](https://github.com/rajeevp727/turnova-platform/pull/57#issuecomment-6690099440) found no unresolved critical/high-risk issue.
- PR #58 merged sequential #6 DISPATCH-01 at `a3a5835c66ec5f77f4b7505b4d99d5a43de18403`; final head `73db39c8cbdf79da99181688f7540e63c88cfc32` passed CI run [42258555372](https://github.com/rajeevp727/turnova-platform/actions/runs/42258555372), all four jobs. Automated review comment [6690682769](https://github.com/rajeevp727/turnova-platform/pull/58#issuecomment-6690682769) found no unresolved critical/high-risk issue.
- DISPATCH-01 remains Blocked: PR #55 implements pickup-zone and positive-finite radius filtering; PR #58 adds out-of-zone-union and missing/invalid-radius regressions. The checked-in radius is 0 and the Hyderabad zone box is a development placeholder. Production radius/zone approval, live Cosmos query/index validation and Android/iOS pickup-zone acceptance remain external gates.
- DISPATCH-02 implementation groups eligible Cab drivers by union, caps each union at three, orders by pickup distance then DriverId ordinal, and does not require FIFO queue membership. PR #59 merged at `508a789246cec1a9452651c62fa08aa3908140a4`; final-head CI [42259580145](https://github.com/rajeevp727/turnova-platform/actions/runs/42259580145) passed all four jobs.
- PR #61 merged DISPATCH-04 active-ride/Union-transfer regression coverage at `118929955d3959f078ddb59f7f5bdf3c3965d52a`; final-head CI [42262634978](https://github.com/rajeevp727/turnova-platform/actions/runs/42262634978) passed all four jobs. The test confirms an accepted ride keeps the Driver reservation and prevents Union transfer.
- PR #63 implements the REALTIME-01 Rider reconnect fix at head `d51acc55093f71efec190b10ba85b8cc738f985c`; CI run [42267819480](https://github.com/rajeevp727/turnova-platform/actions/runs/42267819480) passed all four jobs. It adds authenticated hub-group tests, shared mobile active-state tests, and Rider authoritative history/ride/agreement refresh on initial SignalR connection and reconnect.

## 2. Overall MVP completion

- **Weighted progress: 42.1%** under the revised Cab-only acceptance mapping.
- Formula: `(Verified × 1 + In progress × 0.5 + Not started × 0 + Blocked × 0) ÷ 19 × 100`.
- Counts: **0 Verified, 16 In progress, 0 Not started, 3 Blocked**.
- Calculation: `(0 + 16 × 0.5 + 0 × 0 + 3 × 0) ÷ 19 × 100 = 42.1%`.
- **Strict fully verified: 0/19.** DISPATCH-01 is blocked after independent code/test work because the production pickup radius and zone boundaries require explicit product approval; live Cosmos/device evidence also remains.
- This is a conservative status reclassification from current merged code evidence, not production acceptance. Each row remains unverified until all evidence in the matrix passes.

## 3. Ordered acceptance checklist (1–19)

| # | ID | Status | Current evidence / remaining gate |
|---:|---|---|---|
| 1 | AUTH-01 | In progress | PRs #35, #39 and #40 cover fake-TriSend API contracts, JWT/role boundaries and startup configuration guards; mobile runtime/session and full profile lifecycle remain. |
| 2 | AUTH-02 | In progress | PRs #37 and #41 plus shared mobile refresh tests cover single-flight refresh and retry-after-failure; rotation/reuse, restart, expiry, logout and secure-storage device tests remain. |
| 3 | RIDER-01 | In progress | PR #55 merged; union-grouped Cab discovery, three-driver cap, pickup-proximity order, approved-photo gating and local Rider selection are CI-validated. Production radius, routing/provider actions, Cosmos and device gates remain. |
| 4 | RIDER-02 | In progress | PR #56 merged at `9d39c83d13f619bcc7a5ec575669a4167b3cc5f8`; final-head CI run [42245873224](https://github.com/rajeevp727/turnova-platform/actions/runs/42245873224) passed all four jobs. RIDER-03 supplies the selected-booking and optional-stop route snapshot connection; live Cosmos and physical-device acceptance remain. |
| 5 | RIDER-03 | In progress | PR #57 merged at `c5ff07843365e949b70fe1d4b9753bcd88022e10`; final synchronized PR head `8c846db3c93cfbe6f1eb574c838eb3ab0bf4fb9e` passed CI run [42254924983](https://github.com/rajeevp727/turnova-platform/actions/runs/42254924983), all four jobs green. Automated review found no unresolved critical/high-risk issue. Live Cosmos ETag/restart and Android/iOS booking/expiry acceptance remain. |
| 6 | DISPATCH-01 | Blocked | PR #55 implements zone/radius and Active Cab union filtering; PR #58 merged at `a3a5835c66ec5f77f4b7505b4d99d5a43de18403`; final head `73db39c8cbdf79da99181688f7540e63c88cfc32` passed CI [42258555372](https://github.com/rajeevp727/turnova-platform/actions/runs/42258555372), all four jobs. Production radius and zone boundaries require product approval; live Cosmos query/index and Android/iOS pickup-zone tests remain. |
| 7 | DISPATCH-02 | In progress | PR #55 plus RIDER-03 provide the three-driver cap, pickup-distance order, queue-independent discovery and explicit Rider selection/reservation. PR #59 adds an equal-distance deterministic DriverId tie-break regression; live Cosmos/device validation and the internal FIFO role decision remain. |
| 8 | DISPATCH-03 | In progress | RIDER-03 adds expiring selected-driver leases, lazy expiry, deterministic interrupted-write recovery and terminal cleanup; live Cosmos restart/ETag/outbox validation remains. No customer-facing automatic advancement. |
| 9 | DISPATCH-04 | In progress | PR #57 implements per-driver reservation leases and lifecycle cleanup; PR #61 adds accepted-active-ride Union-transfer conflict coverage. CI [42262634978](https://github.com/rajeevp727/turnova-platform/actions/runs/42262634978) passed all four jobs. Live Cosmos multi-instance ETag/restart/crash recovery and approved negotiation lifetime remain. |
| 10 | REALTIME-01 | In progress | PR #63 reloads Rider owner-scoped history/current ride and agreement versions on connect/reconnect; Driver reconnect already rehydrates selected/active state and now shares the tested role-specific selector. Hub group authorization and mobile recovery tests passed CI [42267819480](https://github.com/rajeevp727/turnova-platform/actions/runs/42267819480), all four jobs. Android/iOS physical-device SignalR delivery/reconnect remains. |
| 11 | DRIVER-01 | In progress | PR #52 enforces Cab-only new onboarding and prevents in-place legacy non-Cab conversion; union selfie approval exists; Cab vehicle checks and secure document controls remain. |
| 12 | DRIVER-02 | In progress | PR #52 blocks legacy non-Cab profiles; RIDER-03 adds selected booking, Driver notification, accept/decline and active-reservation offline protection. GPS/zone/device validation and internal FIFO decision remain. |
| 13 | TRIP-01 | In progress | PRs #47/#48 provide owner-scoped history; PR #56 mutual-confirmation gates; RIDER-03 adds Negotiating/AgreementConfirmed/Expired states and stop snapshots. Cosmos/device/location-privacy validation remains. |
| 14 | DRIVER-03 | In progress | Monthly PDF work-metrics payslip exists; no monetary earnings/commission/payout policy is assumed. Direct-payment report is tracked under CAB-RECON-01. |
| 15 | DRIVER-04 | Blocked | Driver support categories, escalation owner and operational process are not approved. |
| 16 | RIDER-04 | In progress | Rider history merged in PR #47; direct-payment reporting/reconciliation and ratings remain. Customer dispute/confirmation and non-zero-delta next-ride policy are unresolved. |
| 17 | SUPPORT-01 | Blocked | Rider help/issue-reporting categories, escalation owner, process, API/UI, audit and device acceptance remain. |
| 18 | PLATFORM-01 | In progress | PR #49 CORS/health and PR #51 queue audit/safe exception response merged; durable exception audit, metrics/traces/alerts, Cosmos failure/recovery and load gates remain. |
| 19 | RELEASE-01 | In progress | CI validates backend, native API publish, Expo config/typechecks and shared auth tests; Android/iOS production builds, signed artifacts and physical-device pilot checks remain. |

## 4. Current item evidence and review

- PR #58 merged sequential #6 DISPATCH-01 at `a3a5835c66ec5f77f4b7505b4d99d5a43de18403`; final head `73db39c8cbdf79da99181688f7540e63c88cfc32` passed CI run [42258555372](https://github.com/rajeevp727/turnova-platform/actions/runs/42258555372), all four jobs. Automated review comment [6690682769](https://github.com/rajeevp727/turnova-platform/pull/58#issuecomment-6690682769) found no unresolved critical/high-risk issue.
- PR #59 merged sequential #7 DISPATCH-02; final-head CI [42259580145](https://github.com/rajeevp727/turnova-platform/actions/runs/42259580145) passed all four jobs.
- PR #61 merged sequential #9 DISPATCH-04 regression coverage; automated review found no unresolved critical/high-risk issue and final-head CI [42262634978](https://github.com/rajeevp727/turnova-platform/actions/runs/42262634978) passed all four jobs. No local test run is claimed.
- PR #63 is the sequential #10 REALTIME-01 increment. Automated review found no unresolved critical/high-risk issue; final-head CI [42267819480](https://github.com/rajeevp727/turnova-platform/actions/runs/42267819480) passed all four jobs. No physical-device test is claimed.

- PR #56 merged at `9d39c83d13f619bcc7a5ec575669a4167b3cc5f8`; final-head CI run [42245873224](https://github.com/rajeevp727/turnova-platform/actions/runs/42245873224) passed all four jobs. Its automated review found no critical/high-risk issue.

- PR #47 merged Rider history: https://github.com/rajeevp727/turnova-platform/pull/47; merge commit `32cacbf430e1443835e3bd8e4294c15d3c137fe3`; CI run 42214516012 passed all four jobs.
- PR #48 merged Driver history: https://github.com/rajeevp727/turnova-platform/pull/48; merge commit `2ec84e4eb99b5b1f66fb8b059d71fa36b01c6135`; CI run 42215718229 passed all four jobs.
- PR #49 merged CORS/health hardening: https://github.com/rajeevp727/turnova-platform/pull/49; merge commit `67621e02b00d823b84d8d5e3f74f199c283fa3e7`; CI run 42216606101 passed all four jobs.
- PR #51 merged queue audit/safe exception handling: https://github.com/rajeevp727/turnova-platform/pull/51; merge commit `1f30ffd7e268a09ba5863f8f9f58e3457e1ef9f0`; final-head CI run https://github.com/rajeevp727/turnova-platform/actions/runs/42219327319 passed all four jobs.
- PR #50 merged the approved Cab-only documentation reset at `6bd8f734e56ce35716595307cbeb9d7ab9decc59`; final-head CI run 42220682290 passed all four jobs.
- PR #52 merged Cab-only Rider/Driver/Union foundations; post-merge CI run 42223126817 passed all four jobs. PR #53 merged the remaining legacy non-Cab endpoint/UI guards at `7642168f42e277e04b76d7fba4c28ba90d3faa22`; final-head CI run 42232390997 passed all four jobs. Legacy profiles and ride-history service-class values remain preserved.
- PR #55 implements approved-union grouping, a three-driver cap, pickup-proximity ordering, selfie privacy and Rider selection. PR #57 adds durable booking/reservation, optional-stop route snapshots, deterministic create-if-absent idempotency recovery, ride-aware expired-lease reconciliation, serialized Union membership transfer, Driver SignalR reconnect recovery and privacy-safe selected-driver display. Implementation CI run [42254186398](https://github.com/rajeevp727/turnova-platform/actions/runs/42254186398) and documentation-synchronized CI run [42254417468](https://github.com/rajeevp727/turnova-platform/actions/runs/42254417468) passed all four jobs. Automated review comments [6689967817](https://github.com/rajeevp727/turnova-platform/pull/57#issuecomment-6689967817) and [6690099440](https://github.com/rajeevp727/turnova-platform/pull/57#issuecomment-6690099440) found no unresolved critical/high-risk issue. Final progress/matrix pre-refresh head `4b2b0516141badebfd33460d1726eb144d7749b4` passed run [42254713959](https://github.com/rajeevp727/turnova-platform/actions/runs/42254713959), all four jobs green. This tracker refresh is documentation-only and will receive one final CI rerun before merge.
- Fare estimate remains null; platform payment gateway/escrow/collection is out of scope. PR #56 adds versioned negotiation/agreement APIs and mobile controls; RIDER-03 connects selected booking and route stops. Driver-reported reconciliation remains a later code gap.
- The old FIFO/offer machinery exists; do not extend customer-facing FIFO or automatic assignment. Internal FIFO role remains unresolved.
- Real Cosmos transactional batch/ETag/restart/retention, Android/iOS physical-device tests and release signing remain infrastructure/device gates.

## 5. Approved supplemental requirements (not counted in the 19 areas)

| ID | Requirement | Status |
|---|---|---|
| CAB-SCOPE-01 | New Rider rides, Driver onboarding and new Union configuration are Cab-only; preserve historical records and exclude legacy non-Cab profiles from new eligibility. | Implemented and CI-validated for covered API/mobile paths — PRs #52/#53 merged; physical-device/production acceptance remains. |
| CAB-DISCOVERY-01 | Show approved unions serving pickup; up to 2–3 eligible/available Cab drivers per union ordered by pickup proximity; Rider selects. No customer-facing FIFO ranking or automatic assignment. | In progress — PR #55 merged; RIDER-03 adds durable selected booking/reservation; production radius and device validation remain. |
| CAB-AGREEMENT-01 | Direct Rider/Driver negotiation; persist immutable agreement versions; both parties confirm the current version; ride cannot start before mutual confirmation. | In progress — PR #56 merged; RIDER-03 connects selected booking and persists optional stops in agreement snapshots; device/Cosmos/expiry-policy validation remain. |
| CAB-RECON-01 | Driver reports amount received as unverified; persist agreed-minus-reported signed delta; expose only to authorised union roles. No platform payment gateway/escrow/collection. | Not started |
| CAB-UNION-01 | Cab membership, Cab vehicle verification, union approval and approved selfie visibility. | Partially evidenced; Cab-only validation and full union workflow remain |
| AI-01 | Separately approved provider-agnostic, bounded Rider/Driver AI assistance. Keep outside the 19-area denominator and do not let AI book, set fares, approve KYC or alter dispatch/ride state. | Not started |

## 6. Unresolved decisions and blockers

- Internal FIFO role: no customer-facing FIFO ranking or automatic assignment; do not assume whether internal fairness/rotation remains.
- Payment reconciliation: customer dispute/confirmation process and the next-ride rule for a non-zero signed delta are unresolved.
- Negotiation/booking: the exact negotiation lifetime is unapproved. Production appsettings remains null and booking fails closed until approval; 15 minutes is used only in integration-test fixtures, not as a product default.
- Union operations: legal partnership terms and minimum necessary union oversight/retention require review before production.
- Support: Rider/Driver issue categories, escalation owner and operating process are not approved.
- Infrastructure/device gates: development Cosmos access for transactional-batch/ETag/partition tests; Android/iOS physical-device validation; production provider/hosting/signing configuration.
- No credentials or secrets are stored in this file. Docker/container builds and deployments remain prohibited.

## 7. Next steps

1. DISPATCH-03 audit found temporary reservation acquisition, expiry, cancellation/decline cleanup, permanent promotion after acceptance and idempotent interrupted-write recovery covered by existing API/store tests; live Cosmos ETag/restart/outbox validation remains blocked.
2. DISPATCH-04 received PR #61: accepted active rides retain the per-driver reservation and a Union transfer attempt conflicts without changing membership. Keep the row In progress until live Cosmos multi-instance ETag/restart/crash recovery and the approved negotiation-lifetime gate are satisfied.
3. REALTIME-01 implementation is covered by PR #63 and CI [42267819480](https://github.com/rajeevp727/turnova-platform/actions/runs/42267819480); keep the row In progress until Android/iOS device-to-device WebSocket/reconnect/background acceptance passes.
4. Continue sequentially with #11 DRIVER-01: audit Cab-only onboarding, identity/licence/Cab vehicle verification, Union approval and union-approved selfie visibility; add only missing end-to-end API/mobile tests.
5. Revisit blocked DISPATCH-01 when the product owner approves production pickup radius and reviewed zone boundaries; then run live Cosmos query/index and Android/iOS pickup-zone acceptance.
6. Keep internal FIFO fairness/availability, customer dispute/confirmation, non-zero-delta next-ride policy and legal union terms unresolved until explicitly approved.


## 8. DISPATCH-03 audit handoff (2026-11-06)

- PR #59 is merged; final PR-head CI run [42259580145](https://github.com/rajeevp727/turnova-platform/actions/runs/42259580145) passed all four jobs.
- API and integration-test audit found coverage for temporary reservation acquisition, concurrent booking exclusion, Rider cancellation release, Driver decline release, expiry via ride read and discovery, terminal-state reconciliation, permanent promotion after mutual agreement/acceptance, and interrupted-write idempotent recovery.
- Reservation stores deliberately retain expired leases until the API reconciles the linked ride; Cosmos mutation paths use ETag conditions for promotion, expiry changes and release. Live Cosmos multi-instance/restart/ETag testing remains unavailable and is not claimed.
- No customer-facing FIFO ranking or automatic advancement is permitted. DISPATCH-04 is now audited with PR #61; current sequential focus advances to #10 REALTIME-01.


## 9. DISPATCH-04 audit handoff (2026-11-06)

- PR #61 merged at `118929955d3959f078ddb59f7f5bdf3c3965d52a`; final PR head `84763cce86f3413b86577cfc4a2cb724e49dc752` passed CI run [42262634978](https://github.com/rajeevp727/turnova-platform/actions/runs/42262634978), all four jobs.
- Automated engineering review was recorded on PR #61 and found no unresolved critical/high-risk issue. The test exercises Rider selection, mutual agreement confirmation, Driver acceptance, permanent reservation retention and a blocked Union transfer with unchanged membership.
- Backend restore/build/test, native non-container publish and artifact verification passed; Rider/Driver Expo config/typechecks and shared mobile-auth tests passed.
- DISPATCH-04 remains In Progress: live Cosmos cross-instance ETag contention and restart/crash recovery plus the product-approved negotiation lifetime are external gates. No live Cosmos/device validation is claimed.
- PR #63 completed the #10 REALTIME-01 implementation audit; physical-device SignalR gates remain. Current sequential focus advances to #11 DRIVER-01. Do not introduce customer-facing FIFO ranking or automatic next-driver advancement.


## 10. REALTIME-01 reconnect audit handoff (2026-11-06)

- PR #63 fixes the identified Rider gap: reconnect previously refreshed agreement history only and could leave ride status/location stale after missed events. Rider now reloads owner-scoped history and the authoritative current ride snapshot on initial SignalR connection and reconnect, then refreshes agreement versions. Event callbacks are guarded after effect cleanup.
- Driver selected-booking and active-trip reconnect paths already reload authoritative Driver history/available requests; both apps now use the shared role-specific active-ride selector so terminal-only history clears stale active state consistently.
- `RideTrackingHubTests` covers Rider ownership, assigned Driver ownership, Driver self-only notification group membership and rejection before group join. `rideRecovery.test.cjs` covers Rider/Driver active-state selection, terminal-state clearing and invalid roles.
- PR #63 final-head CI run [42267819480](https://github.com/rajeevp727/turnova-platform/actions/runs/42267819480) passed all four jobs: backend restore/build/test, native non-container API publish/artifact verification, Rider and Driver Expo config/typechecks, and shared mobile state/refresh tests.
- REALTIME-01 remains In progress until physical Android/iOS SignalR delivery, reconnect, background/resume and device-to-device agreement/status/location checks pass. No live device validation is claimed.
- Next sequential item: #11 DRIVER-01. Do not add customer-facing FIFO or automatic dispatch.
