# TurnOva MVP Acceptance Matrix

**Scope:** TurnOva Rider and TurnOva Driver Android/iOS MVP only. Fleet and Ops applications are Phase 2 and excluded.
**Source of truth:** `PRD.md`, especially sections 7, 8, 9, 10, 12, 14 and the authoritative Cab-only scope in §15; `docs/test-plan.md`.
**Last audited:** 2026-11-06 against main after PR #54 merge `7dd1aedf56fdc9ef36b6b04add952d78687a4266`. PR #54's final-head CI run `42234802360` passed all four jobs. CAB-SCOPE-01 is implemented and CI-validated for covered API/mobile paths; physical-device and production acceptance remain.
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
| RIDER-01 | Cab-only pickup/drop-off selection, location permission and union-grouped nearby Cab discovery | PR #45 zone/capacity/freshness groundwork; PRs #52/#53 Cab-only and legacy guards; PR #55 adds union-grouped discovery, active membership/verified-document/Online/fresh-location/capacity/reservation filtering, 3-driver cap, pickup-distance ordering, current-union selfie gating, privacy-safe response and Rider grouped-card/local selection UI; CI run 42236711865 passed backend build/tests, native non-container API publish, both mobile typechecks and shared mobile-auth tests | Durable selected-driver reservation/booking, optional-stop persistence, maps journey distance, call/chat provider actions, approved production radius, Cosmos multi-instance validation and Android/iOS device tests | In progress |
| RIDER-02 | Direct Rider/Driver fare negotiation and mutually confirmed agreement before ride start; no platform fare estimate | Fare estimate is intentionally null; no negotiation history, immutable agreement versions or mutual-confirmation start gate exist | Proposal/counterproposal history, immutable versions, both-party confirmation, start gate, expiry/cancellation and idempotency tests | Not started |
| RIDER-03 | Customer-selected Cab booking, status, cancellation and duplicate-submit safety | PR #55 disables legacy FIFO Rider request creation by default; discovery now supplies selected-driver IDs, but no durable selected-driver booking/reservation is created yet | Selected-driver booking, no automatic FIFO assignment, reservation/expiry/cancel, durable persistence/restart and duplicate-submit tests | In progress |
| DISPATCH-01 | Pickup resolves to configured zone/radius and approved unions serving Cab | PR #55 uses configured zone coverage and a required Dispatch:NearbyRadiusKm; Active Cab unions must serve the resolved zone; CI run 42236711865 passed | Product-approved production radius/zone boundaries, Cosmos query validation and cross-zone/device tests | In progress |
| DISPATCH-02 | Rider selects up to 2–3 approved, available Cab drivers per union ordered by pickup proximity; no customer-facing FIFO or automatic assignment | PR #55 discovery bypasses queue ordering, caps at three per union, sorts by pickup distance and renders selectable grouped cards; legacy nearby endpoint returns 410 and legacy request path is disabled by default; CI run 42236711865 passed | Durable server-side selection/reservation, expiry/cancellation and physical-device validation; internal FIFO role remains unresolved | In progress |
| DISPATCH-03 | Durable selected-driver negotiation/reservation expiry and audit; no automatic next-driver advancement | PR #55 discovery excludes existing active reservations; selected-driver negotiation reservation/expiry is not yet created. Legacy FIFO offer worker remains preserved but is not exposed by the Rider flow | Negotiation reservation/outbox/expiry, stale request suppression, restart/ETag/recovery tests | In progress |
| DISPATCH-04 | A selected Cab driver cannot be reserved for overlapping negotiations or active rides | PR #55 filters drivers with existing active per-driver reservations; selection does not yet acquire a new reservation, so overlapping selection is not prevented by this slice | Concurrent selected-driver reservation, negotiation expiry/cancel, crash recovery and real Cosmos cross-partition concurrency | In progress |
| REALTIME-01 | Rider/Driver receive live negotiation, agreement and Cab trip updates and recover after reconnect | SignalR ride and offer channels exist; negotiation/agreement state events are not implemented; customer-facing FIFO queue updates are not required | Negotiation/agreement notifications, agreement-version updates, trip reconnect and device-to-device tests | In progress |
| DRIVER-01 | Cab-only Driver onboarding, identity/licence/Cab vehicle verification, union approval and selfie visibility | PR #52 merged Cab-only onboarding and legacy-profile retention; PR #53 merged guards against legacy non-Cab selfie replacement and approval | Cab vehicle checks, secure document storage/scanning/consent/retention, reviewer RBAC and full onboarding device tests | In progress |
| DRIVER-02 | Cab Driver availability/location eligibility and selected-driver negotiation; internal FIFO only if approved | PR #52 merged Cab-only Online/queue eligibility; PR #53 merged legacy non-Cab location, queue-read, offer, reject and timeout guards plus mobile polling/SignalR suppression; selected-driver negotiation is not implemented | Location permission/resume/zone-exit, selected-driver negotiation/reservation/re-entry and internal FIFO decision | In progress |
| TRIP-01 | Cab trip lifecycle, location sharing, history and Rider live progress gated by mutual agreement | PR #47/#48 merged Rider and Driver owner-scoped history and reconnect restoration; agreement start gate and negotiation state remain unimplemented | Mutual-agreement start gate, device-to-device trip tests, Cosmos history persistence, background/permission and location-privacy tests | In progress |
| DRIVER-03 | Driver work metrics; no monetary earnings/commission/payout policy is assumed | Monthly PDF work-metrics payslip exists; direct-payment reporting is tracked under CAB-RECON-01, not as platform earnings | Work-metrics PDF device tests and authorised access/privacy verification | In progress |
| DRIVER-04 | Driver support and issue reporting | No verified end-to-end Driver support workflow evidence | Driver support process, issue categories, escalation owner, API/UI, audit and device acceptance | Blocked |
| RIDER-04 | Trip history/ratings and direct-payment reconciliation reporting; no in-app gateway, escrow or collection | Rider history API/Activity merged in PR #47; direct payment reporting/reconciliation and ratings remain unimplemented | Driver-reported/unverified amount, signed delta, least-privilege union view, ratings; customer dispute/confirmation and next-ride policy remain unresolved | In progress |
| SUPPORT-01 | Rider help and issue reporting | No verified end-to-end Rider support workflow evidence | Rider help process, issue categories, escalation owner, API/UI, audit and device acceptance | Blocked |
| PLATFORM-01 | Secure, observable, reliable API with persistent audit trail | PR #49 merged explicit CORS allow-list and distinct liveness/readiness; PR #51 merged append-only queue audit events in same-partition Cosmos batches and generic traceId Problem Details with exception-type-only logs | Durable exception audit with actor/reason, real Cosmos transactional-batch/ETag/restart/retention failure tests, metrics/traces/alerts, Cosmos-unavailable readiness, production origin configuration and load tests | In progress |
| RELEASE-01 | Android and iOS builds pass agreed functional, security and pilot-readiness tests | Main CI run 42219327319 passed backend build/tests, native non-container API publish/artifact verification, Rider/Driver Expo config/typechecks and shared mobile auth tests | Android/iOS production builds, signed artifacts, physical-device functional/security/pilot checklist | In progress |

## Current verified count and weighted progress

- Acceptance areas: **19 / 19**. The Cab-only business model reclassifies legacy fare-estimate, payment-gateway and customer-facing FIFO requirements without changing the denominator. Driver earnings and Driver support remain separate rows.
- Status counts after the scope reset: **0 Verified, 16 In progress, 1 Not started, 2 Blocked**.
- Weighted progress: **42.1%** = `(Verified × 1 + In progress × 0.5 + Not started × 0 + Blocked × 0) ÷ 19` = `(0 + 16 × 0.5 + 0 + 0) ÷ 19` = `8 ÷ 19`.
- Strict fully verified count: **0 / 19**. The pre-reset 36.8% was based on the obsolete fare-estimate/payment-gateway/FIFO mapping and is not directly comparable.
- PR #47 merged Rider history at `32cacbf430e1443835e3bd8e4294c15d3c137fe3`; CI run 42214516012 passed all four jobs.
- PR #48 merged Driver history at `2ec84e4eb99b5b1f66fb8b059d71fa36b01c6135`; CI run 42215718229 passed all four jobs.
- PR #49 merged CORS/health hardening at `67621e02b00d823b84d8d5e3f74f199c283fa3e7`; CI run 42216606101 passed all four jobs.
- PR #51 merged queue audit and safe exception handling at `1f30ffd7e268a09ba5863f8f9f58e3457e1ef9f0`; CI run 42219327319 passed all four jobs.
- PR #50 merged the approved Cab-only documentation reset at `6bd8f734e56ce35716595307cbeb9d7ab9decc59`; final-head CI run 42220682290 passed all four jobs.
- PR #52 merged Cab-only new-workflow validation at `6ba7b1968c1b5b2de61843cab04e85f7631e50fc`; post-merge CI run 42223126817 passed all four jobs.
- PR #53 merged legacy non-Cab API/mobile action guards at `7642168f42e277e04b76d7fba4c28ba90d3faa22`; final-head CI run [42232390997](https://github.com/rajeevp727/turnova-platform/actions/runs/42232390997) passed all four jobs.
- Do not mark an area Verified until all evidence in its row has passed and links/results are recorded.

## Approved supplemental requirement (not counted in the 19 PRD areas)

| ID | Scope | Status | Evidence / remaining work |
|---|---|---|---|
| AI-01 | Provider-agnostic, privacy-safe, bounded Rider/Driver AI assistance: rider trip-intent draft for human review; approved FAQ/ride-state explanations with escalation; optional driver offer explanation. AI must not book, charge, set fares, approve KYC, alter FIFO/eligibility or change ride state. | Not started | Implement the approved service boundary, validated contracts, timeout/cancellation, redaction, rate controls and deterministic fallback with mock-based tests; keep provider/model/credentials as infrastructure placeholders. |

## Ordered 1–19 checklist

1. AUTH-01 — In progress
2. AUTH-02 — In progress
3. RIDER-01 — In progress
4. RIDER-02 — Not started
5. RIDER-03 — In progress
6. DISPATCH-01 — In progress
7. DISPATCH-02 — In progress
8. DISPATCH-03 — In progress
9. DISPATCH-04 — In progress
10. REALTIME-01 — In progress
11. DRIVER-01 — In progress
12. DRIVER-02 — In progress
13. TRIP-01 — In progress; Rider and Driver history code merged in PRs #47 and #48; Cosmos/device acceptance remains
14. DRIVER-03 — In progress
15. DRIVER-04 — Blocked
16. RIDER-04 — In progress
17. SUPPORT-01 — Blocked
18. PLATFORM-01 — In progress; PRs #49/#51 merged CORS/health, queue audit and safe exception handling; metrics/traces/Cosmos gates remain
19. RELEASE-01 — In progress

## Highest-priority next work

1. Complete RIDER-01 / CAB-DISCOVERY-01: add durable selected-driver reservation/booking and exact expiry/cancel handling without customer-facing FIFO; then verify Android/iOS discovery and photo/location behaviour.
2. Implement CAB-AGREEMENT-01: direct Rider/Driver negotiation, immutable versioned agreement, both parties confirm current version, and no ride start before mutual confirmation.
3. Implement CAB-RECON-01: Driver reports amount received as unverified; persist agreed-minus-reported signed delta and least-privilege union visibility. Customer dispute/confirmation and non-zero-delta next-ride policy remain unresolved.
4. Complete CAB-UNION-01 and the remaining Driver/Rider support, platform observability and release evidence.
5. Keep AI-01 as the separately approved supplemental requirement, outside the 19-area denominator.

## Deployment constraint

The MVP API is validated through a native .NET 10 publish artifact. Docker, container images, container registries and container-based hosting are explicitly excluded. The API hosting provider remains a post-coding infrastructure decision; Azure Static Web Apps is not the API host or native mobile distribution target.

## Audit limitations

This audit reflects main after PR #53 merge `7642168f42e277e04b76d7fba4c28ba90d3faa22`. PR #53's final-head CI run `42232390997` passed all four jobs. This is not a substitute for executing Cosmos integration tests, provider sandbox tests or physical-device acceptance. Keep the matrix current in every feature PR.

## Approved Cab-only business-model reconciliation (2026-11-06)

This clarification supersedes conflicting legacy row descriptions without changing the 19-row denominator. Cab is the only supported new MVP service category. Reject Scooty/Bike/Auto in new workflows; preserve historical records safely.

- RIDER-01 / DISPATCH-01: pickup-based approved-union discovery and customer-selected Cab drivers; maximum 2–3 eligible, approved, available drivers per union, ordered by pickup proximity. No FIFO ranking/automatic Rider assignment.
- RIDER-02: platform fare estimate is explicitly not required. Direct negotiation and mutual agreement are tracked as CAB-AGREEMENT-01.
- RIDER-03 / DISPATCH-04 / REALTIME-01: include negotiation reservation/expiry, mutual-agreement start gate, immutable agreement versions and Cab ride lifecycle.
- DRIVER-01 / DRIVER-02: Cab-only onboarding, verified vehicle, union-approved selfie visibility and availability/location eligibility.
- RIDER-04: in-app payment gateway/escrow/collection is out of scope. Driver-reported direct-payment reconciliation is CAB-RECON-01.
- DRIVER-03: work-metrics PDF is not monetary earnings. Do not invent compensation, commission or payout policy.

### Approved supplemental acceptance requirements (not counted in 19)
| ID | Acceptance requirement | Status |
|---|---|---|
| CAB-SCOPE-01 | Cab-only new Rider/Driver/Union/API workflows; reject Scooty/Bike/Auto; preserve legacy records | Implemented and CI-validated for covered API/mobile paths — PR #52/#53 merged; PR #53 final-head CI run 42232390997 passed all four jobs. Physical-device/production acceptance remains. |
| CAB-DISCOVERY-01 | Approved nearby unions; max 2–3 eligible drivers per union; pickup proximity ordering; customer selection | In progress — PR #55 implements server grouping/eligibility/cap/order/photo privacy and Rider local card selection; CI run 42236711865 passed all four jobs. Durable reservation/booking and production radius/device gates remain |
| CAB-AGREEMENT-01 | Direct negotiation; durable immutable versioned agreement; both confirm; no start without current mutual confirmation | Not verified |
| CAB-RECON-01 | Driver-reported/unverified amount, signed delta, authorised union visibility and audit | Not verified |
| CAB-UNION-01 | Approved membership, Cab/vehicle verification, union selfie approval and least-privilege oversight | Partially evidenced; end-to-end pending |

Unresolved: internal FIFO role; customer dispute/confirmation; non-zero-delta next-ride policy; cancellation policy; legal partnership/T&C review. Do not invent decisions. The 42.1% weighted score is a reclassification from current merged code evidence; no CAB supplemental requirement is verified yet.
