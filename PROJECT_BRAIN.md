# PROJECT BRAIN — TurnOva

Last updated: 2026-11-23 (Run 110)

## Platform phase

- API: `in_progress`
- Android: `not_started`
- iOS: `not_started`
- Required order: API → Android Rider → Android Driver → iOS Rider → iOS Driver.
- No mobile completion work is authorized until the API phase exit criteria pass on freshly re-read main.

## Live repository state

- Main: `e5366ad58a2f3ff1d39c9c0641fbb74a1d348860`.
- Sole active API feature PR: [#200](https://github.com/rajeevp727/turnova-platform/pull/200), branch `test/api-nondev-support-readiness-20261120`.
- Source/test checkpoint: `3a76458b2d00f042d5b08e1919fcf988b58855c6`; compare was 801 ahead / 0 behind main; GitHub reported mergeable.
- PR diff at refresh: 172 files (63 backend, 107 docs, phase metadata/history) and no mobile paths. Scope check passed.
- Exact source/test CI [#45225230574](https://github.com/rajeevp727/turnova-platform/actions/runs/45225230574) passed 1,219/1,219 backend tests, .NET 10 Release build (0 warnings/0 errors), native non-container linux-x64 publish and verifier. Artifact [14841638796](https://github.com/rajeevp727/turnova-platform/actions/runs/45225230574/artifacts/14841638796). Documentation/dead-code cleanup commit requires its own exact-head CI.
- PR #200 has three owner-authored COMMENTED reviews, zero independent approvals and zero unresolved threads. Do not self-approve, bypass protections or merge without independent human approval.
- PRs #196/#198/#205/#206 remain Draft/parked. PR #207 merged Cosmos-native auth; PR #204 is configuration-only. PR #207's merge without independent approval and PR #197's earlier exception remain disclosed.

## Current API engineering

- Cosmos-native Rider/Driver registration/login/refresh/logout and JWT issuance are in main via PR #207.
- PR #200 adds same-partition transactional email reservation + user creation, PBKDF2-SHA256 password material (210,000 iterations, 16-byte salt, 32-byte hash), ETag-conditional login/refresh/logout, cancellation precedence and fail-closed malformed persisted identity/password checks.
- The 1,219-test source/test checkpoint is deterministic Cosmos SDK mock evidence, not live Cosmos proof.
- Current API maps no demo-login route and no TriSend auth proxy. `POST /api/v1/auth/demo/{role}` remains a negative 404 regression. Unused demo-session and TriSend URL/handler policy types/tests are removed from the PR branch. `DemoRide` is a legacy ride persistence model, not demo authentication.
- `GET /api/v1/auth/me` is read-only. Profile-edit fields/validation and existing TriSend-account migration require explicit Product/Operations decisions; no update endpoint is invented.

## Acceptance accounting

- Strict acceptance: **0/19 Verified**, 16 In Progress, 3 Blocked (DISPATCH-01, DRIVER-04, SUPPORT-01).
- This is not an implementation percentage. Some in-scope engineering is implemented and tested; strict verification still requires all acceptance evidence.
- Production readiness and live Cosmos/provider validation are not established.

## External gates

- Live Cosmos transactional batch, ETag contention, partition, multi-instance and restart evidence.
- Production JWT signing key, Cosmos credentials, non-container API hosting and deployment.
- Existing-account migration, profile-edit contract and shared multi-instance auth rate limiting/trusted proxy configuration.
- Product-approved dispatch zones/radius/location freshness/negotiation lifetime; maps/routing; SignalR/call/chat/push providers.
- Private-media storage/scanning/consent/retention; support/escalation and reconciliation policies; Union legal approval.
- Android/iOS physical-device, signing, store and TestFlight evidence.

## Next exact action

1. Verify exact-head API-only CI on the documentation/dead-code cleanup PR head.
2. Obtain independent human review/approval for PR #200; merge only when repository policy permits.
3. Re-read main and perform a fresh API phase-exit audit before Android Rider.
4. Complete Android Rider, Android Driver, iOS Rider, iOS Driver strictly in that order.

No Docker/container builds or deployments. Never claim production/device/live Cosmos validation unless performed.
