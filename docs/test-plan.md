# TurnOva MVP test plan

This document is the index for the executable test strategy. The product scope and acceptance denominator are defined by [`PRD.md`](../PRD.md) and the 19-row [MVP acceptance matrix](MVP_ACCEPTANCE_MATRIX.md).

## Test sources

- Backend/API: `src/backend/TurnOva/TurnOva.Tests` (.NET 10, xUnit, ASP.NET Core integration host and deterministic Cosmos SDK mocks).
- Rider and Driver mobile: each app's own unit/source-integration tests and Expo/native validation. These are intentionally gated until the API phase exit criteria are met.
- Detailed API test IDs, scenarios, expected outcomes and run-specific evidence: [`test-plan-api-addendum.md`](test-plan-api-addendum.md).

## API phase verification

The API-only CI must run the .NET 10 Release build and the complete backend test project, then perform a native non-container Linux x64 API publish, publish-script syntax validation and native publish verifier (negative fixtures plus a valid fixture). Mobile-auth, mobile, Android/emulator and iOS/simulator jobs may be skipped only while the API-first platform gate is active; skipped jobs are not reported as passing evidence.

Deterministic SDK mocks establish adapter and cancellation/concurrency contracts, not live Cosmos DB ETag/partition/transaction/multi-instance/restart/failover behavior. Those require a configured non-production Cosmos environment and separate evidence. Native publish does not establish hosted API readiness, production identity/key custody, provider integration or production deployment.

## Platform sequence and acceptance accounting

1. Complete API implementation, deterministic tests, API contract/docs and native non-container publish evidence.
2. Only after the API phase exit audit, complete Android Rider and then Android Driver, including tests, debug/release builds and emulator install/launch smoke.
3. Only after both Android apps exit, complete iOS Rider and then iOS Driver, including tests, simulator builds and launch smoke.

A passing automated test suite is engineering evidence only. An acceptance row is strictly Verified only when every PRD criterion and its required external evidence pass. Keep strict acceptance count separate from implementation/test progress; do not infer production readiness from mocked or local-only evidence.
