# TurnOva MVP Progress

**Last updated:** 2026-11-06  
**Scope:** 19 agreed Rider/Driver MVP acceptance areas. Fleet/Ops Phase 2 is excluded.

## 1. Current step

**13/19 — TRIP-01: Driver trip lifecycle, location sharing, trip history and Rider live progress.**  
Current task: review and verify Driver-owned trip history in [PR #48](https://github.com/rajeevp727/turnova-platform/pull/48). Rider-owned history was merged in PR #47; PR #48 adds the Driver API/UI and ownership/privacy tests.

## 2. Overall MVP completion

- **Weighted progress: 36.8%.**
- Formula: `(Verified × 1 + In progress × 0.5 + Not started × 0 + Blocked × 0) ÷ 19 × 100`.
- Counts: **0 Verified, 14 In progress, 0 Not started, 5 Blocked**.
- Calculation: `(0 + 14 × 0.5 + 0 + 0) ÷ 19 × 100 = 36.8%`.
- **Strict fully verified: 0/19.** Weighted implementation progress is not acceptance completion or production readiness.

## 3. Ordered acceptance checklist (1–19)

| # | ID | Status | Current evidence / remaining gate |
|---:|---|---|---|
| 1 | AUTH-01 | In progress | PRs #35, #39 and #40 cover fake-TriSend API contracts, JWT/role boundaries and startup configuration guards; mobile runtime/session and full profile lifecycle remain. |
| 2 | AUTH-02 | In progress | PRs #37 and #41 plus shared mobile refresh tests cover single-flight refresh and retry-after-failure; rotation/reuse, restart, expiry, logout and secure-storage device tests remain. |
| 3 | RIDER-01 | In progress | PR #45 and CI run 42209346759 cover nearby filtering and API boundaries; maps/geocoding and Android/iOS location-permission/resume/device checks remain. |
| 4 | RIDER-02 | Blocked | Fare estimate stays null; requires approved base fare, distance/time components, taxes and service-class rules. |
| 5 | RIDER-03 | In progress | Ride lifecycle, ownership and idempotency tests exist; full transition/cancellation and durable persistence/restart verification remain. |
| 6 | DISPATCH-01 | In progress | Zone and service-class matching implemented; reviewed production boundaries, overlap/cross-zone policy and end-to-end matching remain. |
| 7 | DISPATCH-02 | In progress | Approval, availability, FIFO and queue-head enforcement exist; full eligibility matrix and Cosmos multi-instance verification remain. |
| 8 | DISPATCH-03 | In progress | Durable offers, expiry, retry-safe rejection, next-driver advancement and outbox tests exist; Cosmos restart/ETag/failure injection remains. |
| 9 | DISPATCH-04 | In progress | Per-driver reservation and ride ETag guards exist; cross-partition crash recovery and simultaneous multi-instance acceptance remain. |
| 10 | REALTIME-01 | In progress | SignalR ride/offer flows and reconnect recovery exist; Android/iOS interruption, reconnect and device-to-device checks remain. |
| 11 | DRIVER-01 | In progress | Onboarding and union selfie approval exist; secure document storage/scanning, retention, reviewer RBAC and device tests remain. |
| 12 | DRIVER-02 | In progress | GPS-gated Online, zone exit and queue removal exist; full offer/timeout/re-entry and physical-device checks remain. |
| 13 | TRIP-01 | In progress | Rider history merged in PR #47; Driver-owned history API/UI and ownership/privacy tests are being implemented on `feat/driver-trip-history`. Cosmos history, device-to-device and location-privacy checks remain. |
| 14 | DRIVER-03 | Blocked | Work-metrics PDF exists; monetary earnings need approved compensation, commission and payout policy. |
| 15 | DRIVER-04 | Blocked | Driver support workflow needs approved categories, escalation owner and process. |
| 16 | RIDER-04 | Blocked | Rider history sub-work is in PR #47; payment provider/method, sandbox, webhook/idempotency/refund, receipts and ratings remain. |
| 17 | SUPPORT-01 | Blocked | Rider help/issue-reporting process, categories, escalation owner, API/UI, audit and device acceptance remain. |
| 18 | PLATFORM-01 | In progress | .NET 10 API, health/readiness and native non-container publish are present; real Cosmos, security, observability and load gates remain. |
| 19 | RELEASE-01 | In progress | CI validates backend, native API publish, Expo config/typechecks and shared auth tests; Android/iOS production builds, signed artifacts and physical-device pilot checks remain. |

## 4. Current item evidence and review

- PR #47 merged: https://github.com/rajeevp727/turnova-platform/pull/47; merge commit `32cacbf430e1443835e3bd8e4294c15d3c137fe3`.
- Latest PR #47 head `1c08b96e78bef42c175b29593de153a151382f96` passed all four jobs in CI run https://github.com/rajeevp727/turnova-platform/actions/runs/42214516012.
- Rider history API tests cover owner isolation despite client-supplied riderId, newest-first ordering, response privacy, Rider/Driver role separation and empty history. Rider UI restores the latest active ride, ignores stale responses after Activity unmount/session change, and clears stale active state when history contains no active ride.
- PR #48 is open: https://github.com/rajeevp727/turnova-platform/pull/48. It adds Driver-only `GET /api/v1/driver/rides/mine`, an owner-scoped safe summary, active-trip restoration in Driver Trips UI with SignalR reconnect refresh, and API ownership/privacy/role/empty-history tests.
- Initial CI runs 42215153478 and 42215250054 exposed the same Driver TypeScript issue (`styles.status` did not exist); it was corrected to the existing `styles.label` in commit `213ac477c1868f4768d60938436dd86c742e106d`.
- Driver-history implementation head `5724a3634d8a6c9f7bdbf86cf8ad0c7ecef848db` passed all four jobs in CI run https://github.com/rajeevp727/turnova-platform/actions/runs/42215519184: backend restore/build/tests, native API publish/artifact verification, Rider and Driver Expo config/typechecks, and shared mobile auth tests.
- A final documentation-only reconciliation commit follows that validated implementation head; require its PR CI run to pass before merge.
- Real Cosmos cross-partition query/RU behaviour is not verified by the in-memory API tests.

## 5. Approved supplemental requirement (not counted in the 19 rows)

**AI-01 — Not started.** Provider-agnostic, privacy-safe Rider/Driver assistance: rider natural-language trip intent becomes a human-reviewed draft; explain only approved ride/queue states and in-repo help with escalation; optional driver offer explanation from authoritative API state. No booking, charging, fare setting, KYC approval, FIFO/eligibility override or autonomous ride-state change. Provider/model/credentials remain placeholders. Implement service boundary, validated contracts, timeout/cancellation, redaction, abuse controls, deterministic fallback and mock-based tests in a separate focused increment, updating PRD, architecture, test plan, matrix and PROJECT_BRAIN.md together.

## 6. Next step

Finish automated review of PR #48, wait for fresh CI on its latest head, fix any remaining failures, and merge only with all required checks green. After TRIP-01, record blocked decision-dependent areas and proceed in order to the next actionable acceptance area (PLATFORM-01). Do not start a parallel feature PR.

## 7. Blockers

- Fare policy: base fare, distance/time components, taxes and service-class rules.
- Driver monetary earnings: compensation, commission and payout treatment.
- Payments/receipts/ratings: enabled method/provider, sandbox and webhook/refund policy.
- Driver and Rider support: issue categories, escalation owner and operational workflow.
- Infrastructure-backed verification: development Cosmos access for cross-partition, ETag, partition and multi-instance tests.
- Release evidence: Android/iOS production build/signing and physical devices; maps/geocoding and other provider sandboxes as applicable.
- API hosting remains a post-coding decision; Docker/container builds and deployments are prohibited. No credentials or secrets are stored in this file.
