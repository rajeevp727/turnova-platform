## Queue store read and mutation input validation — Run 194

| Test-plan ID | Scenario | Automated evidence | Expected result |
|---|---|---|---|
| QUEUE-STORE-017 | Queue store read and mutation operations receive blank zone/service scope or blank DriverId | `QueueStoreInputValidationTests.CosmosQueueReadsRejectBlankScopeBeforeCallingCosmos`; `QueueStoreInputValidationTests.CosmosQueueMutationsRejectBlankDriverOrScopeBeforeCallingCosmos`; `QueueStoreInputValidationTests.InMemoryQueueReadsRejectBlankScope`; `QueueStoreInputValidationTests.InMemoryQueueMutationsRejectBlankDriverOrScopeBeforeStateMutation`; `QueueStoreInputValidationTests.CosmosQueueInputsHonorPreCancelledTokenBeforeValidation`; `QueueStoreInputValidationTests.InMemoryQueueInputsHonorPreCancelledTokenBeforeValidation` | Invalid scope/identity fails before Cosmos calls or in-memory mutation; pre-cancelled tokens take precedence |

`CosmosQueueStore` and `InMemoryQueueStore` now validate nonblank zone/service scope on eligible/audit reads and nonblank DriverId on remove, queue rotation and offer-scoped rotation. This is adapter-boundary validation only; valid legacy service classes remain supported and no FIFO ranking, Rider assignment or schema changes. Exact source/test API-only CI [#45702855191](https://github.com/rajeevp727/turnova-platform/actions/runs/45702855191) passed **1,738/1,738 backend tests (0 failed, 0 skipped)**, Release build (0 warnings/errors), native non-container Linux x64 publish, publish-script syntax and verifier (11 negative fixtures plus valid fixture); artifact [15064650726](https://github.com/rajeevp727/turnova-platform/actions/runs/45702855191/artifacts/15064650726), ZIP SHA-256 `01f6a7f31e1f9e06cd2be8df0d13b001d7e2cda896a9c895f11d9bffd4215eeb`.

## Queue join validation across domain and persistence boundaries — Run 193

| Test-plan ID | Scenario | Automated evidence | Expected result |
|---|---|---|---|
| QUEUE-STORE-016 | Queue joins with blank DriverId, ZoneId or ServiceClass, or default JoinedAt are submitted directly to the domain queue, in-memory adapter or Cosmos adapter | `DriverQueueEntryValidationTests.InMemoryStoreRejectsMalformedJoinBeforeQueueOrAuditMutation`; `DriverQueueEntryValidationTests.CosmosStoreRejectsMalformedJoinBeforeCreatingTransactionalBatch`; `DriverQueueEntryValidationTests.DomainQueueRejectsMalformedJoinWithoutAddingAnEntry`; `DriverQueueEntryValidationTests.InMemoryStoreStillAcceptsNonCabLegacyQueueEntries` | Invalid inputs throw before queue/audit mutation or Cosmos transactional-batch creation; valid legacy non-Cab queue records remain accepted |

`DriverQueueEntry.ValidateForJoin` centralizes required-field validation and is applied at the application-service, domain-queue, in-memory-store and Cosmos-store boundaries. It does not introduce Cab-only filtering into historical internal queue compatibility and does not alter FIFO ordering, Rider ranking or assignment. Exact source/test API-only CI [#45701326478](https://github.com/rajeevp727/turnova-platform/actions/runs/45701326478) passed **1,726/1,726 backend tests (0 failed, 0 skipped)**, Release build (0 warnings/errors), native non-container Linux x64 publish, publish-script syntax and verifier (11 negative fixtures plus valid fixture); artifact [15064016448](https://github.com/rajeevp727/turnova-platform/actions/runs/45701326478/artifacts/15064016448), ZIP SHA-256 `99d3cc2dc0aa62b75ff7f7a1cd9aaf44753aafd3e6c672550463ee4a1372092f`.

# API Test Plan Addendum — 2026-11-26 (Run 192)

## QueueService join input validation — Run 192

| Test-plan ID | Scenario | Automated evidence | Expected result |
|---|---|---|---|
| QUEUE-SVC-001 | `QueueService.JoinAsync` receives a `DriverQueueEntry` with a default `JoinedAt` timestamp | `QueueStoreTests.QueueServiceJoinRejectsDefaultTimestampBeforeStoreMutation` | Throws `ArgumentException` before `IQueueStore.AddAsync`; the in-memory queue and audit remain unchanged |

`QueueService.JoinAsync` now rejects a default join timestamp before delegating to any queue store. This complements `COSMOS-STORE-038`, which protects the Cosmos adapter's transactional-batch boundary. The validation does not change internal FIFO ordering or expose FIFO to Rider discovery. Exact-head API-only CI [#45683111993](https://github.com/rajeevp727/turnova-platform/actions/runs/45683111993) passed **1,713/1,713 backend tests**, Release build (0 warnings/errors), native non-container Linux x64 publish, script syntax and verifier (11 negative fixtures plus valid fixture), artifact [15055142197](https://github.com/rajeevp727/turnova-platform/actions/runs/45683111993/artifacts/15055142197).

## Previous API test plan additions — Run 191

## Cosmos queue ordering round-trip and write validation — Run 191

| Test-plan ID | Scenario | Automated evidence | Expected result |
|---|---|---|---|
| COSMOS-STORE-037 | A persisted queue item has a positive `Sequence` that does not equal `JoinedAt.UtcTicks`; a valid Cosmos queue join is read back under the same invariant | `CosmosQueueResourceContractTests.QueueEligibilityRejectsMissingJoinTimestampOrSequenceThatDoesNotMatchUtcTicks`; `CosmosQueueCancellationTests.QueueJoinRemainsSuccessfulWhenCancellationArrivesAfterAtomicCommit` | Malformed ordering metadata fails closed; valid writer output preserves exact UTC-tick sequence metadata |
| COSMOS-STORE-038 | A queue join is attempted with a default `JoinedAt` timestamp | `CosmosQueueResourceContractTests.QueueJoinRejectsDefaultTimestampBeforeCreatingTransactionalBatch` | `CosmosQueueStore.AddAsync` throws before creating a Cosmos transactional batch |

`CosmosQueueStore.RequireEligibleQueryItem` now requires `Sequence == JoinedAt.UtcTicks`, matching the existing Cosmos write contract. `CosmosQueueStore.AddAsync` rejects a default join timestamp before persistence. This protects internal FIFO snapshot integrity without changing the ordering algorithm, Rider ranking, customer-facing assignment, schema or mobile source.

Exact source/test head [`504ec1accb1d7acf5523eb97e5beaba83bf7de8a`](https://github.com/rajeevp727/turnova-platform/commit/504ec1accb1d7acf5523eb97e5beaba83bf7de8a) passed API-only CI [#45681128508](https://github.com/rajeevp727/turnova-platform/actions/runs/45681128508): **1,712/1,712 backend tests (0 failed, 0 skipped)**; .NET 10 Release build (0 warnings, 0 errors); native non-container Linux x64 publish (44,087,248 bytes); publish-script syntax; verifier (11 negative fixtures plus valid fixture); artifact [15055460209](https://github.com/rajeevp727/turnova-platform/actions/runs/45681128508/artifacts/15055460209), 17,041,246 bytes, SHA-256 `b0862ee5a39e1ff7d74b88c0c2567d3f6ed633e123d658ffce18e95b783a09aa`. Mobile-auth/mobile/Android/emulator/iOS/simulator jobs were intentionally skipped under the API-first phase gate.

These are deterministic SDK-mock and native publish checks only; live Cosmos ETag, transaction, partition, multi-instance, restart and failover behavior remains an external gate. Strict acceptance remains **0/19 Verified** (16 In Progress, 3 Blocked); no acceptance row is promoted.

## Queue eligibility ordering metadata — Run 190

- **COSMOS-STORE-036** — `CosmosQueueResourceContractTests.QueueEligibilityRejectsMissingJoinTimestampOrInvalidSequence` covers a default `JoinedAt`, zero `Sequence` and negative `Sequence`. A malformed persisted record must fail before it enters the internal FIFO snapshot.
- Runtime guard: `CosmosQueueStore.RequireEligibleQueryItem` now requires a non-default `JoinedAt` and positive `Sequence`, consistent with the Cosmos writer's `JoinedAt.UtcTicks` sequence contract.
- Exact source/test head `6334923d1bb95d5137871a79c2626247c1f13b03` passed API-only CI [#45676966031](https://github.com/rajeevp727/turnova-platform/actions/runs/45676966031): **1,707/1,707 tests (0 failed, 0 skipped)**, .NET 10 Release build (0 warnings/errors), native non-container Linux x64 publish, publish-script syntax and verifier (11 negative fixtures plus valid fixture). Artifact [15052072326](https://github.com/rajeevp727/turnova-platform/actions/runs/45676966031/artifacts/15052072326), 17,040,914 bytes; ZIP SHA-256 `38676d5c9979decf57d80488455288a03d7f128d219dfc182a191a65eefe06b8`.
- This deterministic Cosmos mock coverage does not establish live Cosmos ETag, transaction, multi-instance, restart or failover behavior.


## Queue audit persisted-event contract validation — Run 189

- **COSMOS-STORE-034** — `CosmosQueueResourceContractTests.QueueAuditReadAcceptsEverySupportedEventShape` verifies the five persisted audit event types and their valid metadata shapes. The companion seven-case theory rejects an unknown event type, offer metadata on a plain mutation, a rotation without an offer ID, dispatch-exception records missing required trace/reason fields, and incompatible mixed metadata.
- **COSMOS-STORE-035** — `CosmosQueueResourceContractTests.QueueAuditReadRejectsMissingOrNonUtcTimestamp` rejects default and non-UTC persisted audit timestamps.
- Runtime guard: `CosmosQueueStore.RequireAuditQueryItem` now fails closed for unsupported event types, mismatched per-type metadata and invalid timestamps; no queue ranking/assignment or persistence schema changes.
- Exact source/test head `1915b2d9b3c6fc6121b35dcc591b1ebd6f47630b` passed API-only CI [#45675351598](https://github.com/rajeevp727/turnova-platform/actions/runs/45675351598): **1,704/1,704 tests (0 failed, 0 skipped)**, .NET 10 Release build (0 warnings/errors), native non-container Linux x64 publish, publish-script syntax and verifier (11 negative fixtures plus valid fixture). Artifact [15052401027](https://github.com/rajeevp727/turnova-platform/actions/runs/45675351598/artifacts/15052401027), 17,040,915 bytes; ZIP SHA-256 `0243cc5d3c888005dfe33932fa76fce954cb19a2e0bb33288c925ef24f8b0e83`.
- Mobile-auth/mobile/Android/emulator/iOS/simulator jobs were intentionally skipped by the API-first phase gate. Deterministic SDK mocks do not establish live Cosmos ETag/multi-instance/restart/failover behavior.

- **Final documentation/matrix head:** [`dfa0d9b2`](https://github.com/rajeevp727/turnova-platform/commit/dfa0d9b291a1ef3231c7c767ce2820d061c893ba) passed exact-head API-only CI [#45622711487](https://github.com/rajeevp727/turnova-platform/actions/runs/45622711487): scope/backend success; **1,666/1,666 tests (0 failed, 0 skipped)**; .NET 10 Release build (0 warnings/errors); native non-container Linux x64 publish (44,086,076 bytes); publish-script syntax; native verifier (11 negative fixtures plus valid fixture); artifact [15028000422](https://github.com/rajeevp727/turnova-platform/actions/runs/45622711487/artifacts/15028000422), 17,040,803 bytes, ZIP SHA-256 `25a43c89a7afbac408da17075cee78af98203ca055797f476faa54838e581490`. Mobile-auth/mobile/Android/emulator/iOS/simulator jobs were intentionally skipped under the API-first gate.

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

The API/Infrastructure scan covered 35 C# source files and found no raw Cosmos SDK point-read/page-read/Resource/ETag access outside the shared guard after this change. The documentation-bearing checkpoint `62fe2b826bc39dfbcca219bd36fe5445b17b774f` also passed exact-head API-only CI [#45566520367](https://github.com/rajeevp727/turnova-platform/actions/runs/45566520367): 1,631/1,631 tests, Release build, native non-container publish/verifier, artifact [15000700200](https://github.com/rajeevp727/turnova-platform/actions/runs/45566520367/artifacts/15000700200). No acceptance row is promoted; `REALTIME-01` remains In Progress. SDK mocks do not prove live Cosmos or SignalR behavior.

## Run 165 — SignalR negotiate JWT boundary

These integration tests exercise the real ASP.NET Core SignalR negotiate endpoint through the Testing host. They validate HTTP authentication and cache policy, not a live WebSocket transport or backplane.

| Test-plan ID | Scenario | Automated evidence | Expected result |
|---|---|---|---|
| SIGNALR-AUTH-001 | Anonymous client posts to `/hubs/rides/negotiate` | `AuthApiIntegrationTests.RideHubNegotiationRequiresAnAuthenticatedJwt` | HTTP 401 with private no-store/no-cache headers; no negotiation response is issued |
| SIGNALR-AUTH-002 | A valid Rider JWT is supplied as `access_token` on the ride-hub negotiate request and then on an ordinary API request | `AuthApiIntegrationTests.RideHubNegotiationAcceptsQueryBearerOnlyOnRideHub` | Hub negotiate returns HTTP 200; `/api/v1/auth/me?access_token=...` remains HTTP 401 with no-store/no-cache |
| SIGNALR-AUTH-003 | A valid signed JWT with a role but no canonical subject is supplied to the ride-hub negotiate endpoint | `AuthApiIntegrationTests.SubjectlessSignedTokenCannotNegotiateRideHub` | HTTP 403 with no-store/no-cache before SignalR negotiation |

**Exact source/test evidence:** [PR #200 source/test head `b26063b356003278e5b52e4440256315f1d05968`](https://github.com/rajeevp727/turnova-platform/commit/b26063b356003278e5b52e4440256315f1d05968) passed API-only CI [#45572865415](https://github.com/rajeevp727/turnova-platform/actions/runs/45572865415): 1,634/1,634 backend tests, Release build (0 warnings/errors), native non-container Linux x64 API publish, publish-script syntax and native verifier (11 negative fixtures plus valid fixture). Artifact [15003437229](https://github.com/rajeevp727/turnova-platform/actions/runs/45572865415/artifacts/15003437229), 17,039,981 bytes; ZIP SHA-256 `41967a707e3a5deaf4351b1014563421a2ca0ec522293ef5529c1abbb459779c`.

No runtime source changed. Query-string token logging must be redacted by hosted ingress/proxy/telemetry; live WebSocket/backplane/reconnect and physical-device evidence remain external.

## Run 166 — fail-closed Union selfie visibility

These tests validate the current-Union review snapshot required before a Driver selfie can be returned by public Rider discovery or the Operations Union-driver list. Legacy approvals without immutable review metadata remain compatible; once metadata is present, incomplete, mismatched or Operations-marked snapshots fail closed.

| Test-plan ID | Scenario | Automated evidence | Expected result |
|---|---|---|---|
| UNION-SELFIE-VIS-001 | Legacy profile has `Approved` status and `ProfilePhotoApprovedByUnionId` but no immutable Union-review snapshot | `DriverProfileReviewGuardTests.UnionPhotoVisibilityPreservesLegacyApprovalWithoutReviewSnapshot` | Image remains visible only to the same Union; another Union cannot see it |
| UNION-SELFIE-VIS-002 | Approved profile has incomplete, cross-Union or Operations-review metadata | `DriverProfileReviewGuardTests.UnionPhotoVisibilityFailsClosedForIncompleteOrMismatchedReviewMetadata` | Visibility is false for incomplete/cross-Union snapshots and any Operations-review marker; rejected status is not visible |
| UNION-SELFIE-VIS-003 | Rider discovery and Operations Union-driver listing read a profile whose review snapshot names a different Union | `RideApiIntegrationTests.RiderDiscoveryAndOperationsHideSelfieWhenUnionReviewSnapshotConflictsWithCurrentUnion` | Both responses retain the eligible Driver record but return a null selfie; no private image is exposed |

**Exact source/test evidence:** [PR #200 head `74d9f46b9ccbffc4d9a93f3a85896af8b745377e`](https://github.com/rajeevp727/turnova-platform/commit/74d9f46b9ccbffc4d9a93f3a85896af8b745377e) passed API-only CI [#45576488879](https://github.com/rajeevp727/turnova-platform/actions/runs/45576488879): **1,637/1,637 backend tests (0 failed, 0 skipped)**, .NET 10 Release build (0 warnings/errors), native non-container Linux x64 API publish, publish-script syntax and native publish verifier (11 negative fixtures plus valid fixture). Artifact [15004243227](https://github.com/rajeevp727/turnova-platform/actions/runs/45576488879/artifacts/15004243227), 17,040,094 bytes; ZIP SHA-256 `d6c93c3c1c74c98b42724a8b486097cc40a097610f1e17ad23d97e2b8a36beed`.

The tests use deterministic profile fixtures and the in-memory API test host. They do not establish live Cosmos consistency, private-media provider retention/scanning, WebSocket, production identity or physical-device behavior. No acceptance row is promoted by this increment.

## Run 167 — strict JWT role boundary and persisted identity contract

These cases are deterministic integration/SDK-mock evidence. They do not establish production identity-provider behavior or live Cosmos behavior.

| Test-plan ID | Scenario | Automated evidence | Expected result |
|---|---|---|---|
| AUTH-API-070 | A valid signed token contains both `sub` and `nameid` subject aliases with the same non-empty value | `AuthApiIntegrationTests.DuplicateSubjectAliasesAreRejectedEvenWhenTheirValuesMatch` | HTTP 401, no-store/no-cache and generic Bearer challenge; duplicate aliases are not silently deduplicated |
| AUTH-API-071 | A signed token uses a Phase 2 `FleetManager` role or JWT `Operations` role | `AuthApiIntegrationTests.RolesOutsideTheMvpJwtContractAreRejectedByJwtValidation` | HTTP 401 with no-store/no-cache and no validation detail |
| AUTH-API-072 | A `UnionManager` JWT with a `unionId` claim accesses the partner API | `AuthApiIntegrationTests.UnionManagerRoleRemainsValidForScopedPartnerApi` | HTTP 200; exact partner role remains accepted |
| AUTH-API-073 | An `Ops` JWT is used without the Operations admin key | `AuthApiIntegrationTests.OpsJwtIsNonPrivilegedAndDoesNotReplaceOperationsAdminKey` | Authenticated non-privileged zone read succeeds; `/api/v1/ops/unions` remains HTTP 401 without `X-Driver-Admin-Key` |
| AUTH-COSMOS-030 | Login query returns a persisted identity with a mismatched `Role` | `CosmosIdentityServiceTests.LoginRejectsPersistedIdentityWithMismatchedRoleOrDocumentTypeBeforePointRead("role")` | HTTP 401; no point read or token write |
| AUTH-COSMOS-031 | Login query returns a persisted identity with a mismatched `DocumentType` | `CosmosIdentityServiceTests.LoginRejectsPersistedIdentityWithMismatchedRoleOrDocumentTypeBeforePointRead("documentType")` | HTTP 401; no point read or token write |
| AUTH-COSMOS-032 | Refresh query returns a persisted identity with a mismatched `Role` | `CosmosIdentityServiceTests.RefreshRejectsPersistedIdentityWithMismatchedRoleOrDocumentTypeBeforePointRead("role")` | HTTP 401; no point read or token rotation |
| AUTH-COSMOS-033 | Refresh query returns a persisted identity with a mismatched `DocumentType` | `CosmosIdentityServiceTests.RefreshRejectsPersistedIdentityWithMismatchedRoleOrDocumentTypeBeforePointRead("documentType")` | HTTP 401; no point read or token rotation |
| AUTH-COSMOS-034 | Logout point read returns a persisted identity with a mismatched `Role` | `CosmosIdentityServiceTests.LogoutRejectsPersistedIdentityWithMismatchedRoleOrDocumentTypeBeforeEtagRead("role")` | HTTP 503; no ETag hydration or revocation write |
| AUTH-COSMOS-035 | Logout point read returns a persisted identity with a mismatched `DocumentType` | `CosmosIdentityServiceTests.LogoutRejectsPersistedIdentityWithMismatchedRoleOrDocumentTypeBeforeEtagRead("documentType")` | HTTP 503; no ETag hydration or revocation write |

**Exact source/test evidence:** [commit 4c453c9b](https://github.com/rajeevp727/turnova-platform/commit/4c453c9bb1bcd71d3e30121072b6ed975e649d9b) passed [API-only CI #45582907257](https://github.com/rajeevp727/turnova-platform/actions/runs/45582907257): **1,648/1,648 backend tests (0 failed, 0 skipped)**, .NET 10 Release build, native non-container Linux x64 publish, publish-script syntax and verifier (11 negative fixtures plus valid fixture). Artifact [15006429362](https://github.com/rajeevp727/turnova-platform/actions/runs/45582907257/artifacts/15006429362), 17,040,250 bytes, SHA-256 `829cbdb5f5a9ab6d5af277c19d610cb725edcc8885aaf9261120d6dff8cb2272`.

**Correction disclosed:** intermediate [CI #45581912648](https://github.com/rajeevp727/turnova-platform/actions/runs/45581912648) failed 17/1,647 tests because the first role allow-list omitted the existing non-privileged `Ops` actor identity used by API-key-protected Operations review/audit tests. The final allow-list preserves `Ops` as non-privileged while keeping the admin-key boundary explicit; the final 1,648-test source/test head above is green.

No acceptance row is promoted. These tests do not establish live Cosmos partition/ETag/transaction/multi-instance/restart behavior or production identity-provider mapping.


## Run 168 — JWT role-claim alias ambiguity

The bearer validator now treats `role`, `roles` and the `ClaimTypes.Role` URI as role representations, and accepts exactly one canonical `role` claim. A token containing a valid canonical role plus a competing alias is rejected before authorization policy evaluation.

| Test-plan ID | Scenario | Automated evidence | Expected result |
|---|---|---|---|
| AUTH-API-074 | A signed JWT contains canonical `role=Rider` plus a competing `roles=Rider` alias | `AuthApiIntegrationTests.AlternateRoleClaimAliasesAreRejectedAlongsideCanonicalRole("roles")` | HTTP 401 with no-store/no-cache and a generic Bearer challenge; competing role representations are rejected |
| AUTH-API-075 | A signed JWT contains canonical `role=Rider` plus the `ClaimTypes.Role` URI alias | `AuthApiIntegrationTests.AlternateRoleClaimAliasesAreRejectedAlongsideCanonicalRole("http://schemas.microsoft.com/ws/2008/06/identity/claims/role")` | HTTP 401 with no-store/no-cache and a generic Bearer challenge; competing role representations are rejected |

**Exact source/test evidence:** [commit `fb0c85d2`](https://github.com/rajeevp727/turnova-platform/commit/fb0c85d26e4ea255eebfa29a40f4d329412b781a) passed API-only CI [#45592523126](https://github.com/rajeevp727/turnova-platform/actions/runs/45592523126): **1,650/1,650 backend tests (0 failed, 0 skipped)**, .NET 10 Release build, native non-container Linux x64 publish, publish-script syntax and verifier (11 negative fixtures plus valid fixture). Artifact [15010434175](https://github.com/rajeevp727/turnova-platform/actions/runs/45592523126/artifacts/15010434175), 17,040,300 bytes; ZIP SHA-256 `668b3549e899c2e32ca3a355fa4e9de4396ca99774b35b10fb84c068f04fadbc`.

No acceptance row is promoted. This is deterministic integration evidence, not live production identity-provider claim-mapping evidence.


## Run 169 — UnionManager multi-union claim scope

The PRD permits a UnionManager JWT to carry one or more signed `unionId` claims. The member-list API must use that signed claim set and must not widen authorization from caller-supplied query parameters.

| Test-plan ID | Scenario | Automated evidence | Expected result |
|---|---|---|---|
| UNION-SCOPE-001 | A UnionManager with two signed `unionId` claims lists active Cab members while supplying an unrelated `unionId` query parameter | `RideApiIntegrationTests.UnionManagerWithMultipleSignedUnionClaimsSeesOnlyClaimedUnionDrivers` | Exactly the Cab drivers assigned to the two signed unions are returned; the outside-union driver is excluded and caller input cannot expand scope |

**Exact source/test evidence:** [commit `3c8e8e38`](https://github.com/rajeevp727/turnova-platform/commit/3c8e8e384d1b2900454ce7c85ab19a5da2e0d27b) passed API-only CI [#45594320195](https://github.com/rajeevp727/turnova-platform/actions/runs/45594320195): **1,651/1,651 backend tests (0 failed, 0 skipped)**, .NET 10 Release build (0 warnings/errors), native non-container Linux x64 publish, publish-script syntax and verifier (11 negative fixtures plus valid fixture). Artifact [15012387518](https://github.com/rajeevp727/turnova-platform/actions/runs/45594320195/artifacts/15012387518), 17,040,288 bytes; ZIP SHA-256 `cbaf1367c7b37a58ce0cf9cbcbd84f010df1b388d492fb3d2464ad99c42cc625`.

This is deterministic API integration evidence for signed-claim scoping, not live production identity-provider claim issuance evidence. No acceptance row is promoted.


## Run 170 — Cosmos migration container TTL ETag fencing

A full production-source callsite inventory covered all 63 C# files under `TurnOva.API` and `TurnOva.Infrastructure`; no unguarded direct Cosmos SDK write remained. The TTL metadata path was the remaining read/replace sequence without conditional ETag protection. It now requires the ETag from the exact `ReadContainerAsync` response and uses `ContainerRequestOptions.IfMatchEtag` for the metadata replace. Missing/blank ETag fails closed; a stale ETag/412 is not retried. The SDK's conditional If-Match property is documented by [Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/api/microsoft.azure.cosmos.requestoptions.ifmatchetag?view=azure-dotnet).

| Test-plan ID | Scenario | Automated evidence | Expected result |
|---|---|---|---|
| COSMOS-MIG-056 | Container-create response returns a different container ID | `CosmosMigrationDatabaseTests.CreateRejectsMismatchedCreatedContainerIdentity` | Fail closed before TTL or follow-up work |
| COSMOS-MIG-057 | Container-create response returns a different partition-key path | `CosmosMigrationDatabaseTests.CreateRejectsMismatchedCreatedPartitionKeyPath` | Fail closed before TTL or follow-up work |
| COSMOS-MIG-058 | Container create commits while cancellation arrives before response hydration | `CosmosMigrationDatabaseTests.CreateDoesNotRetroactivelyCancelACommittedContainerCreate` | Successful create remains authoritative; response identity is validated without retroactive cancellation |
| COSMOS-MIG-059 | TTL change reads a matching container and replaces only the requested TTL using the observed ETag | `CosmosMigrationDatabaseTests.TtlUpdateValidatesCreatedAndCurrentContainerThenReplacesOnlyTtl` | Exact current ETag is passed as `IfMatchEtag`; updated properties retain the requested container ID/partition path |
| COSMOS-MIG-060 | Current TTL already matches the requested TTL | `CosmosMigrationDatabaseTests.TtlUpdateDoesNotReplaceWhenExistingTtlAlreadyMatches` | No ETag hydration or container replace occurs |
| COSMOS-MIG-061 | Created container handle ID differs from the requested container | `CosmosMigrationDatabaseTests.TtlUpdateRejectsMismatchedContainerHandleBeforeReadingIt` | Fail closed before `ReadContainerAsync` |
| COSMOS-MIG-062 | TTL read response returns a mismatched container resource | `CosmosMigrationDatabaseTests.TtlUpdateRejectsMismatchedCurrentContainerBeforeReplacingIt` | Fail closed before `ReplaceContainerAsync` |
| COSMOS-MIG-063 | TTL update requires an observed nonblank container ETag | `CosmosMigrationDatabaseTests.TtlUpdateRequiresObservedEtagBeforeReplacingContainer` | Missing ETag fails closed; no replace is attempted |
| COSMOS-MIG-064 | TTL replace commits while caller cancellation arrives after the provider returns success | `CosmosMigrationDatabaseTests.TtlReplaceRemainsAuthoritativeWhenCancellationArrivesAfterCommit` | Conditional replace remains authoritative; no retroactive cancellation |

**Exact final-head evidence:** [commit `ce50a09d`](https://github.com/rajeevp727/turnova-platform/commit/ce50a09d213c87d3ce6a45c0b76d12ca3222ca56) passed API-only CI [#45598764966](https://github.com/rajeevp727/turnova-platform/actions/runs/45598764966): **1,660/1,660 backend tests (0 failed, 0 skipped)**, .NET 10 Release build (0 warnings/errors), native non-container Linux x64 publish, publish-script syntax and verifier (11 negative fixtures plus valid fixture). Artifact [15014069031](https://github.com/rajeevp727/turnova-platform/actions/runs/45598764966/artifacts/15014069031), 17,040,514 bytes; ZIP SHA-256 `21705bc2d97cd241e29e15481c49b53d9b1b5620de38b8148b8c9f3ef429d734`.

Intermediate test-only CI failures [#45597246954](https://github.com/rajeevp727/turnova-platform/actions/runs/45597246954) and [#45598123283](https://github.com/rajeevp727/turnova-platform/actions/runs/45598123283) were corrected by narrowing test assertions/verification and supplying the observed ETag in the existing TTL cancellation fixtures. The final exact-head CI above is green. Mobile-auth/mobile/Android/emulator/iOS/simulator jobs were intentionally skipped under the API-first phase gate.

- **Documentation-bearing head CI:** [#45629857059](https://github.com/rajeevp727/turnova-platform/actions/runs/45629857059) passed on `ebefbf83ec4fea001507f4df6b90530ce0ce491e`: 1,671/1,671 backend tests (0 failed, 0 skipped), .NET 10 Release build (0 warnings/errors), native non-container Linux x64 publish, publish-script syntax and verifier (11 negative fixtures plus valid fixture). Artifact [15030680918](https://github.com/rajeevp727/turnova-platform/actions/runs/45629857059/artifacts/15030680918), 17,040,799 bytes; ZIP SHA-256 `27361e1072e73467b75f07b847eb455a592e6c7611d5631c96a9f0da1fd63342`.

This is deterministic SDK-mock and native-publish evidence only. It does not establish live Cosmos 412 contention, partition/transaction behavior, multi-instance/restart/failover/RU, or production readiness. No acceptance row is promoted.


## Run 171 — AI-01 provider data-minimization regression

| Test-plan ID | Scenario | Automated evidence | Expected result |
|---|---|---|---|
| AI-PRIVACY-001 | Rider trip-intent text contains synthetic identity, contact, precise coordinates, licence/KYC, bank-account, payment-token and bearer-token-shaped values | `AiAssistanceServiceTests.ProviderReceivesOnlyRedactedTripTokensAndCannotReplaceRoute` | The provider receives only the fixed `[PICKUP]`/`[DROPOFF]` placeholders and allow-listed generic trip-action words; none of the synthetic sensitive values or raw pickup/destination text appears in the provider prompt. The local editable Cab draft retains its parsed pickup/dropoff and still requires Rider confirmation. |

Source/test commit [`952d8a6d3bf23464de3b58cec44d3d660a8577d8`](https://github.com/rajeevp727/turnova-platform/commit/952d8a6d3bf23464de3b58cec44d3d660a8577d8). Fresh exact-head API-only CI is pending; the prior passing run [#45598764966](https://github.com/rajeevp727/turnova-platform/actions/runs/45598764966) applies to the previous head only. This supplemental AI-01 evidence does not promote any of the 19 acceptance rows. No live AI provider, model or credentials are configured.

## Run 172 — Cab-only UnionManager member-list scope

| Test-plan ID | Scenario | Automated evidence | Expected result |
|---|---|---|---|
| UNION-SCOPE-002 | A UnionManager has signed claims for an active Cab-serving Union and an active non-Cab Union that retains a stale active Cab assignment | `RideApiIntegrationTests.UnionManagerMemberListExcludesAssignmentsFromUnionsThatNoLongerServeCab` | `GET /api/v1/union/drivers` returns only the Cab-serving Union's active Cab driver. The non-Cab Union's stale assignment is not exposed. |

The API member-list guard now requires the claimed Union to exist, be Active and explicitly include Cab in its service classes. Runtime commit [`d0ffc527d47e2f1b8833e9dcb38dc13d8846d8d4`](https://github.com/rajeevp727/turnova-platform/commit/d0ffc527d47e2f1b8833e9dcb38dc13d8846d8d4); deterministic regression commit [`6e5d34a1af481cf29e3419158265990bce60ef74`](https://github.com/rajeevp727/turnova-platform/commit/6e5d34a1af481cf29e3419158265990bce60ef74). Exact-head API-only CI is pending. This test does not prove live Cosmos or production identity-provider behavior and does not promote an acceptance row.


## Run 173 — UnionManager Cab-scope compile correction

The first Run172 exact-head workflow [#45601521567](https://github.com/rajeevp727/turnova-platform/actions/runs/45601521567) failed in the .NET Release build because `StringComparison` was passed to `IReadOnlyList<string>.Contains`. No backend tests ran in that failed job. The API guard now uses `Contains("cab", StringComparer.OrdinalIgnoreCase)` in commit [`ba03e4ec482c9f6ba919504fe9294acb9897ee0d`](https://github.com/rajeevp727/turnova-platform/commit/ba03e4ec482c9f6ba919504fe9294acb9897ee0d). Fresh exact-head CI is required; the previous passing Run170 workflow is not reused as evidence for the corrected head.


## Run 174 — deterministic regression-fixture corrections

Exact-head API-only CI [#45602030019](https://github.com/rajeevp727/turnova-platform/actions/runs/45602030019) completed with Release build success and **1,659/1,661 tests passing, 2 failing**; publish did not run after the test gate. The AI-01 trip-intent test still expected the old drop-off `HITEC City` after its fixture changed to `Secret Clinic`; assertion corrected in [`be4da1bc207440848ce787cb3e0fc135800e01fd`](https://github.com/rajeevp727/turnova-platform/commit/be4da1bc207440848ce787cb3e0fc135800e01fd). The UnionManager scope test called `SeedBookableDriver` before creating the Driver profiles; it now initializes both test clients before seeding in [`2f99d483ab68f0c4e8d4fd13b600a0613671dab6`](https://github.com/rajeevp727/turnova-platform/commit/2f99d483ab68f0c4e8d4fd13b600a0613671dab6). Neither correction changes production behavior. Fresh exact-head CI is required.


## Run 175 — AI-01 destination assertion correction

Run174 exact-head CI [#45602671349](https://github.com/rajeevp727/turnova-platform/actions/runs/45602671349) passed Release compilation and 1,659/1,661 tests; the two failures were both in AI-01 test assertions. A broad replacement had changed the existing unavailable-provider fallback expectation to `Secret Clinic` while leaving the provider-enabled test expecting `HITEC City`. Commit [`0130aed62420fafb1ca5288a66fec9beab9c26e2`](https://github.com/rajeevp727/turnova-platform/commit/0130aed62420fafb1ca5288a66fec9beab9c26e2) now keeps the fallback expectation `HITEC City` and expects `Secret Clinic` only for the synthetic provider-enabled test. No production AI behavior changed. The new `UNION-SCOPE-002` test passed in that run. Fresh exact-head CI is required.


## Run 176 — malformed persisted Union coverage metadata

| Test-plan ID | Scenario | Automated evidence | Expected result |
|---|---|---|---|
| UNION-COVERAGE-001 | A persisted Union has null `ZoneIds` or `ServiceClasses` while an otherwise eligible Cab Driver is assigned | `RideApiIntegrationTests.MalformedUnionCoverageMetadataIsExcludedFromDiscoveryAndPartnerActions` (two deterministic cases) | The malformed Union is excluded from Rider discovery and the UnionManager member list; private-document retrieval and selfie review return 404; Driver profile, assignment and Union audit remain unchanged; temporary review reservation is released |

The API now validates the required coverage collections at both point-read and list-read boundaries. Invalid persisted records are treated as unavailable rather than dereferenced. [source/test commit `f3c80cc3228fe654c3a1e7090f78bdb878d9bb8c`](https://github.com/rajeevp727/turnova-platform/commit/f3c80cc3228fe654c3a1e7090f78bdb878d9bb8c) passed exact-head API-only [CI #45605922257](https://github.com/rajeevp727/turnova-platform/actions/runs/45605922257): **1,663/1,663 backend tests (0 failed, 0 skipped)**, .NET 10 Release build (0 warnings/errors), native non-container Linux x64 publish (44,086,076 bytes; 125,829,120-byte limit), publish-script syntax, native verifier (11 negative fixtures plus valid fixture), artifact [15017702313](https://github.com/rajeevp727/turnova-platform/actions/runs/45605922257/artifacts/15017702313), ZIP SHA-256 `07413f7395caa22c0a9c59935c0725248800f49ed979d7be8c615169cf7e1b94`.

The first source/test head [CI #45605244707](https://github.com/rajeevp727/turnova-platform/actions/runs/45605244707) failed compilation because the initial test referenced an out-of-scope helper; the corrected exact-head run above is the passing evidence. The tests use deterministic in-memory fixtures and private-media test storage. They do not establish live Cosmos serialization/partition/ETag behavior or production identity. No acceptance row is promoted.

The documentation checkpoint `c3a295bf4fdaf7e369d49799ff51259fb95445d2` passed exact-head API-only CI [#45606695500](https://github.com/rajeevp727/turnova-platform/actions/runs/45606695500), including the same 1,663/1,663 tests and native non-container publish/verifier. This follow-up matrix/handoff synchronization requires its own exact-head CI.


## Run 177 — public authentication failure cache-safety

| Test-plan ID | Scenario | Automated evidence | Expected result |
|---|---|---|---|
| AUTH-API-076 | Public login storage failure, refresh storage failure, missing refresh token, authenticated logout storage failure and unsupported role failures | `AuthApiIntegrationTests.LoginRequiresConfiguredCosmosStorage`, `RefreshRequiresConfiguredCosmosStorage`, `RefreshRejectsMissingToken`, `LogoutRequiresConfiguredCosmosStorage`, `UnsupportedRoleIsNotAccepted` | HTTP 503/400/404 responses retain `Cache-Control: no-store` and `Pragma: no-cache`; no refresh credential or validation detail is returned |

The regression closes an evidence gap in the public authentication cache policy: registration already asserted non-cacheable storage failures, while login/refresh/unsupported-role paths did not all assert it. The refresh storage case also proves the public refresh route fails closed with HTTP 503 when Cosmos is not configured. Test-only change; no runtime source, token semantics, rate-limit policy or acceptance-row status changes.

Exact source/test API-only CI [#45610227195](https://github.com/rajeevp727/turnova-platform/actions/runs/45610227195) passed on `49dd524a145752e3cb4f3602796bda535df59c37`: **1,664/1,664 backend tests (0 failed, 0 skipped)**; .NET 10 Release build (0 warnings/errors); native non-container Linux x64 publish (44,086,072 bytes; 125,829,120-byte limit); publish-script syntax; native publish verifier (11 negative fixtures plus valid fixture); artifact [15019852642](https://github.com/rajeevp727/turnova-platform/actions/runs/45610227195/artifacts/15019852642), 17,040,751 bytes, ZIP SHA-256 `d1e3cb9deb3d009f392e002c87ed741575760ce0684e390b7da14d6380ce2941`. This CI is API-only; mobile-auth/mobile/Android/emulator/iOS/simulator jobs were intentionally skipped under the API-first gate.

The Run177 auth cache-safety regression now also exercises authenticated `POST /api/v1/auth/logout` with a valid Rider JWT and no configured Cosmos container; it must return HTTP 503 with both non-cacheable headers. This extends `AUTH-API-076` to the full register/login/refresh/logout lifecycle failure surface.

Exact source/test API-only CI [#45611574478](https://github.com/rajeevp727/turnova-platform/actions/runs/45611574478) passed on `36ae609a33be05cdb3eba215fd4af6b796633b2c`: **1,665/1,665 backend tests (0 failed, 0 skipped)**; .NET 10 Release build (0 warnings/errors); native non-container Linux x64 publish (44,086,076 bytes; 125,829,120-byte limit); publish-script syntax; native publish verifier (11 negative fixtures plus valid fixture); artifact [15020237803](https://github.com/rajeevp727/turnova-platform/actions/runs/45611574478/artifacts/15020237803), 17,040,803 bytes, ZIP SHA-256 `0b1d7f9a400b21b01e27a0f4827b7057812747f33d4fe5662f661c79e30bd1e0`. Mobile-auth/mobile/Android/emulator/iOS/simulator jobs were intentionally skipped under the API-first phase gate.


## Run 178 — anonymous logout authorization and cache-safety

| Test-plan ID | Scenario | Automated evidence | Expected result |
|---|---|---|---|
| AUTH-API-077 | Anonymous `POST /api/v1/auth/logout` while no Cosmos storage is configured | `AuthApiIntegrationTests.LogoutRejectsAnonymousRequest` | Authorization rejects the request with HTTP 401 before logout/storage work; response retains `Cache-Control: no-store` and `Pragma: no-cache` |

The existing logout storage-failure regression uses a valid Rider JWT and verifies HTTP 503. This additional case covers the unauthenticated route boundary independently, proving the endpoint does not reach storage for an anonymous caller and the global API cache policy applies to the 401. Test-only change; no runtime source, token semantics, mobile source, rate-limit policy or acceptance-row status changes.

Exact source/test API-only CI [#45620586804](https://github.com/rajeevp727/turnova-platform/actions/runs/45620586804) passed on `c01e5968db43b031879d41be9ca515ebbef13c45`: **1,666/1,666 backend tests (0 failed, 0 skipped)**; .NET 10 Release build (0 warnings/errors); native non-container Linux x64 publish (44,086,072 bytes; 125,829,120-byte limit); publish-script syntax; native publish verifier (11 negative fixtures plus valid fixture); artifact [15024930817](https://github.com/rajeevp727/turnova-platform/actions/runs/45620586804/artifacts/15024930817), 17,040,772 bytes, ZIP SHA-256 `400d02e6a6e5ace71fede8754cdb216f174ee4d22816d167a0d0cf0b65bbab55`. Scope/backend passed. Mobile-auth/mobile/Android/emulator/iOS/simulator jobs were intentionally skipped under the API-first gate.


- **Documentation checkpoint:** [`ee00375d`](https://github.com/rajeevp727/turnova-platform/commit/ee00375d48ad508f36c6c32bc4220227568adbb4) passed exact-head API-only CI [#45621736867](https://github.com/rajeevp727/turnova-platform/actions/runs/45621736867): **1,666/1,666 backend tests (0 failed, 0 skipped)**; .NET 10 Release build (0 warnings/errors); native non-container Linux x64 publish (44,086,080 bytes); publish-script syntax; native verifier (11 negative fixtures plus valid fixture); artifact [15024249523](https://github.com/rajeevp727/turnova-platform/actions/runs/45621736867/artifacts/15024249523), 17,040,799 bytes, ZIP SHA-256 `1638e451107c2f8c5cf984b5579c4059eeb75b77e081095683bf6c16157eab6f`. Scope/backend passed; mobile-auth/mobile/Android/emulator/iOS/simulator jobs were intentionally skipped under the API-first gate. This checkpoint is superseded only by the final documentation-sync head below.

## Run 179 — malformed public-auth payload cache-safety

| ID | Scenario | Test | Expected result |
|---|---|---|---|
| AUTH-API-078 | Malformed JSON body sent to Rider registration, Driver login or refresh | `AuthApiIntegrationTests.MalformedJsonOnPublicAuthEndpointsIsRejectedWithoutCaching` (three theory cases) | Minimal-API body binding returns HTTP 400 and the API response policy retains `Cache-Control: no-store` plus `Pragma: no-cache`; no auth handler/storage work is reached |

Source/test head `5bb43ebcbd0f0ce7db84a99eee8d74f0e6ecba0e` passed exact-head API-only CI [#45625067412](https://github.com/rajeevp727/turnova-platform/actions/runs/45625067412): 1,669/1,669 backend tests, Release build, native non-container publish/verifier and artifact [15028747138](https://github.com/rajeevp727/turnova-platform/actions/runs/45625067412/artifacts/15028747138). Mobile jobs were intentionally skipped under the API-first gate.


## Run 180 — malformed JSON on every public authentication route

| Test-plan ID | Scenario | Test | Expected result |
|---|---|---|---|
| AUTH-API-079 | Malformed JSON sent to Rider registration, Driver registration, Rider login, Driver login and refresh | `AuthApiIntegrationTests.MalformedJsonOnPublicAuthEndpointsIsRejectedWithoutCaching` (five theory cases) | Minimal-API body binding returns HTTP 400 and the API response policy retains `Cache-Control: no-store` plus `Pragma: no-cache`; no identity handler or storage operation is reached |

The four role-specific routes share `/{role}/register` and `/{role}/login` endpoint handlers. This closes the two missing role/operation combinations left by AUTH-API-078 without changing runtime behavior.

Exact source/test API-only CI [#45629147447](https://github.com/rajeevp727/turnova-platform/actions/runs/45629147447) passed on [`6d1c3935`](https://github.com/rajeevp727/turnova-platform/commit/6d1c39359ba482f825d8037107560fb6210e1615): **1,671/1,671 backend tests (0 failed, 0 skipped)**; .NET 10 Release build (0 warnings/errors); native non-container Linux x64 publish; publish-script syntax; native verifier (11 negative fixtures plus valid fixture); artifact [15029628943](https://github.com/rajeevp727/turnova-platform/actions/runs/45629147447/artifacts/15029628943), 17,040,759 bytes, ZIP SHA-256 `9d552707ef420e9a749553b6f968529bfa33782967d1c0e99a21fbd5d4447a9c`. Mobile-auth/mobile/Android/emulator/iOS/simulator jobs were intentionally skipped under the API-first phase gate.


## Run 181 — unsupported media types on every public authentication route

| Test-plan ID | Scenario | Automated evidence | Expected result |
|---|---|---|---|
| AUTH-API-080 | A `text/plain` request body is sent to Rider registration, Driver registration, Rider login, Driver login and refresh | `AuthApiIntegrationTests.UnsupportedMediaTypeOnPublicAuthEndpointsIsRejectedWithoutCaching` (five theory cases) | Minimal-API body binding returns HTTP 415 and the API response policy retains `Cache-Control: no-store` plus `Pragma: no-cache`; no identity handler or storage operation is reached |

This is a test-only regression. It extends malformed-JSON coverage (AUTH-API-079) to unsupported media types for all five public auth endpoints; no runtime API behavior or mobile source changed.

Exact source/test API-only CI [#45639725625](https://github.com/rajeevp727/turnova-platform/actions/runs/45639725625) passed on `fb69378f73b54f0b4d3f80281a765dcb1d72d5b8`: **1,676/1,676 backend tests (0 failed, 0 skipped)**; .NET 10 Release build (0 warnings, 0 errors); native non-container Linux x64 publish (44,086,076 bytes); publish-script syntax; native verifier (11 negative fixtures plus valid fixture). Artifact [15034314000](https://github.com/rajeevp727/turnova-platform/actions/runs/45639725625/artifacts/15034314000), 17,040,783 bytes; ZIP SHA-256 `06e2ffc67c575d7543066d8528c1ad8ce86bb96b11f21776b2518b417bd9394d`. Mobile-auth/mobile/Android/emulator/iOS/simulator jobs were intentionally skipped under the API-first phase gate.


## Run 182 — unsupported HTTP methods on every public authentication route

| Test-plan ID | Scenario | Automated evidence | Expected result |
|---|---|---|---|
| AUTH-API-081 | GET is sent to the POST-only Rider registration, Driver registration, Rider login, Driver login and refresh routes | `AuthApiIntegrationTests.UnsupportedMethodOnPublicAuthEndpointsReturnsMethodNotAllowedWithoutCaching` (five deterministic cases) | Every route returns HTTP 405 and retains `Cache-Control: no-store` plus `Pragma: no-cache`; no authentication handler or persistence mutation is invoked |

The test closes the method-mismatch case in the API response cache boundary. It complements AUTH-API-078/079 malformed-body (400), AUTH-API-080 unsupported-media-type (415), unmatched-route (404), CORS preflight (204) and unhandled-exception (500) coverage. It is test-only; no runtime API or mobile behavior changed.

Source/test commit [`91059af21f49335c4c03c0093aec01fc44239f45`](https://github.com/rajeevp727/turnova-platform/commit/91059af21f49335c4c03c0093aec01fc44239f45) passed exact-head API-only CI [#45650448111](https://github.com/rajeevp727/turnova-platform/actions/runs/45650448111): **1,681/1,681 backend tests (0 failed, 0 skipped)**, .NET 10 Release build (0 warnings/errors), native non-container Linux x64 publish (44,086,068 bytes), publish-script syntax and native verifier (11 negative fixtures plus valid fixture). Artifact [15039134806](https://github.com/rajeevp727/turnova-platform/actions/runs/45650448111/artifacts/15039134806), 17,040,760 bytes; ZIP SHA-256 `288e43bf7e21a6b6b925d601ce924bd4789656ad1c9d87050a71796ce3038786`. Mobile-auth/mobile/Android/emulator/iOS/simulator jobs were intentionally skipped under the API-first phase gate. The subsequent documentation-bearing head requires its own exact-head API-only CI.

Strict acceptance remains **0/19 Verified** (16 In Progress, 3 Blocked: DISPATCH-01, DRIVER-04, SUPPORT-01). No row is promoted. PR #200 remains the sole ready/active API PR and requires independent human maintainer approval; the three owner-authored COMMENTED reviews are not approvals. PR #196/#198/#205/#206 remain Draft/parked. Android/iOS remain gated. No live Cosmos, production identity/provider, hosting or device readiness is claimed.


## Run 183 — Cosmos Driver reservation promotion/expiry resource contract

| Test-plan ID | Scenario | Automated evidence | Expected result |
|---|---|---|---|
| COSMOS-STORE-028 | Cosmos returns a Driver reservation resource with a different DriverId or blank RideId during promotion or expiry update | `CosmosReservationResourceContractTests.DriverPromotionAndExpiryRejectMalformedResourceBeforeStartingBatch` (four deterministic cases) | The malformed resource throws `InvalidOperationException` before ETag hydration or transactional batch creation; no reservation or audit mutation occurs |

This closes the resource-identity test gap for Driver reservation promotion and expiry. Existing tests covered active read and release; this increment adds the remaining mutation paths. It is deterministic Cosmos SDK-mock evidence only and does not establish live ETag conflict, transaction, multi-instance, restart or failover behavior. No runtime source changed and no acceptance row is promoted.

Source/test commit [`a9d578022b0de4977564e00642ec06f6b339abf1`](https://github.com/rajeevp727/turnova-platform/commit/a9d578022b0de4977564e00642ec06f6b339abf1) passed exact-head API-only CI [#45652152959](https://github.com/rajeevp727/turnova-platform/actions/runs/45652152959): **1,685/1,685 backend tests (0 failed, 0 skipped)**; .NET 10 Release build (0 warnings/errors); native non-container Linux x64 publish (44,086,076 bytes); publish-script syntax and verifier (11 negative fixtures plus valid fixture). Artifact [15040497757](https://github.com/rajeevp727/turnova-platform/actions/runs/45652152959/artifacts/15040497757), 17,040,802 bytes; ZIP SHA-256 `bd6513e33d025daa4eba0dc42f57ab94c999f30b98193fa674f969f353944e82`. Mobile-auth/mobile/Android/emulator/iOS/simulator jobs were intentionally skipped under the API-only phase gate. The documentation-bearing head created by this handoff requires its own exact-head API-only CI.

Strict acceptance remains **0/19 Verified** (16 In Progress, 3 Blocked: DISPATCH-01, DRIVER-04, SUPPORT-01). No row is promoted. PR #200 remains the sole ready/active API PR and requires independent human maintainer approval; owner-authored COMMENTED reviews are not approvals. PR #196/#198/#205/#206 remain Draft/parked. Android/iOS remain gated. No live Cosmos or production readiness is claimed.


## Run 184 — Rider active-booking promotion resource contract

| Test-plan ID | Scenario | Automated evidence | Expected result |
|---|---|---|---|
| COSMOS-STORE-029 | Cosmos returns a Rider reservation resource with a different RiderId, blank RideId or blank RequestHash during permanent promotion | `CosmosReservationResourceContractTests.RiderPromotionRejectsMalformedResourceBeforeStartingBatch` (three deterministic cases) | The malformed resource throws `InvalidOperationException` before ETag hydration or transactional-batch creation; no reservation or audit mutation occurs |

This closes the Rider-side resource-contract gap corresponding to the Driver promotion/expiry checks in COSMOS-STORE-028. The test uses a strict SDK response mock and verifies the ETag getter is never called and no batch is created. It is deterministic SDK-mock evidence only; it does not establish live Cosmos ETag conflict, transaction, multi-instance, restart or failover behavior. No runtime source changed and no acceptance row is promoted.

Source/test commit [`64d732229fdd66125fa12014264c3c9f45728035`](https://github.com/rajeevp727/turnova-platform/commit/64d732229fdd66125fa12014264c3c9f45728035) passed exact-head API-only CI [#45654057980](https://github.com/rajeevp727/turnova-platform/actions/runs/45654057980): **1,688/1,688 backend tests (0 failed, 0 skipped)**; .NET 10 Release build (0 warnings/errors); native non-container Linux x64 publish (44,086,072 bytes); publish-script syntax and verifier (11 negative fixtures plus valid fixture). Artifact [15041698443](https://github.com/rajeevp727/turnova-platform/actions/runs/45654057980/artifacts/15041698443), 17,040,768 bytes; ZIP SHA-256 `953093d7277f8f5056189bb4419eb78701af0f3dfea35445b521a51bd692f7c2`. Mobile-auth/mobile/Android/emulator/iOS/simulator jobs were intentionally skipped under the API-first phase gate.

Strict acceptance remains **0/19 Verified** (16 In Progress, 3 Blocked: DISPATCH-01, DRIVER-04, SUPPORT-01). No row is promoted. PR #200 remains the sole ready/active API PR and requires independent human maintainer approval; owner-authored COMMENTED reviews are not approvals. PR #196/#198/#205/#206 remain Draft/parked. Android/iOS remain gated. No live Cosmos or production readiness is claimed.


## Run 185 — Driver/Rider reservation release resource contract

| Test-plan ID | Scenario | Automated evidence | Expected result |
|---|---|---|---|
| COSMOS-STORE-030 | Cosmos returns a Driver reservation with a mismatched DriverId or blank RideId, or a Rider reservation with a mismatched RiderId, blank RideId or blank RequestHash during release | `CosmosReservationResourceContractTests.DriverReleaseRejectsMalformedResourceBeforeEtagHydrationOrDeleteBatch` (two theory cases) and `RiderReleaseRejectsMalformedResourceBeforeEtagHydrationOrDeleteBatch` (three theory cases) | Each malformed resource throws `InvalidOperationException` after one Resource read but before ETag hydration or transactional delete/audit batch creation; no reservation mutation occurs |

This closes the release-path persisted-resource validation gap on both reservation stores. The strict SDK response mocks deliberately throw if ETag is accessed, and strict Container mocks verify no transactional batch is created. The evidence is deterministic Cosmos SDK-mock coverage only; it does not establish live Cosmos ETag contention, transaction, multi-instance, restart or failover behavior. Test-only change: no runtime source, schema, mobile behavior or acceptance-row status changed.

Source/test commit [`bb21a4f1`](https://github.com/rajeevp727/turnova-platform/commit/bb21a4f1df56f3654e8d494c640676474314142b) passed exact-head API-only CI [#45661440024](https://github.com/rajeevp727/turnova-platform/actions/runs/45661440024): **1,691/1,691 backend tests (0 failed, 0 skipped)**; .NET 10 Release build (0 warnings/errors); native non-container Linux x64 publish (44,086,072 bytes); publish-script syntax; native verifier (11 negative fixtures plus valid fixture). Artifact [15044987551](https://github.com/rajeevp727/turnova-platform/actions/runs/45661440024/artifacts/15044987551), 17,040,771 bytes; ZIP SHA-256 `3cae378942c1204c509f0c1284f15da36f207ab4c53acd80c98f85dbee54cac2`. Mobile-auth/mobile/Android/emulator/iOS/simulator jobs were intentionally skipped under the API-first phase gate.

Strict acceptance remains **0/19 Verified** (16 In Progress, 3 Blocked: DISPATCH-01, DRIVER-04, SUPPORT-01). No row is promoted. PR #200 remains open and requires independent human maintainer approval; the three owner-authored COMMENTED reviews are not approvals. PR #196/#198/#205/#206 remain Draft/parked. Android/iOS remain gated. No live Cosmos or production readiness is claimed.



## Run 186 — eligible queue partition-key isolation

| Test-plan ID | Scenario | Automated evidence | Expected result |
|---|---|---|---|
| COSMOS-STORE-031 | Cosmos returns an eligible queue item whose ZoneId and ServiceClass match the requested Cab queue but whose stored ZoneServiceKey belongs to a different partition | `CosmosQueueResourceContractTests.QueueEligibilityRejectsQueryItemWithWrongPartitionKeyEvenWhenZoneAndServiceMatch` | The malformed item throws `InvalidOperationException` and is not returned to the caller; query enumeration stops at the invalid item |

This closes a distinct partition-identity regression gap: wrong-zone/service coverage alone could fail without proving that the stored partition-key field is checked independently when the remaining routing fields match. The deterministic FeedIterator/FeedResponse mock exercises the query-result validation boundary. It does not establish live Cosmos cross-partition behavior or production RU/transaction behavior. Test-only change; no runtime source, queue ordering, schema, mobile behavior or acceptance-row status changed.

Source/test commit [`e4ac79ddfa23ed8924cd49c4df7e6c4bdef5d8b7`](https://github.com/rajeevp727/turnova-platform/commit/e4ac79ddfa23ed8924cd49c4df7e6c4bdef5d8b7) passed exact-head API-only CI [#45663377134](https://github.com/rajeevp727/turnova-platform/actions/runs/45663377134): **1,692/1,692 backend tests (0 failed, 0 skipped)**; .NET 10 Release build (0 warnings/errors); native non-container Linux x64 publish (44,086,068 bytes); publish-script syntax; native verifier (11 negative fixtures plus valid fixture). Artifact [15046621981](https://github.com/rajeevp727/turnova-platform/actions/runs/45663377134/artifacts/15046621981), 17,040,774 bytes; ZIP SHA-256 `cc3fec9380e72a9655c618ac3925f0b1d3a4a5da528b702926e295ac09496ac6`. Mobile-auth/mobile/Android/emulator/iOS/simulator jobs were intentionally skipped under the API-first phase gate.

Strict acceptance remains **0/19 Verified** (16 In Progress, 3 Blocked: DISPATCH-01, DRIVER-04, SUPPORT-01). No row is promoted. PR #200 remains open and requires independent human maintainer approval; the three owner-authored COMMENTED reviews are not approvals. PR #196/#198/#205/#206 remain Draft/parked. Android/iOS remain gated. No live Cosmos or production readiness is claimed.



## Run 187 — malformed eligible queue item on a later Cosmos query page

| Test-plan ID | Scenario | Automated evidence | Expected result |
|---|---|---|---|
| COSMOS-STORE-032 | A valid first eligible-queue page is followed by an otherwise eligible item whose stored `ZoneServiceKey` belongs to another partition | `CosmosQueueResourceContractTests.QueueEligibilityRejectsMalformedLaterPageInsteadOfReturningPartialCandidates` | `GetEligibleAsync` throws on the malformed second page; it never returns the valid first-page item as a partial candidate collection |

This closes the multi-page fail-closed boundary adjacent to COSMOS-STORE-031. The deterministic FeedIterator/FeedResponse mock supplies one valid page and then a malformed cross-partition item. It verifies both pages are read and the query fails before a result is returned. This is SDK-mock evidence only; it does not establish live Cosmos cross-partition, RU, transaction, ETag or multi-instance behavior. Test-only change; no runtime source, queue ordering, schema, mobile behavior or acceptance-row status changed.

Source/test commit [`c1e05834`](https://github.com/rajeevp727/turnova-platform/commit/c1e0583406ee05cd62aa3142ba28c57c617ea952) passed exact-head API-only CI [#45670614245](https://github.com/rajeevp727/turnova-platform/actions/runs/45670614245): **1,693/1,693 backend tests (0 failed, 0 skipped)**; .NET 10 Release build (0 warnings/errors); native non-container Linux x64 publish (44,086,068 bytes); publish-script syntax; native verifier (11 negative fixtures plus valid fixture). Artifact [15048329794](https://github.com/rajeevp727/turnova-platform/actions/runs/45670614245) was uploaded (17,040,776 bytes). Mobile-auth/mobile/Android/emulator/iOS/simulator jobs were intentionally skipped under the API-first phase gate.

Strict acceptance remains **0/19 Verified** (16 In Progress, 3 Blocked: DISPATCH-01, DRIVER-04, SUPPORT-01). No row is promoted. PR #200 remains the sole ready/active feature PR and requires independent human maintainer approval; the three owner-authored COMMENTED reviews are not approvals. PR #196/#198/#205/#206 remain Draft/parked. Android/iOS remain gated. No live Cosmos or production readiness is claimed.


Documentation checkpoint [`ee28d68b7dfec30938d0a0a8380f522887ceb503`](https://github.com/rajeevp727/turnova-platform/commit/ee28d68b7dfec30938d0a0a8380f522887ceb503) passed exact-head API-only CI [#45671519619](https://github.com/rajeevp727/turnova-platform/actions/runs/45671519619): 1,693/1,693 backend tests, Release build with 0 warnings/errors, native non-container publish/verifier (11 negative fixtures plus valid fixture), artifact [15050127228](https://github.com/rajeevp727/turnova-platform/actions/runs/45671519619/artifacts/15050127228). Mobile jobs were intentionally skipped under the API-first phase gate.


## Run 188 — malformed queue audit event on a later Cosmos query page

| Test-plan ID | Scenario | Automated evidence | Expected result |
|---|---|---|---|
| COSMOS-STORE-033 | A valid first queue-audit page is followed by an event whose stored `ZoneServiceKey` belongs to another partition | `CosmosQueueResourceContractTests.QueueAuditReadRejectsMalformedLaterPageInsteadOfReturningPartialHistory` | `GetAuditEventsAsync` throws on the malformed second page; it never returns the valid first-page event as partial audit history |

This closes the multi-page fail-closed boundary for internal queue audit reads adjacent to COSMOS-STORE-032. The deterministic FeedIterator/FeedResponse mock supplies one valid page and then a malformed cross-partition event. It verifies both pages are read and the query fails before a result is returned. This is SDK-mock evidence only; it does not establish live Cosmos cross-partition, RU, transaction, ETag or multi-instance behavior. Test-only change; no runtime source, audit schema, queue ordering, mobile behavior or acceptance-row status changed.

Source/test commit [`f3c0bb98`](https://github.com/rajeevp727/turnova-platform/commit/f3c0bb9863ea22443558b399ce7ca6c4d27c87f5) passed exact-head API-only CI [#45673119646](https://github.com/rajeevp727/turnova-platform/actions/runs/45673119646): **1,694/1,694 backend tests (0 failed, 0 skipped)**; .NET 10 Release build (0 warnings/errors); native non-container Linux x64 publish (44,086,072 bytes); publish-script syntax; native verifier (11 negative fixtures plus valid fixture). Artifact [15049754253](https://github.com/rajeevp727/turnova-platform/actions/runs/45673119646/artifacts/15049754253) was uploaded (17,040,778 bytes). Mobile-auth/mobile/Android/emulator/iOS/simulator jobs were intentionally skipped under the API-first phase gate.

Strict acceptance remains **0/19 Verified** (16 In Progress, 3 Blocked: DISPATCH-01, DRIVER-04, SUPPORT-01). No row is promoted. PR #200 remains the sole ready/active feature PR and requires independent human maintainer approval; the three owner-authored COMMENTED reviews are not approvals. PR #196/#198/#205/#206 remain Draft/parked. Android/iOS remain gated. No live Cosmos or production readiness is claimed.
