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
