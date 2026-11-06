# TurnOva MVP Progress

**Last updated:** 2026-11-06  
**Scope:** 19 agreed Rider/Driver MVP acceptance areas. Fleet/Ops Phase 2 is excluded.

## 1. Current step

**18/19 — PLATFORM-01: Secure, observable, reliable API with persistent audit trail.**  
Current task: remove wildcard CORS, enforce an explicit origin allow-list, and keep process liveness distinct from Cosmos-aware readiness; add integration coverage.

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
| 13 | TRIP-01 | In progress | Rider and Driver history code merged in PRs #47 and #48; Cosmos cross-partition history, device-to-device and location-privacy checks remain. |
| 14 | DRIVER-03 | Blocked | Work-metrics PDF exists; monetary earnings need approved compensation, commission and payout policy. |
| 15 | DRIVER-04 | Blocked | Driver support workflow needs approved categories, escalation owner and process. |
| 16 | RIDER-04 | Blocked | Rider history sub-work is in PR #47; payment provider/method, sandbox, webhook/idempotency/refund, receipts and ratings remain. |
| 17 | SUPPORT-01 | Blocked | Rider help/issue-reporting process, categories, escalation owner, API/UI, audit and device acceptance remain. |
| 18 | PLATFORM-01 | In progress | Explicit CORS allow-list and separate liveness/readiness endpoints are being added with integration tests; Cosmos failure injection, security/logging, metrics/traces/audit and load gates remain. |
| 19 | RELEASE-01 | In progress | CI validates backend, native API publish, Expo config/typechecks and shared auth tests; Android/iOS production builds, signed artifacts and physical-device pilot checks remain. |

## 4. Current item evidence and review

- PR #47 merged: https://github.com/rajeevp727/turnova-platform/pull/47; merge commit `32cacbf430e1443835e3bd8e4294c15d3c137fe3`; latest-head CI run 42214516012 passed all four jobs.
- PR #48 merged: https://github.com/rajeevp727/turnova-platform/pull/48; merge commit `2ec84e4eb99b5b1f66fb8b059d71fa36b01c6135`; final-head CI run https://github.com/rajeevp727/turnova-platform/actions/runs/42215718229 passed backend restore/build/tests, native API publish/artifact verification, Rider/Driver Expo config/typechecks and shared auth tests.
- TRIP-01 Rider and Driver history code is merged. The row remains In progress pending Cosmos cross-partition/RU verification, Android/iOS active-trip/device recovery and location-privacy acceptance.
- Current PLATFORM-01 branch: `fix/platform-cors-health`. It removes wildcard CORS, accepts explicit `Cors:AllowedOrigins` origins only (HTTPS except HTTP in Development), rejects wildcard/path/query/userinfo origins, removes the duplicate `/health/ready` mapping, and adds CORS and liveness/readiness integration tests.
- PR #49 is open: https://github.com/rajeevp727/turnova-platform/pull/49. The full eight-file diff is under review; latest-head CI is required before merge.
- Remaining platform gates include Cosmos-unavailable readiness failure injection, production CORS origin configuration, secrets/logging review, metrics/traces/alerts, durable audit failure/recovery tests and load tests.

## 5. Approved supplemental requirement (not counted in the 19 rows)

**AI-01 — Not started.** Provider-agnostic, privacy-safe Rider/Driver assistance: rider natural-language trip intent becomes a human-reviewed draft; explain only approved ride/queue states and in-repo help with escalation; optional driver offer explanation from authoritative API state. No booking, charging, fare setting, KYC approval, FIFO/eligibility override or autonomous ride-state change. Provider/model/credentials remain placeholders. Implement service boundary, validated contracts, timeout/cancellation, redaction, abuse controls, deterministic fallback and mock-based tests in a separate focused increment, updating PRD, architecture, test plan, matrix and PROJECT_BRAIN.md together.

## 6. Next step

Finish the PLATFORM-01 CORS/health slice on `fix/platform-cors-health`, add its focused PR, review the full diff, and merge only with latest-head green CI. Then continue the remaining PLATFORM-01 observability/audit/reliability gaps before RELEASE-01. Do not start a parallel feature PR.

## 7. Blockers

- Fare policy: base fare, distance/time components, taxes and service-class rules.
- Driver monetary earnings: compensation, commission and payout treatment.
- Payments/receipts/ratings: enabled method/provider, sandbox and webhook/refund policy.
- Driver and Rider support: issue categories, escalation owner and operational workflow.
- Infrastructure-backed verification: development Cosmos access for cross-partition, ETag, partition and multi-instance tests.
- Release evidence: Android/iOS production build/signing and physical devices; maps/geocoding and other provider sandboxes as applicable.
- API hosting remains a post-coding decision; Docker/container builds and deployments are prohibited. No credentials or secrets are stored in this file.
