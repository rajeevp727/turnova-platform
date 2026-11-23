# TurnOva Project Brain — Current Snapshot

**Last updated:** 2026-11-23 (Run 106)

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
- Run 106 validates Cosmos migration container identity on every create response, not only TTL-configured containers. TTL read-back identity and missing handle fail closed. Deterministic regression coverage includes mismatched ID, partition key, null resources and null handle.
- Final source/test head [ef6a7f9](https://github.com/rajeevp727/turnova-platform/commit/ef6a7f91f6676353d1ef6a0605d24410043909f7) passed exact-head API-only CI [#45157967922](https://github.com/rajeevp727/turnova-platform/actions/runs/45157967922): **1,334/1,334 tests**, Release build 0 warnings/0 errors, native non-container Linux x64 publish 44,004,574 bytes, verifier 11 negative fixtures plus valid fixture, artifact [14808728990](https://github.com/rajeevp727/turnova-platform/actions/runs/45157967922/artifacts/14808728990) (17,011,239 bytes; SHA-256 `e6cdb0a81cc31ac48ad459d681007519bc6a09544da3437bec38ddfc91447df5`). The initial test attempt [#45157334681](https://github.com/rajeevp727/turnova-platform/actions/runs/45157334681) failed five Moq invocation verifications only; corrected in `ef6a7f9` and disclosed in progress docs.
- Current compare: 757 commits ahead / 2 behind main, merge base `4061357c095debbc31fc5027a7bff27bd8b06265`. The two main-only commits add the weekly-build workflow and PR #204's trusted-LAN Android app configuration; do not merge unrelated mobile changes into the API PR.
- PR #200 remains open/non-draft/mergeable/unmerged. Three owner-authored COMMENTED reviews, 0 independent approvals, 0 unresolved threads. Do not self-approve, bypass protection or merge without independent human approval.
- PR #207 is a Draft candidate for native Cosmos registration/login and removal of demo routes, but mixes API changes with out-of-sequence Rider/Driver mobile changes. It remains parked; review comments record missing successful Cosmos persistence tests, ETag concurrency protection, atomic registration/cleanup and public-auth rate limiting.
- PRs #196/#198/#205/#206 remain Draft/parked; PR #204 is trusted-LAN configuration only. PR #197's historical merge without independent human approval remains disclosed.
- PR branch phase file is API `in_progress`, Android/iOS `not_started`. Main phase metadata is stale and must not authorize Android work.

## Acceptance and external gates

Strict acceptance remains **0/19 Verified** (16 In Progress, 3 Blocked: DISPATCH-01, DRIVER-04, SUPPORT-01). This is not an implementation percentage. Production readiness is not established.

External gates: live Cosmos ETag/partition/multi-instance/restart and durable Staging/QA; production identity and account migration decisions, secrets, API hosting, shared auth rate limiting/trusted proxy; Product-approved dispatch radius/zones/location freshness/negotiation lifetime; maps/routing/SignalR/call/chat/push; private-media storage/scanning/managed identity/consent/retention/deletion; support/escalation and reconciliation policy; Union legal approval; later Android/iOS physical-device/signing/store/TestFlight evidence.

## Next exact action

Continue API-only source/test/docs audit. Next inspect the migration runner's History/Lock container bootstrap response identity checks, then continue the API-only authentication/storage audit. Obtain independent human approval for PR #200; after a permitted merge, re-read main and perform a fresh API phase-exit audit before Android Rider.

## Historical project notes

The previous long-form project-brain history is preserved byte-for-byte in [PROJECT_BRAIN_HISTORY.md](PROJECT_BRAIN_HISTORY.md). The detailed progress history is preserved in [docs/MVP_PROGRESS_HISTORY.md](docs/MVP_PROGRESS_HISTORY.md).
