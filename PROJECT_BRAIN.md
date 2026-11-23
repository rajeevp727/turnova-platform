# TurnOva Project Brain — Current Snapshot

**Last updated:** 2026-11-23 (Run 107)

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

- API: in_progress; Android/iOS: not_started.
- Live main: c9280eec23dd4fb5fab06b11e24d2a619b489235.
- Sole active feature PR: [#200](https://github.com/rajeevp727/turnova-platform/pull/200), branch test/api-nondev-support-readiness-20261120, current head 8a384a2c30866a485f2a5c3158d82de5e05b88bd.
- Run 107 validates both Cosmos migration bootstrap responses before lease acquisition: MigrationHistory and MigrationLocks must each return a non-null resource with exact container ID and /id partition-key path, plus a non-null handle. Eight deterministic regressions cover null resource, wrong ID/path and missing handle for both containers.
- Exact-head API-only CI [#45160544999](https://github.com/rajeevp727/turnova-platform/actions/runs/45160544999) passed **1,342/1,342 backend tests**, Release build 0 warnings/0 errors, native non-container Linux x64 publish 44,005,758 bytes, publish verifier (11 negative fixtures plus valid fixture), and artifact [14809629894](https://github.com/rajeevp727/turnova-platform/actions/runs/45160544999/artifacts/14809629894) (17,011,781 bytes). Initial CI #45160375851 exposed a compile-time namespace/type collision in the new helper; the explicit CosmosDatabase alias correction is in 8a384a2, and the final-head CI passed.
- These are deterministic SDK mock contracts, not live Cosmos ETag/partition/multi-instance/restart evidence.
- Current compare: **764 commits ahead / 2 behind** main, merge base 4061357c095debbc31fc5027a7bff27bd8b06265. The two main-only commits are the weekly-build workflow and PR #204's trusted-LAN Android app configuration; do not merge unrelated mobile changes into the API PR.
- PR #200 remains open/non-draft/mergeable/unmerged. Three owner-authored COMMENTED reviews, 0 independent approvals, 0 unresolved threads. Do not self-approve, bypass protection or merge without independent human approval.
- PR #207 is Draft/parked because it mixes native Cosmos authentication API work with out-of-sequence mobile changes; recorded review findings include missing successful Cosmos persistence tests, ETag concurrency, atomic registration/cleanup and public-auth rate limiting. PRs #196/#198/#205/#206 remain Draft/parked; PR #204 is configuration-only. PR #197's historical merge without independent human approval remains disclosed.
- PR branch phase file is API in_progress, Android/iOS not_started. Main phase metadata remains stale and must not authorize Android work.

## Acceptance and external gates

Strict acceptance remains **0/19 Verified** (16 In Progress, 3 Blocked: DISPATCH-01, DRIVER-04, SUPPORT-01). This is not an implementation percentage. Production readiness is not established.

External gates: live Cosmos ETag/partition/multi-instance/restart and durable Staging/QA; production identity and account migration decisions, secrets, API hosting, shared auth rate limiting/trusted proxy; Product-approved dispatch radius/zones/location freshness/negotiation lifetime; maps/routing/SignalR/call/chat/push; private-media storage/scanning/managed identity/consent/retention/deletion; support/escalation and reconciliation policy; Union legal approval; later Android/iOS physical-device/signing/store/TestFlight evidence.

## Next exact action

Continue API-only Cosmos persistence/auth/privacy/contract audit. Next inspect CosmosQueueStore rotation reads and CosmosRideOfferStore point reads for null/mismatched successful Resource handling, adding deterministic cases only for confirmed gaps. Keep PR #207 Draft. Obtain independent human approval for PR #200; after a permitted merge, re-read main and perform a fresh API phase-exit audit before Android Rider.

## Historical project notes

The previous long-form project-brain history is preserved byte-for-byte in [PROJECT_BRAIN_HISTORY.md](PROJECT_BRAIN_HISTORY.md). The detailed progress history is preserved in [docs/MVP_PROGRESS_HISTORY.md](docs/MVP_PROGRESS_HISTORY.md).
