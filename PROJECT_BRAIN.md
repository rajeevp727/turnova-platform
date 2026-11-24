# PROJECT BRAIN — TurnOva

Last updated: 2026-11-24 (Run 127)

## Platform phase

- API: `in_progress`
- Android: `not_started`
- iOS: `not_started`
- Required order: API → Android Rider → Android Driver → iOS Rider → iOS Driver.
- No Android completion work is authorized until API implementation, deterministic evidence, API contract/docs, available API acceptance checks, independent human review/permitted merge and a fresh main/API phase-exit audit pass.

## Run 127 follow-on — preserve cancellation during in-memory Driver bulk reservation reads (2026-11-24)

- **Platform phase:** API `in_progress`; Android/iOS `not_started`. No mobile completion work started.
- **Audit finding:** `InMemoryDriverRideReservationStore.GetActiveForDriversAsync` validated a null Driver-ID collection before checking cancellation, unlike the Cosmos adapter. It then used LINQ materialization without observing cancellation during input enumeration or the in-memory reservation snapshot.
- **Implementation:** cancellation now takes precedence over null validation; the method checks the token for each requested Driver ID, after input enumeration, for each reservation dictionary entry and immediately before returning the immutable array snapshot. Ordinal ID matching and existing expired-lease visibility are preserved.
- **Deterministic regressions:** four tests cover pre-cancelled null input, ordinary null rejection, cancellation during requested-ID enumeration and cancellation at the end of enumeration before snapshot creation.
- **Exact-head API-only CI:** [#45346736464](https://github.com/rajeevp727/turnova-platform/actions/runs/45346736464) passed **1,380/1,380 backend tests (0 failed, 0 skipped)**; .NET 10 Release build (0 warnings/0 errors); native non-container Linux x64 framework-dependent publish (44,028,780 bytes); publish-script syntax and verifier (11 negative fixtures plus valid fixture). Artifact [14899006505](https://github.com/rajeevp727/turnova-platform/actions/runs/45346736464/artifacts/14899006505), 17,019,258 bytes, SHA-256 `3897bc9f2b5d2362ff9e8ed9118ee55dca0108d21e094362280ecc5d6db5928a`.
- **Documentation checkpoint:** prior docs head [`494ccd75b9994ca4bbf79d21177aa3092d258414`](https://github.com/rajeevp727/turnova-platform/commit/494ccd75b9994ca4bbf79d21177aa3092d258414) passed exact-head API-only CI [#45346235425](https://github.com/rajeevp727/turnova-platform/actions/runs/45346235425): 1,376/1,376 tests, Release build (0 warnings/0 errors), native non-container publish/verifier, artifact [14898815658](https://github.com/rajeevp727/turnova-platform/actions/runs/45346235425/artifacts/14898815658). The current documentation sync requires its own exact-head CI.
- **Acceptance:** strict acceptance remains **0/19 Verified** (16 In Progress, 3 Blocked: `DISPATCH-01`, `DRIVER-04`, `SUPPORT-01`). This is full-evidence accounting, not an implementation percentage. In-memory deterministic tests do not establish live Cosmos/production readiness.
- **Review/phase gate:** PR #200 remains open/non-draft/mergeable. Three owner-authored `COMMENTED` reviews, **0 independent approvals**, 0 unresolved threads. PRs #196/#198/#205/#206 remain Draft/parked; PRs #197/#207 remain disclosed historical merges without independent approval. Do not self-approve, bypass protection or merge.
- **Scope/build policy:** API-only scope/backend passed; mobile-auth/mobile/Android/emulator/iOS jobs were intentionally skipped. No mobile files, Dockerfiles, container CI, images, registries or deployments were added.
- **External gates:** live Cosmos ETag/transaction/multi-instance/restart and partition/RU evidence; production identity/secrets/hosting/live TriSend; Product-approved radius/zones/location freshness/negotiation lifetime/internal FIFO fairness; maps/routing, SignalR/call/chat/push; private-media storage/scanning/consent/retention; support/escalation and reconciliation policies; Union legal approval; Android/iOS physical-device, signing, store and TestFlight evidence.

**Next exact actions:** verify the documentation-bearing head's own exact-head API-only CI; refresh main/PR/reviews; continue API-only Cosmos persistence/idempotency/authorization/privacy audits; obtain independent human review and approval. After a permitted approved merge, re-read main and perform a fresh API phase-exit audit before Android Rider → Android Driver → iOS Rider → iOS Driver.

## Run 127 — honor cancellation after waiting for in-memory queue mutation locks (2026-11-24)

- **Platform phase:** API `in_progress`; Android/iOS `not_started`. No mobile completion work started.
- **Main / PR:** main `e5366ad58a2f3ff1d39c9c0641fbb74a1d348860`; sole active API PR [#200](https://github.com/rajeevp727/turnova-platform/pull/200), branch `test/api-nondev-support-readiness-20261120`; source/test head [`8fb9738aaa320b9feb50bc578589a0e363302e0a`](https://github.com/rajeevp727/turnova-platform/commit/8fb9738aaa320b9feb50bc578589a0e363302e0a).
- **Audit finding:** in-memory queue `RemoveAsync`, `MoveToBackAsync` and `MoveToBackOnceAsync` checked cancellation before looking up the per-queue lock, but not after waiting to acquire it. A request cancelled while contending for that lock could still remove/rotate membership and append an audit event after the caller had cancelled.
- **Implementation:** all three methods now check cancellation immediately after acquiring the queue lock and before reading/mutating membership, rotation markers or audit history. Existing pre-lock checks remain. Valid queue ordering and idempotent offer-scoped rotation semantics are unchanged.
- **Deterministic regressions:** three tests deliberately hold the private per-queue lock, wait until a dedicated worker is blocked on it, cancel the operation, then release the lock. They assert cancellation is propagated and membership order plus audit count remain unchanged for remove, ordinary move-to-back and offer-scoped move-to-back.
- **Exact-head API-only CI:** [#45345681104](https://github.com/rajeevp727/turnova-platform/actions/runs/45345681104) passed **1,376/1,376 backend tests (0 failed, 0 skipped)**; .NET 10 Release build (0 warnings/0 errors); native non-container Linux x64 framework-dependent publish (44,029,176 bytes); publish-script syntax and verifier (11 negative fixtures plus valid fixture). Artifact [14897409213](https://github.com/rajeevp727/turnova-platform/actions/runs/45345681104/artifacts/14897409213), 17,019,378 bytes, SHA-256 `35e48fdb4ff0c53e97f6a2b23e33b0b260e80bab5781316935f8d5b43b336b5e`.
- **Acceptance:** strict acceptance remains **0/19 Verified** (16 In Progress, 3 Blocked: `DISPATCH-01`, `DRIVER-04`, `SUPPORT-01`). This is full-evidence accounting, not an implementation percentage. Deterministic in-memory tests do not establish live Cosmos or production readiness.
- **Review/phase gate:** PR #200 remains open/non-draft/mergeable. Three owner-authored `COMMENTED` reviews, **0 independent approvals**, and 0 unresolved review threads. PRs #196/#198/#205/#206 remain Draft/parked. PRs #197/#207 are disclosed historical merges without independent human approval; those exceptions are not precedent. Do not self-approve, bypass protection or merge.
- **Build/scope policy:** API-only scope/backend passed. Mobile-auth/mobile/Android/emulator/iOS jobs were intentionally skipped under API-first gating. No mobile app files, Dockerfiles, container CI, images, registries or deployments were added.
- **External gates remain:** live Cosmos ETag/transaction/multi-instance/restart and partition/RU evidence; production identity/secrets/hosting/live TriSend; Product-approved radius/zones/location freshness/negotiation lifetime/internal FIFO fairness; maps/routing and SignalR/call/chat/push; private-media storage/scanning/consent/retention; support/escalation and reconciliation policies; Union legal approval; Android/iOS physical-device, signing, store and TestFlight evidence.

**Next exact actions:** verify the documentation-bearing head's own exact-head API-only CI; refresh main/PR/reviews; continue API-only Cosmos persistence/idempotency/authorization/privacy audits; obtain independent human review and approval. After a permitted approved merge, re-read main and perform a fresh API phase-exit audit before Android Rider → Android Driver → iOS Rider → iOS Driver.


## Run 126 — queue cancellation and input-contract parity (2026-11-24)

- **Platform phase:** API `in_progress`; Android/iOS `not_started`. No mobile completion work started.
- **Main / PR:** main `e5366ad58a2f3ff1d39c9c0641fbb74a1d348860`; sole active feature PR [#200](https://github.com/rajeevp727/turnova-platform/pull/200), branch `test/api-nondev-support-readiness-20261120`; source/test head [`934a39ed60a8329b9fb2733257482b2b66144257`](https://github.com/rajeevp727/turnova-platform/commit/934a39ed60a8329b9fb2733257482b2b66144257).
- **Queue adapter cancellation:** `InMemoryQueueStore.GetEligibleAsync` now observes cancellation before lookup and again after acquiring the queue lock. `AddAsync` observes cancellation before input handling and again after acquiring the lock, so a caller cancelled while waiting does not add a queue entry or audit event.
- **Queue add input parity:** in-memory and Cosmos `AddAsync` reject null `DriverQueueEntry` with `ArgumentNullException` after the cancellation-first check and before queue/audit/Cosmos mutation. `QueueService.JoinAsync` now checks cancellation and null before dereferencing the entry.
- **Queue service cancellation precedence:** `JoinAsync`, `MoveToBackAsync`, `LeaveAsync` and `RecordDispatchExceptionAsync` observe cancellation before argument validation. This does not alter valid queue ordering, FIFO fairness compatibility, Cab discovery ranking or dispatch/assignment policy.
- **Deterministic regressions:** 11 tests cover in-memory eligible-read cancellation; in-memory add cancellation/no queue or audit mutation; null add and cancellation precedence in both adapters; QueueService null/cancellation handling for Join; and cancellation-before-validation for MoveToBack, Leave and dispatch-exception auditing.
- **Exact-head API-only CI:** [#45338387286](https://github.com/rajeevp727/turnova-platform/actions/runs/45338387286) passed **1,373/1,373 backend tests (0 failed, 0 skipped)**; .NET 10 Release build succeeded with **0 warnings / 0 errors**; native non-container Linux x64 publish succeeded (44,029,168 bytes); publish-script syntax and verifier passed (11 negative fixtures plus valid fixture). Artifact [14894956714](https://github.com/rajeevp727/turnova-platform/actions/runs/45338387286/artifacts/14894956714), 17,019,387 bytes, SHA-256 `bd4c24dd39290f8caf586977bd66f54fcf58bff899901db694657d45fae5fa86`.
- **Prior Run 125 documentation CI:** [#45330985445](https://github.com/rajeevp727/turnova-platform/actions/runs/45330985445) passed on docs head `0d222dfac69b1a05834904b1d58f3d6382191e90`: 1,362/1,362 tests, Release build (0 warnings/0 errors), native non-container publish/verifier, artifact [14892316388](https://github.com/rajeevp727/turnova-platform/actions/runs/45330985445/artifacts/14892316388) (17,019,352 bytes; SHA-256 `2bd006113e396d71e54a0e58f14bbe32423ee8f2178e621796fb407b73abfadc`).
- **Acceptance:** strict acceptance remains **0/19 Verified** (16 In Progress, 3 Blocked: `DISPATCH-01`, `DRIVER-04`, `SUPPORT-01`). No row status changed; deterministic mocks do not establish live Cosmos/production acceptance.
- **Review/phase gate:** PR #200 remains the sole ready/active API PR. Independent human approval is still absent; owner-authored COMMENTED reviews are not approval. PRs #196/#198/#205/#206 remain Draft/parked. Do not self-approve, bypass protections or merge.
- **Build/scope policy:** scope/backend passed; mobile-auth, mobile, Android/emulator and iOS/simulator jobs were intentionally skipped under API-first gating. No mobile files, Dockerfiles, container CI, images, registries or deployments were added.
- **External gates remain:** live Cosmos ETag/transaction/multi-instance/restart and partition/RU validation; production identity, secrets, API hosting and live TriSend; Product-approved radius/zones/location freshness/negotiation lifetime and internal FIFO fairness; maps/routing, SignalR/call/chat/push; private-media provider/scanning/consent/retention; support/escalation and reconciliation policies; Union legal approval; Android/iOS device, signing, store and TestFlight evidence.

**Next exact actions:** verify the documentation-bearing head's own exact-head CI; refresh PR/main/reviews; continue API-only Cosmos persistence/idempotency/authorization/privacy audits; obtain independent human review and approval. After a permitted approved merge, re-read main and perform a fresh API phase-exit audit before Android Rider → Android Driver → iOS Rider → iOS Driver.

## Run 125 — preserve cancellation through empty identity queries and validate Cosmos ride-offer writes (2026-11-24)

- **Phase:** API `in_progress`; Android/iOS `not_started`. No mobile completion work started.
- **Main / PR:** main `e5366ad58a2f3ff1d39c9c0641fbb74a1d348860`; sole active feature PR [#200](https://github.com/rajeevp727/turnova-platform/pull/200), branch `test/api-nondev-support-readiness-20261120`; source/test head [`ebeea7e8d6c2daa166bd2dc0cb9624ef5e258c21`](https://github.com/rajeevp727/turnova-platform/commit/ebeea7e8d6c2daa166bd2dc0cb9624ef5e258c21), 945 commits ahead / 0 behind main.
- **Identity cancellation audit:** `CosmosIdentityService.LoginAsync` now checks cancellation after an identity query completes before translating an empty result into HTTP 401. `RefreshAsync` checks cancellation before starting the next Rider/Driver role query, before invalid-token mapping after malformed identity data, and after both role scans before the final HTTP 401. This prevents a cancelled request from continuing the second role scan or being reported as invalid credentials/token.
- **Ride-offer store contract:** `CosmosRideOfferStore.TryCreateAsync` and `TryReplaceAsync` now reject a null `RideOfferState` with `ArgumentNullException` after the cancellation check and before Cosmos access, matching the existing in-memory adapter contract. Pre-cancelled null inputs still propagate cancellation first.
- **Deterministic tests:** three identity-query cancellation cases and two Cosmos ride-offer null-state cases were added; the existing pre-cancelled store test also covers null create/replace inputs. Tests assert no point reads, writes or second-role query after cancellation and no Cosmos invocation for null write state.
- **Exact-head API-only CI:** [#45330522014](https://github.com/rajeevp727/turnova-platform/actions/runs/45330522014) passed **1,362/1,362 backend tests (0 failed, 0 skipped)**; .NET 10 Release build succeeded with **0 warnings / 0 errors**; native non-container Linux x64 framework-dependent publish succeeded (44,029,120 bytes); publish-script syntax and verifier passed (11 negative fixtures plus valid fixture). Artifact [14891109147](https://github.com/rajeevp727/turnova-platform/actions/runs/45330522014/artifacts/14891109147), 17,019,321 bytes, SHA-256 `a0846efbbeb67771facdabd32dfdca4c62fad4e4b1d3e7970453b279c721165e`.
- **Scope gate:** scope/backend passed. Mobile-auth, mobile, Android/emulator and iOS/simulator jobs were intentionally skipped under API-first gating. PR diff remains API/docs only: 67 backend files, 123 documentation files and one phase file; zero mobile paths and zero container files.
- **Acceptance:** strict acceptance remains **0/19 Verified** (16 In Progress, 3 Blocked: `DISPATCH-01`, `DRIVER-04`, `SUPPORT-01`). No acceptance row is promoted to Verified by mock-backed unit tests. Live Cosmos and production readiness are not established.
- **Review gate:** PR #200 remains open/non-draft/mergeable with three owner-authored `COMMENTED` reviews, **0 independent approvals** and 0 unresolved review threads. PRs #196/#198/#205/#206 remain Draft/parked. PRs #197/#207 remain disclosed historical merges without independent approval; those exceptions are not precedent.
- **Next:** verify exact-head CI for this documentation commit; synchronize the PR description; obtain an independent human review and approval. Continue API-only persistence/idempotency/Cosmos adapter audits while review is pending. Do not merge or start Android without the required approval, permitted merge and fresh main/API phase-exit audit.

## Run 124 — Cosmos identity cancellation-first input validation

- Main: `e5366ad58a2f3ff1d39c9c0641fbb74a1d348860`.
- Sole active API PR: [#200](https://github.com/rajeevp727/turnova-platform/pull/200), branch `test/api-nondev-support-readiness-20261120`; source/test head [`3bd4d8628adeafc56e18c5ff0625881022e43f36`](https://github.com/rajeevp727/turnova-platform/commit/3bd4d8628adeafc56e18c5ff0625881022e43f36).
- `CosmosIdentityService` now honors cancellation before Register/Login/Refresh/Logout input validation; null Register/Login requests and null Logout principal fail fast before Cosmos access.
- Seven deterministic tests cover pre-cancelled null inputs and uncancelled null reference inputs.
- Exact-head API-only CI [#45323065686](https://github.com/rajeevp727/turnova-platform/actions/runs/45323065686) passed 1,357/1,357 tests, Release build (0 warnings/0 errors), native non-container Linux x64 publish and verifier; artifact [14887449998](https://github.com/rajeevp727/turnova-platform/actions/runs/45323065686/artifacts/14887449998).
- **Documentation-bearing head:** [`c2226f9cc285f14f7405f6bfd76bd402efb05284`](https://github.com/rajeevp727/turnova-platform/commit/c2226f9cc285f14f7405f6bfd76bd402efb05284) passed its own exact-head API-only CI [#45323615316](https://github.com/rajeevp727/turnova-platform/actions/runs/45323615316): **1,357/1,357 tests**, Release build (0 warnings/0 errors), native non-container Linux x64 publish (44,029,088 bytes), publish-script syntax and verifier (11 negative fixtures plus valid fixture). Artifact [14889125395](https://github.com/rajeevp727/turnova-platform/actions/runs/45323615316/artifacts/14889125395), 17,019,302 bytes, SHA-256 `dfb8c52fd55d62423a7accfcf7642c30fcd5626920710a185cbfc7a5d9f85705`.
- API remains `in_progress`; Android/iOS remain `not_started`. Strict acceptance remains 0/19 Verified (16 In Progress, 3 Blocked). Live Cosmos and production readiness are not established.
- PR #200 has no independent human approval; never self-approve or bypass protection. Historical PR #197/#207 merges without independent approval remain disclosed exceptions.

## Run 123 — Rider reservation null-input cancellation precedence

- Main: `e5366ad58a2f3ff1d39c9c0641fbb74a1d348860`.
- Sole active API PR: [#200](https://github.com/rajeevp727/turnova-platform/pull/200), branch `test/api-nondev-support-readiness-20261120`; source/test head [`3eb9b49b188e9f48595c966d7b189ce5a7853492`](https://github.com/rajeevp727/turnova-platform/commit/3eb9b49b188e9f48595c966d7b189ce5a7853492).
- Cosmos and in-memory Rider reservation acquire now check cancellation before null-reservation validation. Four deterministic tests cover pre-cancelled/null and uncancelled/null behavior with no Cosmos call or in-memory mutation.
- Exact-head API-only CI [#45316862617](https://github.com/rajeevp727/turnova-platform/actions/runs/45316862617) passed 1,350/1,350 backend tests, Release build (0 warnings/0 errors), native non-container Linux x64 publish and verifier; artifact [14886790068](https://github.com/rajeevp727/turnova-platform/actions/runs/45316862617/artifacts/14886790068).
- API remains `in_progress`; Android/iOS remain `not_started`. Strict acceptance remains 0/19 Verified (16 In Progress, 3 Blocked). Live Cosmos and production readiness are not established.
- PR #200 still has no independent human approval; never self-approve or bypass protection. Historical PR #197/#207 merges without independent approval remain disclosed exceptions.


## Run 122 — bulk Driver reservation query cancellation precedence

- Main: `e5366ad58a2f3ff1d39c9c0641fbb74a1d348860`.
- Sole active API PR: [#200](https://github.com/rajeevp727/turnova-platform/pull/200), branch `test/api-nondev-support-readiness-20261120`; source/test head [`e4515555c3d51b47533cc91fc5357597cddc9a8a`](https://github.com/rajeevp727/turnova-platform/commit/e4515555c3d51b47533cc91fc5357597cddc9a8a).
- `CosmosDriverRideReservationStore.GetActiveForDriversAsync` now checks cancellation before validating a null DriverId collection, matching the cancellation-first contract of the other reservation operations. Valid query batching/filtering is unchanged.
- Two deterministic tests prove a pre-cancelled/null request yields OperationCanceledException and an uncancelled/null request yields ArgumentNullException; neither calls Cosmos.
- Exact-head API-only CI [#45316137669](https://github.com/rajeevp727/turnova-platform/actions/runs/45316137669) passed 1,346/1,346 tests, Release build (0 warnings/0 errors), native non-container publish and verifier; artifact [14885432455](https://github.com/rajeevp727/turnova-platform/actions/runs/45316137669/artifacts/14885432455).
- API remains `in_progress`; Android/iOS remain `not_started`. Strict acceptance remains 0/19 Verified (16 In Progress, 3 Blocked). No live Cosmos or production readiness is claimed.
- PR #200 still has no independent human approval; never self-approve or bypass protection. Historical PR #197/#207 merges without independent approval remain disclosed exceptions.


## Run 121 — reservation identity validation across read/mutation paths

- Main: `e5366ad58a2f3ff1d39c9c0641fbb74a1d348860`.
- Sole active API PR: [#200](https://github.com/rajeevp727/turnova-platform/pull/200), branch `test/api-nondev-support-readiness-20261120`, source/test head [`0050aab3ecd76c0b82289737380b196e5cd2ffdc`](https://github.com/rajeevp727/turnova-platform/commit/0050aab3ecd76c0b82289737380b196e5cd2ffdc).
- Driver Cosmos/in-memory non-acquire GetActive, TryMakePermanent, TrySetExpiry and TryRelease now reject blank/whitespace DriverId/RideId. Rider Cosmos/in-memory GetActive, TryMakePermanent and TryRelease reject blank/whitespace RiderId/RideId. Cancellation remains first; valid ETag/expiry/idempotency/audit and bulk-query semantics are unchanged.
- 52 deterministic tests cover empty/whitespace owner/ride IDs, no Cosmos calls/no in-memory mutation and pre-cancelled-token precedence.
- Exact-head API-only CI [#45308873205](https://github.com/rajeevp727/turnova-platform/actions/runs/45308873205) passed 1,344/1,344 backend tests, Release build (0 warnings/0 errors), native non-container Linux x64 publish and verifier (11 negative fixtures plus valid fixture); artifact [14882551446](https://github.com/rajeevp727/turnova-platform/actions/runs/45308873205/artifacts/14882551446).
- API remains `in_progress`; Android/iOS remain `not_started`. Strict acceptance remains 0/19 Verified (16 In Progress, 3 Blocked). Live Cosmos and production readiness are not established. PR #200 still has no independent human approval; never self-approve or bypass protection.

## Run 120 — reservation create-conflict readback retry

- Main: `e5366ad58a2f3ff1d39c9c0641fbb74a1d348860`.
- Sole active API PR: [#200](https://github.com/rajeevp727/turnova-platform/pull/200), branch `test/api-nondev-support-readiness-20261120`, source/test head [`b77dc442ddc13eb0e87d231476d05e13789700b7`](https://github.com/rajeevp727/turnova-platform/commit/b77dc442ddc13eb0e87d231476d05e13789700b7), 914 ahead / 0 behind.
- Added Driver/Rider Cosmos acquisition tests for create-batch 409 followed by conflict-readback 404; both bounded retry loops may retry the atomic reservation+audit create when the conflicting record disappeared.
- Exact-head API-only CI [#45302547873](https://github.com/rajeevp727/turnova-platform/actions/runs/45302547873) passed 1,274/1,274 tests, Release build (0 warnings/0 errors), native non-container Linux x64 publish and verifier; artifact [14879496807](https://github.com/rajeevp727/turnova-platform/actions/runs/45302547873/artifacts/14879496807).
- API remains `in_progress`; Android/iOS remain `not_started`. Strict acceptance remains 0/19 Verified (16 In Progress, 3 Blocked). No production Cosmos or hosting readiness is claimed.
- PR #200 still has no independent human approval; never self-approve, bypass protections or merge. Historical PR #197/#207 merges without independent approval remain disclosed exceptions.

## Run 120 follow-on — reservation owner/ride identifier validation

- Main: `e5366ad58a2f3ff1d39c9c0641fbb74a1d348860`.
- Sole active API PR: [#200](https://github.com/rajeevp727/turnova-platform/pull/200), branch `test/api-nondev-support-readiness-20261120`, source/test head [`0d19935ffd386c3b970484cf4494d8e7f859ab98`](https://github.com/rajeevp727/turnova-platform/commit/0d19935ffd386c3b970484cf4494d8e7f859ab98), 916 ahead / 0 behind.
- Cosmos and in-memory Rider/Driver acquire stores reject blank/whitespace owner IDs and RideIds before persistence. Cancellation is checked first. Rider RequestHash validation remains.
- Exact-head API-only CI [#45303575177](https://github.com/rajeevp727/turnova-platform/actions/runs/45303575177) passed 1,292/1,292 tests, Release build (0 warnings/0 errors), native non-container Linux x64 publish and verifier; artifact [14879364230](https://github.com/rajeevp727/turnova-platform/actions/runs/45303575177/artifacts/14879364230).
- API remains `in_progress`; Android/iOS remain `not_started`. Strict acceptance remains 0/19 Verified (16 In Progress, 3 Blocked). Live Cosmos and production readiness are not established.
- PR #200 still has no independent human approval; never self-approve, bypass protections or merge. Historical PR #197/#207 merges without independent approval remain disclosed exceptions.

## Run 119 — Rider reservation RequestHash integrity

- Main: `e5366ad58a2f3ff1d39c9c0641fbb74a1d348860`.
- Sole active API PR: [#200](https://github.com/rajeevp727/turnova-platform/pull/200), branch `test/api-nondev-support-readiness-20261120`.
- Latest source/test head: [`908f48b814563a0b5c657cc522c07d6a1434df96`](https://github.com/rajeevp727/turnova-platform/commit/908f48b814563a0b5c657cc522c07d6a1434df96), 912 ahead / 0 behind main.
- Cosmos Rider reservation reads reject blank persisted RequestHash; Cosmos and in-memory acquire reject blank/whitespace RequestHash before writes. Cancellation is checked first so pre-cancelled requests preserve OperationCanceledException precedence.
- Five deterministic regressions cover persisted read, both invalid-input acquire paths and both pre-cancelled-token paths.
- Exact-head API-only CI [#45295426701](https://github.com/rajeevp727/turnova-platform/actions/runs/45295426701) is pending; no pass is claimed. The earlier xUnit assertion failure was corrected by 126de8b; superseded runs are not counted.
- Strict acceptance remains 0/19 Verified (16 In Progress, 3 Blocked). Mocked Cosmos tests do not establish live Cosmos/production readiness.
- Review gate remains three owner-authored COMMENTED reviews, zero independent approvals and zero unresolved threads. PRs #196/#198/#205/#206 remain Draft/parked. PRs #197/#207 were historical merges without independent human approval; not precedent.

## Live repository state

- Main: `e5366ad58a2f3ff1d39c9c0641fbb74a1d348860`.
- Sole active API PR: [#200](https://github.com/rajeevp727/turnova-platform/pull/200), branch `test/api-nondev-support-readiness-20261120`.
- Latest source/test head is above; documentation-bearing successor requires its own exact-head API-only CI.
- Diff remains API/docs/tests only; no mobile paths or Docker/container build/deployment files.
- Main phase metadata remains stale (`api=complete`, `android=in_progress`); PR #200 branch metadata remains API `in_progress`, Android/iOS `not_started`.

## Current API engineering

- Cosmos-native auth and its transactional/ETag/cancellation tests remain part of the API-only branch.
- Runs 114–118 add cancellation precedence and identity validation across Union assignment/status, Union/Driver/Cab discovery reads, ride-offer reads, support/queue reads and Driver/Rider reservation reads.
- Run 119 adds acquisition conflict-read cancellation regressions, persisted RequestHash validation, pre-write RequestHash validation and cancellation-precedence tests in both Cosmos and in-memory Rider reservation stores.
- Live Cosmos ETag/transaction/partition/multi-instance/restart, production identity/hosting/provider, Product dispatch/location/expiry, realtime/maps, private media, support/reconciliation, legal and later device/signing gates remain external.

# PROJECT BRAIN — TurnOva

Last updated: 2026-11-24 (Run 119)

## Platform phase

- API: `in_progress`
- Android: `not_started`
- iOS: `not_started`
- Required order: API → Android Rider → Android Driver → iOS Rider → iOS Driver.
- No Android completion work is authorized until API implementation, deterministic evidence, API contract/docs, available API acceptance checks, independent human review/permitted merge and a fresh main/API phase-exit audit pass.

## Run 119 — Rider reservation RequestHash integrity

- Main: `e5366ad58a2f3ff1d39c9c0641fbb74a1d348860`.
- Sole active API PR: [#200](https://github.com/rajeevp727/turnova-platform/pull/200), branch `test/api-nondev-support-readiness-20261120`.
- Corrected source/test head: [`126de8b11170eeae9cbb7152ac80b38824ec0ade`](https://github.com/rajeevp727/turnova-platform/commit/126de8b11170eeae9cbb7152ac80b38824ec0ade), 909 ahead / 0 behind main.
- Cosmos Rider reservation reads reject blank persisted RequestHash; Cosmos and in-memory acquire reject blank/whitespace RequestHash before any write. Valid replay and ETag/transaction/audit behavior is unchanged.
- Tests: `RiderActiveReadRejectsBlankRequestHash`, `CosmosAcquireRejectsBlankRequestHashBeforeCallingCosmos`, `InMemoryAcquireRejectsBlankRequestHashBeforeStoringReservation`.
- CI #45294834351 found a test-only xUnit async assertion error; 126de8b corrects it. Exact-head CI on 126de8b is pending; no pass is claimed.
- Strict acceptance remains 0/19 Verified (16 In Progress, 3 Blocked). Mocked Cosmos tests do not establish live Cosmos/production readiness.
- Review gate remains three owner-authored COMMENTED reviews, zero independent approvals and zero unresolved threads. PRs #196/#198/#205/#206 remain Draft/parked. PRs #197/#207 were historical merges without independent human approval; not precedent.

## Live repository state

- Main: `e5366ad58a2f3ff1d39c9c0641fbb74a1d348860`.
- Sole active API PR: [#200](https://github.com/rajeevp727/turnova-platform/pull/200), branch `test/api-nondev-support-readiness-20261120`.
- Latest source/test head is above; documentation-bearing successor requires its own exact-head API-only CI.
- Diff remains API/docs/tests only; no mobile paths or Docker/container build/deployment files.
- Main phase metadata remains stale (`api=complete`, `android=in_progress`); PR #200 branch metadata remains API `in_progress`, Android/iOS `not_started`.

## Current API engineering

- Cosmos-native auth and its transactional/ETag/cancellation tests remain part of the API-only branch.
- Runs 114–118 add cancellation precedence and identity validation across Union assignment/status, Union/Driver/Cab discovery reads, ride-offer reads, support/queue reads and Driver/Rider reservation reads.
- Run 119 adds acquisition conflict-read cancellation regressions, persisted RequestHash validation and pre-write RequestHash validation in both Cosmos and in-memory Rider reservation stores.
- Live Cosmos ETag/transaction/partition/multi-instance/restart, production identity/hosting/provider, Product dispatch/location/expiry, realtime/maps, private media, support/reconciliation, legal and later device/signing gates remain external.

# PROJECT BRAIN — TurnOva

Last updated: 2026-11-24 (Run 119)

## Platform phase

- API: `in_progress`
- Android: `not_started`
- iOS: `not_started`
- Required order: API → Android Rider → Android Driver → iOS Rider → iOS Driver.
- No Android completion work is authorized until API implementation, deterministic evidence, API contract/docs, available API acceptance checks, independent human review/permitted merge and a fresh main/API phase-exit audit pass.

## Run 119 — Rider reservation RequestHash integrity

- Main: `e5366ad58a2f3ff1d39c9c0641fbb74a1d348860`.
- Sole active API PR: [#200](https://github.com/rajeevp727/turnova-platform/pull/200), branch `test/api-nondev-support-readiness-20261120`.
- Source/test head: [`c5c69fef055d67d14a3f64b88c8c164c9516904f`](https://github.com/rajeevp727/turnova-platform/commit/c5c69fef055d67d14a3f64b88c8c164c9516904f), 908 ahead / 0 behind main.
- Cosmos Rider reservation read validation rejects blank persisted RequestHash. Cosmos and in-memory acquire validate RequestHash before any write; valid same-ride replay and ETag/transaction/audit semantics are unchanged.
- Deterministic tests: `RiderActiveReadRejectsBlankRequestHash`, `CosmosAcquireRejectsBlankRequestHashBeforeCallingCosmos`, `InMemoryAcquireRejectsBlankRequestHashBeforeStoringReservation`.
- Exact-head API-only CI [#45294834351](https://github.com/rajeevp727/turnova-platform/actions/runs/45294834351) is pending on c5c69fe; no pass is claimed. Intermediate cbcef7d CI was cancelled by the successor.
- Strict acceptance remains 0/19 Verified (16 In Progress, 3 Blocked: DISPATCH-01, DRIVER-04, SUPPORT-01). Mocked Cosmos tests do not establish live Cosmos/production readiness.
- Review gate remains three owner-authored COMMENTED reviews, zero independent approvals and zero unresolved threads. PRs #196/#198/#205/#206 remain Draft/parked. PRs #197 and #207 were historical merges without independent human approval; not approval precedent.

## Live repository state

- Main: `e5366ad58a2f3ff1d39c9c0641fbb74a1d348860`.
- Sole active API PR: [#200](https://github.com/rajeevp727/turnova-platform/pull/200), branch `test/api-nondev-support-readiness-20261120`.
- Latest source/test head is above; the documentation-bearing successor must pass its own exact-head API-only CI.
- The branch diff remains API/docs/tests only; no mobile paths or Docker/container build/deployment files were introduced.
- Main phase metadata remains stale (`api=complete`, `android=in_progress`); PR #200 branch metadata remains API `in_progress`, Android/iOS `not_started`.

## Current API engineering

- Cosmos-native auth and its transactional/ETag/cancellation tests remain part of the API-only branch.
- Runs 114–118 add cancellation precedence and identity validation across Union assignment/status, Union/Driver/Cab discovery reads, ride-offer reads, support/queue reads and Driver/Rider reservation reads.
- Run 119 adds acquisition conflict-read cancellation regressions, persisted RequestHash validation and pre-write RequestHash validation in both Cosmos and in-memory Rider reservation stores.
- Live Cosmos ETag/transaction/partition/multi-instance/restart, production identity/hosting/provider, Product dispatch/location/expiry, realtime/maps, private media, support/reconciliation, legal and later device/signing gates remain external.

# PROJECT BRAIN — TurnOva

Last updated: 2026-11-24 (Run 119)

## Platform phase

- API: `in_progress`
- Android: `not_started`
- iOS: `not_started`
- Required order: API → Android Rider → Android Driver → iOS Rider → iOS Driver.
- No Android completion work is authorized until API implementation, deterministic evidence, API contract/docs, available API acceptance checks, independent human review/permitted merge and a fresh main/API phase-exit audit pass.

## Run 119 — persisted Rider reservation identity

- Main: `e5366ad58a2f3ff1d39c9c0641fbb74a1d348860`.
- Sole active API PR: [#200](https://github.com/rajeevp727/turnova-platform/pull/200), branch `test/api-nondev-support-readiness-20261120`.
- Source/test head: [`cbcef7d75f51d76bb7b4ecea17b64d4d0002ca80`](https://github.com/rajeevp727/turnova-platform/commit/cbcef7d75f51d76bb7b4ecea17b64d4d0002ca80), 906 ahead / 0 behind main.
- `CosmosRiderActiveBookingReservationStore.RequireResource` now rejects a blank/whitespace RequestHash as malformed persisted idempotency identity, in addition to mismatched RiderId or blank RideId. Valid same-ride replay and all write/ETag/audit behavior are unchanged.
- `RiderActiveReadRejectsBlankRequestHash` covers fail-closed behavior. Exact-head API-only CI [#45294471693](https://github.com/rajeevp727/turnova-platform/actions/runs/45294471693) is in progress; no pass is claimed yet.
- The preceding Run 119 acquisition conflict-read cancellation tests passed in CI [#45285221683](https://github.com/rajeevp727/turnova-platform/actions/runs/45285221683): 1,267/1,267 tests, Release build (0 warnings/0 errors), native non-container Linux x64 publish/verifier, artifact [14870974964](https://github.com/rajeevp727/turnova-platform/actions/runs/45285221683/artifacts/14870974964). That run was on older head `3932577ab558a73d5e9cade13454e8561c069902`.
- Strict acceptance remains 0/19 Verified (16 In Progress, 3 Blocked: DISPATCH-01, DRIVER-04, SUPPORT-01). Mocked Cosmos tests do not establish live Cosmos/production readiness.
- Review gate remains three owner-authored COMMENTED reviews, zero independent approvals and zero unresolved threads. PRs #196/#198/#205/#206 remain Draft/parked. PRs #197 and #207 were historical merges without independent human approval; not approval precedent.

## Live repository state

- Main: `e5366ad58a2f3ff1d39c9c0641fbb74a1d348860`.
- Sole active API PR: [#200](https://github.com/rajeevp727/turnova-platform/pull/200), branch `test/api-nondev-support-readiness-20261120`.
- The latest Run 119 source/test head is above; the documentation-bearing successor must pass its own exact-head API-only CI.
- The branch diff remains API/docs/tests only; no mobile paths or Docker/container build/deployment files were introduced.
- Main phase metadata remains stale (`api=complete`, `android=in_progress`); PR #200 branch metadata remains API `in_progress`, Android/iOS `not_started`.

## Current API engineering

- Cosmos-native auth and its transactional/ETag/cancellation tests remain part of the API-only branch.
- Runs 114–118 add cancellation precedence and identity validation across Union assignment/status, Union/Driver/Cab discovery reads, ride-offer reads, support/queue reads and Driver/Rider reservation reads.
- Run 119 adds cancellation-race tests for both reservation acquisition conflict-read paths and rejects blank persisted Rider reservation RequestHash.
- Live Cosmos ETag/transaction/partition/multi-instance/restart, production identity/hosting/provider, Product dispatch/location/expiry, realtime/maps, private media, support/reconciliation, legal and later device/signing gates remain external.

# PROJECT BRAIN — TurnOva

Last updated: 2026-11-24 (Run 118)

## Platform phase

- API: `in_progress`
- Android: `not_started`
- iOS: `not_started`
- Required order: API → Android Rider → Android Driver → iOS Rider → iOS Driver.
- No Android completion work is authorized until API implementation, deterministic evidence, API contract/docs, available API acceptance checks, independent human review/permitted merge and a fresh main/API phase-exit audit pass.

## Run 118 — reservation read cancellation handoff

- Source/test commit: [`9ef07d01b886cf5a9d02dbca8429176388f4a84f`](https://github.com/rajeevp727/turnova-platform/commit/9ef07d01b886cf5a9d02dbca8429176388f4a84f); final source/test head: [`02c0ef8300cce2dd8886d40f83f2d2453ef85a0d`](https://github.com/rajeevp727/turnova-platform/commit/02c0ef8300cce2dd8886d40f83f2d2453ef85a0d); documentation checkpoint: [`32f0c5776f33f96b8cf93732ac0732199bc8e0d9`](https://github.com/rajeevp727/turnova-platform/commit/32f0c5776f33f96b8cf93732ac0732199bc8e0d9).
- Applied `CosmosReadResponseGuard.AwaitWithCancellationPrecedenceAsync` to all nine point-read awaits and the Driver reservation query-page await across the two reservation stores. Existing 404 mapping, ETags, transactional batches, audit facts, expiry and successful-write semantics are unchanged.
- Eight deterministic provider-failure/cancellation-race tests were added. Exact-head API-only CI [#45276818135](https://github.com/rajeevp727/turnova-platform/actions/runs/45276818135) passed on documentation head [`32f0c5776f33f96b8cf93732ac0732199bc8e0d9`](https://github.com/rajeevp727/turnova-platform/commit/32f0c5776f33f96b8cf93732ac0732199bc8e0d9): **1,265/1,265 backend tests (0 failed, 0 skipped)**; .NET 10 Release build (0 warnings, 0 errors); native non-container Linux x64 framework-dependent publish; publish-script syntax; verifier (11 negative fixtures plus valid fixture). Publish output: 44,027,836 bytes. Artifact [14868371134](https://github.com/rajeevp727/turnova-platform/actions/runs/45276818135/artifacts/14868371134), 17,018,982 bytes, ZIP SHA-256 `477790da63ecec4d1ed9ee89f5dfac9aee32ca6176ddc2634674655d9e91b92e`. Mobile-auth/mobile/Android/emulator/iOS jobs were intentionally skipped under the API-only phase gate.
- PR #200 compare at the docs checkpoint: 900 ahead / 0 behind main. Review gate remains three owner-authored COMMENTED reviews, zero independent approvals, zero unresolved threads. PRs #196/#198/#205/#206 remain Draft/parked.
- Strict acceptance remains 0/19 Verified (16 In Progress, 3 Blocked). No production or live Cosmos readiness is claimed.

## Live repository state

- Main: `e5366ad58a2f3ff1d39c9c0641fbb74a1d348860`.
- Sole active API PR: [#200](https://github.com/rajeevp727/turnova-platform/pull/200), branch `test/api-nondev-support-readiness-20261120`.
- Run 118 final docs checkpoint [`32f0c5776f33f96b8cf93732ac0732199bc8e0d9`](https://github.com/rajeevp727/turnova-platform/commit/32f0c5776f33f96b8cf93732ac0732199bc8e0d9) passed exact-head API-only CI [#45276818135](https://github.com/rajeevp727/turnova-platform/actions/runs/45276818135): **1,265/1,265 tests**, Release build (0 warnings/0 errors), native non-container Linux x64 publish/verifier. Artifact [14868371134](https://github.com/rajeevp727/turnova-platform/actions/runs/45276818135/artifacts/14868371134), 17,018,982 bytes, SHA-256 `477790da63ecec4d1ed9ee89f5dfac9aee32ca6176ddc2634674655d9e91b92e`.
- Documentation-bearing head: [`50534fafd37ed004310e8827094e8d45e4744214`](https://github.com/rajeevp727/turnova-platform/commit/50534fafd37ed004310e8827094e8d45e4744214); exact-head API-only CI [#45261591022](https://github.com/rajeevp727/turnova-platform/actions/runs/45261591022) passed 1,250/1,250 tests, Release build (0 warnings/0 errors), native non-container publish/verifier. Artifact [14860497441](https://github.com/rajeevp727/turnova-platform/actions/runs/45261591022/artifacts/14860497441), 17,016,654 bytes, SHA-256 `8872abf82418360e02aac4a43c34e13a3be9d65da15310d520717960647218ea`.
- Follow-up handoff-only documentation head [`a86554533aa888ad9dc4e33e01418458331ab628`](https://github.com/rajeevp727/turnova-platform/commit/a86554533aa888ad9dc4e33e01418458331ab628) passed exact-head API-only CI [#45262230425](https://github.com/rajeevp727/turnova-platform/actions/runs/45262230425): 1,250/1,250 tests, Release build (0 warnings/0 errors), native non-container publish/verifier. Artifact [14860263716](https://github.com/rajeevp727/turnova-platform/actions/runs/45262230425/artifacts/14860263716), 17,016,620 bytes, SHA-256 `4060a43237208d31cf862e60e64769f28338e1e245c8ba4a37892d95a4bf25de`.
- Latest documentation-bearing head [`32f0c5776f33f96b8cf93732ac0732199bc8e0d9`](https://github.com/rajeevp727/turnova-platform/commit/32f0c5776f33f96b8cf93732ac0732199bc8e0d9) passed exact-head API-only CI #45276818135 with 1,265/1,265 tests, Release build and native non-container publish/verifier; this evidence-bearing status sync will receive its own exact-head CI.
- Documentation-bearing head: `bb63e9bca0e5191d85e107b4a43e94d808e93d95`; exact-head API-only CI [#45257892556](https://github.com/rajeevp727/turnova-platform/actions/runs/45257892556) also passed 1,243/1,243 tests, Release build (0 warnings/0 errors), native non-container publish/verifier; artifact [14859416923](https://github.com/rajeevp727/turnova-platform/actions/runs/45257892556/artifacts/14859416923), 17,015,317 bytes, SHA-256 `724ad1e0b2813eb0b7cbee972839134c3bb2b848d1f71b3bedb9da5f1a95b979`.
- Source/test branch comparison: 899 ahead / 0 behind main at the Run 118 source/test checkpoint; latest docs checkpoint is 900 ahead / 0 behind. GitHub reports PR #200 mergeable. The diff remains API/docs/tests only, with no mobile paths or Docker/container build/deployment files. Documentation-bearing head 50534 comparison: 873 ahead / 0 behind; follow-up handoff head comparison: 874 ahead / 0 behind, 175 files; GitHub reports mergeable. The diff contains no mobile paths or Docker/container build/deployment files.
- PR #200 has three owner-authored COMMENTED reviews, zero independent approvals and zero unresolved threads. Never self-approve or bypass protection. Do not begin Android until an independent review, permitted merge and fresh main/API phase-exit audit are complete.
- PRs #196/#198/#205/#206 remain Draft/parked. PR #204 is trusted-LAN Android cleartext configuration only, not Android completion evidence.
- Main phase metadata is stale (`api=complete`, `android=in_progress`); PR #200 branch metadata remains API `in_progress`, Android/iOS `not_started`.
- PR #197 and PR #207 were historically merged without independent human approval. Disclose these exceptions; they are not approval precedent.

## Current API engineering

- Run 118 applies cancellation precedence to every read await in the Cosmos Driver reservation and Rider active-booking reservation stores (nine point reads plus the Driver multi-Driver query-page read). Eight deterministic 503/cancellation-race tests were added; exact-head CI #45276818135 passed 1,265/1,265 on docs head 32f0c5776f33f96b8cf93732ac0732199bc8e0d9.


- Cosmos-native Rider/Driver registration/login/refresh/logout and JWT issuance are on main via PR #207.
- PR #200 contains same-partition transactional email reservation + user creation, PBKDF2-SHA256 password material, ETag-conditional login/refresh/logout, fail-closed persisted identity/password checks, bootstrap resource validation and cancellation precedence. Run 114 extends cancellation precedence to Union assignment/status conflict mappings. Run 115 adds `CosmosReadResponseGuard` and applies null/identity/cancellation checks to Union, assignment, audit, Union-list, Driver profile and Cab discovery point/query reads. Run 116 adds `AwaitWithCancellationPrecedenceAsync` and applies cancellation precedence to all ride-offer read/query/hydration paths.
- Run 113 adds cancellation checks after PBKDF2, before registration batch execution, after login password verification (including invalid-password results), and before refresh/logout ETag writes. Non-success registration batch responses preserve cancellation; a successful committed batch response remains authoritative. Run 114 adds nine deterministic Union assignment/status cancellation-race cases; successful writes remain authoritative. Run 115 adds 24 deterministic Cosmos read guard cases, including Driver discovery identity/zone/eligibility checks and cancellation precedence. Run 116 adds seven deterministic tests for ride-offer provider-error/cancellation races and wrapper semantics.
- Exact-head Run 116 CI passed 1,250/1,250 tests. Run 117 source/test CI passed 1,257/1,257 tests, 0 warnings/0 errors and native non-container Linux x64 publish/verifier. Documentation-bearing Run 117 head is still under exact-head CI. This is deterministic Cosmos SDK mock evidence, not live Cosmos proof.
- Current API maps no demo-login route and no TriSend auth proxy; the demo path remains a negative 404 regression. Existing TriSend-account migration and profile-edit contract require Product/Operations decisions.

## Acceptance accounting

- Strict acceptance: **0/19 Verified**, 16 In Progress, 3 Blocked (DISPATCH-01, DRIVER-04, SUPPORT-01).
- This is not an implementation percentage. Substantial in-scope engineering exists and is tested, but full acceptance evidence remains incomplete.
- No 19-row acceptance status changed in Run 117. Strict acceptance remains 0/19 Verified (16 In Progress, 3 Blocked: DISPATCH-01, DRIVER-04, SUPPORT-01). Production readiness and live Cosmos/provider validation are not established.

## External gates

- Live Cosmos transactional batch, partition, ETag contention, multi-instance and restart evidence.
- Production JWT signing key, Cosmos credentials, non-container API hosting/deployment.
- Existing-account migration, profile-edit contract and shared multi-instance auth rate limiting/trusted-proxy configuration.
- Product-approved dispatch zones/radius/location freshness/negotiation lifetime; maps/routing; SignalR/call/chat/push providers.
- Private-media storage/scanning/consent/retention; support/escalation and reconciliation policies; Union legal approval.
- Android/iOS physical-device, signing, store and TestFlight evidence after each platform phase is authorized.

## Next exact action

1. Keep API as the active phase; verify exact-head CI [#45265851337](https://github.com/rajeevp727/turnova-platform/actions/runs/45265851337) on `71c6f2262d83ea54fa8a00e7d32409a74f38422d` (or the superseding documentation-bearing head). After a green final-head run, record the exact test count and native non-container publish/verifier evidence; continue API-only reservation-store cancellation/identity/persistence audits, then auth/authorization, privacy and contract audits.
2. Obtain independent human review/approval for PR #200. Merge only when repository policy permits and all final-head checks are green.
3. Re-read main and perform a fresh API phase-exit audit before Android Rider.
4. Complete Android Rider, Android Driver, iOS Rider and iOS Driver strictly in that order.

No Docker/container builds or deployments. Never claim production/device/live Cosmos validation unless performed.
