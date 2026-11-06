# TurnOva MVP Progress

**Last updated:** 2026-11-06  
**Scope:** 19 agreed Rider/Driver MVP acceptance areas. Fleet/Ops Phase 2 is excluded.

## 1. Current step

**#5 RIDER-03: durable Rider-selected Cab booking, driver reservation, lifecycle and idempotency.**

- PR #52 merged Cab-only foundations at `6ba7b1968c1b5b2de61843cab04e85f7631e50fc`.
- PR #53 merged legacy non-Cab endpoint and mobile action guards at `7642168f42e277e04b76d7fba4c28ba90d3faa22`; final-head CI run [42232390997](https://github.com/rajeevp727/turnova-platform/actions/runs/42232390997) passed all four jobs.
- CAB-SCOPE-01 is implemented and CI-validated for the covered API/mobile paths. Physical-device and production acceptance remain outstanding.
- PR #54 merged tracker reconciliation at `7dd1aedf56fdc9ef36b6b04add952d78687a4266`; its final-head CI run 42234802360 passed all four jobs.
- PR #55 merged at `78ddb9558b453b9171bc020ea83e51087f6e5f2f`; final-head CI run [42237572056](https://github.com/rajeevp727/turnova-platform/actions/runs/42237572056) passed all four jobs. RIDER-01 remains In progress because selected-driver booking/reservation, optional stops persistence, routing/provider actions and physical-device validation remain.

## 2. Overall MVP completion

- **Weighted progress: 44.7%** under the revised Cab-only acceptance mapping.
- Formula: `(Verified × 1 + In progress × 0.5 + Not started × 0 + Blocked × 0) ÷ 19 × 100`.
- Counts: **0 Verified, 17 In progress, 0 Not started, 2 Blocked**.
- Calculation: `(0 + 17 × 0.5 + 0 × 0 + 2 × 0) ÷ 19 × 100 = 44.7%`.
- **Strict fully verified: 0/19.** The pre-reset 36.8% used the obsolete fare-estimate/payment-gateway/customer-facing FIFO mapping and is not directly comparable.
- This is a conservative status reclassification from current merged code evidence, not production acceptance. Each row remains unverified until all evidence in the matrix passes.

## 3. Ordered acceptance checklist (1–19)

| # | ID | Status | Current evidence / remaining gate |
|---:|---|---|---|
| 1 | AUTH-01 | In progress | PRs #35, #39 and #40 cover fake-TriSend API contracts, JWT/role boundaries and startup configuration guards; mobile runtime/session and full profile lifecycle remain. |
| 2 | AUTH-02 | In progress | PRs #37 and #41 plus shared mobile refresh tests cover single-flight refresh and retry-after-failure; rotation/reuse, restart, expiry, logout and secure-storage device tests remain. |
| 3 | RIDER-01 | In progress | PR #55 merged at `78ddb9558b453b9171bc020ea83e51087f6e5f2f`; final-head CI run 42237572056 passed all four jobs. Durable selection reservation/booking, stops persistence, route distance, provider actions and device validation remain. |
| 4 | RIDER-02 | In progress | [PR #56](https://github.com/rajeevp727/turnova-platform/pull/56), code/test head `74d4a2164576572186a31b081c73ad5ba6d7e049`: versioned direct negotiation, append-only confirmations, start/completion gates and Rider/Driver controls. CI run [42245873224](https://github.com/rajeevp727/turnova-platform/actions/runs/42245873224) passed all four jobs. Booking integration, stop snapshot, negotiation expiry/cancellation policy, live Cosmos concurrency and physical-device acceptance remain. |
| 5 | RIDER-03 | In progress | Branch feature/rider-selected-booking-v1 adds POST /api/v1/rider/bookings, request fingerprint idempotency, selected Union/Driver validation, optional-stop persistence, expiring per-driver reservation leases, Driver acceptance after mutual agreement, cancellation/decline/expiry/completion release and Rider/Driver UI wiring. CI and review pending. |
| 6 | DISPATCH-01 | In progress | PR #55 adds Active Cab union/zone filtering and requires Dispatch:NearbyRadiusKm; checked-in radius is 0 until product approval. |
| 7 | DISPATCH-02 | In progress | PR #55 adds up to three per union, pickup-distance order and Rider local selection; durable server reservation is still missing. Internal FIFO role remains unresolved. |
| 8 | DISPATCH-03 | In progress | RIDER-03 adds selected-driver reservation leases and lazy expiry; legacy offer worker remains compatibility-only and cannot advance customer-facing selection. Restart/outbox recovery and live Cosmos validation remain. |
| 9 | DISPATCH-04 | In progress | RIDER-03 uses temporary per-driver reservation leases, permanent promotion on acceptance, ETag-protected release/expiry and active-reservation offline protection; live Cosmos multi-instance contention remains. |
| 10 | REALTIME-01 | In progress | SignalR ride/offer flows and reconnect recovery exist; negotiation/agreement version events and device-to-device checks remain. |
| 11 | DRIVER-01 | In progress | PR #52 enforces Cab-only new onboarding and prevents in-place legacy non-Cab conversion; union selfie approval exists; Cab vehicle checks and secure document controls remain. |
| 12 | DRIVER-02 | In progress | PR #52 blocks legacy non-Cab profiles from new Online/queue eligibility; GPS/zone protections remain. Selected-driver negotiation remains; internal FIFO role is unresolved. |
| 13 | TRIP-01 | In progress | Rider and Driver history merged in PRs #47/#48; mutual-agreement start gate, Cosmos history and Android/iOS device/location-privacy checks remain. |
| 14 | DRIVER-03 | In progress | Monthly PDF work-metrics payslip exists; no monetary earnings/commission/payout policy is assumed. Direct-payment report is tracked under CAB-RECON-01. |
| 15 | DRIVER-04 | Blocked | Driver support categories, escalation owner and operational process are not approved. |
| 16 | RIDER-04 | In progress | Rider history merged in PR #47; direct-payment reporting/reconciliation and ratings remain. Customer dispute/confirmation and non-zero-delta next-ride policy are unresolved. |
| 17 | SUPPORT-01 | Blocked | Rider help/issue-reporting categories, escalation owner, process, API/UI, audit and device acceptance remain. |
| 18 | PLATFORM-01 | In progress | PR #49 CORS/health and PR #51 queue audit/safe exception response merged; durable exception audit, metrics/traces/alerts, Cosmos failure/recovery and load gates remain. |
| 19 | RELEASE-01 | In progress | CI validates backend, native API publish, Expo config/typechecks and shared auth tests; Android/iOS production builds, signed artifacts and physical-device pilot checks remain. |

## 4. Current item evidence and review

- RIDER-02 PR #56 is open: https://github.com/rajeevp727/turnova-platform/pull/56. Latest verified code/test head `74d4a2164576572186a31b081c73ad5ba6d7e049`; CI run [42245873224](https://github.com/rajeevp727/turnova-platform/actions/runs/42245873224) passed all four jobs. Automated review comment #6688847543 found no critical/high-risk issue. Merge is still gated on the next documentation-updated head's CI and repository merge rules.

- PR #47 merged Rider history: https://github.com/rajeevp727/turnova-platform/pull/47; merge commit `32cacbf430e1443835e3bd8e4294c15d3c137fe3`; CI run 42214516012 passed all four jobs.
- PR #48 merged Driver history: https://github.com/rajeevp727/turnova-platform/pull/48; merge commit `2ec84e4eb99b5b1f66fb8b059d71fa36b01c6135`; CI run 42215718229 passed all four jobs.
- PR #49 merged CORS/health hardening: https://github.com/rajeevp727/turnova-platform/pull/49; merge commit `67621e02b00d823b84d8d5e3f74f199c283fa3e7`; CI run 42216606101 passed all four jobs.
- PR #51 merged queue audit/safe exception handling: https://github.com/rajeevp727/turnova-platform/pull/51; merge commit `1f30ffd7e268a09ba5863f8f9f58e3457e1ef9f0`; final-head CI run https://github.com/rajeevp727/turnova-platform/actions/runs/42219327319 passed all four jobs.
- PR #50 merged the approved Cab-only documentation reset at `6bd8f734e56ce35716595307cbeb9d7ab9decc59`; final-head CI run 42220682290 passed all four jobs.
- PR #52 merged Cab-only Rider/Driver/Union foundations; post-merge CI run 42223126817 passed all four jobs. PR #53 merged the remaining legacy non-Cab endpoint/UI guards at `7642168f42e277e04b76d7fba4c28ba90d3faa22`; final-head CI run 42232390997 passed all four jobs. Legacy profiles and ride-history service-class values remain preserved.
- PR #55 implements approved-union grouping, a three-driver cap, pickup-proximity ordering, selfie privacy and Rider selection. RIDER-03 now adds durable booking/reservation and optional-stop route persistence; final CI/review pending.
- Fare estimate remains null; platform payment gateway/escrow/collection is out of scope. PR #56 adds versioned negotiation/agreement APIs and mobile controls; RIDER-03 connects selected booking and route stops. Driver-reported reconciliation remains a later code gap.
- The old FIFO/offer machinery exists; do not extend customer-facing FIFO or automatic assignment. Internal FIFO role remains unresolved.
- Real Cosmos transactional batch/ETag/restart/retention, Android/iOS physical-device tests and release signing remain infrastructure/device gates.

## 5. Approved supplemental requirements (not counted in the 19 areas)

| ID | Requirement | Status |
|---|---|---|
| CAB-SCOPE-01 | New Rider rides, Driver onboarding and new Union configuration are Cab-only; preserve historical records and exclude legacy non-Cab profiles from new eligibility. | Implemented and CI-validated for covered API/mobile paths — PRs #52/#53 merged; physical-device/production acceptance remains. |
| CAB-DISCOVERY-01 | Show approved unions serving pickup; up to 2–3 eligible/available Cab drivers per union ordered by pickup proximity; Rider selects. No customer-facing FIFO ranking or automatic assignment. | In progress — PR #55 merged; durable selection reservation/booking and device/radius validation remain. |
| CAB-AGREEMENT-01 | Direct Rider/Driver negotiation; persist immutable agreement versions; both parties confirm the current version; ride cannot start before mutual confirmation. | In progress — PR #56 merged; RIDER-03 connects selected booking and persists optional stops in agreement snapshots; device/Cosmos/expiry-policy validation remain. |
| CAB-RECON-01 | Driver reports amount received as unverified; persist agreed-minus-reported signed delta; expose only to authorised union roles. No platform payment gateway/escrow/collection. | Not started |
| CAB-UNION-01 | Cab membership, Cab vehicle verification, union approval and approved selfie visibility. | Partially evidenced; Cab-only validation and full union workflow remain |
| AI-01 | Separately approved provider-agnostic, bounded Rider/Driver AI assistance. Keep outside the 19-area denominator and do not let AI book, set fares, approve KYC or alter dispatch/ride state. | Not started |

## 6. Unresolved decisions and blockers

- Internal FIFO role: no customer-facing FIFO ranking or automatic assignment; do not assume whether internal fairness/rotation remains.
- Payment reconciliation: customer dispute/confirmation process and the next-ride rule for a non-zero signed delta are unresolved.
- Negotiation/booking: cancellation and negotiation expiry policy need explicit approval before implementing policy-dependent behaviour.
- Union operations: legal partnership terms and minimum necessary union oversight/retention require review before production.
- Support: Rider/Driver issue categories, escalation owner and operating process are not approved.
- Infrastructure/device gates: development Cosmos access for transactional-batch/ETag/partition tests; Android/iOS physical-device validation; production provider/hosting/signing configuration.
- No credentials or secrets are stored in this file. Docker/container builds and deployments remain prohibited.

## 7. Next steps

1. Complete RIDER-03 code review and final-head CI, then merge only if all checks pass and repository rules permit.
2. Continue sequentially with #6 DISPATCH-01; revisit RIDER-02/RIDER-03 device, live Cosmos, approved radius and negotiation-lifetime evidence when those external gates become available.
4. Complete approved supplemental slices (CAB-RECON-01, CAB-UNION-01 and AI-01) in the documented sequential order without expanding Phase 2. Leave dispute/next-ride policy blocked until approved.
