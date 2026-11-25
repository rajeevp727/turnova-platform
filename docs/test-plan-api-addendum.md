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


## Run 162 — Cosmos query-page enumeration cancellation boundaries

These deterministic tests supplement the Cosmos persistence/query guard evidence. They exercise SDK-mock behavior only; they do not prove live Cosmos partition, ETag, transaction, multi-instance, restart or failover behavior.

| Test-plan ID | Scenario | Automated evidence | Expected result |
|---|---|---|---|
| COSMOS-READ-043 | A page enumerator's `Current` getter throws while caller cancellation is signalled | `CosmosReadResponseGuardTests.ReadPageItemsCancellationTakesPrecedenceOverCurrentGetterFailure` | Cancellation takes precedence; the enumerator is still disposed exactly once |
| COSMOS-READ-044 | A page enumerator's `Current` getter fails without caller cancellation | `CosmosReadResponseGuardTests.ReadPageItemsPreservesCurrentGetterFailureWhenNotCancelled` | The original provider exception instance is propagated; cleanup still occurs |
| COSMOS-READ-045 | Cancellation arrives after the `Current` getter returns | `CosmosReadResponseGuardTests.ReadPageItemsHonorsCancellationAfterCurrentGetterReturns` | Cancellation is observed before the item is added or another page-enumerator operation begins |
| COSMOS-READ-046 | Cancellation arrives during successful enumerator disposal | `CosmosReadResponseGuardTests.ReadPageItemsHonorsCancellationAfterEnumeratorDispose` | Cleanup completes, then caller cancellation is surfaced rather than returning a completed page |
| COSMOS-READ-047 | Enumerator disposal throws while caller cancellation is signalled | `CosmosReadResponseGuardTests.ReadPageItemsCancellationTakesPrecedenceOverEnumeratorDisposeFailure` | Cancellation takes precedence over the disposal failure; disposal is attempted once |
| COSMOS-READ-048 | Enumerator disposal fails without caller cancellation | `CosmosReadResponseGuardTests.ReadPageItemsPreservesEnumeratorDisposeFailureWhenNotCancelled` | The original disposal exception instance is propagated |

**Exact source/test evidence:** [PR #200 source/test head `b7d9c08ea4246187d5aafe54113d0ec057320f4e`](https://github.com/rajeevp727/turnova-platform/commit/b7d9c08ea4246187d5aafe54113d0ec057320f4e) passed API-only CI [#45551885376](https://github.com/rajeevp727/turnova-platform/actions/runs/45551885376): 1,626/1,626 tests, Release build with 0 warnings/errors, native non-container Linux x64 publish and verifier (11 negative fixtures plus valid fixture). Artifact [14994491824](https://github.com/rajeevp727/turnova-platform/actions/runs/45551885376/artifacts/14994491824), 17,039,927 bytes; ZIP SHA-256 `761740b4a6eb4f394b2aff9c956bf15cfbfad132f67d1d2ede21bea8beca6d14`. This is a test-only increment; no acceptance row is promoted.



## Run 163 — identity-container bootstrap commit/cancellation boundary

| Test-plan ID | Scenario | Automated evidence | Expected result |
|---|---|---|---|
| COSMOS-BOOTSTRAP-042 | `CreateContainerIfNotExistsAsync` successfully commits while caller cancellation arrives before the successful response is consumed | `CosmosIdentityContainerBootstrapTests.RegisterStopsBeforeIdentityBatchWhenContainerBootstrapCommitsAfterCancellation` | The committed bootstrap is not retroactively reported as a provider failure; cancellation is surfaced before response hydration/registration batch work, and no identity transactional batch begins |

This is deterministic Cosmos SDK-mock evidence. It does not establish live Cosmos container creation, partitioning, ETag, multi-instance or restart behavior.


## Run 164 — guarded Cosmos reads for SignalR ride tracking

These deterministic tests cover the Cosmos-backed SignalR connection path. They do not establish live Cosmos, WebSocket delivery, multi-instance backplane or device reconnect behavior.

| Test-plan ID | Scenario | Automated evidence | Expected result |
|---|---|---|---|
| SIGNALR-COSMOS-001 | Authenticated owning Rider connects to a Cosmos-backed ride group | `RideTrackingHubTests.OnConnectedAsyncUsesGuardedCosmosReadAndAddsOnlyTheOwningRider` | Uses the ride ID as both item ID and partition key, validates the returned RideId, forwards `ConnectionAborted`, and adds only the owning Rider |
| SIGNALR-COSMOS-002 | Cosmos point read returns 404 | `RideTrackingHubTests.OnConnectedAsyncAbortsWhenCosmosRideDoesNotExist` | Connection is aborted and no group membership is added |
| SIGNALR-COSMOS-003 | Cosmos returns a null/identity-mismatched ride resource | `RideTrackingHubTests.OnConnectedAsyncAbortsWhenCosmosReturnsMismatchedRideIdentity` | Invalid aggregate is rejected and no group membership is added |
| SIGNALR-COSMOS-004 | Connection aborts after Cosmos returns a successful response | `RideTrackingHubTests.OnConnectedAsyncStopsBeforeResourceHydrationWhenConnectionAbortsAfterCosmosRead` | Cancellation is surfaced before lazy Resource hydration and before group membership |

**Exact source/test evidence:** [PR #200 head `5dbfb67badca92d6a80b0bb9e287ebf675695f65`](https://github.com/rajeevp727/turnova-platform/commit/5dbfb67badca92d6a80b0bb9e287ebf675695f65) passed API-only CI [#45565641809](https://github.com/rajeevp727/turnova-platform/actions/runs/45565641809): **1,631/1,631 backend tests (0 failed, 0 skipped)**, .NET 10 Release build (0 warnings/errors), native non-container Linux x64 API publish, publish-script syntax and verifier (11 negative fixtures plus valid fixture). Artifact [14999882815](https://github.com/rajeevp727/turnova-platform/actions/runs/45565641809/artifacts/14999882815), 17,040,040 bytes; ZIP SHA-256 `0fa4962fcf2482d720da17fa7a26465dd4ba0762b4948045252c6ae1969c9528`.

The API/Infrastructure scan covered 35 C# source files and found no raw Cosmos SDK point-read/page-read/Resource/ETag access outside the shared guard after this change. No acceptance row is promoted; `REALTIME-01` remains In Progress. SDK mocks do not prove live Cosmos or SignalR behavior.
