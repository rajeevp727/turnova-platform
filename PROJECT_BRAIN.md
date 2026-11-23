# PROJECT BRAIN — TurnOva

Last updated: 2026-11-23 (Run 112)

## Platform phase

- API: `in_progress`
- Android: `not_started`
- iOS: `not_started`
- Required order: API → Android Rider → Android Driver → iOS Rider → iOS Driver.
- No mobile completion work is authorized until the API phase exit criteria pass on freshly re-read main.

## Live repository state

- Main: `e5366ad58a2f3ff1d39c9c0641fbb74a1d348860`.
- Sole active API PR: [#200](https://github.com/rajeevp727/turnova-platform/pull/200), branch `test/api-nondev-support-readiness-20261120`.
- Latest source/test checkpoint: `24b467e84e04e40a1191fb7fb2fc33d7e3b7726e`; it passed exact-head API-only CI #45234425436. Documentation sync follows that checkpoint; refresh the live PR branch head before merge or phase-gate decisions. The 168-file PR diff has no mobile paths.
- Source/test commits: `59c619a52fdf7d51ee21871b4d522f877d195a4d` and `24b467e84e04e40a1191fb7fb2fc33d7e3b7726e`.
- Exact-head API-only CI [#45234425436](https://github.com/rajeevp727/turnova-platform/actions/runs/45234425436) passed **1,202/1,202 backend tests**, .NET 10 Release build (0 warnings/0 errors), native non-container Linux x64 publish/verifier. Artifact [14848606717](https://github.com/rajeevp727/turnova-platform/actions/runs/45234425436/artifacts/14848606717). Mobile-auth/mobile/Android/emulator/iOS jobs were intentionally skipped.
- PR #200 has three owner-authored COMMENTED reviews, zero independent approvals and zero unresolved threads. Do not self-approve, bypass protection or merge without independent human approval.
- PRs #196/#198/#205/#206 remain Draft/parked. PR #207 and PR #197's merges without independent approval remain disclosed historical process exceptions.
- Main phase metadata is stale (`api=complete`, `android=in_progress`); PR #200 phase metadata correctly remains API in_progress / Android and iOS not_started.

## Current API engineering

- Cosmos-native Rider/Driver registration/login/refresh/logout and JWT issuance are in main via PR #207.
- PR #200 adds same-partition transactional email reservation + user creation, PBKDF2-SHA256 password material, ETag-conditional login/refresh/logout, cancellation precedence and fail-closed malformed persisted identity/password checks.
- `CosmosIdentityService` validates the first-use `TurnOvaUsers` bootstrap response and preserves cancellation across all CosmosException catch/translation paths for registration, login, refresh and logout. Ten new deterministic failure-race tests supplement the existing successful-response cancellation tests.
- The 1,202-test checkpoint is deterministic Cosmos SDK mock evidence, not live Cosmos proof. See [CI #45234425436](https://github.com/rajeevp727/turnova-platform/actions/runs/45234425436).
- Current API maps no demo-login route and no TriSend auth proxy; the demo path remains a negative 404 regression. Existing TriSend-account migration and profile-edit contract require Product/Operations decisions.

## Acceptance accounting

- Strict acceptance: **0/19 Verified**, 16 In Progress, 3 Blocked (DISPATCH-01, DRIVER-04, SUPPORT-01).
- This is not an implementation percentage. In-scope engineering exists and is tested, but full acceptance evidence remains incomplete.
- Production readiness and live Cosmos/provider validation are not established.

## External gates

- Live Cosmos transactional batch, partition, ETag contention, multi-instance and restart evidence.
- Production JWT signing key, Cosmos credentials, non-container API hosting/deployment.
- Existing-account migration, profile-edit contract and shared multi-instance auth rate limiting/trusted-proxy configuration.
- Product-approved dispatch zones/radius/location freshness/negotiation lifetime; maps/routing; SignalR/call/chat/push providers.
- Private-media storage/scanning/consent/retention; support/escalation and reconciliation policies; Union legal approval.
- Android/iOS physical-device, signing, store and TestFlight evidence.

## Next exact action

1. Continue API-only safe work while PR #200 remains the sole active feature PR.
2. Obtain independent human review/approval; merge only when repository policy permits.
3. Re-read main and conduct a fresh API phase-exit audit before Android Rider.
4. Complete Android Rider, Android Driver, iOS Rider and iOS Driver strictly in that order.

No Docker/container builds or deployments. Never claim production/device/live Cosmos validation unless performed.
