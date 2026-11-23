# PROJECT BRAIN — TurnOva

Last updated: 2026-11-23 (Run 113)

## Platform phase

- API: `in_progress`
- Android: `not_started`
- iOS: `not_started`
- Required order: API → Android Rider → Android Driver → iOS Rider → iOS Driver.
- No Android completion work is authorized until API implementation, deterministic evidence, API contract/docs, available API acceptance checks, independent human review/permitted merge and a fresh main/API phase-exit audit pass.

## Live repository state

- Main: `e5366ad58a2f3ff1d39c9c0641fbb74a1d348860`.
- Sole active API PR: [#200](https://github.com/rajeevp727/turnova-platform/pull/200), branch `test/api-nondev-support-readiness-20261120`.
- Source/test checkpoint: `0c4eb6524517ce996b2df0527e5653fe250f71e7`; exact-head API-only CI [#45240051473](https://github.com/rajeevp727/turnova-platform/actions/runs/45240051473) passed **1,210/1,210 backend tests**, Release build (0 warnings/0 errors), native non-container Linux x64 publish/verifier. Artifact [14849514469](https://github.com/rajeevp727/turnova-platform/actions/runs/45240051473/artifacts/14849514469), 17,012,254 bytes, SHA-256 `f82e95e6771f1d6354247a63b5d786a1499dc610ba7b121dddbafb60e30e09b9`.
- Source/test branch comparison at this checkpoint: 855 ahead / 0 behind main; GitHub reported mergeable. The 169-file diff contained no mobile paths. The documentation sync in this run must pass its own exact-head API-only CI before merge consideration.
- PR #200 has three owner-authored COMMENTED reviews, zero independent approvals and zero unresolved threads. Never self-approve or bypass protection.
- PRs #196/#198/#205/#206 remain Draft/parked. PR #204 is trusted-LAN Android cleartext configuration only, not Android completion evidence.
- Main phase metadata is stale (`api=complete`, `android=in_progress`); PR #200 branch metadata remains API `in_progress`, Android/iOS `not_started`.
- PR #197 and PR #207 were historically merged without independent human approval. Disclose these exceptions; they are not approval precedent.

## Current API engineering

- Cosmos-native Rider/Driver registration/login/refresh/logout and JWT issuance are on main via PR #207.
- PR #200 contains same-partition transactional email reservation + user creation, PBKDF2-SHA256 password material, ETag-conditional login/refresh/logout, fail-closed persisted identity/password checks, bootstrap resource validation and cancellation precedence.
- Run 113 adds cancellation checks after PBKDF2, before registration batch execution, after login password verification (including invalid-password results), and before refresh/logout ETag writes. Non-success registration batch responses preserve cancellation; a successful committed batch response remains authoritative.
- Eight new deterministic test cases cover cancellation during batch preparation/response mapping, successful committed registration, login valid/invalid password, and refresh/logout identity hydration. The 1,210-test CI checkpoint is deterministic Cosmos SDK mock evidence, not live Cosmos proof.
- Current API maps no demo-login route and no TriSend auth proxy; the demo path remains a negative 404 regression. Existing TriSend-account migration and profile-edit contract require Product/Operations decisions.

## Acceptance accounting

- Strict acceptance: **0/19 Verified**, 16 In Progress, 3 Blocked (DISPATCH-01, DRIVER-04, SUPPORT-01).
- This is not an implementation percentage. Substantial in-scope engineering exists and is tested, but full acceptance evidence remains incomplete.
- No 19-row acceptance status changed in Run 113. Production readiness and live Cosmos/provider validation are not established.

## External gates

- Live Cosmos transactional batch, partition, ETag contention, multi-instance and restart evidence.
- Production JWT signing key, Cosmos credentials, non-container API hosting/deployment.
- Existing-account migration, profile-edit contract and shared multi-instance auth rate limiting/trusted-proxy configuration.
- Product-approved dispatch zones/radius/location freshness/negotiation lifetime; maps/routing; SignalR/call/chat/push providers.
- Private-media storage/scanning/consent/retention; support/escalation and reconciliation policies; Union legal approval.
- Android/iOS physical-device, signing, store and TestFlight evidence after each platform phase is authorized.

## Next exact action

1. Keep API as the active phase; verify exact-head CI for the documentation-bearing PR #200 head.
2. Obtain independent human review/approval. Merge only when repository policy permits.
3. Re-read main and perform a fresh API phase-exit audit before Android Rider.
4. Complete Android Rider, Android Driver, iOS Rider and iOS Driver strictly in that order.

No Docker/container builds or deployments. Never claim production/device/live Cosmos validation unless performed.
