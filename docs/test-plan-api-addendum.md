# API Test Plan Addendum — 2026-11-25 (Run 159)

This append-only supplement records API persistence regression tests added during Runs 158–159. It does not create or promote MVP acceptance rows. The canonical `docs/test-plan.md` remains unchanged.

The exact source/test head `d2ef0131ba4fa682f7d9feff9b72de4030c0d965` passed API-only CI [#45545449149](https://github.com/rajeevp727/turnova-platform/actions/runs/45545449149): **1,611/1,611 tests (0 failed, 0 skipped)**, .NET 10 Release build (0 warnings/errors), native non-container Linux x64 publish, publish-script syntax and verifier (11 negative fixtures plus valid fixture). Artifact [14990598522](https://github.com/rajeevp727/turnova-platform/actions/runs/45545449149/artifacts/14990598522); ZIP SHA-256 `814491754631a8fa9051f647288eb6179ce6b3edee40d1adb8a83a2a0b6ee9d3`. Documentation checkpoint `700da1ba2bad6f29f02435702da7842120e2647b` passed exact-head API-only CI [#45546607928](https://github.com/rajeevp727/turnova-platform/actions/runs/45546607928): 1,611/1,611 tests, Release 0 warnings/errors, native non-container publish/verifier, artifact [14991961064](https://github.com/rajeevp727/turnova-platform/actions/runs/45546607928/artifacts/14991961064), ZIP SHA-256 `c15875b1eb366f64f9f8a5ade5e2bc307f99fd11e29ebbe82ca472335f90d223`. This final handoff/compare-count update still requires its own exact-head CI.

## Legacy idempotency committed-delete cancellation

- **API-CANCEL-LEGACY-IDEM-001** — `CosmosLegacyRideIdempotencyCleanupTests.DeleteProviderFailureTakesPrecedenceOverCancellation`: a delete-side provider failure racing caller cancellation surfaces cancellation rather than a stale provider exception.
- **API-CANCEL-LEGACY-IDEM-002** — `CosmosLegacyRideIdempotencyCleanupTests.SuccessfulDeleteRemainsAuthoritativeWhenCancellationArrivesAfterCommit`: a successful conditional delete remains authoritative when cancellation arrives after commit.

## Union create-if-absent

- **API-CANCEL-UNION-001** — `UnionCreateIfAbsentTests.CosmosCreateConflictPreservesCancellation`: a Cosmos 409 racing cancellation surfaces cancellation and never falls back to the in-memory store.
- **API-CANCEL-UNION-002** — `UnionCreateIfAbsentTests.CosmosCreateReturnsCommittedUnionWhenCancellationArrivesAfterWrite`: a successful Cosmos create remains authoritative after cancellation and does not write to the in-memory store.

## Conditional Driver ride acceptance

- **API-CANCEL-RIDE-ACCEPT-001** — `RideAcceptWriteGuardTests.ConditionalConflictReleasesReservationBeforeSurfacingCancellation`: a definite 412 releases the temporary Driver reservation with a non-cancelled cleanup token before cancellation is surfaced.
- **API-CANCEL-RIDE-ACCEPT-002** — `RideAcceptWriteGuardTests.ConditionalConflictReleasesReservationAndPreservesConflictWithoutCancellation`: a 404 releases the reservation and preserves the original conflict when the caller is not cancelled.
- **API-CANCEL-RIDE-ACCEPT-003** — `RideAcceptWriteGuardTests.OtherProviderFailureTakesPrecedenceOverCancellationWithoutReleasingReservation`: a non-conflict provider failure racing cancellation surfaces cancellation and does not release a reservation on an ambiguous write outcome.
- **API-CANCEL-RIDE-ACCEPT-004** — `RideAcceptWriteGuardTests.SuccessfulCommittedWriteRemainsAuthoritativeWhenCancellationArrivesAfterCommit`: a successful committed accept remains authoritative after cancellation.
- **API-CANCEL-RIDE-ACCEPT-005** — `RideAcceptWriteGuardTests.PreCancelledRequestDoesNotStartWriteOrReleaseReservation`: pre-cancellation prevents both the write and cleanup callback.

## Runtime call-site audit

The remaining direct Cosmos writes in API `Program.cs` now route through `CosmosReadResponseGuard.AwaitCommittedWriteWithCancellationPrecedenceAsync`, except conditional Driver ride acceptance, which uses the tested `RideAcceptWriteGuard` to preserve definite-conflict reservation cleanup. The guard's generic success/failure semantics are covered by `CosmosReadResponseGuardTests`. No Cosmos ETag, multi-instance, restart or live-provider proof is inferred from these SDK-mock tests.
