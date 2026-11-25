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


## Run 160 — 2026-11-25 — post-commit cancellation and auth cache-safety paths

- `UnionAssignmentWriterTests.CosmosReplacementReturnsCommittedAssignmentWhenCancellationArrivesAfterWrite` verifies an ETag-guarded assignment replacement remains successful when cancellation arrives after the provider confirms the committed write.
- `CosmosQueueCancellationTests.QueueMutationsRemainSuccessfulWhenCancellationArrivesAfterAtomicCommit` has three deterministic cases for Remove, MoveToBack and MoveToBackOnce. Each verifies the observed ETag, transactional audit event and successful result after cancellation races the completed batch.
- `AuthApiIntegrationTests.SubjectlessSignedTokenCannotReadIdentity` and `AmbiguousSubjectClaimsAreRejectedByJwtValidation` now assert no-store/no-cache response headers on 403 and 401 respectively.
- Exact source/test head [`e83be0aab4c10ea6102af7d56b1a3a3e4f0e0f25`](https://github.com/rajeevp727/turnova-platform/commit/e83be0aab4c10ea6102af7d56b1a3a3e4f0e0f25) passed API-only CI [#45547947572](https://github.com/rajeevp727/turnova-platform/actions/runs/45547947572): 1,615/1,615 backend tests, 0 failed/0 skipped; .NET 10 Release build, 0 warnings/0 errors; native non-container Linux x64 publish; publish-script syntax; verifier (11 negative fixtures plus valid fixture). Artifact [14992172562](https://github.com/rajeevp727/turnova-platform/actions/runs/45547947572/artifacts/14992172562), 17,039,900 bytes; SHA-256 `411debb0c9083ff84d9ecae097cd7083703c420b1cbaaa7420b9fd8b27fdbba8`.
- Test-only increment; no runtime source or mobile paths changed. No acceptance row is promoted; strict acceptance remains 0/19 Verified (16 In Progress, 3 Blocked). Live Cosmos and production readiness remain unproven.


## Run 161 — 2026-11-25 — reservation committed-write and API-route security coverage

This addendum supersedes the older Run 157 operational phase-gate note in the canonical `docs/test-plan.md`; the canonical file remains unchanged as the historical test-plan baseline.

| Test-plan ID | Scenario | Automated evidence | Expected result |
|---|---|---|---|
| COSMOS-STORE-019 | Queue removal batch commits and caller cancellation arrives after the commit response | `CosmosQueueCancellationTests.QueueMutationsRemainSuccessfulWhenCancellationArrivesAfterAtomicCommit("remove")` | Remove returns true; ETag-fenced delete and append-only Left audit fact remain authoritative |
| COSMOS-STORE-020 | Queue MoveToBack batch commits and caller cancellation arrives after the commit response | `CosmosQueueCancellationTests.QueueMutationsRemainSuccessfulWhenCancellationArrivesAfterAtomicCommit("move-to-back")` | MoveToBack returns true; ETag-fenced replace and append-only MovedToBack audit fact remain authoritative |
| COSMOS-STORE-021 | Queue MoveToBackOnce batch commits and caller cancellation arrives after the commit response | `CosmosQueueCancellationTests.QueueMutationsRemainSuccessfulWhenCancellationArrivesAfterAtomicCommit("move-to-back-once")` | MoveToBackOnce returns true; ETag-fenced replace and append-only rotation audit fact remain authoritative |
| COSMOS-STORE-022 | Union assignment replacement commits and caller cancellation arrives after the response | `UnionAssignmentWriterTests.CosmosReplacementReturnsCommittedAssignmentWhenCancellationArrivesAfterWrite` | ETag-guarded replacement returns success; committed assignment is not misreported as cancelled |
| COSMOS-STORE-023 | Driver reservation promotion commits and caller cancellation arrives after the response | `CosmosReservationCancellationTests.DriverReservationMutationRemainsSuccessfulWhenCancellationArrivesAfterAtomicCommit("make-permanent")` | Reservation remains permanent; ETag and MadePermanent audit event are committed atomically |
| COSMOS-STORE-024 | Driver reservation expiry change commits and caller cancellation arrives after the response | `CosmosReservationCancellationTests.DriverReservationMutationRemainsSuccessfulWhenCancellationArrivesAfterAtomicCommit("set-expiry")` | New expiry and ExpiryChanged audit event remain committed atomically |
| COSMOS-STORE-025 | Driver reservation release commits and caller cancellation arrives after the response | `CosmosReservationCancellationTests.DriverReservationMutationRemainsSuccessfulWhenCancellationArrivesAfterAtomicCommit("release")` | Matching reservation deletion and Released audit event remain committed atomically |
| COSMOS-STORE-026 | Rider active-booking promotion commits and caller cancellation arrives after the response | `CosmosReservationCancellationTests.RiderReservationMutationRemainsSuccessfulWhenCancellationArrivesAfterAtomicCommit("make-permanent")` | Rider reservation remains permanent; ETag and MadePermanent audit event are committed atomically |
| COSMOS-STORE-027 | Rider active-booking release commits and caller cancellation arrives after the response | `CosmosReservationCancellationTests.RiderReservationMutationRemainsSuccessfulWhenCancellationArrivesAfterAtomicCommit("release")` | Matching reservation deletion and Released audit event remain committed atomically |
| AUTH-API-068 | A subjectless signed token is denied from `/api/v1/auth/me` with 403, or an ambiguous subject is denied with 401 | `AuthApiIntegrationTests.SubjectlessSignedTokenCannotReadIdentity`; `AuthApiIntegrationTests.AmbiguousSubjectClaimsAreRejectedByJwtValidation` | Both responses retain `Cache-Control: no-store` and `Pragma: no-cache` |
| AUTH-API-069 | Every OpenAPI operation under `/api/v1` is classified by its actual authentication boundary | `AuthApiIntegrationTests.OpenApiDocumentDescribesCoreRoutesAndAccurateSecuritySchemes` | Public register/login/refresh have no security requirement; all ops routes require `OpsAdminKey`; all other API routes, including AI trip-intent/help, require Bearer JWT and not the ops key |

**Exact evidence:** source/test head [`6274ca79a0a39942112ed671036112e28304aceb`](https://github.com/rajeevp727/turnova-platform/commit/6274ca79a0a39942112ed671036112e28304aceb) passed API-only CI [#45549495019](https://github.com/rajeevp727/turnova-platform/actions/runs/45549495019): **1,620/1,620 backend tests (0 failed, 0 skipped)**, .NET 10 Release build (0 warnings/0 errors), native non-container Linux x64 publish, publish-script syntax and verifier (11 negative fixtures plus valid fixture). Artifact [14993038336](https://github.com/rajeevp727/turnova-platform/actions/runs/45549495019/artifacts/14993038336), 17,039,909 bytes; ZIP SHA-256 `0674e4d3727407b2c16e0e5e86301f5bde54300a807acbbe487bb66882f9e783`.

The initial test attempt [#45548881557](https://github.com/rajeevp727/turnova-platform/actions/runs/45548881557) failed compilation because C# expression trees do not support `is null` patterns in Moq predicates. The test was corrected to `== null` in commit [`aa2b8ad3ec9e62c3430b5850471652722b0d2e82`](https://github.com/rajeevp727/turnova-platform/commit/aa2b8ad3ec9e62c3430b5850471652722b0d2e82); the final exact-head build/test/publish above passed.

No runtime source changed and no acceptance row is promoted. These are deterministic mocked Cosmos SDK and OpenAPI contract tests, not live Cosmos ETag/transaction/multi-instance/restart or production identity evidence.
