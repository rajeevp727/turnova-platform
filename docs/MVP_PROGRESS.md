# TurnOva MVP Progress — Current Snapshot

**Last updated:** 2026-11-23 (Run 108)

## Platform phase gate

- **Backend API:** `in_progress`.
- **Android Rider/Driver:** `not_started`.
- **iOS Rider/Driver:** `not_started`.
- Required order remains API → Android Rider → Android Driver → iOS Rider → iOS Driver. No mobile completion work may begin until API implementation, automated evidence, API documentation, available API acceptance checks, independent review/permitted merge and a fresh main audit satisfy the phase exit criteria.

## Run 108 — current-main reconciliation and iterator-disposal test correction (2026-11-23)

- **Platform phase:** API remains `in_progress`; Android/iOS remain `not_started` for completion work.
- **Main advanced:** `e5366ad58a2f3ff1d39c9c0641fbb74a1d348860`. PR #207 merged native Cosmos account auth and real Rider/Driver sign-in changes at `779939d109216b0d7de6e98735b4b92b1d95671f`, despite zero recorded reviews. This is a historical process exception. Main head CI [#45168231027](https://github.com/rajeevp727/turnova-platform/actions/runs/45168231027) passed 866/866 backend tests (26 warnings, 0 errors); mobile/native Android/emulator jobs passed, iOS jobs skipped. Those results do not complete Android phase and do not validate PR #200's additional API work.
- **PR #200 current head:** [`b3c885b`](https://github.com/rajeevp727/turnova-platform/commit/b3c885b79d4949a63d26b170c27e9abb3e3b9e20), 769 ahead / 6 behind main, merge base `4061357c095debbc31fc5027a7bff27bd8b06265`; GitHub reports `mergeable_state=dirty`. Only conflicting leaf files are `Program.cs` and `AuthApiIntegrationTests.cs`. PR remains open/non-draft; three owner-authored COMMENTED reviews, 0 independent approvals, 0 unresolved threads.
- **CI correction:** PR #200 CI [#45162925132](https://github.com/rajeevp727/turnova-platform/actions/runs/45162925132) on `c9f8801` failed 2/1,345 tests because two nested Moq verifications did not account for `FeedIterator.Dispose(true)`; 1,343 tests passed, Release build succeeded with 0 warnings/0 errors. Test-only commit `b3c885b` explicitly verifies protected iterator disposal in both cases. **No exact-head CI is recorded for b3c885b yet; it is not claimed green.**
- **Cosmos queue/ride-offer read audit:** `CosmosQueueStore` already fails closed on null resources, wrong document Id, wrong DriverId, and mismatched zone/service/partition before mutation; `CosmosRideOfferStore` validates hydrated RideId/DriverId and its notification worker re-reads the current offer before SignalR delivery. The production guards were present. A test-coverage blind spot was found because the prior queue fixture changed both Id and DriverId at once. Commit [95fe65a](https://github.com/rajeevp727/turnova-platform/commit/95fe65aeb411d0beb7fd0d0028fe0680aeb296e4) now independently tests both mismatches for Remove, MoveToBack and MoveToBackOnce (six deterministic cases). This is test-only; no FIFO/dispatch behavior changed. Exact-head CI is pending.
- **API auth gaps introduced by merged PR #207:** registration reservation/user writes are non-transactional and cleanup uses the request cancellation token; login/refresh/logout lack ETag conditional replacement; successful Cosmos auth persistence/rotation/rollback/concurrency tests are missing; public register/login/refresh lack auth-specific rate limiting. These are safe API engineering tasks, not external gates. Existing TriSend accounts are not automatically migrated and require an explicit owner/operations decision.
- Main phase metadata remains stale (`api=complete`, `android=in_progress`); PR #200 branch phase metadata remains `api=in_progress`, Android/iOS `not_started`. Strict acceptance remains **0/19 Verified** (16 In Progress, 3 Blocked: DISPATCH-01, DRIVER-04, SUPPORT-01); no row status changed. Production/live Cosmos readiness is not established.
- **Next:** reconcile main into PR #200 without overwriting either API hardening or Cosmos-native auth; deliberately resolve the two conflicting files, add deterministic native-auth persistence/concurrency/rate-limit tests, rerun exact-head API-only CI and update the contract/progress/matrix. Obtain independent human approval; do not self-approve or merge. Do not start Android until API phase exit and a fresh main audit.
- **Handoff:** [Run 108](handoffs/2026-11-23-run-108.md).

## Latest engineering checkpoint

**COSMOS-MIG-036..043 — migration History/Lock bootstrap response integrity** is implemented in the active API PR.

- CosmosMigrationRunner now validates both MigrationHistory and MigrationLocks creation responses before lease acquisition or migration execution.
- Each response must contain a non-null resource whose container ID matches the requested name, whose partition-key path is exactly /id, and a non-null container handle. Null/mismatched resources and missing handles fail closed.
- Eight deterministic cases cover null resource, wrong ID, wrong partition-key path and missing handle independently for History and Lock containers. Existing cancellation tests now provide correct response metadata and continue to prove cancellation after either create prevents later bootstrap/lease work.
- Source/test implementation: [e3f7f8a](https://github.com/rajeevp727/turnova-platform/commit/e3f7f8a1cf74f08e9865f451625f4f1e104482ff); compile correction: [8a384a2](https://github.com/rajeevp727/turnova-platform/commit/8a384a2c30866a485f2a5c3158d82de5e05b88bd).
- Exact-head API-only CI [#45160544999](https://github.com/rajeevp727/turnova-platform/actions/runs/45160544999) passed **1,342/1,342 backend tests (0 failed, 0 skipped)**; .NET 10 Release build **0 warnings / 0 errors**; native non-container Linux x64 publish **44,005,758 bytes**; publish-script syntax; verifier (11 negative fixtures plus valid fixture); artifact [14809629894](https://github.com/rajeevp727/turnova-platform/actions/runs/45160544999/artifacts/14809629894) (**17,011,781 bytes**).
- Correction transparency: initial CI [#45160375851](https://github.com/rajeevp727/turnova-platform/actions/runs/45160375851) failed compilation because the infrastructure namespace's Database name shadowed the Cosmos SDK type in the new helper. The helper now uses the explicit CosmosDatabase alias; the final exact-head run above passed.
- These are deterministic Cosmos SDK mock-contract tests, not live Cosmos partition/ETag/multi-instance/restart evidence. No endpoint, schema, reservation, ride-state, FIFO or customer-facing dispatch behavior changed.

## Active repository gate

- Live main: e5366ad58a2f3ff1d39c9c0641fbb74a1d348860.
- Sole ready feature PR: [#200](https://github.com/rajeevp727/turnova-platform/pull/200), branch test/api-nondev-support-readiness-20261120.
- Source/test correction commit: b3c885b79d4949a63d26b170c27e9abb3e3b9e20. The documentation-bearing branch checkpoint was 03a3e7f1379d1483ac95ab455ff2c4364da9cb4c before the remaining Run 108 doc sync commits; refresh the live ref before acting. Exact-head CI is pending; the last PR CI #45162925132 failed 2/1,345 tests on prior head c9f8801 and the two test-harness assertions were corrected in b3c885b.
- Compare at Run 108 checkpoint: **775 commits ahead / 6 behind** live main; merge base 4061357c095debbc31fc5027a7bff27bd8b06265; GitHub reports dirty/conflicting. Main-only changes now include PR #207's Cosmos-native auth API and mobile sign-in changes as well as the weekly-build workflow and PR #204 configuration. Resolve Program.cs and AuthApiIntegrationTests.cs deliberately; do not overwrite either side.
- PR #200 remains open, non-draft and unmerged. Three reviews are owner-authored COMMENTED, with **0 independent approvals** and **0 unresolved threads**. Do not self-approve, bypass protection or merge without independent human approval.
- PR #207 was merged at 779939d109216b0d7de6e98735b4b92b1d95671f despite zero recorded reviews. Its native auth code is now on main; recorded gaps remain non-transactional registration cleanup, missing ETag concurrency, missing successful persistence/rotation tests and missing public-auth rate limiting. Treat the merge as a historical process exception; continue API-only work.
- PRs #196/#198/#205/#206 remain Draft/parked. PR #204 is configuration-only, not Android completion evidence. PR #197's historical merge without independent human approval remains disclosed.
- PR branch phase file: API in_progress; Android/iOS not_started. Main phase metadata remains stale (api=complete, android=in_progress) and must not authorize Android work.

## Strict acceptance accounting

**0/19 Verified** (16 In Progress, 3 Blocked: DISPATCH-01, DRIVER-04, SUPPORT-01). Run 107 adds supplemental API migration-bootstrap integrity evidence only; no 19-row acceptance status changed. This is strict full-evidence accounting, not an implementation percentage and not a statement that nothing is implemented.

## Production readiness and external gates

Production readiness is **not established**. Remaining external evidence includes live Cosmos ETag/partition/multi-instance/restart and durable Staging/QA; production identity and account migration decisions, secrets, API hosting, shared auth rate limiting/trusted proxy; Product-approved dispatch radius/zones/location freshness/negotiation lifetime; maps/routing/SignalR/call/chat/push; private-media storage/scanning/managed identity/consent/retention/deletion; support/escalation and reconciliation policy; Union legal approval; and later Android/iOS physical-device, signing, store and TestFlight evidence.

## Next exact actions

1. Synchronize PR #200 with current main and resolve Program.cs/AuthApiIntegrationTests.cs while preserving API hardening and Cosmos-native auth.
2. Add deterministic native-auth persistence, rollback, ETag concurrency and auth rate-limit tests; then run exact-head API-only CI, native publish and verifier.
3. Update API contract, test plan, progress, matrix and phase metadata from actual evidence; obtain independent human approval. Do not self-approve, bypass or merge.
4. After a permitted merge, re-read main and conduct a fresh API phase-exit audit before Android Rider, then Android Driver, iOS Rider and iOS Driver.

## Historical run log

The previous long-form progress log is preserved byte-for-byte in [docs/MVP_PROGRESS_HISTORY.md](MVP_PROGRESS_HISTORY.md). This file is the concise operational snapshot so current progress remains retrievable through GitHub's file API.
