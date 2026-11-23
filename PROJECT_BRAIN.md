# PROJECT BRAIN — TurnOva

Last updated: 2026-11-24 (Run 116)

## Platform phase

- API: `in_progress`
- Android: `not_started`
- iOS: `not_started`
- Required order: API → Android Rider → Android Driver → iOS Rider → iOS Driver.
- No Android completion work is authorized until API implementation, deterministic evidence, API contract/docs, available API acceptance checks, independent human review/permitted merge and a fresh main/API phase-exit audit pass.

## Live repository state

- Main: `e5366ad58a2f3ff1d39c9c0641fbb74a1d348860`.
- Sole active API PR: [#200](https://github.com/rajeevp727/turnova-platform/pull/200), branch `test/api-nondev-support-readiness-20261120`.
- Source/test head: `e9fe5d6271c3febde47f691f5f219dd293be7a9d`; exact-head API-only CI [#45260778123](https://github.com/rajeevp727/turnova-platform/actions/runs/45260778123) passed **1,250/1,250 backend tests**, Release build (0 warnings/0 errors), native non-container Linux x64 publish/verifier. Artifact [14859029219](https://github.com/rajeevp727/turnova-platform/actions/runs/45260778123/artifacts/14859029219), 17,016,645 bytes, SHA-256 `8b136fc03fd4c7b65da78726ecf13c4bd9326950cd14385f8796ce148b21ed46`.
- Documentation-bearing head: [`50534fafd37ed004310e8827094e8d45e4744214`](https://github.com/rajeevp727/turnova-platform/commit/50534fafd37ed004310e8827094e8d45e4744214); exact-head API-only CI [#45261591022](https://github.com/rajeevp727/turnova-platform/actions/runs/45261591022) passed 1,250/1,250 tests, Release build (0 warnings/0 errors), native non-container publish/verifier. Artifact [14860497441](https://github.com/rajeevp727/turnova-platform/actions/runs/45261591022/artifacts/14860497441), 17,016,654 bytes, SHA-256 `8872abf82418360e02aac4a43c34e13a3be9d65da15310d520717960647218ea`.
- The follow-up handoff-only documentation sync in this commit will receive its own exact-head CI check; no pass is claimed for that next head.
- Documentation-bearing head: `bb63e9bca0e5191d85e107b4a43e94d808e93d95`; exact-head API-only CI [#45257892556](https://github.com/rajeevp727/turnova-platform/actions/runs/45257892556) also passed 1,243/1,243 tests, Release build (0 warnings/0 errors), native non-container publish/verifier; artifact [14859416923](https://github.com/rajeevp727/turnova-platform/actions/runs/45257892556/artifacts/14859416923), 17,015,317 bytes, SHA-256 `724ad1e0b2813eb0b7cbee972839134c3bb2b848d1f71b3bedb9da5f1a95b979`.
- Source/test branch comparison: 872 ahead / 0 behind main. Documentation-bearing head comparison: 873 ahead / 0 behind, 175 files; GitHub reports mergeable. The diff contains no mobile paths or Docker/container build/deployment files.
- PR #200 has three owner-authored COMMENTED reviews, zero independent approvals and zero unresolved threads. Never self-approve or bypass protection. Do not begin Android until an independent review, permitted merge and fresh main/API phase-exit audit are complete.
- PRs #196/#198/#205/#206 remain Draft/parked. PR #204 is trusted-LAN Android cleartext configuration only, not Android completion evidence.
- Main phase metadata is stale (`api=complete`, `android=in_progress`); PR #200 branch metadata remains API `in_progress`, Android/iOS `not_started`.
- PR #197 and PR #207 were historically merged without independent human approval. Disclose these exceptions; they are not approval precedent.

## Current API engineering

- Cosmos-native Rider/Driver registration/login/refresh/logout and JWT issuance are on main via PR #207.
- PR #200 contains same-partition transactional email reservation + user creation, PBKDF2-SHA256 password material, ETag-conditional login/refresh/logout, fail-closed persisted identity/password checks, bootstrap resource validation and cancellation precedence. Run 114 extends cancellation precedence to Union assignment/status conflict mappings. Run 115 adds `CosmosReadResponseGuard` and applies null/identity/cancellation checks to Union, assignment, audit, Union-list, Driver profile and Cab discovery point/query reads. Run 116 adds `AwaitWithCancellationPrecedenceAsync` and applies cancellation precedence to all ride-offer read/query/hydration paths.
- Run 113 adds cancellation checks after PBKDF2, before registration batch execution, after login password verification (including invalid-password results), and before refresh/logout ETag writes. Non-success registration batch responses preserve cancellation; a successful committed batch response remains authoritative. Run 114 adds nine deterministic Union assignment/status cancellation-race cases; successful writes remain authoritative. Run 115 adds 24 deterministic Cosmos read guard cases, including Driver discovery identity/zone/eligibility checks and cancellation precedence. Run 116 adds seven deterministic tests for ride-offer provider-error/cancellation races and wrapper semantics.
- Exact-head Run 116 CI passed 1,250/1,250 tests with 0 warnings/0 errors and native non-container Linux x64 publish/verifier. This is deterministic Cosmos SDK mock evidence, not live Cosmos proof.
- Current API maps no demo-login route and no TriSend auth proxy; the demo path remains a negative 404 regression. Existing TriSend-account migration and profile-edit contract require Product/Operations decisions.

## Acceptance accounting

- Strict acceptance: **0/19 Verified**, 16 In Progress, 3 Blocked (DISPATCH-01, DRIVER-04, SUPPORT-01).
- This is not an implementation percentage. Substantial in-scope engineering exists and is tested, but full acceptance evidence remains incomplete.
- No 19-row acceptance status changed in Run 116. Production readiness and live Cosmos/provider validation are not established.

## External gates

- Live Cosmos transactional batch, partition, ETag contention, multi-instance and restart evidence.
- Production JWT signing key, Cosmos credentials, non-container API hosting/deployment.
- Existing-account migration, profile-edit contract and shared multi-instance auth rate limiting/trusted-proxy configuration.
- Product-approved dispatch zones/radius/location freshness/negotiation lifetime; maps/routing; SignalR/call/chat/push providers.
- Private-media storage/scanning/consent/retention; support/escalation and reconciliation policies; Union legal approval.
- Android/iOS physical-device, signing, store and TestFlight evidence after each platform phase is authorized.

## Next exact action

1. Keep API as the active phase; source/test head `e9fe5d6271c3febde47f691f5f219dd293be7a9d` passed [#45260778123](https://github.com/rajeevp727/turnova-platform/actions/runs/45260778123), and documentation-bearing head `50534fafd37ed004310e8827094e8d45e4744214` passed [#45261591022](https://github.com/rajeevp727/turnova-platform/actions/runs/45261591022). Verify the follow-up handoff-only documentation sync commit's exact-head CI, then continue API-only cancellation, auth/authorization, privacy, persistence and contract audits.
2. Obtain independent human review/approval for PR #200. Merge only when repository policy permits and all final-head checks are green.
3. Re-read main and perform a fresh API phase-exit audit before Android Rider.
4. Complete Android Rider, Android Driver, iOS Rider and iOS Driver strictly in that order.

No Docker/container builds or deployments. Never claim production/device/live Cosmos validation unless performed.
