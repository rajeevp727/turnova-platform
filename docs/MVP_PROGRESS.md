# TurnOva MVP Progress

**Last updated:** 2026-11-06  
**Scope:** 19 agreed Rider/Driver MVP acceptance areas. Fleet/Ops Phase 2 is excluded.

## 1. Current step

**#3 RIDER-01 / CAB-DISCOVERY-01: implement Rider-selected, union-grouped Cab discovery.**

- PR #52 merged Cab-only foundations at `6ba7b1968c1b5b2de61843cab04e85f7631e50fc`.
- PR #53 merged legacy non-Cab endpoint and mobile action guards at `7642168f42e277e04b76d7fba4c28ba90d3faa22`; final-head CI run [42232390997](https://github.com/rajeevp727/turnova-platform/actions/runs/42232390997) passed all four jobs.
- CAB-SCOPE-01 is implemented and CI-validated for the covered API/mobile paths. Physical-device and production acceptance remain outstanding.
- PR #54 merged tracker reconciliation at `7dd1aedf56fdc9ef36b6b04add952d78687a4266`; its final-head CI run 42234802360 passed all four jobs.
- PR #55 is open with union-grouped API discovery, eligibility/radius/photo/privacy rules, legacy customer-facing FIFO paths disabled by default and Rider grouped-card/local selection UI. CI run [42237256233](https://github.com/rajeevp727/turnova-platform/actions/runs/42237256233) passed all four jobs on code/test head `5b5d57d79b10b64b0fe9b475deb46d2ca9c21f69`: backend restore/build/tests, native non-container API publish, Rider/Driver Expo config/typechecks and shared mobile-auth tests. Subsequent changes are documentation-only; final PR-head checks remain a merge gate. Automated review found no critical/high-risk issue. Durable selected-driver reservation/booking and device/provider gates remain.

## 2. Overall MVP completion

- **Weighted progress: 42.1%** under the revised Cab-only acceptance mapping.
- Formula: `(Verified × 1 + In progress × 0.5 + Not started × 0 + Blocked × 0) ÷ 19 × 100`.
- Counts: **0 Verified, 16 In progress, 1 Not started, 2 Blocked**.
- Calculation: `(0 + 16 × 0.5 + 1 × 0 + 2 × 0) ÷ 19 × 100 = 42.1%`.
- **Strict fully verified: 0/19.** The pre-reset 36.8% used the obsolete fare-estimate/payment-gateway/customer-facing FIFO mapping and is not directly comparable.
- This is a conservative status reclassification from current merged code evidence, not production acceptance. Each row remains unverified until all evidence in the matrix passes.

## 3. Ordered acceptance checklist (1–19)

| # | ID | Status | Current evidence / remaining gate |
|---:|---|---|---|
| 1 | AUTH-01 | In progress | PRs #35, #39 and #40 cover fake-TriSend API contracts, JWT/role boundaries and startup configuration guards; mobile runtime/session and full profile lifecycle remain. |
| 2 | AUTH-02 | In progress | PRs #37 and #41 plus shared mobile refresh tests cover single-flight refresh and retry-after-failure; rotation/reuse, restart, expiry, logout and secure-storage device tests remain. |
| 3 | RIDER-01 | In progress | PR #55 adds approved-union grouping, max three eligible drivers per union, pickup-proximity order, approved-photo privacy and Rider local selection cards; CI run 42236711865 passed all four jobs. Durable selection reservation/booking, stops persistence, route distance, provider actions and device validation remain. |
| 4 | RIDER-02 | Not started | Platform fare estimates are out of scope; direct fare negotiation, immutable agreement versions and mutual confirmation before ride start are not implemented. |
| 5 | RIDER-03 | In progress | PR #55 disables legacy FIFO Rider request creation by default; selected-driver booking/reservation, agreement start gate and end-to-end status remain. |
| 6 | DISPATCH-01 | In progress | PR #55 adds Active Cab union/zone filtering and requires Dispatch:NearbyRadiusKm; checked-in radius is 0 until product approval. |
| 7 | DISPATCH-02 | In progress | PR #55 adds up to three per union, pickup-distance order and Rider local selection; durable server reservation is still missing. Internal FIFO role remains unresolved. |
| 8 | DISPATCH-03 | In progress | Durable offer/expiry/outbox exists; selected-driver negotiation reservation/expiry remains, and automatic next-driver advancement must not drive customer-facing assignment. |
| 9 | DISPATCH-04 | In progress | Per-driver reservation and ride ETag guards exist; adapt them to selected-driver negotiation and exact expiry/cancel release; Cosmos concurrency remains. |
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

- PR #47 merged Rider history: https://github.com/rajeevp727/turnova-platform/pull/47; merge commit `32cacbf430e1443835e3bd8e4294c15d3c137fe3`; CI run 42214516012 passed all four jobs.
- PR #48 merged Driver history: https://github.com/rajeevp727/turnova-platform/pull/48; merge commit `2ec84e4eb99b5b1f66fb8b059d71fa36b01c6135`; CI run 42215718229 passed all four jobs.
- PR #49 merged CORS/health hardening: https://github.com/rajeevp727/turnova-platform/pull/49; merge commit `67621e02b00d823b84d8d5e3f74f199c283fa3e7`; CI run 42216606101 passed all four jobs.
- PR #51 merged queue audit/safe exception handling: https://github.com/rajeevp727/turnova-platform/pull/51; merge commit `1f30ffd7e268a09ba5863f8f9f58e3457e1ef9f0`; final-head CI run https://github.com/rajeevp727/turnova-platform/actions/runs/42219327319 passed all four jobs.
- PR #50 merged the approved Cab-only documentation reset at `6bd8f734e56ce35716595307cbeb9d7ab9decc59`; final-head CI run 42220682290 passed all four jobs.
- PR #52 merged Cab-only Rider/Driver/Union foundations; post-merge CI run 42223126817 passed all four jobs. PR #53 merged the remaining legacy non-Cab endpoint/UI guards at `7642168f42e277e04b76d7fba4c28ba90d3faa22`; final-head CI run 42232390997 passed all four jobs. Legacy profiles and ride-history service-class values remain preserved.
- Current discovery is service-class/FIFO-oriented; approved-union grouping, 2–3 driver limit per union, pickup-proximity ordering and Rider selection are not implemented.
- Fare estimate remains null; platform payment gateway/escrow/collection is not part of the approved Cab-only scope. Direct negotiation/agreement and Driver-reported payment reconciliation remain code gaps.
- The old FIFO/offer machinery exists; do not extend customer-facing FIFO or automatic assignment. Internal FIFO role remains unresolved.
- Real Cosmos transactional batch/ETag/restart/retention, Android/iOS physical-device tests and release signing remain infrastructure/device gates.

## 5. Approved supplemental requirements (not counted in the 19 areas)

| ID | Requirement | Status |
|---|---|---|
| CAB-SCOPE-01 | New Rider rides, Driver onboarding and new Union configuration are Cab-only; preserve historical records and exclude legacy non-Cab profiles from new eligibility. | Implemented and CI-validated for covered API/mobile paths — PRs #52/#53 merged; physical-device/production acceptance remains. |
| CAB-DISCOVERY-01 | Show approved unions serving pickup; up to 2–3 eligible/available Cab drivers per union ordered by pickup proximity; Rider selects. No customer-facing FIFO ranking or automatic assignment. | Not started |
| CAB-AGREEMENT-01 | Direct Rider/Driver negotiation; persist immutable agreement versions; both parties confirm the current version; ride cannot start before mutual confirmation. | Not started |
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

1. Complete RIDER-01 / CAB-DISCOVERY-01: add durable selected-driver reservation/booking and exact expiry/cancel handling without customer-facing FIFO; then verify Android/iOS discovery and photo/location behaviour.
2. Implement CAB-AGREEMENT-01: versioned direct negotiation and mutual confirmation gate.
3. Implement CAB-RECON-01: Driver-reported amount and signed delta; leave dispute/next-ride policy blocked until approved.
4. Complete CAB-UNION-01 and remaining 19-area verification plus AI-01 sequentially. Do not open parallel feature PRs.
