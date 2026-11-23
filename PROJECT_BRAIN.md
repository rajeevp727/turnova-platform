# TurnOva Project Brain — Current Snapshot

**Last updated:** 2026-11-23 (Run 103)

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
- Main: `c9280eec23dd4fb5fab06b11e24d2a619b489235`.
- Sole active feature PR: [#200](https://github.com/rajeevp727/turnova-platform/pull/200), branch `test/api-nondev-support-readiness-20261120`.
- Latest source/test checkpoint: `6996b81b44b8f371b9e114d132319268950da925`; exact API-only CI [#45125685120](https://github.com/rajeevp727/turnova-platform/actions/runs/45125685120) passed 1,319/1,319 backend tests, .NET 10 Release build (0 warnings/errors), native non-container Linux x64 publish/verifier and artifact [14795427939](https://github.com/rajeevp727/turnova-platform/actions/runs/45125685120/artifacts/14795427939).
- Run 103 hardens Driver/Rider reservation resource identity and Driver batch-query ownership; malformed/cross-owner resources fail closed before mutation. No schema, ETag/CAS, lifecycle or FIFO behavior changed.
- PR #200 remains open/non-draft/mergeable/unmerged with three owner-authored COMMENTED reviews, 0 independent approvals and 0 unresolved threads. Do not self-approve or bypass protections.
- PRs #196/#198/#205/#206 are Draft/parked; PR #204 is Android trusted-LAN configuration only. PR #197's historical merge without independent human approval remains disclosed.
- Main's phase metadata is stale (API complete/Android in_progress); the active PR branch phase metadata remains API in_progress and Android/iOS not_started. Do not start Android from main's stale metadata.

## Acceptance and external gates

Strict acceptance remains **0/19 Verified** (16 In Progress, 3 Blocked: DISPATCH-01, DRIVER-04, SUPPORT-01). This is not an implementation percentage. Production readiness is not established.

External gates: live Cosmos ETag/partition/multi-instance/restart and durable Staging/QA; production identity/TriSend/secrets/API hosting/shared rate limiting/trusted proxy; Product-approved dispatch radius/zones/location freshness/negotiation lifetime; maps/routing/SignalR/call/chat/push; private-media storage/scanning/managed identity/consent/retention; support/escalation and reconciliation policy; Union legal approval; later Android/iOS physical-device/signing/store/TestFlight evidence.

## Next exact action

Verify the documentation-bearing PR #200 head's API-only CI, continue API-only code/test/docs audit, obtain independent human approval, and merge only when repository policy permits. Re-read main and perform a fresh API phase-exit audit before Android Rider.

## Historical project notes

The previous long-form project-brain history is preserved byte-for-byte in [PROJECT_BRAIN_HISTORY.md](PROJECT_BRAIN_HISTORY.md). The detailed progress history is preserved in [docs/MVP_PROGRESS_HISTORY.md](docs/MVP_PROGRESS_HISTORY.md).
