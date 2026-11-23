# TurnOva Project Brain — Current Snapshot

**Last updated:** 2026-11-23 (Run 108)

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
- Live main: e5366ad58a2f3ff1d39c9c0641fbb74a1d348860.
- Sole active feature PR: [#200](https://github.com/rajeevp727/turnova-platform/pull/200), branch test/api-nondev-support-readiness-20261120, source/test correction commit b3c885b79d4949a63d26b170c27e9abb3e3b9e20; Run 108 docs checkpoint 03a3e7f1379d1483ac95ab455ff2c4364da9cb4c before the final progress/matrix sync commits. Compare at that checkpoint was 775 ahead / 6 behind main and GitHub reports dirty; Program.cs and AuthApiIntegrationTests.cs require deliberate reconciliation.
- Run 107 validates both Cosmos migration bootstrap responses before lease acquisition: MigrationHistory and MigrationLocks must each return a non-null resource with exact container ID and /id partition-key path, plus a non-null handle. Eight deterministic regressions cover null resource, wrong ID/path and missing handle for both containers.
- Exact-head API-only CI [#45160544999](https://github.com/rajeevp727/turnova-platform/actions/runs/45160544999) passed **1,342/1,342 backend tests**, Release build 0 warnings/0 errors, native non-container Linux x64 publish 44,005,758 bytes, publish verifier (11 negative fixtures plus valid fixture), and artifact [14809629894](https://github.com/rajeevp727/turnova-platform/actions/runs/45160544999/artifacts/14809629894) (17,011,781 bytes). Initial CI #45160375851 exposed a compile-time namespace/type collision in the new helper; the explicit CosmosDatabase alias correction is in 8a384a2, and the final-head CI passed.
- These are deterministic SDK mock contracts, not live Cosmos ETag/partition/multi-instance/restart evidence.
- Run 108 compare checkpoint: **775 commits ahead / 6 behind** main at docs checkpoint 03a3e7f1379d1483ac95ab455ff2c4364da9cb4c; later docs-only sync commits advanced the branch further. Refresh the live ref before acting. Main includes PR #207's native Cosmos auth API and real sign-in UI as well as earlier workflow/configuration changes. Do not overwrite either PR #200 API hardening or the merged native auth implementation.
- PR #200 remains open/non-draft/unmerged and is currently dirty. Three owner-authored COMMENTED reviews, 0 independent approvals, 0 unresolved threads. Prior CI #45162925132 failed 2/1,345 tests on c9f8801 due two unverified iterator Dispose calls; test-only correction b3c885b adds protected Dispose verification, but exact-head CI is pending. Do not self-approve, bypass protection or merge without independent human approval.
- PR #207 was merged at 779939d109216b0d7de6e98735b4b92b1d95671f despite zero recorded reviews; this is a historical process exception. Main CI #45168231027 passed 866/866 backend tests (26 warnings, 0 errors); its Android build/emulator checks do not satisfy Android phase exit. Native-auth API gaps remain: non-transactional registration cleanup, no ETag conditional refresh/login/logout, missing successful persistence/concurrency tests, and no auth-specific public-route rate limit. PRs #196/#198/#205/#206 remain Draft/parked; PR #204 is configuration-only. PR #197's historical merge without independent human approval remains disclosed.
- PR branch phase file is API in_progress, Android/iOS not_started. Main phase metadata remains stale and must not authorize Android work.

## Acceptance and external gates

Strict acceptance remains **0/19 Verified** (16 In Progress, 3 Blocked: DISPATCH-01, DRIVER-04, SUPPORT-01). This is not an implementation percentage. Production readiness is not established.

External gates: live Cosmos ETag/partition/multi-instance/restart and durable Staging/QA; production identity and account migration decisions, secrets, API hosting, shared auth rate limiting/trusted proxy; Product-approved dispatch radius/zones/location freshness/negotiation lifetime; maps/routing/SignalR/call/chat/push; private-media storage/scanning/managed identity/consent/retention/deletion; support/escalation and reconciliation policy; Union legal approval; later Android/iOS physical-device/signing/store/TestFlight evidence.

## Next exact action

Synchronize main into PR #200, deliberately resolving Program.cs and AuthApiIntegrationTests.cs while preserving API hardening and Cosmos-native auth. Add deterministic auth persistence/rollback/ETag concurrency/rate-limit tests, run exact-head API-only CI and native publish/verifier, then obtain independent human approval. After permitted merge, re-read main and perform a fresh API phase-exit audit before Android Rider.

## Historical project notes

The previous long-form project-brain history is preserved byte-for-byte in [PROJECT_BRAIN_HISTORY.md](PROJECT_BRAIN_HISTORY.md). The detailed progress history is preserved in [docs/MVP_PROGRESS_HISTORY.md](docs/MVP_PROGRESS_HISTORY.md).
