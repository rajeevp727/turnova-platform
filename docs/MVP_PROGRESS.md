# TurnOva MVP Progress

**Last updated:** 2026-11-06  
**Scope:** 19 agreed Rider/Driver MVP acceptance areas. Fleet/Ops Phase 2 is excluded.

## 1. Current step

**CAB-SCOPE-01 (approved supplemental requirement; not part of the 19-area denominator): enforce Cab-only new Rider and Driver workflows while preserving historical records.**

Current main gap: new ride requests and Driver onboarding still accept non-Cab classes. Branch feat/cab-only-scope now enforces Cab-only new Rider/Driver/Union/API workflows and preserves historical non-Cab records; focused PR and latest-head CI are pending.

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
| 3 | RIDER-01 | In progress | PR #45 covers pickup-zone/capacity/freshness; CAB-SCOPE-01 branch restricts new requests/nearby discovery to Cab; union grouping, max 2–3 drivers per union, pickup-proximity ordering and Rider selection remain. |
| 4 | RIDER-02 | Not started | Platform fare estimates are out of scope; direct fare negotiation, immutable agreement versions and mutual confirmation before ride start are not implemented. |
| 5 | RIDER-03 | In progress | Ride lifecycle, ownership and idempotency exist; selected-driver booking/negotiation reservation and agreement start gate remain. |
| 6 | DISPATCH-01 | In progress | Zone resolution exists; Cab-only zone catalog and non-Cab nearby rejection are implemented on the current branch; approved-union discovery remains. |
| 7 | DISPATCH-02 | In progress | Cab-only queue/offer eligibility is implemented on the current branch; the legacy customer-facing FIFO/automatic-offer path still needs replacement by union-grouped Rider selection. Internal FIFO role remains unresolved. |
| 8 | DISPATCH-03 | In progress | Durable offer/expiry/outbox exists; selected-driver negotiation reservation/expiry remains, and automatic next-driver advancement must not drive customer-facing assignment. |
| 9 | DISPATCH-04 | In progress | Per-driver reservation and ride ETag guards exist; adapt them to selected-driver negotiation and exact expiry/cancel release; Cosmos concurrency remains. |
| 10 | REALTIME-01 | In progress | SignalR ride/offer flows and reconnect recovery exist; negotiation/agreement version events and device-to-device checks remain. |
| 11 | DRIVER-01 | In progress | Onboarding and union selfie approval exist; Cab-only validation, Cab vehicle checks and secure document controls remain. |
| 12 | DRIVER-02 | In progress | GPS-gated Online, zone exit and queue removal exist; Cab-only availability and selected-driver negotiation remain. Internal FIFO role is unresolved. |
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
- PR #50 contains the approved Cab-only documentation reset. Original CI run 42217994808 passed on its initial head; the branch was reconciled with current main in merge commit `5eda5a9a4800e67c028018a8395c85efaa1fa7b3`, and final-head CI run 42220458874 passed all four jobs. PR #50 remains open pending merge.
- Current main still accepts non-Cab service/vehicle classes in new Rider/Driver workflows. Branch feat/cab-only-scope implements Cab-only validation, UI restriction, non-Cab queue/offer blocking, legacy-driver offline cleanup and historical ride preservation; it still needs review, PR and latest-head CI.
- Current discovery is service-class/FIFO-oriented; approved-union grouping, 2–3 driver limit per union, pickup-proximity ordering and Rider selection are not implemented.
- Fare estimate remains null; platform payment gateway/escrow/collection is not part of the approved Cab-only scope. Direct negotiation/agreement and Driver-reported payment reconciliation remain code gaps.
- The old FIFO/offer machinery exists; do not extend customer-facing FIFO or automatic assignment. Internal FIFO role remains unresolved.
- Real Cosmos transactional batch/ETag/restart/retention, Android/iOS physical-device tests and release signing remain infrastructure/device gates.

## 5. Approved supplemental requirements (not counted in the 19 areas)

| ID | Requirement | Status |
|---|---|---|
| CAB-SCOPE-01 | New Rider rides and Driver onboarding are Cab-only; preserve historical records and do not destructively migrate legacy vehicle classes. | In progress — implementation/tests on feat/cab-only-scope; PR/CI pending |
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

1. Complete review and open one focused PR for CAB-SCOPE-01; merge only after latest-head CI passes.
2. After merge, implement CAB-DISCOVERY-01: approved-union grouping, 2–3 available Cab drivers per union, pickup-proximity ordering and Rider selection.
3. Implement CAB-AGREEMENT-01: versioned direct negotiation and mutual confirmation gate.
4. Implement CAB-RECON-01: Driver-reported amount and signed delta; leave dispute/next-ride policy blocked until approved.
5. Continue the remaining 19-area verification and AI-01 supplemental work sequentially. Do not open parallel feature PRs.
