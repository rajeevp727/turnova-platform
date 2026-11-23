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
- Run 104 hardens Cosmos ride-offer query/point-read identity: requested RideId and candidate DriverId must match hydrated resources; pending-driver query results must belong to the requested Driver. Malformed/blank identity fails closed before an offer is exposed.
- Source/test head `52a4904548ce7581d4eb3976e1529718c03e6c26`; exact API-only CI [#45128278439](https://github.com/rajeevp727/turnova-platform/actions/runs/45128278439) passed **1,324/1,324 tests**, .NET 10 Release build 0 warnings/errors, native non-container Linux x64 publish/verifier and artifact [14796717309](https://github.com/rajeevp727/turnova-platform/actions/runs/45128278439/artifacts/14796717309). These are mocked Cosmos adapter checks, not live Cosmos evidence.
- Current documentation sync requires its own exact-head CI; the source/test CI above is not reused for the documentation-bearing commit.
- PR #200 remains open/non-draft/mergeable/unmerged with three owner-authored COMMENTED reviews, 0 independent approvals and 0 unresolved threads. Do not self-approve or bypass protections.
- PRs #196/#198/#205/#206 are Draft/parked; PR #204 is Android trusted-LAN configuration only. PR #197's historical merge without independent human approval remains disclosed.
- Main's phase metadata is stale (API complete/Android in_progress); the active PR branch phase metadata remains API in_progress and Android/iOS not_started. Do not start Android from main's stale metadata.

## Acceptance and external gates

Strict acceptance remains **0/19 Verified** (16 In Progress, 3 Blocked: DISPATCH-01, DRIVER-04, SUPPORT-01). This is not an implementation percentage. Production readiness is not established.

External gates: live Cosmos ETag/partition/multi-instance/restart and durable Staging/QA; production identity/TriSend/secrets/API hosting/shared rate limiting/trusted proxy; Product-approved dispatch radius/zones/location freshness/negotiation lifetime; maps/routing/SignalR/call/chat/push; private-media storage/scanning/managed identity/consent/retention/deletion; support/escalation and reconciliation policy; Union legal approval; later Android/iOS physical-device/signing/store/TestFlight evidence.

## Next exact action

Verify the documentation-bearing PR #200 head's API-only CI, continue API-only source/test/docs audit, obtain independent human approval, and merge only when repository policy permits. Re-read main and perform a fresh API phase-exit audit before Android Rider.

## Historical project notes

The previous long-form project-brain history is preserved byte-for-byte in [PROJECT_BRAIN_HISTORY.md](PROJECT_BRAIN_HISTORY.md). The detailed progress history is preserved in [docs/MVP_PROGRESS_HISTORY.md](docs/MVP_PROGRESS_HISTORY.md).
