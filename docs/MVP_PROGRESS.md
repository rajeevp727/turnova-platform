# TurnOva MVP Progress

**Last updated:** 2026-11-06  
**Scope:** 19 agreed Rider/Driver MVP acceptance areas. Fleet/Ops Phase 2 is excluded.

## 1. Current step

**#5 RIDER-03: durable Rider-selected Cab booking, driver reservation, lifecycle and idempotency.**

- PR #52 merged Cab-only foundations at `6ba7b1968c1b5b2de61843cab04e85f7631e50fc`.
- PR #53 merged legacy non-Cab endpoint and mobile action guards at `7642168f42e277e04b76d7fba4c28ba90d3faa22`; final-head CI run [42232390997](https://github.com/rajeevp727/turnova-platform/actions/runs/42232390997) passed all four jobs.
- CAB-SCOPE-01 is implemented and CI-validated for the covered API/mobile paths. Physical-device and production acceptance remain outstanding.
- PR #54 merged tracker reconciliation at `7dd1aedf56fdc9ef36b6b04add952d78687a4266`; its final-head CI run 42234802360 passed all four jobs.
- PR #55 merged at `78ddb9558b453b9171bc020ea83e51087f6e5f2f`; final-head CI run [42237572056](https://github.com/rajeevp727/turnova-platform/actions/runs/42237572056) passed all four jobs. RIDER-01 discovery is implemented; selected booking/reservation and optional stops are tracked under sequential #5 RIDER-03. Routing/provider actions and physical-device validation remain.

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
| 3 | RIDER-01 | In progress | PR #55 merged; union-grouped Cab discovery, three-driver cap, pickup-proximity order, approved-photo gating and local Rider selection are CI-validated. Production radius, routing/provider actions, Cosmos and device gates remain. |
| 4 | RIDER-02 | In progress | PR #56 merged at `9d39c83d13f619bcc7a5ec575669a4167b3cc5f8`; final-head CI run [42245873224](https://github.com/rajeevp727/turnova-platform/actions/runs/42245873224) passed all four jobs. RIDER-03 supplies the selected-booking and optional-stop route snapshot connection; live Cosmos and physical-device acceptance remain. |
| 5 | RIDER-03 | In progress | PR #57 adds durable selected-driver Negotiating rides, Union/route snapshots, create-if-absent idempotent persistence, per-driver leases that are reconciled against ride state before release, Union transfer exclusion through the same driver lease, expiry/cancel/decline/completion handling, Driver notification/reconnect recovery and Rider/Driver UI wiring. Implementation head `b65bbe7e74e1f4669c07e7df121f9473c8611a3a` passed CI run [42254186398](https://github.com/rajeevp727/turnova-platform/actions/runs/42254186398); documentation-synchronized head `9efd4ad354fe440a203f670bf1688e7ba314f9b4` passed run [42254417468](https://github.com/rajeevp727/turnova-platform/actions/runs/42254417468), all four jobs green. Automated review comments [6689967817](https://github.com/rajeevp727/turnova-platform/pull/57#issuecomment-6689967817) and [6690099440](https://github.com/rajeevp727/turnova-platform/pull/57#issuecomment-6690099440) found no unresolved critical/high-risk issue. Final progress/matrix pre-refresh head `4b2b0516141badebfd33460d1726eb144d7749b4` passed run [42254713959](https://github.com/rajeevp727/turnova-platform/actions/runs/42254713959), all four jobs green. This tracker refresh is documentation-only and will receive one final CI rerun before merge. |
| 6 | DISPATCH-01 | In progress | PR #55 adds Active Cab union/zone filtering and requires Dispatch:NearbyRadiusKm; checked-in radius is 0 until product approval. |
| 7 | DISPATCH-02 | In progress | PR #55 plus RIDER-03 provide up to three per union, pickup-distance order, explicit Rider selection and server reservation; live Cosmos/device evidence and internal FIFO decision remain. |
| 8 | DISPATCH-03 | In progress | RIDER-03 adds expiring selected-driver leases, lazy expiry, deterministic interrupted-write recovery and terminal cleanup; live Cosmos restart/ETag/outbox validation remains. No customer-facing automatic advancement. |
| 9 | DISPATCH-04 | In progress | RIDER-03 uses temporary per-driver reservation leases, permanent promotion after mutual agreement/Driver acceptance, ETag-protected release/expiry, terminal-reservation reconciliation and offline protection; live Cosmos multi-instance contention remains. |
| 10 | REALTIME-01 | In progress | PR #56 agreement events plus RIDER-03 driver-group RideSelected notification and mobile refresh; device-to-device reconnect acceptance remains. |
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

1. Re-run CI on the final progress/matrix-synchronized PR #57 head, then merge only if all required checks remain green and repository rules permit.
2. Re-audit main after merge and continue sequentially with #6 DISPATCH-01; revisit RIDER-02/RIDER-03 live Cosmos, approved radius/negotiation lifetime and Android/iOS device evidence when those external gates become available.
3. Continue approved supplemental slices (CAB-RECON-01, CAB-UNION-01 and AI-01) in the agreed sequential order without expanding Phase 2. Keep customer dispute/confirmation and non-zero-delta next-ride policy blocked until approved.
