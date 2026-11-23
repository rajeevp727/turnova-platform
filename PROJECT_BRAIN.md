# TurnOva Project Brain — Current Snapshot

**Last updated:** 2026-11-23 (Run 104)

## Product and scope

TurnOva is a union-connected, Cab-only Rider/Driver MVP. Fleet/Ops Phase 2 is excluded. Rider discovery is grouped by active approved Cab-serving unions and shows up to 2–3 eligible, available, union-approved Cab drivers per union, ordered by pickup proximity. Riders explicitly select; no customer-facing FIFO ranking or automatic next-driver assignment. Only union-approved selfies and verified Cab details may be public.

Rider and selected Driver negotiate directly. Agreements are durable, immutable/versioned, record amount/currency/route/stops/timestamps and both confirmations; renegotiation requires both parties to reconfirm. No platform fare estimate/recommendation, payment gateway, escrow or in-app collection. After completion the Driver reports amount received directly; agreed vs received delta is recorded as unverified reconciliation evidence. Do not invent customer disputes/confirmation or non-zero-delta next-ride policy.

Unions own approved membership, Cab vehicle/document verification and selfie approval. Union oversight must be least-privilege. No private chat or unrelated customer data. AI-01 is supplemental only: provider-agnostic boundary, safe local fallback, approved FAQ/help, editable trip-intent drafts; AI cannot book, price, approve KYC or change eligibility/ride state.

## Stack and delivery order

- Backend: .NET 10 API, Azure Cosmos DB, SignalR, JWT.
- Mobile: React Native + Expo Rider and Driver apps.
- No Docker/container builds, images, registries or deployments.
- Strict platform order: API → Android Rider → Android Driver → iOS Rider → iOS Driver. External gates do not stop safe in-phase code/test/docs work.

## Current platform phase and repository

- API: `in_progress`; Android/iOS: `not_started`.
- Live main: `c9280eec23dd4fb5fab06b11e24d2a619b489235`.
- Sole active feature PR: [#200](https://github.com/rajeevp727/turnova-platform/pull/200), branch `test/api-nondev-support-readiness-20261120`.
- Run 105 hardens Cosmos canonical point-read identity: queue-audit reads must match requested EventId and ZoneServiceKey; support-request conflict read-back must match requested Id and ReporterId. Four deterministic regression cases fail closed on mismatches.
- Source/test head [48d476e](https://github.com/rajeevp727/turnova-platform/commit/48d476e2cfdf81c69693b6ce99022b31e1ed3b8a) passed exact-head API-only CI [#45133278455](https://github.com/rajeevp727/turnova-platform/actions/runs/45133278455): 1,328/1,328 tests, Release build 0 warnings/0 errors, native non-container Linux x64 publish 44,003,478 bytes, verifier 11 negative fixtures plus valid fixture, artifact [14798144808](https://github.com/rajeevp727/turnova-platform/actions/runs/45133278455/artifacts/14798144808) (17,011,072 bytes; SHA-256 `4d090738f386a4caff8d3fcf4a696ccb280169d97b759796102ab3bce19074cf`). Documentation checkpoint [1eb4eea](https://github.com/rajeevp727/turnova-platform/commit/1eb4eeac45debba2264b8a09caf9c518b2a81454) passed exact-head API-only CI [#45133946707](https://github.com/rajeevp727/turnova-platform/actions/runs/45133946707): 1,328/1,328 tests, Release build 0 warnings/0 errors, native non-container publish/verifier and artifact [14798698718](https://github.com/rajeevp727/turnova-platform/actions/runs/45133946707/artifacts/14798698718) (17,011,081 bytes; SHA-256 `29aed04f7faded6877add276b5113199fccdb975d243d458b08ef9887b3765bb`). Final Run 105 status-sync head [68633b8](https://github.com/rajeevp727/turnova-platform/commit/68633b8bef9af4605daa4cdfa9ed9a8a2b203829) passed exact-head API-only CI [#45134546211](https://github.com/rajeevp727/turnova-platform/actions/runs/45134546211): 1,328/1,328 tests, Release build 0 warnings/0 errors, native non-container publish/verifier, artifact [14799726071](https://github.com/rajeevp727/turnova-platform/actions/runs/45134546211/artifacts/14799726071) (17,011,091 bytes; SHA-256 `df66263ad8fb5263ae4a56951b0293466e823c845ef2f4d3173f8f7d45843504`). These are mocked Cosmos adapter checks, not live Cosmos evidence.
- Current compare: 750 commits ahead / 2 behind main, merge base `4061357c095debbc31fc5027a7bff27bd8b06265`. The two main-only commits add `.github/workflows/weekly-build.yml` and PR #204's trusted-LAN Android app configuration; do not merge unrelated mobile changes into the API PR.
- PR #200 remains open/non-draft/mergeable/unmerged. Three owner-authored COMMENTED reviews, 0 independent approvals, 0 unresolved threads. Do not self-approve, bypass protection or merge without independent human approval.
- PRs #196/#198/#205/#206 remain Draft/parked; PR #204 is Android trusted-LAN configuration only. PR #197's historical merge without independent human approval remains disclosed.
- PR branch phase file is API `in_progress`, Android/iOS `not_started`. Main phase metadata is stale and must not authorize Android work.

## Acceptance and external gates

Strict acceptance remains **0/19 Verified** (16 In Progress, 3 Blocked: DISPATCH-01, DRIVER-04, SUPPORT-01). This is not an implementation percentage. Production readiness is not established.

External gates: live Cosmos ETag/partition/multi-instance/restart and durable Staging/QA; production identity/TriSend/secrets/API hosting/shared rate limiting/trusted proxy; Product-approved dispatch radius/zones/location freshness/negotiation lifetime; maps/routing/SignalR/call/chat/push; private-media storage/scanning/managed identity/consent/retention/deletion; support/escalation and reconciliation policy; Union legal approval; later Android/iOS physical-device/signing/store/TestFlight evidence.

## Next exact action

Verify the documentation-bearing PR #200 head's API-only CI, continue API-only source/test/docs audit, obtain independent human approval, and merge only when repository policy permits. Re-read main and perform a fresh API phase-exit audit before Android Rider.

## Historical project notes

The previous long-form project-brain history is preserved byte-for-byte in [PROJECT_BRAIN_HISTORY.md](PROJECT_BRAIN_HISTORY.md). The detailed progress history is preserved in [docs/MVP_PROGRESS_HISTORY.md](docs/MVP_PROGRESS_HISTORY.md).
