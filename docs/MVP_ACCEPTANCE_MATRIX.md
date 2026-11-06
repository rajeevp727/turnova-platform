# TurnOva MVP Acceptance Matrix

**Scope:** TurnOva Rider and TurnOva Driver Android/iOS MVP only. Fleet and Ops applications are Phase 2 and excluded.
**Source of truth:** `PRD.md`, especially sections 7, 8, 9, 10, 12, 14 and the authoritative Cab-only scope in §15; `docs/test-plan.md`.
**Last audited:** 2026-11-07 against main after PR #72 merge `43cb52f4652990d1ddc88efb765f8fef72ec0753`. PR #72 final head `6a025a19d61427a1ed8eb3ec10f5640ab5646fcd` passed CI [42303396400](https://github.com/rajeevp727/turnova-platform/actions/runs/42303396400), all four jobs: 214/214 backend tests, TRX execution guard, native non-container API publish, Rider/Driver Android+iOS production bundle exports, Metro shared-module assertions, Expo config/typechecks, shared mobile tests and three artifacts. Automated review [6697233888](https://github.com/rajeevp727/turnova-platform/pull/72#issuecomment-6697233888) found no unresolved critical/high-risk issue.
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
| RIDER-01 | Cab-only pickup/drop-off selection, location permission and union-grouped nearby Cab discovery | PR #45 zone/capacity/freshness groundwork; PRs #52/#53 Cab-only and legacy guards; PR #55 adds union-grouped discovery and Rider local selection cards | Approved production radius, call/chat provider actions, live Cosmos multi-instance validation and Android/iOS device tests | In progress |
| RIDER-02 | Direct Rider/Driver fare negotiation and mutually confirmed agreement before ride start; no platform fare estimate | PR #56 adds authenticated proposal/counterproposal APIs, immutable agreement versions, append-only confirmations, idempotency, SignalR events and mutual-confirmation gates; PR #57 connects selected booking and optional-stop agreement snapshots. | PR #64 final-head CI [42272254259](https://github.com/rajeevp727/turnova-platform/actions/runs/42272254259) executed 189/189 backend tests, including agreement version/confirmation tests; native API publish and mobile checks passed. Live Cosmos ETag/multi-instance and Android/iOS device acceptance remain. | In progress |
| RIDER-03 | Customer-selected Cab booking, status, cancellation and duplicate-submit safety | PR #57 creates selected-driver Negotiating rides with route/Union snapshots, request-fingerprint idempotency, create-if-absent persistence and deterministic retry recovery; per-driver leases are reconciled against ride lifecycle before release and shared with Union transfer so membership cannot change during an active negotiation/ride; Driver notification/reconnect recovery, mutual-agreement acceptance, cancellation/decline/expiry/completion handling and privacy-safe location snapshots are covered. | PR #57 final synchronized head `8c846db3c93cfbe6f1eb574c838eb3ab0bf4fb9e` passed CI run [42254924983](https://github.com/rajeevp727/turnova-platform/actions/runs/42254924983), all four jobs; automated review comments [6689967817](https://github.com/rajeevp727/turnova-platform/pull/57#issuecomment-6689967817) and [6690099440](https://github.com/rajeevp727/turnova-platform/pull/57#issuecomment-6690099440) found no unresolved critical/high-risk issue. Live Cosmos restart/ETag contention, Android/iOS booking/cancel/expiry tests, production radius and approved negotiation lifetime remain. | In progress |
| DISPATCH-01 | Pickup resolves to configured zone/radius and approved unions serving Cab | PR #55 resolves pickup against configured zones, requires a positive finite Dispatch:NearbyRadiusKm, filters Active Cab unions by pickup zone and excludes drivers outside radius; PR #58 adds explicit out-of-zone-union and missing/invalid-radius regression coverage (CAB-DISC-016/017). | PR #58 merged at `a3a5835c66ec5f77f4b7505b4d99d5a43de18403`; final head `73db39c8cbdf79da99181688f7540e63c88cfc32` passed CI [42258555372](https://github.com/rajeevp727/turnova-platform/actions/runs/42258555372), all four jobs. Production radius/zone approval, live Cosmos query/index and Android/iOS pickup-zone tests remain external gates; checked-in radius 0 intentionally fails closed. | Blocked |
| DISPATCH-02 | Rider selects up to 2–3 approved, available Cab drivers per union ordered by pickup proximity; no customer-facing FIFO or automatic assignment | PR #55 discovery bypasses queue ordering, caps at three per union, sorts by pickup distance and renders selectable grouped cards; RIDER-03 adds durable selection/reservation without FIFO assignment; PR #59 adds deterministic equal-distance tie-break coverage. | PR #64 final-head CI [42272254259](https://github.com/rajeevp727/turnova-platform/actions/runs/42272254259) executed 189/189 backend tests, including union grouping/cap/proximity/photo privacy; live Cosmos/device validation remains. Internal FIFO fairness/availability role is unresolved and must not affect Rider ranking or assignment. | In progress |
| DISPATCH-03 | Durable selected-driver negotiation/reservation expiry and audit; no automatic next-driver advancement | PR #57 creates temporary per-driver reservation leases, promotes them after mutual agreement and Driver acceptance, and releases them on cancel/decline/expiry/completion. Expired leases are not deleted by the store before the API reconciles the linked ride. Legacy FIFO offer worker remains compatibility-only. | CI runs [42254186398](https://github.com/rajeevp727/turnova-platform/actions/runs/42254186398) and [42254417468](https://github.com/rajeevp727/turnova-platform/actions/runs/42254417468) passed the in-memory path; live Cosmos multi-instance ETag/restart/outbox recovery tests remain. No automatic next-driver advancement. | In progress |
| DISPATCH-04 | A selected Cab driver cannot be reserved for overlapping negotiations or active rides | PR #57 acquires/promotes/releases per-driver reservations; PR #61 adds `UnionTransferIsBlockedWhileDriverHasAcceptedActiveRide`. | PR #64 final-head CI [42272254259](https://github.com/rajeevp727/turnova-platform/actions/runs/42272254259) executed 189/189 backend tests, including the accepted-active-ride/Union-transfer regression. Live Cosmos multi-instance ETag contention, restart/crash recovery and approved negotiation lifetime remain. | In progress |
| REALTIME-01 | Rider/Driver receive live negotiation, agreement and Cab trip updates and recover after reconnect | PR #56 adds agreement events; PR #57 adds Driver selected-booking notification/reconnect refresh; PR #63 restores Rider owner-scoped ride/agreement state on connect/reconnect and adds role-specific active-ride selection plus hub ownership tests. | PR #64 final-head CI [42272254259](https://github.com/rajeevp727/turnova-platform/actions/runs/42272254259) executed 189/189 backend tests, including `RideTrackingHubTests`; Rider/Driver Expo typechecks and shared mobile tests passed. | Android/iOS physical-device WebSocket delivery, disconnect/reconnect, background/resume and device-to-device agreement/status/location acceptance remain. No live device validation is claimed. | In progress |
| DRIVER-01 | Cab-only Driver onboarding, identity/licence/Cab vehicle verification, union approval and selfie visibility | PRs #52/#53 retain legacy non-Cab records and enforce Cab-only onboarding; PR #66 merged at `26dafc87451ba49ea4734df81c94f9e75790800d` with signed UnionManager + unionId-scoped member review, least-privilege member listing, union-only selfie approval, retry-safe audit, transfer reapproval and Driver membership display | PR #66 final verified head passed CI [42278673796](https://github.com/rajeevp727/turnova-platform/actions/runs/42278673796): 191/191 backend tests, TRX non-zero guard, native API publish/artifact, both mobile typechecks and shared tests; all four jobs succeeded. Private document storage/scanning/consent/retention, identity-provider claim mapping and Android/iOS onboarding device tests remain | In progress |
| DRIVER-02 | Cab Driver availability/location eligibility and selected-driver negotiation; internal FIFO only if approved | PR #52/#53 merged Cab-only availability and legacy non-Cab action guards; PR #56 supplies negotiation controls; RIDER-03 adds selected booking, Driver notification, accept/decline and reservation lifecycle. PR #67 adds active Cab Union/approved-document gating, shared per-Driver leases for availability and zone-exit mutations, and foreground location refresh on resume | PR #67 final-head CI [42286508190](https://github.com/rajeevp727/turnova-platform/actions/runs/42286508190) passed all four jobs and executed 193/193 backend tests; native non-container API publish/artifact, Rider/Driver Expo config/typechecks and six shared mobile tests passed. Live Cosmos lease races, Android/iOS permission/background/resume and internal FIFO decision remain | In progress |
| TRIP-01 | Cab trip lifecycle, location sharing, history and Rider live progress gated by mutual agreement | PR #47/#48 owner-scoped history/reconnect; PR #56 mutual-confirmation start/completion gates; RIDER-03 selected-booking lifecycle; PR #68 adds selected-Cab negotiation-to-completion API coverage, assigned-Driver-only location updates, finite coordinate validation and a foreground-only watcher that refreshes authoritative Driver history and rechecks foreground permission on resume | PR #68 final head `0d7331aa2a93d1fa61862bf56710bd31290d9c3f` passed CI [42290510329](https://github.com/rajeevp727/turnova-platform/actions/runs/42290510329): 195/195 backend tests, TRX guard, native non-container API publish/artifact, Rider/Driver Expo typechecks and nine shared mobile tests; all four jobs green. Live Cosmos history/ETag and Android/iOS device-to-device SignalR, map, permission and background/resume acceptance remain | In progress |
| DRIVER-03 | Driver work metrics; no monetary earnings/commission/payout policy is assumed | Monthly PDF work-metrics payslip; PR #69 merged at `70738af97d17cb8be715410898138d3e2f10f1cf` with current/incomplete India-local month rejection, Union assignment/transfer checks, authenticated Driver isolation and PDF privacy/snapshot tests | PR #69 final head `047564de5e4d07ee2ca34fe92e87ce1aed29d9b6` passed CI [42291337082](https://github.com/rajeevp727/turnova-platform/actions/runs/42291337082): 196/196 backend tests, non-container API publish, both mobile typechecks and nine shared tests. Android/iOS PDF save/share/accessibility and live Cosmos snapshot/recovery remain | In progress |
| DRIVER-04 | Driver support and issue reporting | No verified end-to-end Driver support workflow evidence | Driver support process, issue categories, escalation owner, API/UI, audit and device acceptance | Blocked |
| RIDER-04 | Trip history and direct-payment reconciliation reporting; no in-app gateway, escrow or collection | Rider history API/Activity merged in PR #47; PR #70 merged at `17c489eac64dc0f99e881a1474147b2f79fe2aac` with immutable Driver-reported reconciliation, signed delta, Driver history and signed-unionId-scoped UnionManager view | PR #70 final head `55cbe53a044f5f1abed9096973cbc0aad06b83a6` passed CI [42295994210](https://github.com/rajeevp727/turnova-platform/actions/runs/42295994210): 210/210 backend tests, non-container API publish, both mobile typechecks and nine shared tests. Live Cosmos ETag/restart and Android/iOS report-entry/history acceptance remain. Customer dispute/confirmation and non-zero-delta next-ride policy remain unresolved. Ratings are not an explicit PRD acceptance criterion. | In progress |
| SUPPORT-01 | Rider help and issue reporting | No verified end-to-end Rider support workflow evidence | Rider help process, issue categories, escalation owner, API/UI, audit and device acceptance | Blocked |
| PLATFORM-01 | Secure, observable, reliable API with persistent audit trail | PR #49 merged explicit CORS allow-list and distinct liveness/readiness; PR #51 merged append-only queue audit events in same-partition Cosmos batches and generic traceId Problem Details. PR #71 adds a bounded three-second storage readiness probe, maps dependency failure/timeout to generic 503 while keeping liveness independent, and preserves caller cancellation | PR #71 final-head CI and tests; remaining: durable audit coverage for all ride/agreement/reconciliation transitions, live Cosmos transactional-batch/ETag/restart/retention tests, metrics/traces/alerts, production origin configuration and load tests | In progress |
| RELEASE-01 | Android and iOS builds pass agreed functional, security and pilot-readiness tests | PR #64 validates backend/native API/mobile checks; PR #71 preserves the non-container API publish and PR #72 adds production JavaScript bundle exports for Rider/Driver on Android/iOS. | PR #72 final head `6a025a19d61427a1ed8eb3ec10f5640ab5646fcd` passed CI [42303396400](https://github.com/rajeevp727/turnova-platform/actions/runs/42303396400), all four jobs: 214/214 backend tests, native non-container API publish, four Android/iOS bundle exports, Metro assertions, both app typechecks, shared mobile tests and three artifacts. These are JavaScript bundles, not native APK/AAB/IPA binaries. Android signing, iOS bundle identifiers/archive/signing and physical-device functional/security/pilot acceptance remain | In progress |

## Current verified count and weighted progress

- Acceptance areas: **19 / 19**. The Cab-only business model reclassifies legacy fare-estimate, payment-gateway and customer-facing FIFO requirements without changing the denominator. Driver earnings and Driver support remain separate rows.
- Status counts after the DISPATCH-01 close-out: **0 Verified, 16 In progress, 0 Not started, 3 Blocked**.
- Weighted progress: **42.1%** = `(Verified × 1 + In progress × 0.5 + Not started × 0 + Blocked × 0) ÷ 19` = `(0 + 16 × 0.5 + 0 + 0) ÷ 19` = `8 ÷ 19`.
- Strict fully verified count: **0 / 19**. DISPATCH-01 is now Blocked after its independent automated guard work; the approved production radius/zone decision and live Cosmos/device gates are not available.
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
| AI-01 | Provider-agnostic, privacy-safe, bounded Rider/Driver AI assistance: Rider trip-intent draft for human review and approved FAQ/ride-state explanations with escalation. AI must not book, charge, set fares, approve KYC, alter eligibility/FIFO or change ride state. | In progress — current focused branch adds the Application service/provider contract, disabled-by-default provider placeholder, locally parsed route draft, approved-content help classifier, Rider UI entry points and per-caller API rate limiting. | Unit/API tests cover redaction, provider output validation, timeout/cancellation, fallback, Rider-only drafting, Rider/Driver help, escalation and rate limits. CI and final PR review are pending. Live provider sandbox and managed-secret validation remain external gates. |

## Ordered 1–19 checklist

1. AUTH-01 — In progress
2. AUTH-02 — In progress
3. RIDER-01 — In progress
4. RIDER-02 — In progress
5. RIDER-03 — In progress
6. DISPATCH-01 — Blocked
7. DISPATCH-02 — In progress
8. DISPATCH-03 — In progress
9. DISPATCH-04 — In progress
10. REALTIME-01 — In progress
11. DRIVER-01 — In progress
12. DRIVER-02 — In progress
13. TRIP-01 — In progress; selected-Cab lifecycle/location regression coverage and foreground-only watcher are being added; Cosmos/device acceptance remains
14. DRIVER-03 — In progress; PR #69 merged and final-head CI passed; Android/iOS and live Cosmos gates remain
15. DRIVER-04 — Blocked
16. RIDER-04 — In progress; PR #70 merged immutable Driver-reported direct-payment reconciliation; live Cosmos and Android/iOS report-entry/history gates remain
17. SUPPORT-01 — Blocked
18. PLATFORM-01 — In progress; PR #71 merged bounded storage readiness failure/timeout handling; live Cosmos, broader audit, telemetry and load gates remain
19. RELEASE-01 — In progress; PR #72 CI production JavaScript bundle exports pass for Rider/Driver Android+iOS; native signed binaries and physical-device gates remain

## Highest-priority next work

1. **Current sequential AI-01 supplemental requirement:** finish the provider-agnostic Rider trip-intent and approved-help boundary, run final-head CI, complete automated review and merge only when all required checks pass. Keep AI-01 outside the 19-row denominator.
2. **#17 SUPPORT-01 remains Blocked:** Rider support categories, escalation owner and operating process are not approved. Do not invent them.
3. **#19 RELEASE-01 remains In progress:** PR #72 verifies production JavaScript bundles, not native signed APK/AAB/IPA or physical-device acceptance.
4. Revisit blocked DISPATCH-01 only after production pickup radius/zone approval, live Cosmos query/index validation and Android/iOS pickup-zone acceptance.

## Deployment constraint

The MVP API is validated through a native .NET 10 publish artifact. Docker, container images, container registries and container-based hosting are explicitly excluded. The API hosting provider remains a post-coding infrastructure decision; Azure Static Web Apps is not the API host or native mobile distribution target.

## Audit limitations

This audit reflects main after PR #70 merge `17c489eac64dc0f99e881a1474147b2f79fe2aac`. PR #70 final-head CI run `42295994210` passed all four jobs and executed 210/210 backend tests. This is not a substitute for live Cosmos integration tests, provider sandbox tests or physical-device acceptance. Keep the matrix current in every feature PR.

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
| CAB-SCOPE-01 | Cab-only new Rider/Driver/Union/API workflows; reject Scooty/Bike/Auto; preserve legacy records | Cab-only guards and historical record preservation are implemented in PR #52/#53. PR #64 final-head CI [42272254259](https://github.com/rajeevp727/turnova-platform/actions/runs/42272254259) executed 189/189 backend tests and passed native API publish/mobile checks. Physical-device/production acceptance remains. |
| CAB-DISCOVERY-01 | Approved nearby unions; max 2–3 eligible drivers per union; pickup proximity ordering; customer selection | In progress — PR #55 implements server grouping/eligibility/cap/order/photo privacy and Rider local card selection; PR #64 final-head CI [42272254259](https://github.com/rajeevp727/turnova-platform/actions/runs/42272254259) executed 189/189 backend tests. Durable reservation/booking, production radius and device gates remain |
| CAB-AGREEMENT-01 | Direct negotiation; durable immutable versioned agreement; both confirm; no start without current mutual confirmation | In progress — PR #56 implements API/mobile proposal and confirmation controls, append-only terms/confirmation history and start/completion gates; booking integration, optional stops and device/Cosmos validation remain. |
| CAB-RECON-01 | Driver-reported/unverified amount, signed delta, authorised union visibility and audit | In progress — PR #70 merged at `17c489eac64dc0f99e881a1474147b2f79fe2aac`; final head `55cbe53a044f5f1abed9096973cbc0aad06b83a6` passed CI [42295994210](https://github.com/rajeevp727/turnova-platform/actions/runs/42295994210) with 210/210 backend tests, both mobile typechecks, nine shared tests and non-container API publish; live Cosmos/device gates remain. Not counted in the 19-area score. |
| CAB-UNION-01 | Approved membership, Cab/vehicle verification, union selfie approval and least-privilege oversight | PR #66 merged at `26dafc87451ba49ea4734df81c94f9e75790800d`; final-head CI [42278673796](https://github.com/rajeevp727/turnova-platform/actions/runs/42278673796) passed all four jobs, including 191/191 backend tests. Active-union creation/activation remains operations-controlled; private document storage/scanning/consent/retention and legal partnership terms remain external gates | In progress |

Unresolved: internal FIFO role; customer dispute/confirmation; non-zero-delta next-ride policy; cancellation policy; legal partnership/T&C review. Do not invent decisions. The 42.1% weighted score is a reclassification from current merged code evidence; no CAB supplemental requirement is verified yet.


## DISPATCH-03 audit note (2026-11-06)

The selected-driver lifecycle is implemented in main and covered by integration tests for concurrent booking exclusion, rider cancellation, driver decline, expired negotiation reconciliation through ride reads and discovery, terminal reservation release, permanent reservation promotion after mutual agreement/acceptance, and interrupted-write idempotent recovery. Cosmos reservation writes use ETag conditions, but live multi-instance/restart/ETag/outbox tests remain an external gate. Keep DISPATCH-03 In progress; do not mark Verified based on in-memory tests alone. No customer-facing FIFO ranking or automatic next-driver advancement is allowed.


## DISPATCH-04 audit note (2026-11-06)

- PR #61 adds UnionTransferIsBlockedWhileDriverHasAcceptedActiveRide: Rider selection, mutual agreement confirmation and Driver acceptance retain the permanent reservation; a Union transfer conflicts without changing membership or releasing the reservation.
- PR #61 automated review found no unresolved critical/high-risk issue. PR #64 final-head CI [42272254259](https://github.com/rajeevp727/turnova-platform/actions/runs/42272254259) executed 189/189 backend tests, including this regression.
- This is deterministic in-memory/API evidence. Live Cosmos multi-instance ETag contention, process restart/crash recovery and approved negotiation-lifetime configuration remain unverified. Keep DISPATCH-04 In progress.
- No customer-facing FIFO ranking or automatic next-driver advancement is introduced. Sequential work remains #11 DRIVER-01.

## REALTIME-01 Rider reconnect audit (2026-11-06)

- PR #63 restores Rider owner-scoped ride history/current state and agreement versions on initial SignalR connection and reconnect; Driver reconnect rehydrates selected/active state using the shared role-specific active-ride selector.
- RideTrackingHubTests covers Rider ownership, assigned Driver ownership, Driver self-only notification group membership and rejection before group join.
- PR #64 final-head CI [42272254259](https://github.com/rajeevp727/turnova-platform/actions/runs/42272254259) executed 189/189 backend tests, including RideTrackingHubTests; native API publish, Rider/Driver Expo typechecks and shared mobile tests passed.
- Physical Android/iOS WebSocket delivery/reconnect, background/resume and device-to-device agreement/status/location checks remain external gates. No live device validation is claimed.

## CI backend-test execution correction (2026-11-06)

- PR #64 merged at `c90699d8fd5e9140d2daccbced106075d86118bb`.
- Final-head CI run [42272254259](https://github.com/rajeevp727/turnova-platform/actions/runs/42272254259) passed all four jobs. The Release backend job executed **189/189 tests**, the TRX non-zero guard passed, and the native non-container API publish/artifact check succeeded.
- Rider and Driver Expo config/typechecks and shared mobile state/refresh tests passed. This restores actual backend test evidence for prior dispatch, agreement and realtime regressions. Live Cosmos, identity-provider, Android/iOS physical-device and production gates remain separate.


## DRIVER-02 availability/location increment (2026-11-07)

- PR #67 is the sequential #12 increment. It serializes Online/Offline transitions and zone-exit mutations with the same per-Driver lease used by selected bookings and Union transfers.
- Going Online requires an approved Cab profile, active Cab-serving Union covering the assigned zone, approved driving licence and vehicle-registration documents, and valid GPS in the assigned zone.
- Driver location tracking is foreground-only; the app pauses sharing during Negotiating, AgreementConfirmed and active rides, stops the watcher in background, then refreshes GPS and restarts the watcher on resume.
- PR #67 final head `a371ae9932e998ee9daaea6fd2a8fdff6e3615c1` passed CI [42286508190](https://github.com/rajeevp727/turnova-platform/actions/runs/42286508190), all four jobs: **193/193 backend tests**, TRX non-zero guard, native non-container API publish/artifact, Rider/Driver Expo config/typechecks and six shared mobile tests. Android/iOS physical-device permission/background/resume and live Cosmos concurrency/ETag validation remain external gates.
- Legacy FIFO queue remains compatibility-only; it does not rank or automatically assign Rider requests. The internal FIFO role remains unresolved.
- The 19-row denominator and weighted score are unchanged: 0 Verified, 16 In progress, 0 Not started, 3 Blocked; 42.1%; strict 0/19.



## AI-01 implementation increment (2026-11-07)

- The Rider app now has a draft-only natural-language trip-intent entry point and a help panel. Draft output is copied into editable fields; the UI explicitly says review is required and no booking/payment occurs.
- The API Application layer defines `IAiAssistantProvider`, explicit request/result contracts, bounded timeout/cancellation, strict output validation and deterministic fallback. The checked-in provider is unavailable and `AiAssistant:Enabled` is false.
- Exact route values are parsed locally. Provider prompts contain only `[PICKUP]` / `[DROPOFF]` placeholders and allow-listed generic tokens; help prompts contain allow-listed keywords, canonical state and approved source IDs only. The API returns exact in-repository help content, not arbitrary provider text.
- Rider-only trip-intent and Rider/Driver help endpoints use per-caller rate limiting. Raw prompt/route/contact/token data is not logged.
- The current branch includes service unit tests and authenticated API integration tests for role boundaries, fallback, provider validation, timeout/cancellation, approved help, unsupported escalation and rate limiting. CI evidence is pending; AI-01 remains In progress until final-head checks and live-provider external validation are recorded.
- The 19-area score remains **42.1%** (0 Verified, 16 In progress, 0 Not started, 3 Blocked; strict **0/19**). AI-01 is supplemental and is not counted in the denominator.
