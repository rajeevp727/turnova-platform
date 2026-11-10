# TurnOva Feature Test Plan

**Scope:** TurnOva Rider and Driver Cab-only Android/iOS MVP, including authentication, union-grouped discovery, selected-driver negotiation, trip lifecycle, direct-payment reporting and release gates. Legacy FIFO cases are compatibility regression tests only; they are not the approved customer-facing Rider flow.
**Test types:** automated unit/CI, API integration, mobile functional, security, and manual acceptance.
**Priority:** P0 = release blocker, P1 = high, P2 = normal.
**Status convention:** Planned means the case is specified but has not yet been executed. A green build/type-check is not evidence that a functional scenario passed. PRD.md §15 is authoritative where earlier test sections describe multi-category or customer-facing FIFO behaviour.

## 1. Authentication — registration

| ID | Scenario | Steps / input | Expected result | Type | Priority |
|---|---|---|---|---|---|
| AUTH-REG-001 | Driver registration succeeds | Submit valid email, valid password and name from Driver app | TriSend creates a `turnova` account with `Driver` role; tokens returned and session stored securely | API + mobile | P0 |
| AUTH-REG-002 | Rider registration succeeds | Submit valid email, valid password and name from Rider app | Account created with `Rider` role; tokens returned and session stored | API + mobile | P0 |
| AUTH-REG-003 | Email is normalized | Register with leading/trailing spaces and mixed case | Email is trimmed; identity follows TriSend normalization rules | API | P1 |
| AUTH-REG-004 | Duplicate email registration | Register same email twice for same app | Second request returns conflict/validation response; no duplicate account/session | API | P0 |
| AUTH-REG-005 | Email missing | Submit null, empty or whitespace email | HTTP 400; TriSend is not called | API | P0 |
| AUTH-REG-006 | Password missing | Submit null, empty or whitespace password | HTTP 400; TriSend is not called | API | P0 |
| AUTH-REG-007 | Invalid email format | Submit malformed email | Validation error from auth service; no token issued | API | P1 |
| AUTH-REG-008 | Weak/short password | Submit password below TriSend policy | Validation error; no account or token created | API | P0 |
| AUTH-REG-009 | Name omitted | Register without optional name | Registration follows TriSend's optional-name contract; no server error | API | P2 |
| AUTH-REG-010 | Role tampering | Add `role: "Admin"` or `role: "Rider"` to Driver request | Server ignores client role; Driver route forces Driver role | API + security | P0 |
| AUTH-REG-011 | Invalid role route | Call `/{role}/register` with `admin`, `fleet`, or unknown role | HTTP 404; TriSend is not called | API | P0 |
| AUTH-REG-012 | Cross-app role conflict | Register/login account whose existing TurnOva role differs from requested app | Request denied; wrong-role tokens are not returned to client | API + security | P0 |
| AUTH-REG-013 | TriSend unavailable | Simulate DNS failure, connection refusal or 5xx | Stable 503 response; no credentials/tokens leaked | API | P0 |
| AUTH-REG-014 | TriSend timeout | Simulate response beyond configured timeout | HTTP 504; client can retry safely | API | P1 |

## 2. Authentication — login and token lifecycle

| ID | Scenario | Steps / input | Expected result | Type | Priority |
|---|---|---|---|---|---|
| AUTH-LOGIN-001 | Driver login succeeds | Existing Driver credentials | Access and refresh tokens returned; Driver app opens authenticated home | API + mobile | P0 |
| AUTH-LOGIN-002 | Rider login succeeds | Existing Rider credentials | Access and refresh tokens returned; Rider app opens authenticated home | API + mobile | P0 |
| AUTH-LOGIN-003 | Incorrect password | Valid email, wrong password | 401/validation response; no token stored | API + mobile | P0 |
| AUTH-LOGIN-004 | Unknown email | Non-existent email | Authentication rejected without account disclosure beyond TriSend's contract | API | P1 |
| AUTH-LOGIN-005 | Empty login fields | Empty email/password | HTTP 400; auth service not called | API | P0 |
| AUTH-LOGIN-006 | Driver tries Rider account | Rider account logs in via Driver route | HTTP 403; session is revoked where possible; no wrong-role token accepted | API + security | P0 |
| AUTH-LOGIN-007 | Rider tries Driver account | Driver account logs in via Rider route | HTTP 403; session is revoked where possible; no wrong-role token accepted | API + security | P0 |
| AUTH-LOGIN-008 | Refresh token rotation | Refresh with valid refresh token | New access/refresh tokens returned; old refresh token cannot be reused if TriSend rotates it | API + mobile | P0 |
| AUTH-LOGIN-009 | Invalid refresh token | Random, malformed or expired refresh token | 401; local session cleared; app returns to sign-in | API + mobile | P0 |
| AUTH-LOGIN-010 | Reuse rotated refresh token | Refresh twice using the old token | Second request rejected; client must use newest token | API | P0 |
| AUTH-LOGIN-011 | Missing refresh token | Empty/null refresh token | 400/401; no token issued | API | P1 |
| AUTH-LOGIN-012 | Expired access token | Call protected API with expired access token | 401; app attempts refresh once and retries original request once | API + mobile | P0 |
| AUTH-LOGIN-013 | Expired access and refresh tokens | Expire both tokens | Session cleared and sign-in screen displayed | Mobile | P0 |
| AUTH-LOGIN-014 | App restart with valid tokens | Kill and relaunch app with a complete stored pair | Session restored using SecureStore and `/me`; no login prompt | Mobile unit + device | P0 |
| AUTH-LOGIN-015 | App restart with invalid access but valid refresh | Store expired access token and valid refresh token, relaunch | `/me` 401 triggers one refresh; rotated pair is stored, then `/me` is fetched with the new access token and only that authoritative profile is restored | Mobile unit + device | P0 |
| AUTH-LOGIN-016 | Concurrent refresh attempts | Trigger multiple 401 responses together | No stale refresh token overwrites newer token; one refresh flow is serialized or safely coordinated | Mobile | P0 |
| AUTH-LOGIN-017 | Logout succeeds | Sign out while online | TriSend session revocation is attempted; both local token keys are deleted; sign-in screen shown | API + mobile | P0 |
| AUTH-LOGIN-018 | Logout while offline | Sign out with network unavailable | Local token deletion proceeds even when server revocation fails; app signs out; server session may remain until expiry | Mobile unit + device | P1 |
| AUTH-LOGIN-019 | Secure storage failure | Simulate SecureStore read/write/delete failure | User sees a recoverable error; no partial token pair is accepted; app does not silently claim a restored or completed logout state | Mobile unit + device | P1 |
| AUTH-LOGIN-020 | Session limit reached | Sign in when TriSend's session limit is reached; test both replaceOldest false/true | TriSend policy is respected; no unexpected session is evicted | API + mobile | P1 |
| AUTH-LOGIN-021 | Concurrent unauthorized API responses | Trigger multiple protected API requests that all receive 401 while refresh-token rotation is enabled | Both apps share one in-flight refresh operation, persist the rotated token pair once, and retry each original request with the new access token | Mobile | P0 |
| AUTH-LOGIN-022 | Late 401 after another request rotates the session | First request refreshes and persists a new token pair; a delayed request using the old access token then receives 401 | Client reads the current stored pair inside the shared refresh operation and retries with the latest access token without reusing the rotated refresh token | Mobile unit | P0 |
| AUTH-LOGIN-023 | Partial secure token pair | Store only the access key or only the refresh key, then restart | Both keys are cleared; no half-authenticated session is restored | Mobile unit | P0 |
| AUTH-LOGIN-024 | Network failure during session restore | Relaunch with a complete token pair while the profile or refresh endpoint is unavailable | Preserve stored tokens and show a recoverable error; do not falsely sign out a potentially valid session | Mobile unit | P1 |
| AUTH-LOGIN-025 | Secure token-pair write failure | Allow access-key write but fail refresh-key write | Best-effort rollback attempts deletion of both keys; no authenticated app state is accepted | Mobile unit | P1 |
| AUTH-LOGIN-026 | Secure token deletion failure | Fail deletion of one key during logout | Attempt both deletions; surface a recoverable error and do not claim logout succeeded | Mobile unit | P1 |
| AUTH-LOGIN-027 | Logout races an in-flight token rotation write | Pause the access-token write, begin local logout, then release the write | Storage mutations are serialized; logout clears both keys after the pending pair write and no credentials remain | Mobile unit | P0 |
| AUTH-LOGIN-028 | Refresh response omits profile | Expire the stored access token; return only access/refresh tokens from refresh | Persist the rotated pair, call `/me` with the new access token and restore the returned profile | Mobile unit | P0 |
| AUTH-LOGIN-029 | Profile lookup fails after token rotation | Return a new token pair, then make `/me` return a transient 503 | Preserve the newly rotated pair and surface a recoverable restore error; do not revert to the consumed refresh token or clear the session | Mobile unit | P0 |
| AUTH-LOGIN-030 | Refreshed access token is rejected | Return a new token pair, then make `/me` return 401 | Clear both stored token keys and return to sign-in; no authenticated profile is restored | Mobile unit | P0 |
| AUTH-LOGIN-031 | Logout invalidates an in-flight refresh during token persistence | Pause the access-token write from a refresh; begin logout so the session epoch advances; then release the write | The epoch-aware save aborts the stale pair, attempts both-key cleanup inside the serialized mutation, and the queued logout leaves no credentials or authenticated state | Shared mobile unit | P0 |
| AUTH-LOGIN-032 | A new login supersedes an older refresh write | Pause an old refresh access-token write; begin a new login and advance the epoch; then release the old write | The old refresh cannot persist either token or restore old in-memory state; after serialized cleanup, the new login pair is the only stored pair | Shared mobile unit + Rider/Driver typecheck | P0 |
| AUTH-LOGIN-033 | A refresh queued after the session epoch changes | Queue a refresh token-pair save, then advance the epoch before its storage mutation begins | The save returns stale without issuing any secure-storage writes; current session credentials remain unchanged | Shared mobile unit | P0 |
| AUTH-LOGIN-034 | Logout invalidates a refresh while the refresh-token key write is in flight | Pause the refresh-token write after the access key has been written; advance the session epoch for logout; then release the refresh-key write | The helper detects the stale epoch after the second key, clears both keys before releasing the serialized mutation, and the queued logout leaves no credentials | Shared mobile unit | P0 |
| AUTH-MOBILE-028 | App-specific profile role guard | Return a Rider profile to the Driver app or a Driver profile to the Rider app during login/register or /me restoration | The client rejects the mismatched profile before saving a new token pair or setting authenticated app state | Shared mobile unit + Rider/Driver typecheck | P0 |

## 2A. Authentication API integration contracts

The API contract tests use a fake named TriSend HTTP handler; CI must never call the production authentication service.

| ID | Scenario | Expected result | Type | Priority |
|---|---|---|---|---|
| AUTH-API-001 | Driver registration | API trims email/name and sends server-owned appId=turnova and role=Driver | API integration | P0 |
| AUTH-API-002 | Missing registration credentials | HTTP 400; TriSend is not called | API integration | P0 |
| AUTH-API-003 | Unsupported registration role | HTTP 404; TriSend is not called | API integration | P0 |
| AUTH-API-004 | Login returns a different app role | HTTP 403; returned access token is sent to TriSend logout | API integration | P0 |
| AUTH-API-005 | Refresh request | Refresh token is forwarded unchanged to TriSend | API integration | P0 |
| AUTH-API-006 | TriSend connection failure | Stable HTTP 503; upstream exception details are not exposed | API integration | P0 |
| AUTH-API-007 | Duplicate registration | TriSend HTTP 409 is preserved as a conflict and no access/refresh token is returned | API integration | P0 |
| AUTH-API-008 | Rider profile read | `/api/v1/auth/me` forwards the bearer token and returns the authenticated profile from TriSend | API integration | P0 |
| AUTH-API-009 | Rider app rejects a Driver account | Log in to the Rider route with a TriSend profile whose role is Driver | HTTP 403; returned access token is revoked through TriSend logout and is not returned to the Rider client | API integration | P0 |

## 3. JWT validation and authorization

| ID | Scenario | Steps / input | Expected result | Type | Priority |
|---|---|---|---|---|---|
| AUTHZ-001 | Valid Driver JWT | Call Driver endpoint with valid Driver token | Request succeeds | API integration | P0 |
| AUTHZ-002 | Valid Rider JWT | Call Rider endpoint with valid Rider token | Request succeeds | API integration | P0 |
| AUTHZ-003 | No bearer token | Call protected endpoint without Authorization header | HTTP 401 | API integration | P0 |
| AUTHZ-004 | Malformed bearer token | Send random string or malformed JWT | HTTP 401; no unhandled exception | API integration | P0 |
| AUTHZ-005 | Invalid signature | Sign token using different key | HTTP 401 | API integration | P0 |
| AUTHZ-006 | Wrong issuer | Token issuer differs from configured issuer | HTTP 401 | API integration | P0 |
| AUTHZ-007 | Wrong audience | Token audience differs from configured audience | HTTP 401 | API integration | P0 |
| AUTHZ-008 | Expired token | Use expired token | HTTP 401 | API integration | P0 |
| AUTHZ-009 | Not-yet-valid token | Use token with future not-before time beyond allowed clock skew | HTTP 401 | API integration | P1 |
| AUTHZ-010 | Rider calls Driver endpoint | Rider token calls queue list/join | HTTP 403 | API integration | P0 |
| AUTHZ-011 | Driver calls Rider endpoint | Driver token requests ride or reads ride | HTTP 403 | API integration | P0 |
| AUTHZ-012 | Unknown role | JWT role is neither Driver nor Rider | HTTP 403 | API integration | P0 |
| AUTHZ-013 | Missing JWT configuration | Start API without issuer/audience/key | Startup fails with clear configuration error | API | P0 |
| AUTHZ-014 | Weak signing key | Configure key shorter than 32 characters | Startup fails | API | P0 |
| AUTHZ-015 | Non-HTTPS TriSend URL | Configure HTTP or invalid BaseUrl | Startup/configuration validation fails | API | P0 |
| AUTHZ-016 | Secrets not in client bundle | Inspect mobile bundle and repository | JWT signing key and backend secrets are absent | Security review | P0 |

## 4. Driver — queue

| ID | Scenario | Steps / input | Expected result | Type | Priority |
|---|---|---|---|---|---|
| DRV-Q-001 | Load queue | Driver requests queue for valid zone/service class | Ordered queue with positions returned | API + mobile | P0 |
| DRV-Q-002 | Join queue | Authenticated Driver joins valid zone/class | Driver is added once and receives correct position | API + mobile | P0 |
| DRV-Q-003 | Duplicate join | Same Driver joins same queue twice | Conflict response; one queue entry remains | API | P0 |
| DRV-Q-004 | Missing zone | Join with empty/null zone | HTTP 400; no entry created | API | P0 |
| DRV-Q-005 | Unknown zone | Join a zone that does not exist | Validation/not-found response; no orphan queue entry | API | P1 |
| DRV-Q-006 | Unsupported service class | Join unsupported class | Validation/not-found response | API | P1 |
| DRV-Q-007 | Missing identity claim | Validly signed token without subject | Request rejected; no queue entry created | API security | P0 |
| DRV-Q-008 | Spoof DriverId | Send another driver's ID in request body | Server uses JWT subject; cannot join as another Driver | API security | P0 |
| DRV-Q-009 | FIFO ordering | Add multiple Drivers with increasing join times/sequences | Queue order remains first-in-first-out | Unit + API | P0 |
| DRV-Q-010 | Ineligible/disabled Driver | Attempt to queue with inactive eligibility state | Driver is not offered/queued when eligibility enforcement is introduced | Domain integration | P1 |
| DRV-Q-011 | Concurrent joins | Same Driver submits parallel join requests | At most one active queue entry is stored | Concurrency | P0 |
| DRV-Q-012 | Queue store unavailable | Simulate Cosmos/in-memory store failure | Controlled 5xx; no false success response | API integration | P0 |
| DRV-Q-013 | Empty queue | Read queue with no entries | HTTP 200 and empty list | API | P2 |
| DRV-Q-014 | Refresh queue view | Driver refreshes current queue | Updated position/list displayed without rejoining | Mobile | P1 |
| DRV-Q-015 | Read own membership | Driver requests the current membership endpoint | Returns queued state and 1-based position, or not queued with null position | API | P0 |
| DRV-Q-016 | Leave queue | Driver leaves its current zone/service-class queue | Only that membership is removed; remaining drivers retain FIFO order | API + mobile | P0 |
| DRV-Q-017 | Repeat leave | Driver repeats leave after membership is already absent | Safe idempotent response; no error or unrelated membership removal | API | P1 |
| DRV-Q-018 | Rejoin after leaving | Driver leaves and joins the same queue again | New membership is placed at the end of the current FIFO queue | API + mobile | P0 |
| DRV-Q-019 | Leave another queue scope | Driver has memberships in different zones/classes and leaves one | Memberships in other queue scopes remain unchanged | API + persistence | P0 |

## 5. Rider — ride request and ownership

| ID | Scenario | Steps / input | Expected result | Type | Priority |
|---|---|---|---|---|---|
| RID-001 | Submit ride request | Valid pickup, drop-off and passenger count | HTTP 201; unique ride ID and requested status returned | API + mobile | P0 |
| RID-002 | Default passenger count | Omit passenger count | Defaults to one passenger | API | P1 |
| RID-003 | Minimum passenger count | Submit one passenger | Accepted | API | P1 |
| RID-004 | Maximum passenger count | Submit six passengers | Accepted | API | P1 |
| RID-005 | Passenger count below minimum | Submit zero or negative count | Reject with HTTP 400 (not silently normalize) | API | P0 |
| RID-006 | Passenger count above maximum | Submit seven or larger | Reject with HTTP 400 (not silently clamp) | API | P0 |
| RID-007 | Missing pickup | Submit empty/null pickup | HTTP 400; no ride created | API + mobile | P0 |
| RID-008 | Missing drop-off | Submit empty/null drop-off | HTTP 400; no ride created | API + mobile | P0 |
| RID-009 | Whitespace locations | Submit whitespace-only pickup/drop-off | HTTP 400 | API | P1 |
| RID-010 | Driver submits ride request | Call ride request with Driver token | HTTP 403 | API security | P0 |
| RID-011 | Read own ride | Rider reads own ride ID | HTTP 200 with ride details | API + mobile | P0 |
| RID-012 | Read another Rider's ride | Rider B reads Rider A's ride ID | HTTP 404/403; no ride data leaked | API security | P0 |
| RID-013 | Unknown ride ID | Read non-existent ID | HTTP 404 | API | P1 |
| RID-014 | Malformed ride ID | Read invalid route value | HTTP 404; no server exception | API | P2 |
| RID-015 | Duplicate submit | Double tap booking / retry same request | Idempotency policy prevents accidental duplicate booking when implemented | API + mobile | P1 |
| RID-016 | Ride storage restart | Restart API and query prior ride | Persisted ride remains available when durable ride storage is implemented; current in-memory MVP is expected to lose it | Integration | P0 before production |
| RID-017 | Rider UI shows demo limitations | Request ride in demo mode | Clearly says no driver assigned and no payment taken | Mobile | P1 |

## 6. Driver/Rider mobile usability and resilience

| ID | Scenario | Steps / input | Expected result | Type | Priority |
|---|---|---|---|---|---|
| MOB-001 | Form validation | Submit with required fields empty | Submit disabled or clear validation displayed | Mobile | P1 |
| MOB-002 | Password visibility | Enter password | Password is masked by default | Mobile | P1 |
| MOB-003 | Double tap submit | Tap login/register/book rapidly | One request in flight; no duplicate action | Mobile | P1 |
| MOB-004 | Network offline | Disable network and authenticate | Clear retryable error; no crash | Mobile | P0 |
| MOB-005 | Slow network | Delay auth response | Loading state visible; repeated submit prevented | Mobile | P1 |
| MOB-006 | API returns 4xx | Submit invalid credentials | Useful safe error shown; no raw stack trace | Mobile | P1 |
| MOB-007 | API returns 5xx | Simulate backend failure | Friendly error shown; session state preserved appropriately | Mobile | P1 |
| MOB-008 | Android physical device | Configure reachable API base URL | App connects to API; emulator-only host is not used on physical device | Mobile | P0 |
| MOB-009 | App background/foreground | Background and resume app | Valid session remains available; no token printed to logs | Mobile | P1 |
| MOB-010 | Sign out navigation | Sign out from Profile | Auth screens appear; protected tabs are inaccessible | Mobile | P0 |
| MOB-011 | Role separation | Attempt to open other app's role-only flow | App routes and backend authorization prevent cross-role operations | Mobile + API | P0 |
| MOB-012 | Accessibility/small screen | Test narrow device, keyboard open, large font | Inputs/buttons remain visible and usable | Mobile | P2 |

## 7. Platform, configuration and data safety

| ID | Scenario | Steps / input | Expected result | Type | Priority |
|---|---|---|---|---|---|
| PLAT-001 | Health endpoint | GET `/health` | HTTP 200 with healthy service status; no secrets disclosed | API | P1 |
| PLAT-002 | Root endpoint | GET `/` | Service/version/storage mode returned; no secrets disclosed | API | P2 |
| PLAT-003 | Partial Cosmos configuration | Configure only endpoint or key | Startup fails with actionable message | API | P0 |
| PLAT-004 | Cosmos unavailable | Start/use API with unavailable Cosmos | Controlled startup/request failure; no false healthy status | Integration | P0 |
| PLAT-005 | In-memory mode | Run without Cosmos settings | Demo queue seeds load; health reports demo mode | Integration | P1 |
| PLAT-006 | CORS | Request from configured mobile origin | Allowed origin succeeds; untrusted origins are denied in production policy | Security | P1 |
| PLAT-007 | Sensitive log review | Trigger login failure and upstream failure | No password, bearer token, refresh token or signing key in logs | Security | P0 |
| PLAT-008 | Dependency/CI check | Run .NET restore/build/test and both app typechecks | All jobs pass on PR | CI | P0 |
| PLAT-RES-001 | Reservation acquisition audit | Acquire a selected-ride or technical Driver lock | Active reservation and ReservationAcquired event are one same-partition Cosmos transaction; deterministic in-memory test records exactly one event | API unit + Cosmos integration | P0 |
| PLAT-RES-002 | Reservation mutation audit | Promote, change expiry and release a matching reservation | Each successful state change appends exactly one event in the same transaction; ETag conflict or missing item appends none | API unit + Cosmos integration | P0 |
| PLAT-RES-003 | Reservation retry/idempotency | Retry same-ride acquire, same-state promotion/expiry and release | No duplicate event on an idempotent no-op; a wrong reservation ID cannot release another lease | API unit | P0 |
| PLAT-RES-004 | Reservation audit privacy | Inspect serialized reservation audit events with invalid actor metadata | Only bounded actor roles/reason categories and opaque actor references persist; route, location, contact, KYC, token, fare and chat data are absent | API unit + security review | P0 |

## 8. Existing automated test baseline

Currently automated in the .NET test project:
- FIFO queue chooses the lowest sequence eligible Driver.
- FIFO queue rejects duplicate Driver entries in the same queue.

The GitHub Actions workflow currently runs:
- .NET 10 restore, Release build and test.
- Driver app TypeScript typecheck.
- Rider app TypeScript typecheck.

**Important:** The CI workflow passing confirms compilation and the existing automated tests/typechecks only. The API integration, TriSend contract, mobile runtime, JWT security and end-to-end scenarios above still require automated coverage or execution against test environments. Cases marked Planned have not been executed.

## 9. Recommended execution order

1. P0 authentication registration/login/refresh/logout and role mismatch tests.
2. P0 JWT signature/issuer/audience/expiry and cross-role authorization tests.
3. P0 queue identity spoofing, duplicate/concurrent join and FIFO tests.
4. P0 ride validation and cross-rider ownership tests.
5. P0 mobile session restore, refresh failure, logout and network failure tests.
6. P1/P2 resilience, usability and platform tests.

## 10. Known acceptance gaps to close before production

- Add API integration tests with a fake TriSend HTTP handler and test JWTs; do not call the production auth service from CI.
- Add mobile component/integration tests for login/register, secure storage, refresh, logout and duplicate taps.
- Serialize concurrent token refreshes to prevent refresh-token rotation races.
- Validate passenger count as 1–6 instead of silently clamping invalid values.
- Add request idempotency for ride creation.
- Add durable ride persistence, driver eligibility/onboarding and production CORS policy.
- Add session revocation strategy if immediate access-token invalidation is required.

## 11. Ride persistence, acceptance and live tracking (planned until CI/device validation)

| ID | Scenario | Expected result | Type | Priority |
|---|---|---|---|---|
| RID-PERSIST-001 | Create ride with valid pickup coordinates | Ride is stored and survives API restart in Cosmos mode | API + Cosmos integration | P0 |
| RID-PERSIST-002 | Invalid passenger count or coordinates | HTTP 400; no ride is created | API | P0 |
| RID-DRV-001 | Driver lists requested rides | Only Requested rides are returned to authenticated Drivers | API | P0 |
| RID-DRV-002 | Driver accepts a requested ride | Ride becomes Accepted and is assigned to JWT subject | API | P0 |
| RID-DRV-003 | Two drivers accept the same ride concurrently | Exactly one driver wins; other receives conflict | Concurrency + Cosmos integration | P0 |
| TRACK-001 | Assigned driver posts valid coordinates | Latest coordinates and timestamp are persisted | API + Cosmos | P0 |
| TRACK-002 | Unassigned driver posts coordinates | Request is denied; no location is changed | API security | P0 |
| TRACK-003 | Rider reads another rider's ride | HTTP 404; no ride or location data is disclosed | API security | P0 |
| TRACK-004 | Rider Activity screen is open | Map refreshes from authoritative ride state while screen is active | Mobile E2E | P1 |
| TRACK-005 | Driver leaves Trips screen | Foreground location subscription is removed | Mobile | P0 |
| TRACK-006 | Location permission denied | Driver sees an actionable error; no false location-sharing state | Mobile | P1 |

## 12. Ride lifecycle, idempotency and SignalR

| ID | Scenario | Expected result | Type | Priority |
|---|---|---|---|---|
| RID-LIFE-001 | Driver accepts requested ride | State changes to Accepted and location sharing becomes active | API | P0 |
| RID-LIFE-002 | Driver marks arrival | Only assigned Driver can move Accepted to Arrived | API + security | P0 |
| RID-LIFE-003 | Driver starts trip | Only assigned Driver can move Arrived to InProgress | API + security | P0 |
| RID-LIFE-004 | Driver completes trip | Only assigned Driver can move InProgress to Completed and sharing stops | API + security | P0 |
| RID-LIFE-005 | Invalid state transition | API returns 409 and state remains unchanged | API | P0 |
| RID-LIFE-006 | Rider cancels before start | Owner can cancel Requested, Accepted or Arrived ride | API | P0 |
| RID-LIFE-007 | Rider cancels after trip start | API returns 409; ride remains InProgress | API | P0 |
| RID-LIFE-008 | Non-owner cancels or subscribes | Ride is not disclosed; request is denied | API + SignalR security | P0 |
| RID-LIFE-009 | Retry arrive, start and complete after each successful transition | Retry returns the same authoritative state; completion audit remains single, reservation stays released, and terminal location sharing cannot resume | API + idempotency | P0 |
| RID-LIFE-010 | Retry completion after the Completed ride write succeeded but work-activity/reservation cleanup was interrupted | Retry repairs the deterministic Completed work-activity record and releases only the matching Driver reservation; repeated retries do not duplicate the activity | API failure-recovery + idempotency | P0 |
| RID-IDEM-001 | Repeat request with same rider and Idempotency-Key | Original ride is returned; no second ride is created | API + Cosmos | P0 |
| RID-IDEM-002 | Same key used by different riders | Separate ride requests are allowed | API + Cosmos | P1 |
| RID-IDEM-003 | Missing or oversized idempotency key | HTTP 400; no ride is created | API | P0 |
| TRACK-007 | Assigned Driver posts location during active ride | Latest location is broadcast to the owning Rider's SignalR group | API + SignalR | P0 |
| TRACK-008 | Driver posts location after completion/cancellation | HTTP 404/409; no location update or broadcast | API security | P0 |
| TRACK-009 | Rider SignalR connection uses another Rider's ride ID | Connection is aborted before group membership | SignalR security | P0 |
| TRACK-010 | Rider loses network for longer than the default SignalR retry window while Activity is open | The client keeps retrying with bounded exponential backoff (maximum 30 seconds), then reloads owner-scoped ride history and agreement versions when reconnected | Mobile helper + Android/iOS device | P0 |
| TRACK-011 | Driver leaves Trips screen | Foreground GPS subscription is disposed | Mobile | P0 |
| TRACK-012 | Rider reconnects after missing a status/location/agreement event | Rider reloads owner-scoped ride history and current ride details plus agreement versions; terminal-only history clears stale active ride state | Mobile + API | P0 |
| TRACK-013 | Driver loses network during a selected booking or active trip, including an outage longer than the default retry window | Driver notification, agreement and ride-status connections continue bounded retries; reconnect reloads available requests, own selected/active/terminal ride history and agreement versions | Mobile helper + Android/iOS device | P0 |
| TRACK-014 | Non-owner Rider/Driver attempts to join a ride group, or Driver requests another Driver notification group | Hub aborts the connection before adding any group membership | SignalR security | P0 |
| TRACK-015 | Driver app backgrounds during an active selected Cab trip or resumes after backgrounding | GPS watcher is removed in background, late callbacks are ignored, and resume reloads authenticated Driver history plus foreground permission before restarting only if the same ride remains Accepted/Arrived/InProgress with server sharing active | Mobile helper + Android/iOS E2E | P0 |
| TRACK-016 | A RideStatusChanged event arrives after its screen effect is cleaned up or for a different ride | Both Rider and Driver ignore the stale/mismatched event; a late Driver callback cannot mutate ride state after tab, ride or session cleanup | Shared mobile unit + Rider/Driver Expo typecheck and production bundles | P0 |
| TRACK-017 | Driver receives a live RideStatusChanged event for the selected ride | The matching active-trip card and matching history row update immutably; unrelated history rows remain unchanged; location sharing is true only for Accepted/Arrived/InProgress and false for terminal statuses; the handler is removed when the Trips effect is cleaned up | Shared helper unit + Driver Expo typecheck and production bundle | P0 |

The shared `signalRConnection.test.cjs` tests the 1s/2s/4s/... retry sequence, 30-second cap, invalid retry-count rejection, initial connection failure retry and cancellation when the screen is unmounted. Rider and Driver SignalR clients use this same capped exponential policy for reconnect after an established connection drops and `startSignalRConnection` retries initial connection failures until the owning screen/effect is disposed. 

The shared `rideRecovery.test.cjs` also verifies immutable `RideStatusChanged` projection updates for the active Driver card and matching history row, no-op behavior for unrelated/malformed updates, and location-sharing alignment with foreground trip statuses. The lifecycle state-machine unit tests and in-memory API integration tests are automated. `RideTrackingHubTests` exercises the actual `OnConnectedAsync` group-add/abort path for an owning Rider and Driver notification subscription, as well as the pure role/ownership authorization rules. API integration coverage includes the full selected-Cab negotiation-to-completion lifecycle, agreement-gated start, assigned-Driver-only location updates, Rider-visible latest coordinates, invalid coordinate rejection, terminal location-sharing shutdown, cancellation and reservation release. `SelectedCabLifecycleRetriesAreIdempotentAndKeepTerminalState` additionally retries arrival, start and completion, then verifies one completion work-activity record, a released reservation, terminal Rider history and rejected post-completion location/restart attempts. `CompletedRideRetryRepairsWorkActivityAndReservationReleaseAfterInterruptedCompletion` simulates a process interruption after the Completed ride snapshot is persisted but before the separate work-activity/reservation writes; a completion retry repairs both idempotently. `RideLifecycleTests` covers finite geographic coordinate validation. shared `rideRecovery` and `tripLocationSharing` tests verify role-specific recovery and foreground-only location eligibility. Live SignalR WebSocket delivery/reconnect and Android/iOS foreground/background/resume behaviour remain physical-device gates; Cosmos-specific concurrency still requires the configured integration environment.

## 13. Cosmos DB configuration, migration and readiness

| ID | Scenario | Expected result | Type | Priority |
|---|---|---|---|---|
| COSMOS-CONFIG-001 | Configure valid HTTPS endpoint, key and database ID | API starts and Cosmos client is configured | Unit + startup | P0 |
| COSMOS-CONFIG-002 | Configure HTTP/invalid endpoint or missing key | API fails startup with a clear configuration error | Unit + startup | P0 |
| COSMOS-CONFIG-003 | Configure invalid database ID | API fails startup before attempting schema changes | Unit + startup | P1 |
| COSMOS-MIG-001 | Two API instances start and run migrations together | Only one instance holds the migration lease at a time | Cosmos integration | P0 |
| COSMOS-MIG-002 | A migration lease expires after an instance stops | Another instance conditionally acquires the expired lease | Cosmos integration | P0 |
| COSMOS-MIG-003 | Lease owner releases after migration | Only the owner ETag can delete the lease document | Cosmos integration | P0 |
| COSMOS-MIG-004 | Migration history record already exists | Migration is skipped without reapplying schema changes | Cosmos integration | P0 |
| COSMOS-MIG-005 | Migration fails before history is recorded | Startup fails; a later startup can retry the idempotent migration | Cosmos integration | P0 |
| COSMOS-READY-001 | Cosmos database is reachable | `GET /health/ready` returns HTTP 200 | API + Cosmos | P0 |
| COSMOS-READY-002 | Cosmos database is unavailable | `GET /health/ready` returns HTTP 503 | API + failure injection | P0 |
| COSMOS-READY-003 | API runs in in-memory demo mode | `GET /health/ready` returns HTTP 200 without Cosmos configuration | API | P1 |

Automated tests cover Cosmos configuration validation. The migration lease, Cosmos data-plane readiness and multi-instance migration scenarios still require a real Cosmos development account; they are not claimed as executed by the in-memory CI suite.

## 14. Driver onboarding and eligibility

| ID | Scenario | Expected result | Type | Priority |
|---|---|---|---|---|
| DRV-ONB-001 | Driver submits valid profile, vehicle and document references | Profile is stored as Pending; client cannot set status | API | P0 |
| DRV-ONB-002 | Driver reads own onboarding status | Only the authenticated Driver's profile is returned | API security | P0 |
| DRV-ONB-003 | Driver submits invalid contact/vehicle/document metadata | HTTP 400; no profile is stored | API validation | P0 |
| DRV-ONB-004 | Driver submits an external document URL | HTTP 400; only opaque storage references are accepted | API security | P0 |
| DRV-ONB-005 | Pending or Rejected driver joins queue | HTTP 409; no queue membership is created | API integration | P0 |
| DRV-ONB-006 | Pending or Rejected driver lists/accepts rides | HTTP 403; no dispatch action occurs | API integration | P0 |
| DRV-ONB-007 | Reviewer approves a pending driver with DrivingLicence and VehicleRegistration references | Status becomes Approved; queue and ride actions are allowed | API integration | P0 |
| DRV-ONB-008 | Reviewer rejects a profile without reason | HTTP 400 | API validation | P1 |
| DRV-ONB-009 | Reviewer suspends an approved driver | Status becomes Suspended, current queue membership is removed and subsequent queue/ride actions are denied | API integration | P0 |
| DRV-ONB-010 | Invalid/missing operations key | HTTP 401; profile remains unchanged | API security | P0 |
| DRV-ONB-011 | Driver attempts to overwrite Approved/Suspended profile | HTTP 409 | API | P0 |
| DRV-ONB-012 | Driver attempts to read another Driver's profile | No other driver's data is returned | API security | P0 |
| DRV-ONB-013 | Cosmos mode stores and reads profile | Profile survives API restart in Drivers container | Cosmos integration | P0 |

Automated in-memory API integration coverage includes valid submission, pending queue denial, approval followed by queue admission, invalid document metadata and suspension preventing ride acceptance. Cosmos persistence and production operations identity integration remain unverified until a development Cosmos account and Ops authentication model are available.

## Union management and driver allocation

| ID | Scenario | Expected result |
|---|---|---|
| UNI-001 | Create a union with unique code and valid zone/service classes | Created with Active status |
| UNI-002 | Create a union using an existing code | Conflict; no duplicate union |
| UNI-003 | Create a union with missing name/code/zones/classes or unsupported service class | Bad request |
| UNI-004 | List and retrieve unions using a valid Ops key | Only persisted union records are returned |
| UNI-005 | Create or manage a union without a valid Ops key | Unauthorized |
| UNI-006 | Assign an approved driver to an active compatible union | Current assignment is returned to that driver |
| UNI-007 | Assign a Pending/Rejected/Suspended or unknown driver | Assignment rejected |
| UNI-008 | Assign a driver where union does not support approved zone/service class | Conflict |
| UNI-009 | Assign a driver to an inactive or suspended union | Conflict |
| UNI-010 | Assign a driver to the same active union again | Conflict; no duplicate audit event |
| UNI-011 | Transfer a driver to another compatible active union | Current assignment changes and transfer audit is appended |
| UNI-012 | Transfer without a reason | Bad request |
| UNI-013 | Read driver's union from another driver's token | Only caller's assignment is returned |
| UNI-014 | Read union members and driver transfer history as Ops | Active members and chronological audit history are returned |
| UNI-015 | Change union status | Status is persisted; new assignments to non-active unions are blocked |
| UNI-016 | Cosmos migration | Unions, DriverUnionAssignments and DriverUnionAudit containers are created with documented partition keys |

These APIs currently use the temporary server-side admin API key and are not a substitute for an authenticated Ops portal/RBAC implementation. Cosmos cross-container writes are not transactionally atomic: an assignment write and audit write can fail independently. Production rollout must add an outbox/reconciliation strategy or a transactional data model before relying on audit completeness.

## Monthly driver payslips (PDF only)

| PAY-014 | Driver app lists recent completed months and downloads a PDF | File is shared as application/pdf; no HTML payslip view or alternate export is shown |

| ID | Scenario | Expected result |
|---|---|---|
| PAY-001 | Driver requests a completed month with union history and activity | PDF is returned with application/pdf and a month-specific filename |
| PAY-002 | Rider requests a driver payslip | Forbidden |
| PAY-003 | Request a future or current incomplete month | Bad request |
| PAY-004 | Invalid year or month | Bad request |
| PAY-005 | Driver has no union assignment during the period | Payslip generation is rejected |
| PAY-006 | Driver transfers unions during a month | Generation is rejected until split-employer support exists |
| PAY-007 | Accept then complete a ride | Accepted and completed each increment once |
| PAY-008 | Driver explicitly rejects an available ride | Rejected increments once; ride remains available to other drivers |
| PAY-009 | Rider cancels a ride | Driver rejected count does not increment |
| PAY-010 | Multiple completed rides on one local date | Days worked increments once for that date |
| PAY-011 | Repeated event delivery | Deterministic activity ID prevents duplicate ledger entries |
| PAY-012 | Payslip requested more than once after snapshot | Same immutable snapshot and PDF metrics are returned |
| PAY-013 | PDF generation | Output begins with a valid PDF header and includes all work metrics |
| PAY-015 | Driver requests the current or a future month | Bad request; only completed India-local months can be snapshotted |
| PAY-016 | Driver supplies another driver's ID in a query string | API derives the driver ID from the authenticated Driver claim and never returns another driver's PDF |
| PAY-017 | Driver has no union assignment or transfers unions within the month | Conflict; no PDF is generated until the period has one unambiguous employer union |
| PAY-018 | Inspect generated PDF content | Work metrics only; no ride/customer identifiers, route details, fare, currency, received-payment or net-payable values |
| PAY-019 | Driver app lists recent completed payslip months at IST month boundaries, regardless of device timezone | Current India-local month is excluded; previous month appears at IST midnight and year rollover is correct | Shared helper + mobile | P1 |
| PAY-020 | A selected Cab ride audit is durable but one or more separate Accepted/Completed/Driver-decline work-activity writes were interrupted before the first monthly snapshot, including a Driver decline before any agreement version exists | Payslip generation reconstructs missing selected-ride activities from authenticated Driver audit facts before saving the immutable snapshot; repeated generation does not duplicate ledger rows or change the PDF | API integration + idempotency | P1 |
| PAY-021 | Two API instances concurrently generate the same first monthly payslip and one CreateItem receives a conflict; the canonical point read is temporarily not visible | The losing request retries the partition-key point read up to three times with bounded backoff and returns only the canonical persisted snapshot; if no canonical item becomes visible or the read fails, the request fails closed and never returns its local candidate | Unit + API persistence seam; live Cosmos validation remains external | P0 |
| PAY-022 | Driver logs out, session expires, app starts without a restored session, or a different account signs in while monthly PDFs exist in app cache | Only exact generated `TurnOva-Payslip-YYYY-MM.pdf` files are removed; unrelated cache files remain. An in-flight download that completes after session change deletes its exact file. Cleanup failure is surfaced and blocks a new account sign-in until cache cleanup succeeds | Shared helper tests + physical Android/iOS acceptance | P0 |
| PAY-023 | Driver and Union names contain Latin plus any of the nine bundled Indic scripts | Visible vector glyphs render; PDF embeds Noto fonts and ToUnicode mappings; PdfPig extracts the original text without replacement question marks | API unit + PDF extraction | P1 |
| PAY-024 | Driver or Union name contains an unbundled script such as Arabic, Sinhala or emoji | API returns a generic HTTP 422; no partial PDF is returned and the unsupported name is not echoed | API integration + security | P1 |
| PAY-025 | Multiple payslips are generated concurrently for different Drivers | Request-local HarfBuzz shaping and serialized PDFsharp resolver/cache use prevent cross-request text leakage | API concurrency + PDF extraction | P1 |
| PAY-026 | Driver name contains a long mixed-script value | Text wraps within the content width, remains searchable/extractable and the immutable payslip snapshot is unchanged | PDF extraction + snapshot regression | P1 |
| PAY-027 | Native Linux x64 API publish includes PDF rendering dependencies | Deterministic fixtures verify success plus rejection of missing native/API assemblies, non-Linux runtime directories, PdfSharp.WPFonts.dll and output above 120 MiB; the real publish artifact must contain both Skia/HarfBuzz Linux libraries and remain within 120 MiB | CI publish artifact + shell fixture tests | P1 |

Payslip output is PDF only; monetary calculations are intentionally absent until the compensation model is approved. Automated tests cover completed-month validation, missing/changed Union assignment, authenticated Driver isolation, exclusion of customer/ride/payment details and the shared `completedIndiaMonths.test.cjs` boundary/year-rollover cases. The Driver app builds its recent-month list using `Asia/Kolkata`, matching the API's completed-month rule rather than the device's local time zone. Before the first snapshot, the API reconstructs missing selected-Cab Accepted/Completed/Driver-decline ledger rows from the same ride's append-only audit events; deterministic activity IDs make repair idempotent. A CreateItem conflict triggers bounded canonical point reads (three attempts, 50 ms then 100 ms); a missing canonical snapshot after those reads fails closed instead of returning locally calculated metrics. `DriverPayslipSnapshotRecoveryTests` cover immediate/delayed visibility, fail-closed behaviour, read failure and cancellation. Driver app cache cleanup targets only exact generated monthly payslip filenames and runs on logout, detected session expiry, no-session startup and before new account authentication; session-epoch tests prove an in-flight write is removed if logout wins the race. `driverPayslipCache.test.cjs` covers exact filename filtering, unrelated-file preservation, partial failures and write/logout races. The PDF uses SkiaSharp/HarfBuzz raw positioned glyph runs for visible vector rendering. A separate PDFsharp 6.2.4 (MIT) text layer appends low-visibility searchable Unicode text with embedded Noto Sans SIL OFL 1.1 fonts and ToUnicode mappings for Latin plus nine Indic scripts. Immutable Skia typefaces are process-cached; HarfBuzz shapers and text placements are request-local; PDFsharp's process-wide font resolver/cache append is serialized. A Skia-only positioned-text experiment did not embed a Unicode font program or preserve searchable long-name text (two backend tests failed), so the PDFsharp text layer is intentionally retained. PdfSharpCore was removed because its transitive ImageSharp 1.0.4 dependency triggered high-severity GIF-decoder advisories; PDFsharp 6.2.4 has no ImageSharp dependency. Its transitive System.Security.Cryptography.Pkcs 8.0.1 was also high-vulnerable, so the API pins patched 10.0.13; backend Directory.Build.props fails restore on audit-feed errors and high/critical advisories (NU1900/NU1903/NU1904). PdfPig tests cover all bundled scripts, embedded fonts/ToUnicode, unsupported-script 422, long-name wrapping, immutable snapshots, concurrent request isolation and repeated-download text equality. The API publish CI target is framework-dependent linux-x64 and rejects unrelated runtime assets or output larger than 120 MiB; production host OS/RID remains unselected. Live Cosmos create-conflict visibility/restart/multi-instance and physical Android/iOS PDF visual/save/share/screen-reader acceptance remain external. The bundled font set excludes Arabic, Sinhala, emoji and other unbundled scripts; these fail closed. The existing DaysWorked definition (distinct India-local dates with at least one completed activity) still requires Product confirmation.

## Light and dark application themes

| ID | Scenario | Expected result |
|---|---|---|
| THEME-001 | First launch with device Light appearance | System mode resolves to Light |
| THEME-002 | First launch with device Dark appearance | System mode resolves to Dark |
| THEME-003 | Select Light from sign-in screen | All app surfaces switch to Light immediately |
| THEME-004 | Select Dark from sign-in screen | All app surfaces and status bar switch to Dark immediately |
| THEME-005 | Select System | App follows current device appearance and later system changes |
| THEME-006 | Restart after explicit theme selection | Preference is restored from SecureStore |
| THEME-007 | Change theme while authenticated | Current screen and navigation update without losing session or form state |
| THEME-008 | Driver onboarding and payslip screens | Inputs, cards, borders, feedback and tab bar remain readable in both themes |
| THEME-009 | Rider booking and activity screens | Inputs, cards, ride status, navigation and map adapt to selected theme |
| THEME-010 | Dark theme primary action | Text remains legible against the accent button |

Validate on Android and iOS physical devices, including system appearance changes and map rendering.

## Driver selfie and union membership profile

| ID | Scenario | Steps / input | Expected result | Type | Priority |
|---|---|---|---|---|---|
| UNION-SELFIE-001 | Capture selfie during driver onboarding | Grant camera permission, use front camera, capture a clear face photo | JPEG is compressed, previewed and submitted with onboarding | Mobile | P0 |
| UNION-SELFIE-002 | Camera permission denied | Deny camera permission | App explains permission is required; no empty or invalid image is submitted | Mobile | P1 |
| UNION-SELFIE-003 | Missing selfie | Submit onboarding without a selfie | API returns HTTP 400; no profile is created | API + mobile | P0 |
| UNION-SELFIE-004 | Invalid Base64 or non-JPEG content | Submit malformed Base64 or a non-JPEG payload | API returns HTTP 400 and does not persist the payload | API + security | P0 |
| UNION-SELFIE-005 | Oversized selfie | Submit a decoded image larger than 300 KB | API returns HTTP 400; client asks driver to retake/compress | API + mobile | P1 |
| UNION-SELFIE-006 | Existing approved driver adds selfie | Capture and save selfie through the authenticated selfie endpoint | Only the authenticated driver's own profile is updated | API + security | P0 |
| UNION-SELFIE-007 | Union assignment without selfie | Attempt to assign an approved driver with no selfie | API returns HTTP 409 and explains selfie capture is required | API | P0 |
| UNION-SELFIE-008 | Union driver list shows photo | Fetch active union members | Each result includes driver profile details and Base64 selfie for authorized operations clients | API + UI | P0 |
| UNION-SELFIE-009 | Union driver details show photo | Fetch one active member by union and driver ID | Response includes profile, selfie metadata and active union assignment | API + UI | P0 |
| UNION-SELFIE-010 | Transfer between unions | Transfer a driver with an existing selfie | Same profile photo remains attached to the driver across union assignments | API | P1 |
| UNION-SELFIE-011 | Unauthorized profile access | Call operations driver/union detail endpoints without valid admin authorization | HTTP 401; no selfie data is returned | API + security | P0 |
| UNION-SELFIE-012 | Driver profile displays selfie | Open the driver's Profile tab | Saved selfie is displayed; driver can retake/update it | Mobile | P1 |

## Union selfie approval and visibility

| ID | Scenario | Steps / input | Expected result | Type | Priority |
|---|---|---|---|---|---|
| UNION-SELFIE-013 | New selfie awaits union approval | Submit onboarding with a valid selfie | Photo status is Pending; no union member-list image is returned | API + mobile | P0 |
| UNION-SELFIE-014 | Union approves selfie | Active assigned union reviews with Approved | Status and approving union are saved; union member list now includes Base64 image | API | P0 |
| UNION-SELFIE-015 | Union rejects selfie | Review with Rejected and a reason | Rejection reason is stored; member-list image remains null/hidden | API | P0 |
| UNION-SELFIE-016 | Replacement selfie resets approval | Upload a replacement after prior approval | Status returns to Pending, previous union approval is cleared, image hidden | API + mobile | P0 |
| UNION-SELFIE-017 | Wrong union cannot approve | Another union attempts to review the driver | HTTP 409; profile review state is unchanged | API + security | P0 |
| UNION-SELFIE-018 | Transfer requires new union approval | Transfer driver after photo was approved by prior union | New union member list hides photo until new union approves | API | P0 |
| UNION-SELFIE-019 | Driver sees own approval state | Open driver Profile after union decision | Driver sees current photo, approval state and rejection reason when present | Mobile | P1 |
| UNION-SELFIE-020 | Pending image not exposed in member list | Query active union members before approval | Photo Base64 is null; no pending photo is exposed in list results | API + security | P0 |

## FIFO rejection and offer-timeout rotation

| ID | Scenario | Expected result | Type | Priority |
|---|---|---|---|---|
| FIFO-ROT-001 | Front driver rejects an available ride | Driver moves behind existing queue members; next eligible driver becomes first | API integration | P0 |
| FIFO-ROT-002 | Front driver acknowledges an expired offer | Driver moves to the back and API returns next driver ID and updated position | API integration | P0 |
| FIFO-ROT-003 | Non-front driver reports offer timeout | HTTP 409; queue order remains unchanged | API security | P0 |
| FIFO-ROT-004 | Driver reports timeout for another zone/service class | HTTP 403; no queue membership changes | API security | P0 |
| FIFO-ROT-005 | Move a driver not present in queue | Returns false; queue remains unchanged | Unit | P1 |
| FIFO-ROT-006 | Concurrent Cosmos queue update conflicts with rotation | ETag precondition failure is reported as unsuccessful rotation; caller must refresh queue | Cosmos integration | P0 |

The timeout endpoint is a client acknowledgement, not a server-side timer. Server-owned offer expiry, offer IDs and retry/recovery behavior remain unimplemented and must be completed before production dispatch.

## FIFO queue-head enforcement

| ID | Scenario | Expected result | Type | Priority |
|---|---|---|---|---|
| FIFO-HEAD-001 | Approved driver is first in the matching zone/service-class queue | Available-rides endpoint returns capacity-compatible requested rides in that zone/class | API | P0 |
| FIFO-HEAD-002 | Approved driver is not first in the matching queue | Available-rides endpoint returns an empty list | API security | P0 |
| FIFO-HEAD-003 | Approved driver is not first but calls ride acceptance directly | HTTP 409; ride remains Requested and unassigned | API security | P0 |
| FIFO-HEAD-004 | Queue head accepts a compatible requested ride | Ride is assigned using the existing conditional state update | API | P0 |
| FIFO-HEAD-005 | Non-head driver attempts to reject a compatible requested ride | HTTP 409; queue and work-activity ledger remain unchanged | API security | P0 |
| FIFO-HEAD-006 | Queue head rejects a compatible requested ride | Driver moves to the back; rejection activity is recorded | API | P0 |
| FIFO-HEAD-007 | Queue membership changes during rejection rotation | HTTP 409; client is instructed to refresh | API/concurrency | P0 |

This increment enforces queue-head access at the API boundary. Durable offers, server-owned timers, automatic expiry/retry, exclusive offer delivery and multi-instance dispatch coordination remain separate production-critical work.

## Durable ride-offer persistence foundation

| ID | Scenario | Expected result | Type | Priority |
|---|---|---|---|---|
| OFFER-STORE-001 | Create the first offer aggregate for a ride | Create succeeds | Unit/store | P0 |
| OFFER-STORE-002 | Create a second aggregate for the same ride | Create-if-absent returns false; no second active aggregate is created | Unit/store | P0 |
| OFFER-STORE-003 | Replace using the current ETag | Update succeeds and returns a new version | Unit/store | P0 |
| OFFER-STORE-004 | Replace using a stale ETag | Update returns false; concurrent state is not overwritten | Unit/store | P0 |
| OFFER-STORE-005 | Query expired offers | Only Pending offers with deadline <= now are returned, bounded by requested batch size | Unit/store | P0 |
| OFFER-STORE-006 | Query a driver's pending offer | Returns only that driver's current Pending offer | Unit/store | P0 |
| OFFER-STORE-007 | Cosmos item replacement races across API instances | One ETag writer succeeds; stale writer retries from authoritative state | Cosmos integration | P0 |

The current increment adds the persistence contract, in-memory/Cosmos adapters and schema migration. API dispatch orchestration and server-owned expiry remain separate increments.

### Dispatch offer expiry coverage

- Expired pending offer is rotated once and marked Expired, including its matching attempt history.
- Reprocessing the same offer after a simulated crash between queue rotation and offer resolution does not rotate twice.
- Missing queue membership leaves the offer pending for retry.
- ETag conflicts never overwrite a concurrently resolved offer.
- Worker polls bounded batches and stops promptly on host cancellation.

## Driver availability management

| ID | Scenario | Expected result | Type | Priority |
|---|---|---|---|---|
| AVAIL-001 | Unapproved driver goes online | HTTP 409; no queue membership is created | API/security | P0 |
| AVAIL-002 | Approved Cab driver with active Cab Union and approved licence/vehicle documents goes online | Availability and initial GPS are persisted; any retained queue membership is compatibility-only and never affects Rider discovery/assignment | API | P0 |
| AVAIL-003 | Driver repeats online request | HTTP 200; no duplicate queue membership or position change | API | P0 |
| AVAIL-004 | Driver goes offline | Availability is persisted as Offline, location is cleared and compatibility queue membership is removed | API | P0 |
| AVAIL-005 | Offline driver attempts queue join | HTTP 409; no queue entry is created | API/security | P0 |
| AVAIL-006 | Offline driver requests legacy available rides or accepts/rejects | Request is forbidden; selected-driver booking remains a separate Rider-selected path | API/security | P0 |
| AVAIL-007 | Driver goes offline with selected negotiation or active ride | HTTP 409; availability and location remain unchanged while the per-Driver reservation is held | API | P0 |
| AVAIL-008 | Driver comes online after offline period | Current GPS is refreshed; any compatibility queue membership is not used for Rider ranking/assignment | API | P0 |
| AVAIL-009 | Driver app restarts | Availability is loaded from the server profile; foreground location is refreshed before discovery eligibility resumes | Mobile/API | P1 |
| AVAIL-010 | Cosmos mode availability change | Availability is persisted in the Drivers profile document and queue changes use the configured queue store | Cosmos integration | P0 |
| AVAIL-011 | Driver without an active Cab Union or approved licence/vehicle documents goes online | HTTP 409; profile remains Offline and no queue membership/location is published | API/security | P0 |
| AVAIL-012 | Driver availability transition races selected booking/Union transfer | `ConcurrentSelectedBookingAndDriverOfflineTransitionCannotBothSucceed` proves exactly one of a selected booking or Offline transition wins the shared per-Driver lease; `AcceptedSelectedRideBlocksAnotherBookingAndDriverAvailabilityChange` proves a permanent active-ride lease blocks both operations | API concurrency | P0 |
| AVAIL-013 | Driver location exits assigned zone while a selected reservation is active | Location transition cannot clear availability during the reservation; after release, a fresh zone-exit update takes the Driver Offline | API concurrency | P0 |
| AVAIL-014 | Driver app resumes from background while Online | Foreground GPS is refreshed and the foreground watcher restarts; revoked permission attempts Offline; zone exit is enforced by API | Android/iOS device | P0 |
| AVAIL-015 | Rider discovery or selected booking runs without an approved positive `Dispatch:NearbyLocationMaxAgeSeconds` | HTTP 503; no implicit 120-second default is used and no Driver reservation is created | API configuration/security | P0 |
| AVAIL-016 | GPS callbacks arrive out of capture order | An older fix returns HTTP 409 and cannot overwrite coordinates, refresh server freshness, or take an Online Driver Offline; the capture watermark and queue membership remain unchanged | API integration/concurrency | P0 |
| AVAIL-017 | Stale GPS fix during Online transition, including after Offline | HTTP 409; the capture watermark survives Offline, Driver remains Offline and no compatibility queue membership is created | API integration | P0 |
| AVAIL-018 | Driver's server receipt timestamp is fresh but GPS capture timestamp is stale during Rider discovery | Driver is excluded from the nearby Cab response; receipt time cannot make a stale coordinate appear fresh | API integration | P0 |
| AVAIL-019 | Driver's server receipt timestamp is fresh but GPS capture timestamp is stale during selected booking | HTTP 409 before reservation/ride/idempotency creation; no stale Driver is booked | API integration | P0 |
| AVAIL-020 | Driver attempts to go Online with a GPS capture timestamp more than one minute in the future | HTTP 400; Driver remains Offline and no compatibility queue entry is created | API integration | P0 |
| AVAIL-021 | Online Driver publishes a GPS fix with a capture timestamp more than one minute in the future | HTTP 400; existing availability, coordinates, receipt timestamp and capture watermark remain unchanged | API integration | P0 |
| AVAIL-022 | Foreground location publish or manual availability transition receives HTTP 409 after server-side availability or selected-ride state changes | Stop the watcher, reload authoritative availability and Driver ride history, stop retrying when Offline or an active selected ride exists, and schedule one delayed foreground refresh only after both reads succeed and the server is still Online with no active selected ride; failed/stale recovery remains stopped until a later foreground resume | `driverAvailabilityRecovery.test.cjs` (409 classification, Offline, Negotiating, retry eligibility, stale-effect and failed-read cases) | Mobile unit + API | P1 |

Online/offline availability is server-authoritative. Going Online requires an approved Cab profile, active Cab-serving Union membership, approved licence/vehicle documents and a valid GPS fix in the assigned zone. Availability changes and zone-exit transitions share the selected-booking/Union-transfer reservation lease. The Driver app tracks location only in the foreground while Online and not in Negotiating, AgreementConfirmed or an active ride; it refreshes location and restarts the watcher on resume. Each GPS request carries the device capture timestamp. The API stores a separate capture watermark, retains it across ordinary Offline transitions and advances it to the accepted out-of-zone fix when zone exit takes the Driver Offline, rejects out-of-order fixes during Online/location updates (including the zone-exit path), and discovery/selected booking evaluate freshness from capture time rather than server receipt time. Capture timestamps more than one minute in the future are rejected before profile or queue mutation; the one-minute allowance is only a clock-skew tolerance, not a freshness threshold. The Driver app also treats location-publish and manual availability HTTP 409 responses as server-state conflicts: it stops the watcher, reloads availability and Driver history, and retries location after 15 seconds only if both authoritative reads succeed and the server still reports Online with no active selected ride. If either read fails or the effect is stale, the watcher stays stopped and no GPS retry is scheduled until a later foreground resume; zone-exit remains immediate Offline. `driverAvailabilityRecovery.test.cjs` covers conflict classification, server Offline, active Negotiating ride, retry eligibility, stale-effect fencing and failed-read atomicity. Legacy FIFO queue membership remains compatibility-only and never controls Rider discovery or assignment. Cosmos cross-container writes and Android/iOS resume/permission behavior still require live integration/device acceptance. `Dispatch:NearbyLocationMaxAgeSeconds` is a product policy, not a technical default: production appsettings omits it, and both discovery and selected booking fail closed with 503 until a positive value is explicitly approved/configured. The 120-second value is used only by the deterministic API test factory. Tests cover missing and non-positive configuration; do not infer a freshness threshold.

## Exclusive driver reservation coverage

| ID | Scenario | Expected result | Type | Priority |
|---|---|---|---|---|
| RESERVE-001 | Same driver concurrently attempts two ride reservations | Exactly one create-if-absent succeeds | Unit + Cosmos integration | P0 |
| RESERVE-002 | Different drivers concurrently accept the same ride | Ride ETag permits at most one assignment | API + Cosmos integration | P0 |
| RESERVE-003 | Driver reservation succeeds but ride is already assigned | Matching reservation is released after a conditional-write conflict | API integration | P0 |
| RESERVE-004 | Driver completes the assigned ride | Matching reservation is released | API integration | P0 |
| RESERVE-005 | Rider cancels an accepted ride | Matching reservation is released | API integration | P0 |
| RESERVE-006 | Stale release references a different ride | Existing reservation is unchanged | Unit + Cosmos integration | P0 |
| RESERVE-007 | API process stops between reservation and ride update | Reservation may remain held; reconciliation is a tracked production blocker | Failure injection | P0 |
| RESERVE-008 | Union transfer races a selected booking or is attempted while a Driver has an accepted active Cab ride | `ConcurrentSelectedBookingAndUnionTransferCannotBothSucceed` proves only one operation wins the per-Driver lease; existing `UnionTransferIsBlockedWhileDriverHasSelectedNegotiation` and `UnionTransferIsBlockedWhileDriverHasAcceptedActiveRide` prove sequential conflict behavior and unchanged assignment | API concurrency | P0 |
| RESERVE-009 | A Driver already holding an accepted active Cab ride is selected for another booking or attempts to go Offline | `AcceptedSelectedRideBlocksAnotherBookingAndDriverAvailabilityChange` returns conflict for both operations and retains the permanent reservation for the original ride | API integration | P0 |
| RESERVE-010 | UnionManager requests selfie review while a per-Driver reservation is expired | After current-assignment/claim authorization, run the shared authoritative reservation reconciliation before acquiring the review lease. Release only an expired orphan/terminal exact Driver+Ride reservation; an expired lease linked to an active Negotiating/AgreementConfirmed/Accepted ride remains held and review returns 409. | API integration + authorization + concurrency | P0 |

In-memory store concurrency tests and API-level selected-booking/availability/Union-transfer race tests are included in CI. Multi-instance Cosmos races and crash-window recovery require a real development Cosmos account and are not claimed as executed by CI unless separately configured. `UnionManagerSelfieReviewReconcilesExpiredOrphanReservationBeforeAcquiringReviewLease` and `UnionManagerSelfieReviewKeepsExpiredReservationForActiveNegotiation` cover the UnionManager technical review lease's orphan-recovery and active-negotiation fail-closed paths. This is not a new ride/queue policy.

## FIFO offer delivery checks
| ID | Scenario | Expected result | Type | Priority |
|---|---|---|---|---|
| OFFER-001 | Ride request with an eligible online FIFO head | Offer is persisted before `RideOfferReceived` is emitted | API/SignalR integration | P0 |
| OFFER-002 | No eligible FIFO driver | Ride remains requested and no offer notification is sent | API integration | P0 |
| OFFER-003 | Driver connects with its own authenticated driverId | Connection joins only its own offer group | SignalR security | P0 |
| OFFER-004 | Driver requests another driver's group | Connection is aborted | SignalR security | P0 |
| OFFER-005 | Driver reconnects with pending offer | Pending-offer endpoint returns offer and expiry | API | P0 |
| OFFER-006 | Wrong driver, resolved offer or expired offer accepts | HTTP 409 and no assignment | API/security | P0 |

Cross-document atomicity, outbox retries, and next-driver progression after rejection/expiry remain production blockers.


## Durable offer rejection

| ID | Scenario | Expected result | Type | Priority |
|---|---|---|---|---|
| OFFER-REJECT-001 | Assigned driver rejects a pending, unexpired offer | Queue rotation uses the offer ID; offer and current attempt become Rejected | API/store | P0 |
| OFFER-REJECT-002 | Another driver rejects the offer | HTTP 409; queue and offer remain unchanged | API/security | P0 |
| OFFER-REJECT-003 | Driver retries after queue rotation succeeded but offer update failed | Same offer ID is recognized; driver is not rotated twice and offer can be resolved | API/store | P0 |
| OFFER-REJECT-004 | Driver rejects an expired or already-resolved offer | HTTP 409; no new queue rotation | API | P0 |
| OFFER-REJECT-005 | Driver repeats a successfully recorded rejection | HTTP 204; no duplicate rotation or work activity | API | P1 |

This retry path depends on the driver retrying after an interrupted response. Automatic reconciliation and next-driver offer issuance remain separate work.


## Idempotent FIFO queue rotation

| ID | Scenario | Expected result | Type | Priority |
|---|---|---|---|---|
| QUEUE-IDEMP-001 | Repeat queue rotation with the same offer ID | First call moves the driver once; retry succeeds without changing queue order again | Unit | P0 |
| QUEUE-IDEMP-002 | Rotate the same driver for a different offer ID | New offer is treated as a new rotation and moves the driver to the back | Unit | P0 |
| QUEUE-IDEMP-003 | Rotate a driver who is not in the queue | Returns false; queue remains unchanged | Unit | P1 |
| QUEUE-IDEMP-004 | Rotate with an empty offer ID | Argument validation rejects the call | Unit | P1 |

These tests exercise the in-memory adapter. Cosmos ETag conflict and persistence behavior still require integration tests against a development Cosmos account. API-level rejection crash/retry behavior also remains a separate integration-test gap.


## Automatic next-driver offer advancement

| ID | Scenario | Expected result | Type | Priority |
|---|---|---|---|---|
| OFFER-NEXT-001 | Rejected offer with an eligible next FIFO driver | New Pending attempt is appended and persisted before SignalR notification | API/store/SignalR | P0 |
| OFFER-NEXT-002 | Expired offer | Expiry worker advances to next eligible driver after marking prior attempt Expired | Worker/API | P0 |
| OFFER-NEXT-003 | Next queue entry is offline, unapproved or incompatible | Entry is removed and selection continues | API/store | P0 |
| OFFER-NEXT-004 | Two advancement calls race | At most one ETag replacement succeeds and one next offer is persisted | Concurrency | P0 |
| OFFER-NEXT-005 | No eligible queue entry remains | No new attempt or notification; ride remains Requested with no pending offer | API | P0 |
| OFFER-NEXT-006 | Process stops after persistence but before SignalR send | Pending-offer fetch returns authoritative offer after reconnect; outbox remains a gap | Failure injection | P0 |

These scenarios still need API/SignalR and multi-instance Cosmos integration coverage; no such integration execution is claimed by in-memory CI.


## Durable offer notification outbox

| ID | Scenario | Expected result | Type | Priority |
|---|---|---|---|---|
| OFFER-OUTBOX-001 | Persist a new initial or advanced offer | Notification state is Pending before any SignalR event | Store/API | P0 |
| OFFER-OUTBOX-002 | Outbox worker sends a pending offer notification | SignalR event includes stable rideId, offerId and expiry; aggregate is ETag-marked Sent | Worker/store | P0 |
| OFFER-OUTBOX-003 | SignalR send fails | Sent marker is not written; later poll retries | Worker/failure injection | P0 |
| OFFER-OUTBOX-004 | Offer is resolved or superseded before delivery | Worker skips stale notification | Worker | P0 |
| OFFER-OUTBOX-005 | Process stops after send but before Sent marker | Event may be delivered again; client de-duplicates by offerId | Failure injection/mobile | P0 |
| OFFER-OUTBOX-006 | Two API instances process same pending notification | Duplicate delivery is tolerated; only current offer state is authoritative | Cosmos integration | P0 |
| OFFER-LIFETIME-001 | Initial offer is created | Expiry uses configured Dispatch:OfferLifetimeSeconds | API | P0 |
| OFFER-LIFETIME-002 | Next offer is advanced after rejection or expiry | Expiry uses the same configured lifetime as initial offers | API | P0 |

The outbox is stored in the ride-partitioned offer aggregate. Delivery is at-least-once, not exactly-once; client offerId de-duplication is required. Cosmos multi-instance concurrency and restart behaviour need validation before production.


## 9. Automated authentication coverage added

- PR #39 adds API integration checks for missing/malformed JWTs, signature, issuer, audience, expiry and Rider/Driver role boundaries.
- PR #40 verifies missing/weak JWT configuration and non-HTTPS or malformed TriSend URLs fail during API startup.
- The shared Rider/Driver refresh coordinator is exercised by Node's built-in test runner for concurrent single-flight behaviour and retry after refresh failure. CI runs this without installing an additional mobile test framework.


## 10. Offer notification outbox reliability tests

- Verify a current pending offer is delivered to the authenticated driver's SignalR group and the sent marker is persisted.
- Verify a stale pending-list snapshot is re-read and suppressed after the offer is resolved.
- Verify a transient SignalR send failure leaves the outbox item pending and a later poll retries it.
- Run two workers against a barrier-controlled shared store to prove at-least-once duplicate delivery can occur while the same offerId remains stable for client de-duplication.
- These tests use the in-memory store; Cosmos multi-instance persistence, process-restart and ETag failure-injection remain separate required gates.


## 11. Rider ride-request validation coverage

- Reject passenger counts below one or above six; accept the maximum supported count of six.
- Reject missing/blank pickup and drop-off descriptions.
- Reject missing pickup coordinates and latitude/longitude values outside their valid ranges.
- Reject unsupported service classes and pickup coordinates outside configured service zones.
- Verify a rejected request does not consume its idempotency key, allowing a corrected retry to create the ride.

## 12. Driver availability and Rider-selected Cab discovery

Legacy FIFO queue tests are compatibility regressions only. They do not define the Rider booking path, rank discovery results, or automatically assign a Driver.

| ID | Scenario | Expected result | Type | Priority |
|---|---|---|---|---|
| NEARBY-001 | Driver goes online without GPS coordinates or with coordinates outside the assigned zone | HTTP 400/409; Driver remains Offline and is not eligible for discovery | API integration | P0 |
| CAB-DISC-001 | Rider searches with valid current pickup coordinates and passenger count | Return only active approved unions covering the pickup zone and Cab service, with active Driver membership; each Driver is approved, Online, location-fresh, document-approved, has sufficient capacity and has no active reservation/ride | API integration | P0 |
| CAB-DISC-002 | More than three eligible Cab Drivers belong to one union | Return at most three for that union, ordered by driver-to-pickup distance ascending with a deterministic Driver ID tie-break | API integration | P0 |
| CAB-DISC-003 | Driver selfie is missing, pending, rejected or approved by a previous union | Expose the selfie only when approval is current and belongs to the Driver's active union; otherwise return a null photo | API security | P0 |
| CAB-DISC-004 | Rider discovery is requested when eligible Drivers have no FIFO queue membership | Discovery still returns eligible union Drivers; it does not use queue position, customer-facing FIFO ranking or automatic assignment. The legacy Rider FIFO booking route remains retired | API integration | P0 |
| NEARBY-003 | Discovery response privacy and stale Driver location | Return selection-safe Driver/vehicle fields only; omit phone number, vehicle registration, exact coordinates and queue position; exclude stale locations and hide unapproved selfies | API integration + security | P0 |
| NEARBY-004 | Driver leaves the assigned zone while Online | Driver becomes Offline and current location is cleared; any legacy queue removal is compatibility cleanup only and does not affect Rider discovery | API integration | P0 |
| CAB-DISC-005 | Nearby radius is missing/invalid or pickup is outside configured service zones | Fail closed: HTTP 503 for missing/invalid radius and HTTP 400 for an unsupported pickup zone; no candidate list is returned | API integration | P0 |
| CAB-DISC-006 | Routing provider is not configured | `journeyDistanceKm` is null and the Rider UI says journey distance is unavailable; `distanceToPickupKm` remains a separate driver-proximity value and no route distance is fabricated | API + mobile | P0 |
| NEARBY-005 | Driver app is Online in foreground or an active ride begins | Foreground availability updates run only while Online, pause during Negotiating/AgreementConfirmed/active rides and stop when Offline or signed out; active-trip location uses the trip tracking endpoint | Android/iOS functional | P0 |
| NEARBY-006 | Rider denies location permission or searches outside configured zones | No discovery request is made without foreground location permission; show a recoverable permission/zone error and return no driver data | Android/iOS + API | P0 |
| NEARBY-007 | Driver submits a location update with missing coordinates or has an active ride | HTTP 400 for missing coordinates; HTTP 409 during an active ride without changing Online state or trip location | API integration | P0 |
| CAB-DISC-007 | Maps/geocoding and Call/Chat providers are not configured | Current foreground device GPS is the pickup coordinate; free-text pickup is a landmark/driver instruction, not a geocoded pin. Journey distance remains unavailable and Call/Chat controls stay disabled with a provider-pending label | Android/iOS functional; external integration gate | P1 |
| RIDER-LOC-001 | Rider denies foreground location permission | No current position is read and no discovery API request is sent; show a recoverable permission error and no driver candidates | Shared mobile unit + Android/iOS functional | P0 |
| RIDER-LOC-002 | Foreground GPS returns non-finite or out-of-range coordinates | Do not call discovery or fall back to last-known/background location; show a recoverable invalid-location error | Shared mobile unit + Android/iOS functional | P0 |
| RIDER-BOOK-LOC-001 | Rider selects a Driver after discovery | First selected-booking attempt requests a new foreground GPS fix; it does not reuse the discovery-time coordinates | Shared mobile unit + Android/iOS functional | P0 |
| RIDER-BOOK-LOC-002 | Booking response is ambiguous and Rider retries the same selection | Reuse the exact first-attempt coordinate pair with the same Idempotency-Key; do not read GPS again or create a changed-payload replay | Shared mobile unit + API idempotency | P0 |
| RIDER-BOOK-LOC-003 | Rider changes pickup/drop-off/stops/passengers or selects another Driver | Clear the captured booking coordinate and rotate the Idempotency-Key; the next attempt obtains a new foreground fix | Shared mobile unit | P0 |
| RIDER-BOOK-LOC-004 | Two booking attempts request a GPS fix concurrently, or an old GPS read completes after selection reset | Share one in-flight read; after reset, reject the stale result and retain only the new attempt's coordinate | Shared mobile unit | P1 |

The API integration tests use deterministic in-memory fixtures and a configured test zone. `RiderDiscoveryGroupsApprovedUnionsCapsThreeAndOrdersByPickupProximityWithoutQueueDependency` verifies union filtering, the three-driver cap, proximity ordering, current-union selfie approval, privacy and null journey distance. `RiderDiscoveryExcludesReservedDriversAndLegacyFifoRiderRoutesAreRetired` verifies reservation exclusion and the retired customer-facing FIFO route. `RiderDiscoveryFailsClosedWhenNearbyRadiusIsMissingOrInvalid` verifies fail-closed configuration behavior. These tests do not establish production Cosmos multi-instance behavior or Android/iOS physical-device permission/resume behavior. Shared mobile tests `Rider discovery does not read GPS or call the API when foreground permission is denied`, `Rider discovery uses a fresh foreground GPS fix and sends the passenger count`, and `invalid current GPS coordinates prevent a Rider discovery request` verify discovery permission and coordinate validation. `selectedBookingLocation.test.cjs` additionally proves a fresh foreground fix for the first booking attempt, stable coordinates across idempotent retries, one shared in-flight GPS read, retry after denied permission, and stale-result fencing after a selection reset. These deterministic tests do not replace Android/iOS physical-device permission/resume and booking acceptance. PR #110 reviewed head `37308fee3c231c5eb4d2ef8815cf8090271b974d` passed exact-head CI [42424122943](https://github.com/rajeevp727/turnova-platform/actions/runs/42424122943), including 57/57 shared mobile tests and both native Android/iOS build jobs; the [PR checks page](https://github.com/rajeevp727/turnova-platform/pull/110/checks) is authoritative for the final docs-sync head.

The Rider currently uses foreground device GPS as the pickup coordinate; the pickup text is a label/landmark for the Driver. Custom pickup pin/geocoding, route-distance calculation and live Call/Chat require configured providers and are not claimed as working. Driver GPS is foreground-only and is not intended as background tracking.
## 13. Concurrent queue membership

| ID | Scenario | Expected result | Type | Priority |
|---|---|---|---|---|
| QUEUE-CONC-001 | Sixteen concurrent AddAsync operations submit the same driver to one zone/service-class queue | Exactly one operation succeeds, duplicate attempts are rejected, and one active membership remains | Unit concurrency | P0 |

This deterministic test exercises the in-memory store. Cosmos multi-instance and partition-level concurrency remain an infrastructure-backed verification gate.

## 14. Fare policy placeholder

| ID | Scenario | Expected result | Type | Priority |
|---|---|---|---|---|
| RIDER-FARE-001 | Rider creates a ride before fare policy is approved | API returns `estimatedFareInr: null`; no demo amount is presented as a quote | API integration + mobile UI | P0 |

Fare calculation remains blocked until the product owner approves base fare, distance/time components, taxes and service-class rules.

## 15. Rider trip history

| ID | Scenario | Expected result | Type | Priority |
|---|---|---|---|---|
| RIDER-HISTORY-001 | Rider requests trip history after creating multiple rides | Only rides owned by the authenticated Rider are returned, newest first, regardless of any client-supplied riderId query value | API integration/security | P0 |
| RIDER-HISTORY-002 | Rider history response is inspected for private fields | Response includes only ride reference, pickup/drop-off notes, passengers, service class, status and timestamps; rider/driver IDs and exact coordinates are omitted | API integration/security | P0 |
| RIDER-HISTORY-003 | Rider with no trips requests history | HTTP 200 with an empty array | API integration | P1 |
| RIDER-HISTORY-004 | Driver requests Rider history endpoint | HTTP 403 | API authorization | P0 |
| RIDER-HISTORY-005 | Rider reopens Activity while a ride is Requested, Offered, Accepted, Arrived or InProgress | Latest active ride details are restored from the owner-scoped history result so status/live tracking can reconnect | Android/iOS functional + SignalR | P0 |

The current automated tests exercise the in-memory store. Cosmos cross-partition history query behaviour, retention and larger-history performance require the configured development Cosmos environment. On Android and iOS, also verify that reopening Activity clears a previously active in-memory ride when the server history contains only terminal states, and that a late response after leaving Activity or signing out cannot overwrite current UI state.


## 16. Driver trip history

| ID | Scenario | Expected result | Type | Priority |
|---|---|---|---|---|
| DRIVER-HISTORY-001 | Driver requests trip history after being assigned multiple rides | Only rides whose DriverId matches the authenticated Driver subject are returned, newest first, regardless of a client-supplied driverId query value | API integration/security | P0 |
| DRIVER-HISTORY-002 | Driver history response is inspected for private fields | Response includes only ride reference, pickup/drop-off notes, passengers, service class, status, request/accept timestamps and active-location-sharing flag; driver/rider IDs, exact coordinates and fare amounts are omitted | API integration/security | P0 |
| DRIVER-HISTORY-003 | Driver with no assigned trips requests history | HTTP 200 with an empty array | API integration | P1 |
| DRIVER-HISTORY-004 | Rider requests Driver history endpoint | HTTP 403 | API authorization | P0 |
| DRIVER-HISTORY-005 | Driver reopens Trips while a ride is Accepted, Arrived or InProgress | Latest active ride summary is restored from the authenticated Driver history result; terminal-only history clears stale in-memory active ride | Android/iOS functional + SignalR | P0 |
| DRIVER-HISTORY-006 | Driver reconnects to SignalR after a network interruption during an active trip | Driver app reloads authoritative history and restores the current active or terminal state instead of trusting missed events | Android/iOS functional + SignalR | P0 |

The automated API tests use the in-memory ride store. Cosmos cross-partition query behaviour, RU consumption, retention and larger-history performance require the configured development Cosmos environment. On Android and iOS, verify active-trip restoration, completion-to-history transition, SignalR reconnect refresh, and that leaving Trips, signing out, or session expiry prevents late history responses from changing UI state and clears the prior account's in-memory trip history before another account is shown.


## 17. API CORS and health probes

| ID | Scenario | Expected result | Type | Priority |
|---|---|---|---|---|
| PLATFORM-CORS-001 | Browser preflight uses an origin configured in `Cors:AllowedOrigins` | API returns the matching `Access-Control-Allow-Origin` header and allows the requested method | API integration/security | P0 |
| PLATFORM-CORS-002 | Browser preflight uses an origin not in the configured allow-list | API does not emit `Access-Control-Allow-Origin` for that origin | API integration/security | P0 |
| PLATFORM-HEALTH-001 | Liveness probe calls `/health/live` | HTTP 200 while the API process is running | API integration | P0 |
| PLATFORM-HEALTH-002 | Readiness probe calls `/health/ready` in demo/in-memory mode | HTTP 200 with `status: ready` and the explicit demo storage mode | API integration | P0 |
| PLATFORM-HEALTH-003 | Readiness probe calls `/health/ready` in Cosmos mode while the database is unavailable | HTTP 503; readiness does not report healthy when the configured persistent store is unreachable | Cosmos integration/failure injection | P0 |

`Cors:AllowedOrigins` accepts explicit HTTPS origins; HTTP origins are allowed only in Development. Wildcards, URL paths, credentials and query strings are rejected. An empty allow-list emits no browser CORS permission; native Android/iOS clients do not depend on CORS. Cosmos-mode readiness failure and production origin configuration remain infrastructure-backed validation.


## 18. Persistent dispatch audit and safe exception logging

| ID | Scenario | Expected result | Type | Priority |
|---|---|---|---|---|
| PLATFORM-AUDIT-001 | Driver successfully joins a zone/service-class queue | Append-only `Joined` audit event is persisted with driver, queue scope and UTC timestamp | Unit + Cosmos integration | P0 |
| PLATFORM-AUDIT-002 | Driver successfully leaves a queue | Append-only `Left` event is committed with the queue membership removal | Unit + Cosmos integration | P0 |
| PLATFORM-AUDIT-003 | Driver is moved to the back or rotated once for an offer | `MovedToBack` or `RotatedToBack` event is committed with the state mutation; offerId is recorded only for offer rotation | Unit + Cosmos integration | P0 |
| PLATFORM-AUDIT-004 | Queue join conflicts, missing membership removal, stale ETag or duplicate offer rotation occurs | No queue-mutation audit event (`Joined`, `Left`, `MovedToBack`, `RotatedToBack`) is written for a failed/no-op state mutation; retrying the same offer does not duplicate its mutation event. A separately typed `DispatchException` is permitted only for unexpected authenticated queue-read or selected-driver reservation-store failures covered by PLATFORM-AUDIT-006 | Unit + Cosmos failure injection | P0 |
| PLATFORM-AUDIT-005 | Audit events are queried for a zone/service-class queue | Only events for that partition are returned, newest first; payload contains no phone/email, exact location or fare | Unit + Cosmos integration/security | P0 |
| PLATFORM-LOG-001 | An unhandled API exception occurs | API returns a generic 500 Problem Details response with traceId; structured error log records traceId and exception type only, not request body, token, exception message or PII | API integration/security | P0 |
| PLATFORM-AUDIT-006 | A non-cancellation queue-read or selected-driver reservation-store exception occurs during authenticated Driver acceptance | A separate `DispatchException` record is appended idempotently in the existing `zoneServiceKey` partition using a deterministic ID derived from server `traceId`, with claim-derived Driver actor reference, allow-listed `QueueReadFailed`, `DriverReservationFailed` or `DriverReservationPromotionRecovered` category and UTC timestamp. A duplicate create is treated as success only when the existing record matches the actor, trace and category. For ambiguous reservation-promotion outcomes, the API re-reads the reservation and treats a same-ride permanent lease as success; otherwise it records the exception and returns generic 500 Problem Details with the same traceId. The ride is not transitioned before permanent reservation is confirmed. Pre-commit failure injection proves the `AgreementConfirmed` ride and temporary lease remain unchanged. No raw exception message, request body, token, contact data, exact location or fare is stored | Unit + API failure injection + Cosmos integration/security | P0 |
| PLATFORM-AUDIT-007 | A deterministic dispatch-exception create returns Cosmos 409 and canonical point-read metadata matches | Treat the retry as success only when event ID, event type, partition, actor, trace ID, allow-listed reason and absence of offer ID match; do not require the original timestamp to equal the retry timestamp | Unit with deterministic persistence fake | P0 |
| PLATFORM-AUDIT-008 | A dispatch-exception create returns Cosmos 409 but canonical metadata differs | Fail closed for mismatched event ID, partition, actor, event type, trace ID, reason category or unexpected offer ID; do not suppress the conflict as idempotent success | Unit with deterministic persistence fake | P0 |
| PLATFORM-AUDIT-009 | A dispatch-exception create returns Cosmos 409 but the canonical point-read returns no item | Fail closed; do not claim the audit write succeeded when the conflicting item cannot be verified | Unit with deterministic persistence fake | P0 |
| PLATFORM-AUDIT-010 | Dispatch-exception create succeeds or cancellation occurs during create/read-back | A successful create performs no unnecessary point read; cancellation propagates and is never converted into a duplicate-success result | Unit with deterministic persistence fake | P1 |

The Cosmos implementation writes queue state and its audit event in one transactional batch within the existing `QueueEntries` container partition (`zoneServiceKey`). In-memory tests validate event semantics; real Cosmos transactional-batch atomicity, ETag conflict, process-restart and audit retention must be verified against the configured development Cosmos account. `DispatchException` is a distinct exception-only record, not a queue mutation event. It is written only for unexpected non-cancellation queue-read exceptions in the legacy Driver accept path or selected-driver reservation-store exceptions in the active Cab Driver accept path; ordinary 409 conflicts, failed/no-op mutations, duplicate offer retries and cancellation do not create it. If a reservation promotion throws after a possible commit, the API re-reads the lease and continues only when the same ride already holds a permanent reservation; otherwise it records the exception and rethrows. Rider discovery and selected-driver assignment remain queue-independent.

## 19. Cab-only union discovery and direct-fare tests (2026-11-06)

These supplement and supersede legacy fare-estimate, payment-gateway and automatic FIFO customer-dispatch tests. Only Cab is valid for new MVP workflows.
| ID | Scenario | Expected result | Type | Priority |
|---|---|---|---|---|
| CAB-SCOPE-001 | Rider submits Scooty/Bike/Auto | API rejects; no ride created | API/security | P0 |
| CAB-SCOPE-002 | Driver attempts non-Cab onboarding | API rejects; no MVP eligibility | API/security | P0 |
| CAB-DISC-001 | Pickup has multiple approved unions | Results grouped by eligible union | API | P0 |
| CAB-DISC-002 | Union has more than three eligible Cab drivers | At most three returned | API | P0 |
| CAB-DISC-003 | Drivers have different pickup distances | Ordered by pickup proximity, not FIFO | API | P0 |
| CAB-DISC-004 | Selfie pending/rejected | Public discovery omits photo | API/privacy | P0 |
| CAB-DISC-005 | Unverified vehicle or unavailable driver | Driver excluded | API | P0 |
| CAB-RES-001 | Two Riders select same driver concurrently | At most one active reservation succeeds | Concurrency | P0 |
| CAB-RES-002 | Negotiation expires/cancels | Exact reservation released; stale request cannot release newer reservation | API | P0 |
| CAB-AGR-001 | Rider proposes and Driver counters | Negotiation history retained | API | P0 |
| CAB-AGR-002 | Only one party confirms | Ride start rejected | API/security | P0 |
| CAB-AGR-003 | Both confirm current version | Start allowed subject to lifecycle rules | API | P0 |
| CAB-AGR-004 | Fare changes after confirmation | New immutable version; both reconfirm | API/persistence | P0 |
| CAB-AGR-005 | Mutate historical confirmed version | Rejected; history unchanged | API/persistence | P0 |
| CAB-REC-001 | Agreed 500, reported 400 | Delta +100; Driver-reported/unverified shortfall | API integration | P0 |
| CAB-REC-002 | Agreed 500, reported 500 | Delta 0; Driver-reported/unverified exact report | API integration | P0 |
| CAB-REC-003 | Agreed 400, reported 500 | Delta -100; Driver-reported/unverified excess | API integration | P0 |
| CAB-REC-004 | Missing, zero, negative, over-precision or decimal amount | An amount is mandatory; zero or more with at most two decimals is accepted; invalid values are rejected; currency is inherited from the agreement | API integration | P0 |
| CAB-REC-005 | Unrelated Driver or Union requests reconciliation | Not found/forbidden; only the assigned Driver and claim-scoped UnionManager can read the minimum report fields | API/security | P0 |
| CAB-REC-006 | Driver submits amount | Ride-linked report records agreement version, actor/time, currency, signed delta and DriverReportedUnverified status | API integration | P0 |
| CAB-REC-007 | Driver reports before completion or without the current mutual agreement | Conflict; no report is stored | API integration | P0 |
| CAB-REC-008 | Same idempotency key is retried or a competing report is submitted | Same key/payload returns the immutable report; another key/payload cannot overwrite it | API/concurrency | P0 |
| CAB-REC-009 | UnionManager lists reconciliation records | Only records matching signed unionId claims are returned; no Rider identity, route/stops, contact data, exact location or chat is exposed | API/privacy | P0 |
| CAB-REC-010 | Driver transfers to another Union after a completed ride has a report | Report remains linked to the original ride Union, remains visible to that Union's authorised manager, and stays in Driver history; cross-Union sharing is not assumed without an approved next-ride eligibility/visibility policy | API/privacy/data-retention | P0 |
| CAB-REC-011 | Driver opens a completed ride before submitting the report | Driver-owned history exposes only the current agreement version, amount and currency needed for the report form; the mobile prompt uses that currency and does not assume INR; Rider history does not gain Driver-only reconciliation fields | API integration + shared mobile unit | P0 |
| CAB-REC-012 | UnionManager reconciliation records are queried from Cosmos | Query projects only service class, ride/Union linkage, agreement version, agreed/received amounts, signed delta, currency, report timestamp, reporting Driver and status; it never selects the full Rides aggregate or Rider identity, route, stops, location or contact fields | `UnionReconciliationCosmosQueryNeverSelectsTheFullRideAggregate`; `UnionReconciliationQueryProjectionContainsOnlyMinimumOversightFields`; existing `UnionManagerSeesOnlyItsMinimalPaymentReconciliationRecords` | API/privacy + unit; live Cosmos query validation remains external | P0 |
| CAB-REC-013 | Historical or mixed-case non-Cab service class is present in the Rides container | Cosmos reconciliation query filters with `LOWER(c.serviceClass) = 'cab'` before materializing the allow-listed projection; signed union/report-union predicates remain and the API Cab post-filter remains defense in depth | `UnionReconciliationCosmosQueryFiltersCabAtStorageBoundaryCaseInsensitively` | Deterministic query-contract test; live Cosmos SQL/index validation remains external | P1 |
| CAB-REC-014 | The in-memory UnionManager reconciliation fallback contains mixed-case Cab and retained legacy non-Cab rides | Uppercase `CAB` remains visible through the case-insensitive API filter; a historical `Scooty` aggregate with an old report is excluded without migration or mutation | `UnionReconciliationExcludesLegacyNonCabRecordsAndKeepsMixedCaseCabRecords` | API integration with deterministic in-memory fixtures; live Cosmos query validation remains external | P1 |
| CAB-REC-015 | A retained historical non-Cab ride is submitted to the direct-payment report endpoint | The Driver receives HTTP 409; no payment report or audit mutation is added, and the legacy service class/status remain unchanged | `DriverPaymentReportRejectsHistoricalNonCabRideWithoutMutation` | API integration with deterministic in-memory fixtures | P1 |
| CAB-UNION-001 | Driver transferred unions | Prior selfie approval invalidated until new union approval | API/privacy | P0 |

Do not test or implement platform fare estimates, in-app payment collection, gateway/escrow or customer-facing FIFO ranking/automatic assignment as MVP behaviour. Dispute and non-zero-delta next-ride outcomes remain pending product decisions.

The UnionManager reconciliation Cosmos read uses an explicit scalar projection rather than `SELECT *` over the Rides aggregate and applies `LOWER(c.serviceClass) = 'cab'` at the storage boundary before materializing records. The signed ride-Union and report-Union predicates remain, and the API's case-insensitive Cab post-filter is retained as defense in depth. Query and DTO allow-list tests prevent reintroducing Rider identity, route/stops, exact location, contact or chat fields. The in-memory API integration test still verifies signed-union filtering and minimum response fields; live Cosmos SQL function/index, partition and query-plan behaviour remains an external development-Cosmos validation gate.



### CAB-SCOPE-01 — Cab-only new workflows and legacy retention

| ID | Scenario | Expected result | Type | Priority |
|---|---|---|---|---|
| CAB-SCOPE-003 | An existing approved non-Cab Driver profile is loaded after the Cab-only release | The historical profile remains readable and unchanged; new Online, queue-join and onboarding-conversion attempts are rejected | API integration/data-retention | P0 |
| CAB-SCOPE-004 | Operations creates a new Union with any non-Cab service class or multiple classes | API rejects the configuration; a new Union must declare Cab only | API integration | P0 |
| CAB-SCOPE-005 | Rider requests nearby discovery using Scooty, Bike or Auto | API rejects the request; only Cab discovery is accepted | API integration | P0 |
| CAB-SCOPE-006 | Existing ride-history records contain legacy non-Cab service classes | History remains readable without changing the stored category | API integration/data-retention | P1 |



### CAB-SCOPE-01 follow-up — legacy non-Cab action guards

| ID | Scenario | Expected result | Type | Priority |
|---|---|---|---|---|
| CAB-SCOPE-007 | Existing non-Cab Driver replaces a selfie or Operations attempts to approve the profile/selfie for new Cab eligibility | API rejects the action; legacy service class and existing history remain unchanged | API integration/security | P0 |
| CAB-SCOPE-008 | Existing non-Cab Driver reads a queue, pending offer or available-ride endpoint | No non-Cab queue or actionable offer is exposed; new queue reads are Cab-only | API integration/security | P0 |
| CAB-SCOPE-009 | Existing non-Cab Driver attempts location sharing, rejection or offer timeout | New activity is rejected; the Driver can still go Offline to remove stale legacy queue membership | API integration/concurrency | P0 |
| CAB-SCOPE-010 | Existing non-Cab Driver opens the mobile app | Profile/history remain readable; queue polling, actionable requests and SignalR offer subscription stay disabled; Offline cleanup remains available | Mobile typecheck + device | P1 |


### CAB-DISCOVERY-01 implementation coverage (PR #55 candidate)

| ID | Scenario | Expected result | Automated evidence | Status |
|---|---|---|---|---|
| CAB-DISC-006 | Rider discovers with multiple eligible unions | Only Active unions serving Cab in the pickup zone are returned; drivers are grouped by their current Active union assignment | RiderDiscoveryGroupsApprovedUnionsCapsThreeAndOrdersByPickupProximityWithoutQueueDependency | PR #64 final-head CI run [42272254259](https://github.com/rajeevp727/turnova-platform/actions/runs/42272254259) executed 189/189 backend tests; live Cosmos/device acceptance remains |
| CAB-DISC-007 | A union has more than three eligible drivers | At most three drivers are returned for that union | Same API integration test; deterministic pickup-distance order | PR #64 final-head CI run [42272254259](https://github.com/rajeevp727/turnova-platform/actions/runs/42272254259) executed 189/189 backend tests; live Cosmos/device acceptance remains |
| CAB-DISC-008 | Driver has pending/rejected/old-union selfie approval | Driver may remain eligible, but public photo is null unless approved by the current union | Same API integration test | PR #64 final-head CI run [42272254259](https://github.com/rajeevp727/turnova-platform/actions/runs/42272254259) executed 189/189 backend tests; live Cosmos/device acceptance remains |
| CAB-DISC-009 | Driver is stale, outside radius, under-capacity, unapproved, non-Cab, unassigned, or union inactive | Driver is excluded | Same API integration test | PR #64 final-head CI run [42272254259](https://github.com/rajeevp727/turnova-platform/actions/runs/42272254259) executed 189/189 backend tests; live Cosmos/device acceptance remains |
| CAB-DISC-010 | Driver has an active reservation | Driver is excluded from Rider discovery | RiderDiscoveryExcludesReservedDriversAndLegacyFifoRiderRoutesAreRetired | PR #64 final-head CI run [42272254259](https://github.com/rajeevp727/turnova-platform/actions/runs/42272254259) executed 189/189 backend tests; live Cosmos/device acceptance remains |
| CAB-DISC-011 | Driver is Online but has no FIFO queue membership | Driver can still appear when all union/profile eligibility rules pass | Discovery fixture seeds Online profiles directly without queue entries | PR #64 final-head CI run [42272254259](https://github.com/rajeevp727/turnova-platform/actions/runs/42272254259) executed 189/189 backend tests; live Cosmos/device acceptance remains |
| CAB-DISC-012 | Discovery response privacy | No phone, full registration or exact driver coordinates; only approved current-union selfie is returned | API integration assertions on serialized response | PR #64 final-head CI run [42272254259](https://github.com/rajeevp727/turnova-platform/actions/runs/42272254259) executed 189/189 backend tests; live Cosmos/device acceptance remains |
| CAB-DISC-013 | Nearby radius is missing, zero, negative or non-finite | API returns 503; no implicit radius fallback | RiderDiscoveryFailsClosedWhenNearbyRadiusIsMissingOrInvalid | PR #58 merged at `a3a5835c66ec5f77f4b7505b4d99d5a43de18403`; final head `73db39c8cbdf79da99181688f7540e63c88cfc32` passed CI run [42258555372](https://github.com/rajeevp727/turnova-platform/actions/runs/42258555372), all four jobs |
| CAB-DISC-014 | Legacy customer-facing FIFO discovery/request | Nearby endpoint returns 410; legacy ride creation is disabled by default | RiderDiscoveryExcludesReservedDriversAndLegacyFifoRiderRoutesAreRetired; CustomerFacingFifoRideCreationIsDisabledOutsideTestCompatibilityMode | PR #64 final-head CI run [42272254259](https://github.com/rajeevp727/turnova-platform/actions/runs/42272254259) executed 189/189 backend tests; live Cosmos/device acceptance remains |
| CAB-DISC-015 | A Driver or unauthenticated caller requests Rider discovery | Driver role is forbidden and unauthenticated requests are unauthorized; only Rider role may call discovery | RiderDiscoveryGroupsApprovedUnionsCapsThreeAndOrdersByPickupProximityWithoutQueueDependency (Driver and anonymous assertions) | PR #64 final-head CI run [42272254259](https://github.com/rajeevp727/turnova-platform/actions/runs/42272254259) executed 189/189 backend tests; live Cosmos/device acceptance remains |
| CAB-DISC-016 | An Active Cab union has an otherwise eligible Driver but does not serve the pickup zone | The union and Driver are excluded even when the Driver is physically within the configured pickup radius | RiderDiscoveryGroupsApprovedUnionsCapsThreeAndOrdersByPickupProximityWithoutQueueDependency | PR #58 merged at `a3a5835c66ec5f77f4b7505b4d99d5a43de18403`; final head `73db39c8cbdf79da99181688f7540e63c88cfc32` passed CI run [42258555372](https://github.com/rajeevp727/turnova-platform/actions/runs/42258555372), all four jobs |
| CAB-DISC-017 | Nearby radius is zero, negative, NaN or positive infinity | API returns 503 and does not apply an implicit/default radius | RiderDiscoveryFailsClosedWhenNearbyRadiusIsMissingOrInvalid | PR #58 merged at `a3a5835c66ec5f77f4b7505b4d99d5a43de18403`; final head `73db39c8cbdf79da99181688f7540e63c88cfc32` passed CI run [42258555372](https://github.com/rajeevp727/turnova-platform/actions/runs/42258555372), all four jobs |
| CAB-DISC-018 | Two eligible drivers have equal pickup distance | Stable DriverId ordinal tie-break keeps result order deterministic before the per-union three-driver cap | RiderDiscoveryGroupsApprovedUnionsCapsThreeAndOrdersByPickupProximityWithoutQueueDependency (equal-distance central-1/central-2 fixture and ordered IDs) | PR #59 final-head CI run [42259580145](https://github.com/rajeevp727/turnova-platform/actions/runs/42259580145) passed all four jobs |
| CAB-DISC-019 | A pickup coordinate matches multiple configured zone rectangles, including an inclusive shared boundary | Discovery and selected booking fail closed independent of configuration order; selected booking creates no ride, idempotency record or reservation; an Online Driver whose location becomes ambiguous is taken Offline | RiderDiscoveryFailsClosedWhenPickupMatchesOverlappingConfiguredZones; SelectedBookingFailsClosedWhenPickupMatchesOverlappingConfiguredZonesBeforeReservation; DriverLocationTakesDriverOfflineWhenPickupMatchesOverlappingConfiguredZones (overlapping interiors and shared inclusive boundaries, both zone orders; 12 cases) | PR #109 exact-head CI [42418661118](https://github.com/rajeevp727/turnova-platform/actions/runs/42418661118) passed 8/8 jobs and 290/290 backend tests |

Zone resolution accepts a pickup only when exactly one configured rectangle matches. Overlapping rectangles and inclusive shared boundaries are ambiguous; no configuration-order precedence is applied. Rider discovery/booking reject ambiguous pickups, while Driver location reconciliation takes the Driver Offline. The 12 deterministic cases in CAB-DISC-019 exercise overlapping interiors and shared inclusive boundaries in both zone orders. Mobile acceptance still requires Android/iOS device checks for union grouping, selected booking request/retry and reconnect recovery, approved-photo rendering, location permission denial, no-provider call/chat messaging, fare proposal/confirmation and the distinct unavailable journey-distance label. The app submits foreground GPS coordinates to the API and renders only server-authorized union/Driver discovery; live maps/routing, Product-approved non-overlapping zone boundaries and radius, approved location freshness/negotiation-lifetime policy, and Cosmos multi-instance query/concurrency validation remain external gates.



### RIDER-02 / CAB-AGR-01 — Direct fare negotiation and immutable agreement

| ID | Scenario | Expected result | Automated evidence | Priority |
|---|---|---|---|---|
| CAB-AGR-007 | Rider proposes and Driver counters with a different amount | Two immutable versions are retained; earlier terms remain unchanged | RideAgreementRequiresBothPartiesToConfirmCurrentImmutableVersionBeforeStart | P0 |
| CAB-AGR-008 | A party confirms an older version after a counterproposal | API rejects stale-version confirmation | Same integration test | P0 |
| CAB-AGR-009 | Proposal is retried with the same Idempotency-Key | Existing version is returned; no duplicate version is created | Same integration test | P0 |
| CAB-AGR-010 | Only one party confirms the current version | Driver start is rejected; after both confirm the current version, start succeeds | Same integration test | P0 |
| CAB-AGR-011 | Fare is renegotiated during an active trip | New version invalidates prior confirmations; completion is blocked until both reconfirm | Same integration test | P0 |
| CAB-AGR-012 | Amount is zero/negative, has more than two decimals, or currency is malformed/lowercase | API rejects the proposal | RideAgreementRejectsInvalidAmountOrCurrency | P0 |
| CAB-AGR-013 | Unrelated Rider attempts to read an agreement | API returns not found; only the assigned Rider and Driver can read | RideAgreementIsVisibleOnlyToTheAssignedRiderAndDriver | P0 |
| CAB-AGR-014 | Two different proposals race for the same current version | One wins; the other receives conflict; only one version is current | ConcurrentAgreementProposalsCannotCreateTheSameCurrentVersion | P0 |
| CAB-AGR-015 | Rider and Driver confirmation audit is read from agreement history | Each immutable version returns both confirmation flags and per-party UTC timestamps; unconfirmed parties have null timestamps; both apps render pending/confirmed labels and valid recorded times safely | RideAgreementRequiresBothPartiesToConfirmCurrentImmutableVersionBeforeStart; agreementConfirmation.test.cjs | P0 |
| CAB-AGR-016 | Same actor reuses an agreement key with a different amount | HTTP 409; the original immutable version and audit state remain unchanged | RideAgreementIdempotencyRejectsChangedAmountOrCurrency | P0 |
| CAB-AGR-017 | Same actor reuses an agreement key with a different currency | HTTP 409; the original immutable version and currency remain unchanged | RideAgreementIdempotencyRejectsChangedAmountOrCurrency | P0 |
| CAB-AGR-018 | A persisted agreement version predates the optional request fingerprint | Matching amount/currency retries replay; changed terms conflict without migration | LegacyAgreementWithoutRequestHashReplaysOnlyMatchingStoredTerms | P0 |
| CAB-AGR-019 | Concurrent same-actor requests reuse one key with different terms | Exactly one proposal version is persisted; the matching key with different payload receives HTTP 409, including the ETag-conflict retry path | ConcurrentSameKeyAgreementProposalsRejectDifferentPayloads | P0 |
| CAB-AGR-020 | Same key is retried with equivalent decimal scale (for example 450.0 and 450.00) | Canonical amount normalization treats the terms as identical and returns the existing version | RideAgreementIdempotencyNormalizesEquivalentDecimalScale | P0 |
| CAB-AGR-021 | Concurrent same-key requests carry the same proposal payload | One immutable version is persisted; one response creates it and the other replays it successfully | ConcurrentSameKeyAgreementProposalsWithSamePayloadReturnOneVersion | P0 |
| CAB-AGR-010 | Only one party confirms the current version | Driver start is rejected; after both confirm the current version, start succeeds | Same integration test | P0 |
| CAB-AGR-011 | Fare is renegotiated during an active trip | New version invalidates prior confirmations; completion is blocked until both reconfirm | Same integration test | P0 |
| CAB-AGR-012 | Amount is zero/negative, has more than two decimals, or currency is malformed/lowercase | API rejects the proposal | RideAgreementRejectsInvalidAmountOrCurrency | P0 |
| CAB-AGR-013 | Unrelated Rider attempts to read an agreement | API returns not found; only the assigned Rider and Driver can read | RideAgreementIsVisibleOnlyToTheAssignedRiderAndDriver | P0 |
| CAB-AGR-014 | Two different proposals race for the same current version | One wins; the other receives conflict; only one version is current | ConcurrentAgreementProposalsCannotCreateTheSameCurrentVersion | P0 |
| CAB-AGR-015 | Rider and Driver confirmation audit is read from agreement history | Each immutable version returns both confirmation flags and per-party UTC timestamps; unconfirmed parties have null timestamps; both apps render pending/confirmed labels and valid recorded times safely | RideAgreementRequiresBothPartiesToConfirmCurrentImmutableVersionBeforeStart; agreementConfirmation.test.cjs | P0 |

RIDER-03 now connects Rider-selected discovery to durable booking, optional-stop persistence, Driver notification, agreement negotiation, reservation expiry/cancellation and the existing mutual-confirmation start gate. The negotiation lifetime remains a product decision: production appsettings is null and the API returns 503 until the approved value is configured; the 15-minute integration-test value is not a product default. The tests below are CI evidence only when the final PR head has passed its required workflow. Live Cosmos ETag/multi-instance contention, Android/iOS device acceptance and production data recovery remain open.



Agreement proposal idempotency additionally fingerprints the canonical two-decimal amount and uppercase currency. Replaying the same actor-scoped key with different terms must return HTTP 409 both when the existing version is found before a write and when a conditional ride replacement loses an ETag race. Older stored versions without the optional fingerprint reconstruct it from their immutable amount/currency; no data migration is required. Tests cover changed amount, changed currency, legacy records, and concurrent same-key/different-payload requests.

RIDER-02 backend tests are now executed evidence: PR #64 final-head CI run [42272254259](https://github.com/rajeevp727/turnova-platform/actions/runs/42272254259) ran 189/189 tests successfully, including the agreement integration suite. Live Cosmos ETag/multi-instance and Android/iOS device acceptance remain separate gates.


## RIDER-03 — selected-driver booking and reservation

- Verify only an active Cab Union, active membership, approved Cab Driver, approved driving licence/vehicle registration, Online state, fresh location, passenger capacity and configured pickup radius can be booked.
- Verify the selected Driver and Union are persisted with pickup, destination and optional stops; the agreement snapshot contains the same route.
- Verify Idempotency-Key retries return the same ride, while reusing a key with a different request fingerprint conflicts.
- Verify interrupted-write recovery reuses the original idempotency CreatedAtUtc for ride RequestedAt, the RideCreated audit timestamp and NegotiationExpiresAtUtc; the recovered ride deadline must equal the already-held reservation expiry and must not be extended by retry time.
- Verify two Riders cannot acquire overlapping reservations for one Driver; Rider cancellation, Driver decline, negotiation expiry and trip completion release only the matching reservation.
- Verify an expired temporary lease is not stolen by the store: the API first transitions the linked Negotiating/AgreementConfirmed ride to Expired (or releases an expired orphan lease), then releases only the matching reservation so a new booking can acquire it. A live lease becomes permanent only after mutual agreement and Driver acceptance; active reservations prevent going offline.
- Verify Driver acceptance is rejected before both parties confirm the current agreement and the selected booking transitions Negotiating → AgreementConfirmed → Accepted.
- Verify expiry is configuration-gated: missing Dispatch:SelectedDriverNegotiationLifetimeMinutes returns 503; no default lifetime is invented. Lazy expiry is triggered on ride reads/history, Rider discovery and Driver availability/reservation checks. Reservation stores expose expired leases to the API and never delete a lease before ride-state reconciliation.
- Verify Rider and Driver history reads reconcile expired selected negotiations before returning them; each transition is recorded once as a System Expired event and releases only the matching Driver reservation.
- Automated tests added in RIDER-03 cover selected booking, route stops and display snapshots, exact-coordinate privacy, idempotent retry/payload mismatch and interrupted-write recovery, create-if-absent persistence, union mismatch, concurrent reservation, Union transfer exclusion while a selected negotiation is active, cancellation/decline, terminal-reservation reconciliation, expiry/release during discovery, missing-lifetime fail-closed behaviour, agreement-gated Driver acceptance, and reservation promotion; and Rider/Driver history-triggered expiry reconciliation (`RiderAndDriverHistoryReconcileExpiredSelectedNegotiationsAndReleaseOnlyTheirReservations`).

RIDER-03 backend tests are executed evidence: PR #64 final-head CI run [42272254259](https://github.com/rajeevp727/turnova-platform/actions/runs/42272254259) ran 189/189 tests successfully, including booking/reservation and Union-transfer coverage. Native non-container API publish, Rider/Driver Expo config/typechecks and shared mobile tests also passed. Live Cosmos ETag/multi-instance/restart and Android/iOS acceptance remain external gates. Automated reviews remain recorded in PR comments [6689967817](https://github.com/rajeevp727/turnova-platform/pull/57#issuecomment-6689967817) and [6690099440](https://github.com/rajeevp727/turnova-platform/pull/57#issuecomment-6690099440).

The recovery regression SelectedBookingRecoversAfterInterruptedRideWriteUsingSameIdempotencyKey now sets an original attempt timestamp and matching live reservation deadline, then asserts the recovered ride preserves that RequestedAt/NegotiationExpiresAtUtc and that the reservation expiry remains identical. PR #108 contains this correction; final-head CI is pending. This is deterministic in-memory failure-recovery evidence, not live Cosmos restart/multi-instance validation.


### RIDER-01 / RIDER-03 — selected booking input boundaries

| ID | Scenario | Expected result | Automated evidence | Status |
|---|---|---|---|---|
| RIDER-BOOK-INPUT-001 | Selected Driver or Union ID is blank/whitespace or exceeds 128 characters | HTTP 400 before reservation or idempotency write | SelectedBookingRejectsInvalidRequestFieldsBeforeReservingDriver | CI pending on final documentation head |
| RIDER-BOOK-INPUT-002 | Pickup or drop-off is blank/whitespace or exceeds 250 characters | HTTP 400 before reservation or idempotency write | SelectedBookingRejectsInvalidRequestFieldsBeforeReservingDriver | CI pending on final documentation head |
| RIDER-BOOK-INPUT-003 | Passenger count is below 1 or above 6 | HTTP 400 before reservation or idempotency write | SelectedBookingRejectsInvalidRequestFieldsBeforeReservingDriver | CI pending on final documentation head |
| RIDER-BOOK-INPUT-004 | Pickup latitude or longitude is outside its valid geographic range | HTTP 400 before reservation or idempotency write | SelectedBookingRejectsInvalidRequestFieldsBeforeReservingDriver | CI pending on final documentation head |
| RIDER-BOOK-INPUT-005 | Idempotency-Key is missing or exceeds 128 characters | HTTP 400; no reservation or idempotency record is created | SelectedBookingRejectsInvalidRequestFieldsBeforeReservingDriver | CI pending on final documentation head |
| RIDER-BOOK-INPUT-006 | Request uses exact supported maxima: six passengers, ten stops, and 250 characters for pickup, drop-off and each stop | HTTP 201; all route fields and stops are persisted; selected Driver reservation is held | SelectedBookingAcceptsMaximumSupportedRouteAndPassengerBoundaries | CI pending on final documentation head |

PR #105 merged at `b4833a210fbb31ea6faefe5892272abcf36357e3`; its final-head CI [42401645940](https://github.com/rajeevp727/turnova-platform/actions/runs/42401645940) passed all eight jobs and executed 257/257 backend tests. PR #106 adds the 14 invalid-input cases plus the positive maximum-boundary case. Its code commit `2d76c556ade57eea01621121859532f5a06c1fcd` passed backend build/tests (272/272) and the Rider/Driver mobile typecheck/bundle jobs in CI [42404536969](https://github.com/rajeevp727/turnova-platform/actions/runs/42404536969); four native Android/iOS jobs were still running when this documentation update was prepared. This is API boundary evidence only; the final PR head must pass all required jobs before merge.

### DISPATCH-03 — selected-driver expiry and reservation reconciliation evidence

- `ExpiredSelectedNegotiationBecomesTerminalAndReleasesDriverReservation` verifies an expired Negotiating ride becomes terminal and releases only its selected Driver lease.
- `DiscoveryExpiresAnExpiredNegotiationBeforeReleasingItsDriverReservation` verifies discovery reconciles the ride lifecycle before returning the Driver to eligible results.
- `DiscoveryReleasesPermanentReservationWhenRideIsAlreadyTerminal` verifies a terminal ride's stale reservation is reconciled before another booking.
- `SelectedBookingReservationPreventsConcurrentBookingsAndRiderCancelReleasesIt` and `DriverDecliningSelectedBookingCancelsRideAndReleasesReservation` verify cancellation/decline cleanup; `ExpiredTemporaryReservationRemainsHeldUntilRideLifecycleReconciliation` and `ReleaseOnlyRemovesMatchingRide` verify expiry retention and matching-ride-only release. `RiderAndDriverHistoryReconcileExpiredSelectedNegotiationsAndReleaseOnlyTheirReservations` additionally verifies both history routes trigger one System Expired audit event and release only the matching reservation (PR #95, CI [42352928382](https://github.com/rajeevp727/turnova-platform/actions/runs/42352928382), 249/249 backend tests).
- The API's selected-booking expiry is lazy: ride reads/history, Rider discovery and Driver availability/reservation checks trigger the authoritative conditional transition. Cosmos ride replacement uses an IfMatch ETag after snapshot comparison; the reservation store returns expired leases to the API rather than deleting them independently.
- `ExpiredSelectedNegotiationDoesNotAdvanceLegacyFifoOffer` (PR #97, commit [`f8774f4`](https://github.com/rajeevp727/turnova-platform/commit/f8774f40b53b8f6d389c260adbc8f60fef8b313b); exact-head CI pending) injects a recording `IRideOfferAdvancer`, expires a customer-selected negotiation through the ride-read path and asserts that the selected flow never invokes FIFO advancement while releasing the matching reservation. `RideOfferExpiryProcessorTests.ProcessBatchAsync_AdvancesAfterOfferIsMarkedExpired` remains the positive control proving the separate legacy offer-expiry worker still advances only its own queue-offer path.
- PR #80 final-head CI [42317693913](https://github.com/rajeevp727/turnova-platform/actions/runs/42317693913) passed all four jobs and executed 235/235 backend tests, 27/27 shared mobile tests, both app typechecks, four Android/iOS production JavaScript bundle exports and native non-container API publish/artifact verification.
- Live Cosmos multi-instance/restart/ETag recovery and Android/iOS booking-expiry, cancellation and reconnect acceptance remain external gates. `Dispatch:SelectedDriverNegotiationLifetimeMinutes` remains unset until the Product Owner approves the expiry lifetime; no default is inferred.


## CI execution integrity

- Release solution builds must include all five backend projects, including `TurnOva.Tests`.
- CI invokes `TurnOva.Tests.csproj` explicitly and fails if its TRX result reports zero executed tests; a green job with no discovered tests is not accepted as backend test evidence.


## DRIVER-01 — union-scoped selfie approval and membership visibility

| ID | Scenario | Expected result | Automated evidence | Priority |
|---|---|---|---|---|
| UNION-SELFIE-021 | A signed-in UnionManager lists its current Cab members | Only active assignments for the caller's signed `unionId` claim(s) are returned; pending selfie is visible to that assigned reviewer; phone/email, exact coordinates, private chat and Rider/ride data are absent | `UnionManagerCanReviewOnlyCurrentAssignedCabDriverSelfiesAndReceivesMinimalData` | P0 |
| UNION-SELFIE-022 | The current UnionManager approves a Cab driver's selfie after profile/licence/vehicle approval | Approval is recorded against the current union; the append-only audit is retry-safe and repeated approval does not duplicate events; Rider discovery exposes the photo only after current-union approval | `UnionManagerCanReviewOnlyCurrentAssignedCabDriverSelfiesAndReceivesMinimalData` | P0 |
| UNION-SELFIE-023 | Rider/Driver role or a UnionManager with no matching current-union claim attempts to list/review | Access is denied; a manager from another union receives NotFound for the driver and cannot acquire a reservation or change approval | `UnionManagerCanReviewOnlyCurrentAssignedCabDriverSelfiesAndReceivesMinimalData` | P0 |
| UNION-SELFIE-024 | Operations admin-key caller attempts to approve a Cab selfie | Approval is forbidden; operations may reject/hide a selfie, but cannot grant public visibility on behalf of the union | `UnionManagerCanReviewOnlyCurrentAssignedCabDriverSelfiesAndReceivesMinimalData` | P0 |
| UNION-SELFIE-025 | An approved Driver is transferred from Union A to Union B | Existing selfie approval is reset to Pending and the old union loses review access; Rider discovery hides the photo until Union B approves it | `UnionTransferResetsSelfieApprovalUntilTheNewUnionApprovesIt` | P0 |
| UNION-SELFIE-026 | Driver reads its own union membership after assignment/transfer | Only the authenticated Driver can read its current active assignment; the Driver profile displays the current union and membership status | `GET /api/v1/driver/union` integration assertion + Driver Expo typecheck; physical-device check remains | P1 |
| UNION-SELFIE-027 | A selected Cab Driver has an active negotiation/ride reservation during selfie review | Review returns Conflict and leaves selfie approval/audit unchanged; review can proceed after the matching reservation is released | `UnionManagerCanReviewOnlyCurrentAssignedCabDriverSelfiesAndReceivesMinimalData` | P0 |
| UNION-SELFIE-028 | Operations rejects/hides a previously Union-approved selfie | The photo is immediately hidden from Rider discovery and an operations rejection audit event is recorded; operations cannot approve | `UnionManagerCanReviewOnlyCurrentAssignedCabDriverSelfiesAndReceivesMinimalData` | P0 |

Union reviewer tokens require a signed `role=UnionManager` claim and one or more signed `unionId` claims. The API does not trust a caller-supplied union ID. Provider-side issuance/mapping of these claims is an infrastructure gate. Private document storage/scanning/consent/retention and Android/iOS camera/document-permission/device tests remain launch gates; no live provider or device validation is claimed.

DRIVER-01 code/test audit (2026-11-07): `DriverOnboardingRequest` accepts Cab only, requires a valid JPEG selfie and opaque storage references, and creates a Pending profile. Operations approval is admin-key protected and requires DrivingLicence and VehicleRegistration references; only approved documents plus an active Cab Union assignment/zone match permit Driver eligibility. UnionManager review is authorized by signed role and current-union claims, serialized by the per-Driver reservation lease, and audited. The member list excludes phone/email, exact location, document references, Rider identity and private chat; pending selfie bytes are visible only to the authorized current Union reviewer, while Rider discovery returns the selfie only after that Union approves it. Union transfer resets approval. API evidence includes `DriverMustSubmitOnboardingAndBeApprovedBeforeJoiningQueue`, `DriverCannotGoOnlineWithoutActiveCabUnionAndApprovedDocuments`, `OperationsCanCreateUnionAssignDriverTransferAndReadAuditHistory`, `UnionAssignmentRejectsPendingAndZoneIncompatibleDrivers`, `UnionManagerCanReviewOnlyCurrentAssignedCabDriverSelfiesAndReceivesMinimalData`, `UnionTransferResetsSelfieApprovalUntilTheNewUnionApprovesIt`, and `DriverOnboardingRejectsNonCabWithoutCreatingOrOverwritingProfile`. Actual private object storage/upload/scanning/consent/retention, identity-provider claim mapping and physical-device tests remain external gates. Operations-admin creation currently activates a Union immediately; a distinct Pending/contract approval lifecycle would require an explicit Product/Legal decision and is not inferred.


### CI evidence refresh after PR #64 (2026-11-06)

- PR #64 merged at `c90699d8fd5e9140d2daccbced106075d86118bb`.
- Final-head CI run [42272254259](https://github.com/rajeevp727/turnova-platform/actions/runs/42272254259) passed all four jobs. The backend Release job executed **189/189 tests**, the TRX non-zero guard passed, and the native non-container API publish/artifact check succeeded.
- Rider and Driver Expo config/typechecks and shared mobile state/refresh tests passed. This restores automated backend evidence for the prior dispatch, agreement and realtime tests; it does not replace live Cosmos, identity-provider, Android/iOS physical-device or production gates.


## PLATFORM-01 — bounded storage readiness

| ID | Scenario | Expected result | Automated evidence | Priority |
|---|---|---|---|---|
| PLATFORM-HEALTH-001 | API runs in demo in-memory mode | Liveness and readiness return HTTP 200; readiness identifies demo storage mode | `HealthEndpointsExposeLivenessAndCosmosAwareReadiness` | P0 |
| PLATFORM-HEALTH-002 | Storage dependency probe fails | Readiness returns generic HTTP 503 while liveness remains HTTP 200; response omits provider exception details | `ReadinessReturns503WhenStorageIsUnavailableWhileLivenessRemainsHealthy` | P0 |
| PLATFORM-HEALTH-003 | Storage probe exceeds its configured bound | Probe cancels after three seconds in production wiring; unit test verifies timeout maps to a readiness failure | `CheckAsyncMapsProbeTimeoutToReadinessFailure` | P0 |
| PLATFORM-HEALTH-004 | Caller cancels the readiness request | Request cancellation propagates and is not misreported as a dependency timeout | `CheckAsyncPropagatesCallerCancellation` | P1 |

The automated failure-path test uses a deterministic fake probe. It does not claim live Cosmos availability or Cosmos SDK timeout validation; those remain infrastructure gates.


## PLATFORM-01 — ride/agreement/reconciliation audit trail

| ID | Scenario | Expected result | Automated evidence | Priority |
|---|---|---|---|---|
| PLATFORM-RIDE-AUDIT-001 | Selected Cab ride completes with an agreed fare and a Driver-reported amount | Ordered append-only events cover ride creation, agreement proposal, both party confirmations, Accepted/Arrived/InProgress/Completed transitions and unverified payment reporting; each event retains ride/Union linkage, actor ID/role and UTC time, plus version/amount/currency/delta where applicable | `CompletedSelectedRidePersistsPrivacyMinimisedAuditEventsForAgreementLifecycleAndPayment` | P0 |
| PLATFORM-RIDE-AUDIT-002 | Agreement confirmation or payment report is retried with the same idempotency identity/payload | The existing confirmation/report is returned and no duplicate audit event is appended | `AgreementConfirmationRetryDoesNotDuplicateAuditEvents`; `CompletedSelectedRidePersistsPrivacyMinimisedAuditEventsForAgreementLifecycleAndPayment` | P0 |
| PLATFORM-RIDE-AUDIT-003 | Rider cancellation, Driver decline and negotiation expiry | A status-change event records the correct actor ID/role and previous/new state; system expiry is attributed to System | `SelectedBookingReservationPreventsConcurrentBookingsAndRiderCancelReleasesIt`; `DriverDecliningSelectedBookingCancelsRideAndReleasesReservation`; `ExpiredSelectedNegotiationBecomesTerminalAndReleasesDriverReservation` | P0 |
| PLATFORM-RIDE-AUDIT-004 | Non-audit Driver location update follows Arrived | The location update preserves the existing event prefix and does not rewrite or drop audit history | `SelectedCabLifecycleRetriesAreIdempotentAndKeepTerminalState` | P0 |
| PLATFORM-RIDE-AUDIT-005 | Audit payload is serialized | No pickup/drop-off/stops, exact coordinates, contact data, document references, tokens or private chat are present | `CompletedSelectedRidePersistsPrivacyMinimisedAuditEventsForAgreementLifecycleAndPayment` | P0 |

Ride audit events are embedded in the existing Rides aggregate and use the same ride partition/ETag replacement as the mutation they describe. These deterministic tests do not replace live Cosmos multi-instance, ETag conflict, restart and retention validation; those remain infrastructure gates. No new audit container or general-purpose admin audit endpoint is added.


## PLATFORM-01 — provider-neutral API diagnostics

| ID | Scenario | Expected result | Automated evidence | Priority |
|---|---|---|---|---|
| PLATFORM-OBS-001 | A selected Cab ride creates, negotiates, confirms, transitions and records an unverified payment report | Audit write metrics and spans are emitted for each persisted audit event with allow-listed event type and success/conflict/failure outcome; no ride/actor/Union identifiers or PII appear as tags | CompletedSelectedRidePersistsPrivacyMinimisedAuditEventsForAgreementLifecycleAndPayment | P0 |
| PLATFORM-OBS-002 | Storage readiness succeeds or its dependency fails | A bounded outcome counter and duration histogram plus one readiness activity are emitted; failure details and endpoint values are not included | ReadinessProbeEmitsBoundedMetricsAndTraceWithoutDependencyDetails | P0 |
| PLATFORM-OBS-003 | A caller cancels readiness | Cancellation propagates and is recorded as the bounded cancelled outcome, not as a dependency timeout | CheckAsyncPropagatesCallerCancellation | P1 |
| PLATFORM-OBS-004 | An audit write conflicts or fails and an unexpected event label is supplied to the diagnostics boundary | Conflict/failure outcomes remain bounded and event types outside the allow-list collapse to other; only event_type/outcome tags are emitted | RideAuditMetricsNormalizeEventTypeAndOutcomeTags | P1 |

These tests validate in-process .NET diagnostic signals only. No telemetry exporter, vendor, public metrics endpoint or alert routing is configured; live export, dashboards/alerts and production collection remain infrastructure gates.

## RELEASE-01 — production mobile bundle validation

| ID | Scenario | Expected result | Automated evidence | Priority |
|---|---|---|---|---|
| RELEASE-BUNDLE-001 | Rider Android production JavaScript bundle | Expo exports the Android production bundle, resolves the shared mobile workspace modules and writes non-empty metadata | CI mobile matrix: `Export Android production JavaScript bundle` + `Verify Android production bundle` | P0 |
| RELEASE-BUNDLE-002 | Rider iOS production JavaScript bundle | Expo exports the iOS production bundle, resolves the shared mobile workspace modules and writes non-empty metadata | CI mobile matrix: `Export iOS production JavaScript bundle` + `Verify iOS production bundle` | P0 |
| RELEASE-BUNDLE-003 | Driver Android production JavaScript bundle | Expo exports the Android production bundle, resolves the shared mobile workspace modules and writes non-empty metadata | CI mobile matrix: `Export Android production JavaScript bundle` + `Verify Android production bundle` | P0 |
| RELEASE-BUNDLE-004 | Driver iOS production JavaScript bundle | Expo exports the iOS production bundle, resolves the shared mobile workspace modules and writes non-empty metadata | CI mobile matrix: `Export iOS production JavaScript bundle` + `Verify iOS production bundle` | P0 |
| RELEASE-BUNDLE-005 | Metro shared workspace is available to each app | Metro config watches `src/mobile/shared` and the shared refresh module exists before production bundling | CI mobile matrix: `Verify Metro can see shared mobile modules` | P0 |

These CI steps validate production JavaScript bundling for both apps and platforms; they do **not** create native APK/AAB/IPA binaries or prove signing. Native Android release artifacts, iOS archive/signing, app-store credentials and physical-device functional/security/pilot acceptance remain external release gates. The checked-in app configs have Android package IDs but no iOS bundle identifiers; do not invent the final store identifiers or signing credentials.

### Native project compilation preflight (unsigned)

| Test ID | Scenario | Expected result | Evidence | Priority |
|---|---|---|---|
| RELEASE-NATIVE-005 | Rider Android native build | Expo prebuild succeeds and Gradle produces a non-empty debug APK | `native-android` CI matrix (`rider-app`) | P0 |
| RELEASE-NATIVE-006 | Driver Android native build | Expo prebuild succeeds and Gradle produces a non-empty debug APK | `native-android` CI matrix (`driver-app`) | P0 |
| RELEASE-NATIVE-007 | Rider iOS native build | CocoaPods resolves; CI selects the root Rider `.xcworkspace` matching the root app `.xcodeproj` (not the nested project workspace); Xcode compiles an unsigned Release configuration for iOS Simulator | `native-ios` CI matrix (`rider-app`) | P0 |
| RELEASE-NATIVE-008 | Driver iOS native build | CocoaPods resolves; CI selects the root Driver `.xcworkspace` matching the root app `.xcodeproj` (not the nested project workspace); Xcode compiles an unsigned Release configuration for iOS Simulator | `native-ios` CI matrix (`driver-app`) | P0 |

The CI-only iOS bundle identifier overrides (`com.turnova.rider` / `com.turnova.driver`) are build settings and are not committed as production store identifiers. The Android output is a debug APK; the iOS output is a simulator `.app`. Neither is a signed production AAB/IPA, and neither replaces physical-device, signing, store-submission or pilot acceptance.

### Release configuration preflight

| Test ID | Scenario | Expected result | Evidence | Priority |
|---|---|---|---|---|
| RELEASE-CONFIG-001 | Rider/Driver app identity and Android package configuration | Each app slug/name/version and established Android package ID match the approved app identity | `scripts/release-preflight.cjs`; shared fixture tests | P0 |
| RELEASE-CONFIG-002 | Generated app icons are resolved after npm postinstall | Configured iOS and Android icon files exist inside the app directory; unsafe paths fail | Per-app mobile CI preflight after `npm install` | P0 |
| RELEASE-CONFIG-003 | EAS development, preview and production profiles are inspected | Development is internal, preview produces an internal APK, and production targets Android App Bundle plus iOS store distribution | Shared fixture tests and per-app CI report | P0 |
| RELEASE-CONFIG-004 | iOS bundle ID, EAS project ID or build-number policy is not yet supplied or externally verified | CI report records missing inputs; syntactically valid IDs still retain Apple-registration and EAS-project-linkage gates until an owner verifies the remote resources | `release-readiness` CI artifact | P0 |
| RELEASE-CONFIG-005 | Malformed package/bundle/project identifiers, missing icons or invalid EAS profiles are committed | Release preflight fails the engineering check and CI job | `releasePreflight.test.cjs` | P0 |
| RELEASE-CONFIG-006 | Native CI artifacts are reported | Android debug APK and unsigned iOS Simulator app are distinguished from signed production AAB/IPA artifacts | `release-readiness` CI artifact | P0 |
| RELEASE-CONFIG-007 | Production API base URL is missing, HTTP/local/private/reserved, an internal hostname, a placeholder or contains query credentials | Report the production HTTPS API endpoint as an external gate without exposing its value; a CI-visible valid URL passes syntax only, while EAS remote environment, DNS, TLS, CORS and health checks remain external | Shared fixture tests and per-app CI report | P0 |
| RELEASE-CONFIG-008 | API endpoint uses private/reserved IPv4/IPv6 or an internal hostname | CI preflight rejects private/reserved IP literals, localhost/internal suffixes and single-label hosts | `releasePreflight.test.cjs` | P0 |
| RELEASE-CONFIG-009 | API endpoint uses a public IPv4/IPv6 literal or public-looking HTTPS hostname | Syntax check may pass, but DNS resolution and EAS remote environment verification are not claimed | `releasePreflight.test.cjs` plus external Platform verification | P1 |
| RELEASE-CONFIG-010 | EAS build-number source or increment is missing/malformed | Explicit `cli.appVersionSource` local/remote and production `autoIncrement=true` are required for CI-visible configuration; Release Engineering still approves and verifies actual EAS build metadata | Shared fixture tests and EAS build evidence | P0 |
| RELEASE-CONFIG-011 | iOS icon path differs from the general Expo icon | The configured iOS icon must exist inside the app directory after postinstall | `releasePreflight.test.cjs` | P0 |

The preflight separates repository-controlled configuration errors from release-owner inputs. A passing structural check is not production acceptance; signing credentials, physical-device/pilot tests and store approval remain external gates.



The two Expo apps share modules from `src/mobile/shared`; each app's Metro configuration adds only that shared directory to `watchFolders` and leaves Metro's default hierarchical dependency resolution intact. This is required for production bundling and does not introduce a new runtime integration.



### Latest native CI evidence and release boundary (2026-11-07)

The latest-main verification is PR #102 exact-head CI [42380032068](https://github.com/rajeevp727/turnova-platform/actions/runs/42380032068), head `7b4320bfdad460511280251963e1b6c4ffc51c80`. All 8/8 jobs passed: backend, Rider and Driver mobile, Rider and Driver Android native, Rider and Driver iOS native, and shared mobile-auth tests. The run executed 254/254 backend tests and 53/53 shared mobile tests, typechecked and exported production JS bundles for both apps, compiled both Android debug APKs and both unsigned iOS Simulator apps, and published the .NET API as a non-container artifact. Seven artifacts were uploaded: API publish; Rider/Driver Android APKs; Rider/Driver iOS Simulator apps; Rider/Driver production JS bundles.

This evidence closes the automated native-compilation preflight only. It does not prove signed Android AAB or iOS IPA generation, production store identifiers/profiles, physical-device installation/runtime, permissions, device-to-device realtime, security or pilot acceptance. Those remain blocked pending approved store/signing inputs and physical Android/iOS validation. Do not place signing credentials or final identifiers in the repository.

## 15. AI-01 — bounded Rider trip intent and approved-content help

| ID | Scenario | Expected result | Type | Priority |
|---|---|---|---|---|
| AI-001 | Rider describes “from … to …” Cab trip with an explicit passenger count | API returns locally parsed pickup/drop-off, Cab category, explicit passenger count and review requirement; no booking or ride is created. If passenger count is absent, it is null and listed in MissingFields | API integration + mobile | P0 |
| AI-002 | Trip text omits pickup, destination or passenger count | Each missing field is returned for manual completion; no location or passenger count is invented | Unit + API + mobile | P0 |
| AI-003 | Passenger count is outside 1–6 | Passenger count is null and clarification is required; value is not clamped | Unit | P0 |
| AI-004 | Provider receives trip-intent prompt | Provider sees only fixed route placeholders and generic action tokens; exact pickup/drop-off, passenger count, family/child context, accessibility/health details, luggage, email, phone and arbitrary free text are absent | Unit/security | P0 |
| AI-005 | Provider is disabled or unavailable | Deterministic local draft is returned with source marked as fallback | Unit + API | P0 |
| AI-006 | Provider returns wrong service class or route token | Provider output is rejected; local Cab draft and locally parsed passenger count are preserved. Provider contract has no passenger-count field | Unit | P0 |
| AI-007 | Provider times out or transient HTTP call fails | Deterministic fallback is returned within the configured timeout | Unit | P0 |
| AI-008 | Caller cancels an assistance request | Cancellation propagates; it is not converted into a successful fallback response | Unit | P1 |
| AI-009 | Rider requests a known ride-state explanation | Exact approved in-repository state explanation is returned; no ride state is changed | Unit + API | P0 |
| AI-010 | Rider/Driver asks approved product help | Provider may select only an approved source ID; API returns the exact in-repository answer | Unit + API | P0 |
| AI-011 | Question is unsupported or provider source ID is unknown | Safe escalation response is returned; no policy, fare, ETA or support SLA is fabricated | Unit + API | P0 |
| AI-012 | Driver calls Rider trip-intent endpoint | HTTP 403; Rider succeeds, Driver is forbidden and anonymous caller receives 401. Covered by `TripIntentRequiresRiderAndReturnsMissingPassengerCountForManualCompletion` | API integration security | P0 |
| AI-013 | Rider or Driver calls help endpoint | Authenticated Rider and Driver succeed; unauthenticated callers receive 401. Covered by `ApprovedHelpEndpointAllowsRiderAndDriverButRejectsAnonymousCaller` | API integration security | P0 |
| AI-014 | Caller exceeds assistance request limit | Further requests receive HTTP 429 within that authenticated subject's rate-limit partition; another subject retains its independent quota. Covered by `AssistantRateLimitIsPartitionedByAuthenticatedSubject` | API integration abuse control | P1 |
| AI-015 | Prompt/log privacy review | Precise route, identity/KYC, phone/email, payment details, tokens and private chat are not sent to provider or written to logs | Security review | P0 |
| AI-016 | AI output attempts to set fare, approve KYC, rank drivers or change ride state | Contract exposes none of these operations; only a human-reviewed draft or approved help source can be returned | Architecture + tests | P0 |
| AI-017 | Rider/Driver asks what queue status means | Approved answer states Rider discovery is not FIFO-ranked or auto-assigned and leaves the internal FIFO role unresolved | Unit | P0 |
| AI-018 | Driver asks about Online/Offline eligibility | Approved answer explains availability as one server-side eligibility input without promising discovery or assignment | Unit | P1 |
| AI-019 | Provider attempts to supply a passenger count absent from Rider text | Provider contract cannot supply passenger count; response keeps it null and lists passengerCount for manual completion. No default is invented | Unit + API | P0 |
| AI-020 | Rider asks a generic “what is my ride status?” question without a canonical state | Assistant escalates instead of guessing an accepted/in-progress/completed state; explicit state names still return their approved definition | Unit | P0 |
| AI-021 | AI trip draft omits passenger count while Rider form previously contains its default | Rider form clears the stale/default passenger value; discovery and booking remain blocked until the Rider manually enters an integer from 1 through 6 | Shared mobile helper + Rider UI | P0 |

| AI-022 | Known ride-state explanation exceeds MaxOutputCharacters | Approved state content is returned only when it fits the configured limit; otherwise the service returns a short deterministic escalation response within the same limit | Unit | P0 |
| AI-023 | Unsupported-help fallback is subject to the configured output limit | Every unsupported/ambiguous help path returns a bounded escalation response; no fallback can exceed MaxOutputCharacters | Unit | P0 |
| AI-024 | Provider-selected approved content exceeds MaxOutputCharacters | The service rejects the oversized approved answer and returns the bounded deterministic escalation response; the provider cannot bypass the local output cap | Unit | P0 |
| AI-025 | Direct service configuration cannot fit the bounded escalation response | Construction rejects an output maximum shorter than the fixed escalation response; the API's validated 100–1000 range remains valid | Unit | P1 |
| AI-026 | Provider ignores its cancellation token | The service independently enforces RequestTimeoutSeconds with Task.WaitAsync, cancels the linked provider token, returns the deterministic local result within the bound and observes any later provider-task fault without logging its content | Unit | P0 |
| AI-027 | Provider throws or faults with TimeoutException or transient HttpRequestException | Both synchronous provider throws and faulted provider tasks return deterministic fallback; no provider exception message or prompt content is logged | Unit | P0 |
| AI-028 | Caller cancels while a provider request is in flight | The linked provider token is cancelled and caller cancellation propagates; it is never converted to a successful fallback | Unit | P0 |
| AI-029 | Caller cancellation races with an already-faulted provider HTTP request | Caller cancellation takes precedence over the provider HTTP failure and propagates as cancellation | Unit | P0 |
| AI-030 | Provider adapter throws an unexpected non-cancellation exception during serialization, parsing or execution | The service returns deterministic local trip/help fallback; caller cancellation still wins; provider exception messages and prompt content are not returned or logged | Unit | P0 |

The CI provider is a deterministic fake. Live provider sandbox behavior, managed-secret wiring and data-processing terms are not claimed until infrastructure is configured. AI assistance must never invoke booking, fare agreement, payment, eligibility, discovery ordering or ride-state mutation endpoints. The help output limit applies to known-state, provider-selected, deterministic and escalation responses. The service refuses an invalid direct configuration whose maximum is shorter than its bounded escalation message; API options validation already requires at least 100 characters. Provider calls use a linked cancellation token plus an independent `Task.WaitAsync` deadline, so a provider that ignores cancellation cannot hold the API request open. Explicit `TimeoutException` and transient `HttpRequestException` failures fall back deterministically; caller cancellation is checked before converting either failure to fallback. Late provider-task faults are observed without logging provider exception contents.

### AI-01 exact-head execution evidence — 2026-11-10

- Code head `05d20e176abf3c97c360650d3daa5e426a8a253a` passed [CI #1105 / 42759995138](https://github.com/rajeevp727/turnova-platform/actions/runs/42759995138) 12/12.
- Documentation head `95ba90830f6cdb4327d5eb18bb32270d8e500ed1` passed [CI #1108 / 42765629466](https://github.com/rajeevp727/turnova-platform/actions/runs/42765629466) 12/12. Backend 371/371, shared JavaScript 102/102, Python fake-SDK/native tests 40/40; all Rider/Driver mobile validation, four native builds and four simulator smokes passed.
- Same-run Rider Android [artifact 13740031968](https://github.com/rajeevp727/turnova-platform/actions/runs/42765629466/artifacts/13740031968), Driver Android [13740945306](https://github.com/rajeevp727/turnova-platform/actions/runs/42765629466/artifacts/13740945306), Rider iOS [13740853124](https://github.com/rajeevp727/turnova-platform/actions/runs/42765629466/artifacts/13740853124) and Driver iOS [13740598505](https://github.com/rajeevp727/turnova-platform/actions/runs/42765629466/artifacts/13740598505) were downloaded; result JSON and screenshots inspected. Android apps installed/launched and showed role-specific login UI. Unsigned iOS apps installed/launched and showed role-specific login UI plus the expected SecureStore-unavailable warning; no Keychain access group or production authentication is claimed.
- Follow-up review identified that provider-specific exceptions other than cancellation, HTTP and timeout failures could still escape as API 500. AI-030 now covers unexpected synchronous/asynchronous provider adapter failures and deterministic fallback; the updated code requires a new exact-head CI run.

PR [#142](https://github.com/rajeevp727/turnova-platform/pull/142) code head `05d20e176abf3c97c360650d3daa5e426a8a253a` passed all 12/12 jobs in [CI run #42759995138 (run 1105)](https://github.com/rajeevp727/turnova-platform/actions/runs/42759995138) after a targeted retry of the Rider Android emulator smoke. The run executed .NET 10 backend tests 371/371, shared JavaScript tests 102/102, Python fake-SDK/native tests 40/40, Rider/Driver mobile validation and production bundle exports, all four native Android/iOS builds, and all four same-run simulator smokes.

The first Rider Android smoke attempt failed because the Pixel Launcher ANR dialog persisted after two bounded Wait attempts; the app's launch command had succeeded, but the harness correctly failed closed. Only that failed job was rerun. The retry passed and its inspected artifact [13739865249](https://github.com/rajeevp727/turnova-platform/actions/runs/42759995138/artifacts/13739865249) reports install/launch passed, stable Rider process, login text verified, no stale-session error and zero Pixel Launcher ANR Wait attempts. The Driver Android artifact [13737179773](https://github.com/rajeevp727/turnova-platform/actions/runs/42759995138/artifacts/13737179773) reports the same successful app evidence and zero ANR attempts. Rider iOS [13738776886](https://github.com/rajeevp727/turnova-platform/actions/runs/42759995138/artifacts/13738776886) and Driver iOS [13738339007](https://github.com/rajeevp727/turnova-platform/actions/runs/42759995138/artifacts/13738339007) both report install/launch passed, stable process and role-specific login screen; both explicitly report unsigned simulator artifact, `secureStoreAvailable=false`, SecureStore-unavailable banner verified, no Keychain access group and production API/authentication not exercised. All four screenshots were downloaded and visually inspected. This does not establish Keychain persistence, signed-device authentication, physical-device or production readiness.

The documentation synchronization is a new PR head and requires a fresh exact-head CI run plus a COMMENT-only review anchored to that SHA before merge. PR #142 remains open; no self-approval or bypass. Strict acceptance remains 0/19 (15 In Progress, 4 Blocked), and production readiness is not established. AI-01 remains supplemental and outside the 19-row denominator.


## DISPATCH-03 — terminal selected-ride reservation read-repair

| ID | Scenario | Steps / input | Expected result | Type | Priority |
|---|---|---|---|---|---|
| DISPATCH-03-READ-001 | Direct read repairs an already-terminal selected ride | Create a Union-linked selected Cab booking, retain its Driver reservation, simulate an interrupted terminal write as Expired, Cancelled or Completed, then GET the ride as its Rider | Read returns the terminal ride and releases only the reservation whose DriverId and RideId both match | API integration | P0 |
| DISPATCH-03-READ-002 | Rider history repairs an already-terminal ride | Simulate terminal state persistence before reservation release, then read `/api/v1/rides/mine` as the owning Rider | History returns the terminal status and retries exact-pair reservation release | API integration | P0 |
| DISPATCH-03-READ-003 | Driver history repairs an already-terminal ride | Simulate terminal state persistence before reservation release, then read `/api/v1/driver/rides/mine` as the assigned Driver | History returns the terminal status and retries exact-pair reservation release | API integration | P0 |
| DISPATCH-03-READ-004 | Old terminal history cannot release a newer Driver reservation | Release the old ride's lease, create a newer selected booking for the same Driver, then read the old terminal ride | Newer reservation remains bound to its own RideId; old read has no effect on it | API integration + concurrency safety | P0 |
| DISPATCH-03-READ-005 | Concurrent expiry reads are idempotent | Read the same expired selected ride concurrently from two Rider requests | Both reads return Expired, exactly one terminal audit transition exists, and the matching reservation is absent | API integration + concurrency | P0 |
| DISPATCH-03-READ-006 | ETag-race reread repairs only a terminal lease | Two API instances race to expire the same selected ride; the losing instance re-reads the latest ride after the conditional transition fails | If the latest ride is terminal, release only its matching Driver/Ride lease; if still active or missing, fail closed and do not release another lease | Cosmos integration / deterministic store simulation | P0 |
| DISPATCH-03-READ-007 | Selected expiry never advances legacy FIFO | Expire a Union-linked selected booking while a legacy offer exists | No `IRideOfferAdvancer` call occurs; the existing legacy offer expiry positive control remains intact | API integration | P0 |

| DISPATCH-03-READ-008 | Unauthorized Rider cannot trigger cleanup for another Rider's ride | Call GET `/api/v1/rides/{rideId}` as an unrelated Rider while the owner’s selected Cab ride is terminal and its reservation is still held | HTTP 404; reservation remains until the owning Rider reads it | API integration + authorization | P0 |
| DISPATCH-03-READ-009 | Legacy non-Cab history is preserved | Simulate a terminal historical non-Cab ride with a UnionId and an existing legacy reservation, then read it | Ride history remains readable and the legacy reservation is not touched by Cab-only read-repair | API integration + compatibility | P0 |
| DISPATCH-03-READ-010 | Legacy Cab history without a usable Union link is preserved | Simulate a terminal historical Cab ride with null, empty or whitespace UnionId and a held legacy reservation, then GET it as its Rider | Read returns the terminal ride; reservation is not released because the record is not demonstrably Union-linked | API integration + compatibility | P0 |

### DRIVER-01 — conditional Driver profile writes and stale document review

| ID | Scenario | Expected result | Automated evidence | Status |
|---|---|---|---|---|
| DRIVER-PROFILE-001 | Driver resubmits onboarding while the current profile contains availability/location state | Submitted identity, Cab, selfie and document fields are updated; the latest availability, coordinates, location timestamp and original submission timestamp are preserved; Approved/Suspended and legacy non-Cab profiles cannot be replaced | OnboardingResubmissionPreservesLatestAvailabilityAndLocationFields plus existing onboarding eligibility regressions | CI [42455925931] passed 8/8 jobs |
| DRIVER-PROFILE-002 | Driver replaces the selfie while other profile fields have changed | Selfie approval is reset to Pending, but the latest unrelated availability/location fields remain intact; a concurrent non-Cab profile change is rejected | DriverSelfieReplacementPreservesLatestAvailabilityAndLocationFields; conditional in-memory/Cosmos profile update | CI [42455925931] passed 8/8 jobs |
| DRIVER-PROFILE-003 | Operations reviews a profile after its submitted document reference/status, vehicle identity, submission state or prior review state changes | The stale review is rejected with HTTP 409; no document or profile approval is applied to a different snapshot | DriverProfileReviewGuardTests.ChangedDocumentReferenceOrStatusRejectsStaleReviewSnapshot and ChangedVehicleIdentityOrReviewStatusRejectsStaleReviewSnapshot | CI [42455925931] passed 8/8 jobs |
| DRIVER-PROFILE-004 | Availability/location changes while Operations reviews the same unchanged document snapshot | Review applies to the current profile using compare-and-swap and preserves the latest unrelated fields | DriverProfileReviewGuardTests.UnrelatedAvailabilityAndLocationChangesDoNotInvalidateDocumentReviewSnapshot | CI [42455925931] passed 8/8 jobs |
| DRIVER-PROFILE-005 | Two initial onboarding requests race to create the same Driver profile | Only one create-if-absent succeeds; a losing create re-reads and conditionally updates the authoritative profile, never blindly upserts | ConcurrentInitialOnboardingSubmissionsLeaveOneCompleteProfile | CI [42455925931] passed 8/8 jobs |

### DRIVER-02 — monotonic availability GPS capture ordering and conflict recovery

| ID | Scenario | Expected result | Automated evidence | Status |
|---|---|---|---|---|
| DRIVER-GPS-001 | Driver goes Online with a foreground GPS fix, then goes Offline and attempts to reuse an older fix | The profile stores capture time separately from server receipt time; Offline clears public coordinates/receipt time but retains the monotonic capture watermark; the stale Online attempt conflicts and does not rejoin the queue | `DriverCannotReusePreOfflineGpsFixWhenGoingOnlineAgain` | CI [42472744096] passed 8/8 jobs on head `91959a7056251687f030a7abfbd7d08078b3d5bf` |
| DRIVER-GPS-002 | Delayed in-zone and out-of-zone location callbacks arrive after a newer accepted fix | Both stale callbacks conflict; neither refreshes receipt time, changes coordinates, takes the Driver Offline nor removes queue membership | `DriverLocationRejectsOutOfOrderFixWithoutRefreshingOrTakingDriverOffline` | CI [42472744096] passed 8/8 jobs |
| DRIVER-GPS-003 | A newer GPS fix exits the assigned zone, followed by an older Online attempt | The zone-exit transition takes the Driver Offline, clears public coordinates and advances the capture watermark; the older Online attempt cannot restore availability or queue membership | `DriverLocationOutsideAssignedZoneTakesDriverOfflineAndRemovesQueueMembership` | CI [42472744096] passed 8/8 jobs |
| DRIVER-GPS-004 | Driver sends a GPS capture timestamp more than one minute in the future | API returns BadRequest and leaves availability, accepted coordinates, receipt time, capture watermark and queue state unchanged | `DriverCannotGoOnlineWithGpsCaptureTimestampMoreThanOneMinuteInFuture`; `DriverLocationRejectsGpsCaptureTimestampMoreThanOneMinuteInFutureWithoutChangingAcceptedFix` | CI [42472744096] passed 8/8 jobs |
| DRIVER-GPS-005 | Server receipt time is recent but the stored GPS capture time is stale | Rider discovery excludes the Driver and selected booking conflicts before creating a ride or reservation | `RiderDiscoveryExcludesLocationWithStaleCaptureEvenWhenServerReceiptIsFresh`; `SelectedBookingRejectsStaleCaptureEvenWhenServerReceiptIsFresh` | CI [42472744096] passed 8/8 jobs |
| DRIVER-GPS-006 | Driver location/manual availability receives HTTP 409 during a stale or reservation race | App stops the stale watcher, reads authoritative availability and Driver-owned ride history together, applies neither result if the effect is stale or either read fails, and schedules one delayed foreground refresh only when Online with no active selected ride | Six tests in `src/mobile/shared/driverAvailabilityRecovery.test.cjs` cover conflict classification, Offline, active negotiation, retry eligibility, stale-effect fencing and failed-read atomicity | CI [42472744096] passed 8/8 jobs |

The Driver app sends Expo GPS `LocationObject.timestamp` for Online, foreground refresh and watcher updates. The API keeps `LocationCapturedAtUtc` (untrusted client capture time) distinct from `LocationUpdatedAtUtc` (server receipt time), rejects future skew greater than one minute and rejects non-advancing capture times through conditional profile writes. Legacy requests without `capturedAtUtc` retain backward compatibility by using server receipt time; the strict ordering guarantee therefore applies to updated clients. The one-minute allowance is a clock-skew tolerance, not a product freshness policy. A positive Product-approved `Dispatch:NearbyLocationMaxAgeSeconds` remains required and missing/non-positive configuration fails closed for discovery/booking. Live Cosmos multi-instance/ETag/restart and physical Android/iOS foreground/resume/location acceptance remain external gates; no live validation is claimed.


The profile writer uses ConcurrentDictionary.TryAdd/TryUpdate in the in-memory adapter and Cosmos CreateItemAsync plus ETag-conditional ReplaceItemAsync in production. Compare-and-swap retries are bounded; a changed or repeatedly contended profile returns a conflict rather than overwriting newer state. Operations document review validates the complete review-relevant identity/vehicle/document snapshot before applying status changes. This does not implement private file upload, scanning, consent or retention policy; those remain external product/legal/storage gates.



### TRIP-01 — active-trip capture ordering, SignalR freshness and Rider map follow

| ID | Scenario | Expected result | Automated evidence | Priority |
|---|---|---|---|---|
| TRIP-LOC-001 | An older Driver client posts an active-trip location without `capturedAtUtc` | API remains backward compatible and stamps a server-time capture watermark while preserving a separate server receipt timestamp | Existing selected-trip API integration test legacy request path | P0 |
| TRIP-LOC-002 | Driver posts a strictly newer active-trip GPS capture | Ride stores coordinates, server receipt time and capture watermark; owning Rider receives both timestamps in `DriverLocationUpdated` | `SelectedCabBookingUsesOnlyAssignedDriverLocationAndLifecycleGuards` | P0 |
| TRIP-LOC-003 | Duplicate or out-of-order active-trip GPS capture arrives after a newer fix | HTTP 409; no coordinates, receipt timestamp or capture watermark changes; no stale location event is broadcast | `SelectedCabBookingUsesOnlyAssignedDriverLocationAndLifecycleGuards`; existing ride ETag/CAS writer | P0 |
| TRIP-LOC-004 | Active-trip GPS capture is more than one minute in the future | HTTP 400; ride state and location watermark remain unchanged | `SelectedCabBookingUsesOnlyAssignedDriverLocationAndLifecycleGuards` | P0 |
| TRIP-LOC-005 | Two active-trip location requests race from the same ride snapshot | At most one conditional replacement succeeds; a losing writer receives HTTP 409 and cannot overwrite the winner's capture watermark | Existing in-memory snapshot guard and Cosmos ETag conditional replacement; live multi-instance contention remains external | P0 |
| TRIP-LOC-006 | Rider receives a stale, duplicate, malformed-coordinate or different-ride SignalR location event | Event is ignored; only a strictly newer capture for the current ride updates the marker; legacy events without capture time compare server receipt timestamps | Five deterministic tests in `src/mobile/shared/tripLocationSharing.test.cjs` | P0 |
| TRIP-LOC-007 | Rider receives first capture-timestamp event while its restored ride snapshot has only legacy receipt time | First capture watermark is accepted without comparing client capture time against server receipt time; subsequent events use strict capture ordering | `first capture watermark upgrades a legacy receipt-only snapshot without mixing clock semantics` | P0 |
| TRIP-MAP-001 | Driver coordinates update while Rider Activity map is following | After the matching native map reports ready, camera fits the Driver and pickup coordinates; if pickup is unavailable it follows the valid Driver coordinate | `map viewport includes valid Driver and pickup coordinates only`; map-ready gating test and Rider map effect use shared helpers | P1 |
| TRIP-MAP-002 | Rider manually pans the Activity map | Automatic camera movement pauses so the Rider's manual map position is respected | Physical-device manual-pan acceptance required | P0 |
| TRIP-MAP-003 | Rider selects Follow driver after manual pan | Camera fits the current Driver and pickup coordinates again and resumes following subsequent accepted SignalR updates | Shared viewport helper and Rider state/effect; physical-device acceptance required | P0 |
| TRIP-MAP-004 | Rider switches to another ride or Activity unmounts | Follow state resets for the new ride; stale events for the prior ride are ignored and map refs are not used after unmount | Ride-ID guard and effect cleanup; physical-device navigation acceptance required | P0 |
| TRIP-MAP-005 | Driver coordinates are available before the native map finishes mounting, or Activity is reopened | Camera fitting waits for onMapReady on the matching ride map, then fits current Driver+pickup; it never calls fit for an unready or different ride map | Rider camera follows only after the matching map is ready and follow is enabled; ride-keyed MapView and onMapReady guard | P1 |

The active-trip capture watermark is optional on existing ride documents and is appended to the existing ride aggregate; no new Cosmos container or cross-partition transaction is introduced. The one-minute future tolerance is only a client-clock skew bound, not a product location-freshness policy. Capture timestamps are untrusted client data; server receipt time remains separate for diagnostics. The endpoint remains restricted to the assigned Driver and active location-sharing lifecycle. No background tracking is added. Live Cosmos multi-instance ETag/restart, SignalR delivery/reconnect, Android/iOS foreground GPS, manual map pan and follow/recenter behavior remain physical/infrastructure gates; simulator compilation and deterministic helper tests do not replace them.

| TRIP-LOC-008 | Rider reconnects with a ride snapshot returned by the API, then receives an older SignalR event | The Rider compares the event capture time with the API's serialized `driverLocationCapturedAtUtc` watermark and ignores the older event; a newer event is accepted | `restored ride API capture watermark rejects an older SignalR capture` in `src/mobile/shared/tripLocationSharing.test.cjs` | P0 |

| TRIP-LOC-009 | A delayed ride-history/reconnect API response returns after a newer SignalR location event | Merge status/agreement/other non-location fields from the response but preserve the newer Driver coordinates and capture watermark; a genuinely newer API snapshot may replace them | `stale history refresh cannot overwrite a newer SignalR location snapshot` in `src/mobile/shared/tripLocationSharing.test.cjs` | P0 |


### TRIP-01 exact-head CI execution evidence (2026-11-08)

- PR #118 exact head `a13a8b706ddd0ee46eb635cbc6cd6ad6a48390a2` passed [GitHub Actions run #746 / 42491279009](https://github.com/rajeevp727/turnova-platform/actions/runs/42491279009), 8/8 jobs.
- Backend Release suite executed **317/317 tests** with zero failures. Rider/Driver typechecks and production JS exports, shared mobile tests, Android debug APK builds, unsigned iOS Simulator builds and native non-container API publish all passed.
- Both npm audits reported 0 critical, 0 high, 11 moderate and 1 low; the JSON reports and generated lockfiles are CI artifacts. No live Cosmos, physical-device, device-to-device SignalR or production validation is implied.


### DRIVER-03 Unicode payslip PDF rendering (2026-11-09)

| ID | Scenario | Steps / input | Expected result | Type | Priority |
|---|---|---|---|---|---|
| DRIVER-PDF-001 | Latin and nine Indic scripts render and remain searchable | Generate a monthly payslip with Driver/Union names containing Latin, Devanagari, Bengali, Gurmukhi, Gujarati, Odia, Tamil, Telugu, Kannada and Malayalam text; open the PDF with PdfPig | Embedded Noto fonts and HarfBuzz shaping produce valid PDF text with ToUnicode mappings; extracted text retains every supplied script without question-mark substitution | API unit + PDF extraction | P0 |
| DRIVER-PDF-002 | Unsupported script fails closed | Generate a payslip containing Arabic, Sinhala, emoji or another script outside the bundled font set | PDF generation stops with a safe unsupported-script result; API returns HTTP 422 with a generic message and does not emit a partial PDF or expose the name in the response | API unit | P1 |
| DRIVER-PDF-003 | Concurrent payslip generation is isolated | Generate four PDFs concurrently with shared immutable Skia typefaces, request-local HarfBuzz shapers/placements and a serialized PDFsharp searchable-layer append | Each request owns its HarfBuzz shapers and placements; the PDFsharp append is serialized around its process-wide font resolver/cache, with no cross-request text mixing | API unit + PDF extraction | P1 |
| DRIVER-PDF-004 | Long Unicode names wrap without changing snapshot data | Generate a PDF with a long mixed-script Driver name and compare the original immutable snapshot | PDF text wraps at safe grapheme boundaries; the persisted/in-memory payslip snapshot remains byte-for-byte unchanged | API unit + PDF extraction | P1 |
| DRIVER-PDF-005 | Payslip text remains least-privilege | Generate a payslip through the authenticated API using fixture ride IDs and work metrics; extract the generated PDF text | Driver/Union identity and approved monthly metrics are present; ride IDs, Rider/customer details, route, fare, currency, payment and net-payable details are absent | API integration + PDF extraction/security | P0 |

The tests are implemented in `DriverPayslipPdfTests` and `RideApiIntegrationTests`: `GeneratedPdfEmbedsFontsAndPreservesSearchableTextForAllBundledScripts` opens generated bytes with test-only PdfPig 0.1.16, asserts `/FontFile2` and `/ToUnicode`, and extracts every bundled script; the concurrency test extracts each parallel PDF and proves unique Driver markers do not cross-contaminate; the long-name test verifies the full normalized name remains extractable and the snapshot is unchanged. The authenticated API integration test extracts the actual payslip and asserts ride IDs, Rider/customer, route, fare, currency and net-payable details are absent. SkiaSharp/HarfBuzz draws the visual glyphs; PDFsharp 6.2.4 appends original Unicode text with embedded Noto fonts and ToUnicode. PdfSharpCore/ImageSharp 1.0.4 were removed after high-severity GIF-decoder advisories. PDFsharp's transitive Pkcs 8.0.1 was pinned to patched 10.0.13 after CVE-2026-84008; backend restore now fails on NU1900/NU1903/NU1904. The Skia-only attempt failed 2/340 tests; the PDFsharp + patched Pkcs replacement requires a fresh exact-head run. PR run #891 discovered all eight jobs after the YAML correction but was cancelled by a competing push event because both event types shared one branch-name concurrency group. Feature validation is now pull_request-only with PR-number concurrency; final-head PR CI #900 / 42543873677 on `c044f31de476edf28d00dba54d1ee858077a0d6f` passed 8/8 jobs, including 341/341 backend tests and Android/iOS Simulator build artifacts. Physical Android/iOS save/share/rendering acceptance remains external. Physical Android/iOS save/share/rendering acceptance remains external.


### RELEASE-01 compiled native artifact identity checks (2026-11-09)

| ID | Scenario | Steps / input | Expected result | Type | Priority |
|---|---|---|---|---|---|
| RELEASE-ART-001 | Android standalone release APK has the configured app identity | Build `assembleRelease`; inspect the actual APK with Android SDK `aapt dump badging`; compare the one embedded package ID with `expo.android.package` | Non-empty APK contains the Metro bundle and exactly one valid embedded package ID equal to the checked-in Rider/Driver package; missing, empty, malformed, ambiguous or mismatched artifact metadata fails CI | Native Android CI + deterministic Python unit tests | P0 |
| RELEASE-ART-002 | iOS Simulator app has the CI-assigned build identity | Build the unsigned Simulator `.app`; parse its actual `Info.plist` using standard-library `plistlib`; compare `CFBundleIdentifier` with the explicit CI-only identifier passed to `xcodebuild` | XML and binary plists are accepted only when a valid non-placeholder bundle ID exists and exactly matches the expected CI build identity; missing or mismatched metadata fails CI | Native iOS CI + deterministic Python unit tests | P0 |
| RELEASE-ART-003 | Native identity verification fails closed | Exercise parser fixtures for missing/ambiguous Android package lines, malformed/placeholder IDs, aapt non-zero exit, missing/empty APK, missing iOS bundle ID, malformed plist and cross-app identity mismatch | No failed inspection or identity mismatch is reported as verified; error output contains only the relevant artifact identity diagnostic | Python standard-library unit tests | P0 |
| RELEASE-ART-004 | Fresh simulator login is visible and platform-specific session failure is classified correctly | Reset the iOS Simulator before app install (Android emulator uses `-wipe-data`), install the same-run Rider/Driver artifacts without entering credentials, and inspect the login UI | Android shows required login text with no stale-session/storage error; unsigned iOS shows required login text plus the exact SecureStore-unavailable banner, while expired-session/token-cleanup errors remain absent; no production API/authentication call is made | Android emulator + iOS Simulator smoke and deterministic fake-SDK tests | P0 |
| RELEASE-ART-005 | Cold iOS Simulator first-boot readiness is bounded and fail-closed | After simulator erase/boot, run `simctl bootstatus` through a bounded Python subprocess; fake-SDK tests exercise a nonzero bootstatus exit and a sleeping fake command with a short timeout | The real bootstatus command is allowed up to the configured bounded timeout (default 600 seconds, validated 1–600); timeout/nonzero exit stops before app install and no success report is emitted | iOS Simulator CI + Python standard-library fake-SDK tests | P0 |

Implementation: `scripts/verify_native_app_identity.py` with 12 deterministic tests in `scripts/tests/test_verify_native_app_identity.py`. These are CI-only artifact checks. The iOS ID is a temporary simulator build identity already supplied by the workflow, not an approved production bundle identifier. No AAB/IPA signing, Apple registration, EAS remote linkage, physical-device acceptance or store approval is implied.

### PLATFORM-01 exact-head CI evidence — dispatch exception audit conflict recovery (2026-11-09)

PR #134 merged at 5f6f9cf3d25ac0fb04739b197c65fa0260d05639. Its exact head 37fe05de79ca8e5f97a49bfcf8ed48d7332164f0 passed GitHub Actions run #42562772947, all 8/8 jobs. The backend suite included the new DispatchExceptionAuditWriter tests for successful create/no read-back, matching canonical read-back despite timestamp drift, seven metadata mismatches, missing canonical record and cancellation propagation. The implementation treats a Cosmos 409 as an idempotent retry only after the canonical same-partition record matches event ID, event type, partition, actor, trace, allow-listed reason and null offer ID. No live Cosmos 409/read-after-write or multi-instance behaviour is claimed.

### RIDER-04 Cab-only reconciliation query CI evidence (2026-11-09)

PR #125 merged at 552dadea76215fa4a1f67c545e04a79070b6d44f and adds the case-insensitive Cab predicate before Cosmos materializes the allow-listed UnionManager reconciliation projection. The initial branch push CI run #42552962718 failed before job creation (zero jobs), so it is not counted as a passing exact-head run. After the workflow correction, main CI [run #42561935666](https://github.com/rajeevp727/turnova-platform/actions/runs/42561935666) passed all 8/8 jobs on 8368b0ef518bda956442e99f5e9cc343a332bba8 and included the merged query change. This is deterministic CI evidence only; live Cosmos SQL function/index/query-plan and partition validation remain external.


### PLATFORM-01 dispatch exception audit concurrency and conflict recovery (2026-11-09)

| ID | Scenario | Steps / input | Expected result | Type | Priority |
|---|---|---|---|---|---|
| PLAT-AUDIT-012 | Concurrent identical dispatch-exception writes | Execute two writes concurrently with the same actor, Cab partition, server trace and allow-listed reason against a barrier-controlled atomic fake store | Exactly one canonical event is created; the competing write reads and verifies that canonical event; both operations complete successfully and no duplicate event is appended | Deterministic .NET unit test | P1 |
| PLAT-AUDIT-013 | Concurrent trace reuse with different actor | Execute two writes concurrently with the same partition/trace/reason but different claim-derived actors | One canonical event is created; the competing write fails closed after canonical actor mismatch; no second event is accepted | Deterministic .NET unit test | P1 |
| PLAT-AUDIT-014 | Cancellation during canonical conflict read-back | Simulate a create conflict; cancel the same supplied CancellationTokenSource from inside the canonical point-read fake | The supplied token is observed as cancelled by the read operation, OperationCanceledException propagates, and the conflict is never treated as an idempotent success | Deterministic .NET unit test | P1 |

Implementation is in `DispatchExceptionAuditWriterTests`. The barrier-controlled fake forces both create attempts to overlap and models an atomic create-if-absent store. PLAT-AUDIT-014 cancels the same supplied `CancellationTokenSource` from inside the fake canonical `ReadAsync`, then asserts that cancellation is observed and propagated; it does not substitute an unrelated synthetic exception. These tests do not claim live Cosmos 409/read-after-write validation.


PR #139 code-bearing exact-head CI [run #42593042733](https://github.com/rajeevp727/turnova-platform/actions/runs/42593042733) passed all 8/8 jobs on `e2e6637fea7d64b49c5419f821dcdd0012c323db`, including 362/362 backend tests. Branch-sync exact-head CI [run #42597528464](https://github.com/rajeevp727/turnova-platform/actions/runs/42597528464) passed all 8/8 jobs on `72d0e16a81e918a5b54b0af01edc2bea79db1971`. The actual-token cancellation refinement and synchronized docs require their own final-head CI. Live Cosmos 409/read-after-write, transactional batch/ETag and multi-instance behaviour remain external.


## RELEASE-01 — native install and launch smoke

| ID | Scenario | Steps / input | Expected result | Type | Priority |
|---|---|---|---|---|---|
| RELEASE-SMOKE-001 | Rider standalone Android release APK install and launch | Download the Rider release APK produced by the native-Android matrix; boot the API 35 Google APIs x86_64 emulator; install the APK; resolve and start the package launch activity; wait for startup and inspect process/activity/logcat | The installed package is `com.turnova.rider`; ActivityManager reports successful launch; the activity remains in the emulator task stack; the same app process remains alive after startup; no app-scoped fatal native exception or unhandled React Native startup exception is present; screenshot and logs are uploaded | Android emulator CI using `scripts/android-emulator-smoke.sh` | P0 |
| RELEASE-SMOKE-002 | Driver standalone Android release APK install and launch | Same as RELEASE-SMOKE-001 using the Driver release artifact | The installed package is `com.turnova.driver`; launch/activity/process checks pass and screenshot/logs are uploaded | Android emulator CI using `scripts/android-emulator-smoke.sh` | P0 |
| RELEASE-SMOKE-003 | Rider unsigned iOS Simulator app install and launch | Download the Rider .app produced by native-iOS; select and boot an available iPhone Simulator; verify Info.plist bundle ID; install and launch with simctl; verify installed app/process and capture screenshot | The installed bundle is `com.turnova.rider`; simctl returns a process ID; the app remains discoverable through launchd/process inspection after startup; screenshot and diagnostics are uploaded | iOS Simulator CI using `scripts/ios-simulator-smoke.sh` | P0 |
| RELEASE-SMOKE-004 | Driver unsigned iOS Simulator app install and launch | Same as RELEASE-SMOKE-003 using the Driver simulator artifact | The installed bundle is `com.turnova.driver`; install/launch/process checks pass and screenshot/diagnostics are uploaded | iOS Simulator CI using `scripts/ios-simulator-smoke.sh` | P0 |

The simulator jobs consume the exact native artifacts built earlier in the same workflow run; they do not rebuild a different app. The smoke intentionally avoids production API calls, authentication-provider calls and credentials so it remains deterministic. Emulator/Simulator launch evidence is not physical-device acceptance and does not validate GPS hardware, push/SignalR provider integration, production hosting, signing, Apple registration, pilot readiness or store submission.


### RELEASE-01 simulator CI bootstrap correction

The first PR #140 exact-head run [#42606662115](https://github.com/rajeevp727/turnova-platform/actions/runs/42606662115) passed the original eight backend/mobile/native-build/auth jobs. Both Android emulator smoke jobs failed before ADB device registration; the uploaded emulator log showed `Unknown AVD name [turnova-release-smoke]`. The smoke script now explicitly sets `ANDROID_AVD_HOME` before invoking `avdmanager` and `emulator`, verifies the generated AVD `.ini` and directory, records AVD-manager output, detects early emulator-process exit and prints emulator diagnostics on timeout. Fatal-exception checks are package-scoped and React Native startup markers are filtered to the app PID. The corrected final head must pass all 12 jobs before this smoke is counted as passing evidence. The initial failure is a test-runner setup defect, not evidence that either APK launched successfully or failed at runtime.


### Deterministic harness and simulator login UI assertions

| ID | Scenario | Steps / input | Expected result | Type | Priority |
|---|---|---|---|---|---|
| RELEASE-SMOKE-005 | Android smoke runner AVD bootstrap and fake-device execution | Run the standard-library Python harness with fake SDK manager, AVD manager, emulator and ADB; supply a custom `ANDROID_AVD_HOME`; exercise package validation, AVD creation, install, launch, process stability and diagnostics | Harness passes without SDK download, real emulator or production calls; AVD files are created in the configured home and deterministic report/screenshot fixture is emitted | Python unittest in `scripts/tests/test_android_emulator_smoke.py` | P1 |
| RELEASE-SMOKE-006 | Android Rider/Driver login surface visible after native launch | Dump API 35 emulator UI hierarchy after process stability; assert login title, email/password hints and sign-in action are present | Fresh-install login UI is discoverable through Android UI hierarchy, not merely a live process | Android emulator CI using `scripts/android-emulator-smoke.sh` | P1 |
| RELEASE-SMOKE-007 | iOS Rider/Driver login surface visible after native launch | Capture iPhone Simulator screenshot and run local Vision OCR for login title, email/password fields and sign-in action | Fresh-install login UI text is visible in the simulator screenshot | iOS Simulator CI using `scripts/ios-simulator-smoke.sh` and `scripts/ios-simulator-screen-text-check.swift` | P1 |
| RELEASE-SMOKE-008 | iOS OCR detects a forbidden session-expiry banner split across visual lines | Feed Vision-equivalent OCR output with `Session` and `expired...` on separate lines | Whitespace normalization detects the full forbidden phrase and fails the smoke; a line wrap cannot bypass the stale-session guard | Deterministic fake-SDK Python unittest + Vision OCR smoke | P1 |
| RELEASE-SMOKE-009 | Unsigned iOS Simulator startup exposes the expected app-specific SecureStore boundary | Keep the native `CODE_SIGNING_ALLOWED=NO` artifact unchanged, reject any signed/ad-hoc app bundle, erase the simulator, install/launch and inspect OCR | Login surface renders; OCR accepts the complete Rider (`Unable to restore session`) or Driver (`Unable to restore your session`) banner plus the full recovery detail; expired-session/token-cleanup errors are absent; report explicitly marks SecureStore unavailable and does not claim Keychain persistence | macOS iOS Simulator CI using simctl + Vision OCR + fake-SDK tests | P1 |
| RELEASE-SMOKE-010 | Cold iOS Simulator first boot can exceed the initial readiness window | Erase and boot an iPhone Simulator, then run the real `simctl bootstatus` under a bounded subprocess; the fake SDK sleeps longer than a short timeout override | Default boot wait is 600 seconds; override remains validated from 1–600; nonzero exit/timeout stops before install and no success report is emitted | iOS Simulator CI + deterministic Python fake-SDK test | P0 |
| RELEASE-SMOKE-011 | iOS install/container/launch failures preserve actionable simulator diagnostics | Fake `simctl install` and `simctl launch` return nonzero with distinct stderr; exercise installed-container lookup failure | The smoke prints captured command output and a stage-specific failure message; no success report or launch artifact is emitted | Deterministic Python fake-SDK unittests; real iOS Simulator CI | P1 |
| RELEASE-SMOKE-012 | iOS Simulator launch denial or early process exit captures actionable system diagnostics | Force `simctl launch` to fail after install, or return a PID then make launchctl/procinfo report that the app exited; capture filtered simulator logs, launchctl state, process info, installed-app inventory, signature metadata and entitlements | Failure artifacts contain available diagnostics, the original failure remains visible, and no launch-success report or screenshot is emitted; collection is best-effort and cannot convert failure to success | Deterministic Python fake-SDK unittest; macOS iOS Simulator CI | P1 |
| RELEASE-SMOKE-013 | Android system-image download retries a transient corrupt archive and fails closed on exhaustion | Fake sdkmanager returns `ZipFile unknown archive` once and then writes a valid package; a second fixture returns the corrupt-archive failure for all attempts | A transient failure retries, removes the entire partial package directory (including non-empty corrupt files), then verifies non-empty `package.xml` and `system.img` before AVD creation; three consecutive failures stop before AVD creation, APK install or success report and leave no partial package directory | Python standard-library fake-SDK unittest + Android emulator CI | P1 |
| RELEASE-SMOKE-014 | Rider/Driver unsigned iOS Simulator SecureStore banner wording parity | Run the same fresh unsigned-Simulator smoke against both app artifacts; Rider uses `Unable to restore session`, Driver uses `Unable to restore your session` | OCR accepts either app-specific marker only when the full recoverable-error detail is present; both apps still reject expired-session, token-cleanup and secure-save errors | iOS Simulator CI + deterministic Python fake-SDK unittest | P1 |
| RELEASE-SMOKE-015 | Failed Android SDK image download removes partial non-empty package files before retry | Fake sdkmanager writes non-empty corrupt `package.xml`, `system.img` and a marker before returning `ZipFile unknown archive`; next attempt refuses to proceed if the marker survives | Only a partial package created by this invocation is removed after a failed/incomplete attempt; a complete pre-existing package is reused unchanged, while an incomplete pre-existing package is preserved and causes immediate fail-closed refusal. A failed new package never reaches AVD creation or success reporting | Python standard-library fake-SDK unittest | P1 |
| RELEASE-SMOKE-016 | iOS app process exit after successful launch captures actionable diagnostics | Fake launch returns a PID, but launchctl list omits the app and procinfo reports process-not-found | Smoke captures filtered system logs, launchctl state, process info, installed-app inventory, signature and entitlements, then fails without screenshot or success report | Python standard-library fake-SDK unittest + iOS Simulator CI | P1 |
| RELEASE-SMOKE-017 | Android Pixel Launcher ANR recovery is exact-dialog-only and bounded | Fake UI hierarchy reports the exact Pixel Launcher ANR with an enabled/clickable Wait control, then a recovered login hierarchy; also exercise persistent ANR, disabled/non-clickable Wait controls and a separate TurnOva app ANR | A transient launcher ANR is dismissed only through the parsed Wait control, at most twice, then login UI is verified; persistent/disabled/other ANR states fail closed without unsafe taps or success report | Python standard-library fake-SDK unittest + Android emulator CI | P1 |

| RELEASE-SMOKE-018 | Pre-existing Android API 35 system-image data is preserved | Seed a complete package with sentinel files and run smoke; separately seed an incomplete package with sentinel files and force sdkmanager failure | A complete pre-existing package is reused byte-for-byte without reinstall; an incomplete pre-existing package is preserved byte-for-byte and causes immediate fail-closed refusal; only a partial package created by this invocation may be removed/retried | Python standard-library fake-SDK unittest + Android emulator CI | P0 |
| RELEASE-SMOKE-019 | Android emulator smoke cannot mutate a developer environment | Invoke scripts/android-emulator-smoke.sh with CI unset or not equal to true, using fake SDK executables and a sentinel SDK/AVD tree | Script exits before SDK discovery/package installation/AVD creation; sentinel SDK/AVD contents remain unchanged and no success report is emitted | Python standard-library fake-SDK unittest | P0 |
| RELEASE-SMOKE-020 | iOS Simulator smoke cannot erase a developer simulator outside CI | Invoke scripts/ios-simulator-smoke.sh with CI unset or not equal to true, using fake xcrun and a sentinel simulator log | Script exits before simulator enumeration, shutdown, erase or boot; fake xcrun records no simulator mutation command and no success report is emitted | Python standard-library fake-SDK unittest | P0 |
| RELEASE-SMOKE-021 | iOS Simulator lifecycle and app operations are individually bounded | Use fake xcrun commands that sleep during erase and boot, with a one-second operation timeout | The timed-out operation exits with code 124, no later simulator mutation/install is attempted, and no success report is emitted; bootstatus retains its separate first-boot bound | Python standard-library fake-SDK unittest + iOS Simulator CI | P0 |
| RELEASE-SMOKE-022 | iOS Simulator operation timeout configuration fails closed | Supply zero, malformed or greater-than-300-second operation timeout values | Configuration is rejected before simulator enumeration or mutation; no success report is emitted | Python standard-library fake-SDK unittest | P1 |

The fake ADB emits an unhandled React Native marker from an unrelated PID during the passing case; the smoke must ignore it. A second invocation emits the same marker under the target app PID and must fail. The fake-device test also returns a deterministic UI hierarchy for the new `uiautomator`/`cat` commands and asserts all four login-surface nodes.

The Android UI hierarchy and iOS OCR checks exercise only the fresh-install login surface; they do not submit credentials or call an authentication/API provider. The iOS Simulator smoke keeps the native artifact unsigned because ad-hoc signing without an Apple-issued TeamIdentifier is rejected by CoreSimulator launchd (`Codesigning issue`, error 162). In this unsigned CI artifact, SecureStore access is unavailable; OCR accepts the Rider or Driver app's complete restore/storage-unavailable banner variant while continuing to reject expired-session, token-cleanup and secure-save errors. This is not Keychain success or clean-session restoration. Signed physical-device validation remains external.

CI discovers all `scripts/tests/test_*.py` cases, including the existing native artifact identity tests and the fake-SDK Android harness. This validates runner control flow; the real emulator and iOS Simulator jobs remain required for platform install/launch and UI evidence.

Failure history: initial PR #140 run [#42606662115](https://github.com/rajeevp727/turnova-platform/actions/runs/42606662115) found the missing explicit AVD home; fake-SDK run [#42610080183](https://github.com/rajeevp727/turnova-platform/actions/runs/42610080183) found an awk no-match success condition; run [#42610948851](https://github.com/rajeevp727/turnova-platform/actions/runs/42610948851) found that the fake ADB fixture lacked the new UI-hierarchy commands. The corresponding fixes are on the current branch and require a fresh exact-head run.

### RELEASE-01 hosted Android emulator KVM preflight

The Android emulator smoke job requires hardware acceleration for its x86_64 API 35 image. CI first verifies that `/dev/kvm` is present as a character device; if the runner user lacks read/write access, it changes `/dev/kvm` ownership to the current isolated GitHub-hosted runner user and applies owner-only mode `0600` and verifies access before invoking the emulator. The job fails closed when KVM is absent or access cannot be granted. This is runner setup only: it does not change production infrastructure or prove an app launch. A successful RELEASE-SMOKE-001/002 result still requires the emulator to install the exact same-run Rider/Driver APK, launch the expected activity, retain a stable process, and render the fresh-install login UI.


### RELEASE-01 simulator clean-state evidence (2026-11-09)

PR #140 merged at `4521c7e9a1448f85208ca744cd4824fd2a02596f`; exact-head CI [#42613834167](https://github.com/rajeevp727/turnova-platform/actions/runs/42613834167) passed 12/12, including 364/364 backend tests and all four simulator install/launch jobs. Android artifacts [13672451875](https://github.com/rajeevp727/turnova-platform/actions/runs/42613834167/artifacts/13672451875) and [13672686636](https://github.com/rajeevp727/turnova-platform/actions/runs/42613834167/artifacts/13672686636) show a clean login screen. iOS artifacts [13672189826](https://github.com/rajeevp727/turnova-platform/actions/runs/42613834167/artifacts/13672189826) and [13673926370](https://github.com/rajeevp727/turnova-platform/actions/runs/42613834167/artifacts/13673926370) show the required login text but also a stale session-restore/secure-storage error banner. The prior script did not erase simulator state; PR #141 adds erase-before-install and a fail-closed forbidden-banner assertion. Do not claim a clean iOS fresh-install result until the corrected exact-head run passes.


The iOS OCR checker collapses recognized whitespace before required/forbidden phrase matching. `test_stale_session_restore_error_fails_the_smoke` emits the forbidden phrase across two OCR lines and proves the check fails closed when Vision wraps the banner.


### RELEASE-01 cold simulator boot readiness timeout test

The iOS Simulator smoke runs `xcrun simctl bootstatus <UDID> -b` under Python `subprocess.run(timeout=...)` because the native `simctl` command does not expose a portable caller-configurable wait budget. The default is 600 seconds; `TURNOVA_SIMULATOR_BOOT_TIMEOUT_SECONDS` is accepted only as an integer from 1 to 600. The fake-SDK harness asserts the exact device/arguments, a nonzero bootstatus exit before install, and an actual timeout using a sleeping fake executable with a one-second budget. A timeout exits 124 and cannot create the smoke success report. This runner-control test does not claim the iOS app launched; only the real same-run simulator job and inspected screenshot/OCR can provide that evidence.


### RELEASE-01 follow-up after exact-head iOS smoke failure (2026-11-09)

Run [#1038 / 42634056917](https://github.com/rajeevp727/turnova-platform/actions/runs/42634056917) on head `03602ad2ad1348a17559213b10476ab205458361` passed the backend, both mobile validations/bundles, shared mobile tests, both Android native builds, both iOS native builds and both Android emulator smokes (10/12 jobs). The Rider iOS smoke exceeded the 300-second bootstatus bound and exited 124 before install. The Driver iOS smoke completed bootstatus but exited 1 later in the script before producing a launch/screenshot artifact. The failing Driver simctl stage was not identifiable because command-substitution output was discarded by `set -e`.

The next PR #141 increment raises the default cold-boot bound to 600 seconds (the existing validated maximum), prints captured install/container/launch command output on failure, and adds fake-SDK coverage for a slow default boot plus install/launch error diagnostics. The stale-session OCR assertion remains fail-closed. Do not count a clean iOS login result until both real simulator jobs pass and their screenshots/OCR are inspected.


### RELEASE-01 iOS launch-denial diagnostics

When `simctl launch` fails after install, the smoke now captures filtered simulator unified logs for SpringBoard, amfid, runningboardd, installd and the expected bundle ID, plus launchctl state, installed-app inventory, code-signature details and embedded entitlements. Collection is best-effort and remains failure-only; it cannot produce a success report. The deterministic fake-SDK regression verifies each diagnostic command/output and confirms no launch report or screenshot is emitted.


The launch-failure harness matches the full `log show` command by prefix because the predicate and time-window arguments are part of the recorded invocation. Its fake codesign emits deterministic signature/entitlement content, and the test verifies the captured files rather than only checking that empty files exist.

### RELEASE-01 unsigned iOS Simulator and SecureStore boundary (2026-11-09)

- `RELEASE-IOS-UNSIGNED-001`: The smoke rejects any signed/ad-hoc app bundle before simulator access and consumes the original `CODE_SIGNING_ALLOWED=NO` native artifact.
- `RELEASE-IOS-UNSIGNED-002`: After simulator erase, install, launch and process-stability checks, Vision OCR must find the required login text and the complete SecureStore-unavailable banner using either current app wording: Rider `Unable to restore session` or Driver `Unable to restore your session`, followed by `Check your connection or secure storage and retry`.
- `RELEASE-IOS-UNSIGNED-003`: Both current app wording variants are accepted only in expected unsigned-Simulator mode. `Session expired`, `Unable to clear secure session storage` and secure token-save failures remain forbidden; a missing/incomplete expected banner fails and cannot create `result.json`.
- The fake-SDK harness covers unsigned/ad-hoc signature gating, both Rider/Driver SecureStore banner variants, missing-banner and line-wrapped expired-session rejection, simulator erase-before-install, boot timeout, install/launch/process-exit failures and failure-only diagnostics.
- Exact-head run [#1049 / 42645314471](https://github.com/rajeevp727/turnova-platform/actions/runs/42645314471) exposed the signing failure: both uploaded iOS diagnostics record `error=162: Codesigning issue` and `TeamIdentifier=not set`. The unsigned smoke is the supported CI path; live Keychain access, token persistence and session restoration require a signed build on approved physical devices and remain external RELEASE-01/auth acceptance gates.


### RELEASE-01 Android SDK system-image download resilience (2026-11-09)

The API 35 Google APIs x86_64 system-image package is downloaded separately from the stable Android platform/emulator packages. The smoke retries that package at most three times when sdkmanager reports a corrupt/truncated archive, verifies non-empty `package.xml` and `system.img` before creating the AVD, removes an incomplete extraction, and fails before app install/report if all attempts fail. The fake-SDK harness injects one corrupt archive followed by success and a second case with three consecutive failures; no real SDK download is needed for these deterministic tests.


### RELEASE-01 Rider/Driver SecureStore banner copy parity (2026-11-09)

The app sources intentionally retain their current recoverable SecureStore wording: Rider displays `Unable to restore session. Check your connection or secure storage and retry.`; Driver displays `Unable to restore your session. Check your connection or secure storage and retry.`. The unsigned Simulator OCR checker accepts either exact marker plus the complete detail, but only when the explicit `--expect-secure-store-unavailable` mode is enabled. It continues rejecting `Session expired`, token-cleanup and secure-save failures after whitespace normalization. The fake-SDK harness exercises both wording variants and verifies the normalized forbidden-session check. This is a test-boundary correction only; no app runtime or authentication copy was changed.

Prior run [#1056 / 42656790721](https://github.com/rajeevp727/turnova-platform/actions/runs/42656790721) on `ecc1882...` passed 10/12 jobs, including both Android emulator smokes and both iOS native builds. The Android Rider/Driver screenshots and UI hierarchies were inspected; both login surfaces were present and production API/authentication was not exercised. The iOS Simulator smoke jobs were cancelled after the branch advanced, so no iOS screenshot/OCR result is claimed. Current PR #141 head is `2f2b095c6b03413e1bbc28ccb61b22348aa68b3c`; exact-head CI and inspection of both iOS simulator artifacts remain required.


### RELEASE-01 latest retry and process-exit diagnostic hardening (2026-11-09)

A failed Android `sdkmanager` attempt can leave non-empty but corrupt `package.xml` and `system.img` files. The retry now deletes the entire isolated API 35 system-image directory after every failed/incomplete attempt, before the next download. The fake SDK writes a corrupt marker and the next attempt fails if it survives; the permanent-failure case asserts that no package directory or AVD remains. For iOS, the smoke now calls the same best-effort system/launchctl/installed-app/signature diagnostics when the app process disappears after a successful launch, not only when `simctl launch` itself fails. The deterministic harness verifies process-info and system diagnostic output and proves no screenshot or success report is emitted. These are CI-only test-runner changes; app runtime, authentication and production configuration are unchanged.

Last code head: `657e663feeb370efee61111fcb344294f1036f3b`. Prior run [#1056 / 42656790721](https://github.com/rajeevp727/turnova-platform/actions/runs/42656790721) passed 10/12 jobs on the earlier head; both Android emulator smoke artifacts were inspected and passed, both iOS native builds passed, but both iOS simulator smoke jobs were cancelled after branch advancement. Exact-head CI after the current code/doc sync must pass all 12 jobs and both iOS screenshots/OCR must be inspected before merge.


### RELEASE-01 Pixel Launcher ANR recovery evidence (2026-11-10)

Run [#1070 / 42663289638](https://github.com/rajeevp727/turnova-platform/actions/runs/42663289638) showed the Android Rider activity and process remained active while the system Pixel Launcher displayed its own `isn't responding` modal. The smoke must not classify that as a TurnOva crash or blindly tap a fixed coordinate. The current script parses the UI hierarchy, requires the exact `Pixel Launcher isn't responding` title, locates `android:id/aerr_wait`, verifies `clickable=true` and `enabled=true`, taps the calculated center, and retries no more than twice. The fake-SDK harness covers a transient recovery, persistent failure and a disabled Wait control; the disabled case must fail before any tap and cannot emit a success report. Run [#1072 / 42669755886](https://github.com/rajeevp727/turnova-platform/actions/runs/42669755886) on the preceding head passed Rider Android and both iOS simulator smokes, but the Driver Android smoke job was absent and the overall run concluded failure. Require a fresh exact-head run with all 12 jobs.


### RELEASE-01 exact-head Android/iOS install-launch smoke evidence (2026-11-10)

Exact-head code run [#1079 / 42679676632](https://github.com/rajeevp727/turnova-platform/actions/runs/42679676632) on 51dbb81b6baf3a6e832889132b63f15b1923b310 passed all 12/12 expected jobs. It includes backend 364/364, shared JavaScript 102/102, Python fake-SDK/native-artifact 28/28, both mobile validation/bundles, all four native builds and all four simulator smoke jobs.

| Test ID | Scenario | Expected/result evidence | Result |
|---|---|---|---|
| RELEASE-ANDROID-SMOKE-001 | Install the Rider standalone release APK on API 35 x86_64 Android emulator | Correct package com.turnova.rider; install/launch pass; process stable; login text visible; stale-session error absent; zero Pixel Launcher Wait attempts. Artifact [13702114989](https://github.com/rajeevp727/turnova-platform/actions/runs/42679676632/artifacts/13702114989). | Passed |
| RELEASE-ANDROID-SMOKE-002 | Install the Driver standalone release APK on API 35 x86_64 Android emulator | Correct package com.turnova.driver; install/launch pass; process stable; login text visible; stale-session error absent; zero Pixel Launcher Wait attempts. Artifact [13702836717](https://github.com/rajeevp727/turnova-platform/actions/runs/42679676632/artifacts/13702836717). | Passed |
| RELEASE-IOS-SMOKE-001 | Install/launch Rider unsigned Simulator app after simulator erase | Bundle com.turnova.rider; install/launch pass; process stable; login text visible; expected unsigned SecureStore-unavailable banner visible; stale-session expired error absent. Artifact [13705430717](https://github.com/rajeevp727/turnova-platform/actions/runs/42679676632/artifacts/13705430717). | Passed |
| RELEASE-IOS-SMOKE-002 | Install/launch Driver unsigned Simulator app after simulator erase | Bundle com.turnova.driver; install/launch pass; process stable; login text visible; expected unsigned SecureStore-unavailable banner visible; stale-session expired error absent. Artifact [13704571529](https://github.com/rajeevp727/turnova-platform/actions/runs/42679676632/artifacts/13704571529). | Passed |

The Android smoke consumes the standalone release APK artifacts produced in the same workflow run; it does not test a Play Store-signed AAB. The iOS smoke consumes the original unsigned Simulator .app artifact; its SecureStore-unavailable banner is expected in this unsigned CI boundary. Both iOS result files report secureStoreAvailable=false, keychainAccessGroup unavailable, and productionApiOrAuthentication not exercised. These tests do not prove Keychain/token persistence, production auth/API, signed AAB/IPA, physical-device permissions, GPS, realtime providers, pilot or store readiness. The documentation-sync commit after code head 51dbb81b6baf3a6e832889132b63f15b1923b310 requires a fresh exact-head 12-job run before PR #141 may merge.


### RELEASE-01 final exact-head install/launch evidence — PR #141 (2026-11-10)

Exact-head CI [#1080 / 42686782043](https://github.com/rajeevp727/turnova-platform/actions/runs/42686782043) passed all 12/12 jobs on final PR head 6d7e6776e27ad34d926eb454856d41c293776a2e: backend 364/364, shared JS 102/102, Python fake-SDK/native-artifact 28/28, both mobile validation/bundle jobs, all four native builds and all four simulator smoke jobs. The Rider Android artifact [13706635203](https://github.com/rajeevp727/turnova-platform/actions/runs/42686782043/artifacts/13706635203) and Driver Android artifact [13704609925](https://github.com/rajeevp727/turnova-platform/actions/runs/42686782043/artifacts/13704609925) were inspected: both standalone release APKs installed/launched on API 35 emulators, remained process-stable, showed the correct role-specific login surface, contained no stale-session error and required zero Pixel Launcher Wait attempts. The Rider iOS artifact [13707380822](https://github.com/rajeevp727/turnova-platform/actions/runs/42686782043/artifacts/13707380822) and Driver iOS artifact [13706204614](https://github.com/rajeevp727/turnova-platform/actions/runs/42686782043/artifacts/13706204614) were inspected: both unsigned Simulator apps installed/launched after simulator erase, remained process-stable, showed the login surface and expected SecureStore-unavailable banner, and had no stale-session-expiry error. These are CI simulator results only; iOS Keychain/token persistence, production auth/API, signed AAB/IPA, physical-device permissions/GPS/realtime, pilot and store acceptance remain external. The latest docs synchronization advances the PR head and therefore requires another exact-head CI run before merge.

### RELEASE-01 exact-head run #1085 — all-platform simulator evidence (2026-11-10)

Exact-head CI [#1085 / 42708832985](https://github.com/rajeevp727/turnova-platform/actions/runs/42708832985) passed all 12/12 jobs on code/evidence head `076b37dd2c0d71524bb8424c639399ece494d6af`: .NET 10 backend 364/364; shared JavaScript 102/102; Python fake-SDK/native-artifact tests 30/30; Rider and Driver mobile configuration/typecheck/production bundle validation; all four Android/iOS native builds; both Android standalone release-APK emulator smokes; and both unsigned iOS Simulator smokes. The four smoke artifacts were downloaded and inspected: Rider Android [13716265571](https://github.com/rajeevp727/turnova-platform/actions/runs/42708832985/artifacts/13716265571), Driver Android [13714823999](https://github.com/rajeevp727/turnova-platform/actions/runs/42708832985/artifacts/13714823999), Rider iOS [13717028524](https://github.com/rajeevp727/turnova-platform/actions/runs/42708832985/artifacts/13717028524), Driver iOS [13714969794](https://github.com/rajeevp727/turnova-platform/actions/runs/42708832985/artifacts/13714969794). Both Android apps installed/launched, remained process-stable, displayed the correct role-specific login screen, had no stale-session error, and needed zero Pixel Launcher Wait attempts. Both iOS simulators were erased before install; both apps installed/launched, remained process-stable, displayed the correct login screen and the expected unsigned-Simulator SecureStore-unavailable banner, with no expired-session error. The iOS result JSON explicitly reports `secureStoreAvailable=false`, no Keychain access group and `productionApiOrAuthentication=not exercised`. This is CI-only startup evidence; it does not prove Keychain persistence, production auth/API, signed release distribution, physical-device behaviour, pilot or store readiness.

The Python suite includes 30 deterministic fake-SDK/native-artifact tests, covering Android system-image retry/exhaustion, AVD setup, launcher-ANR recovery/fail-closed cases, iOS simulator reset/boot/install/launch/OCR readiness, expected Rider/Driver SecureStore banner variants, stale-session rejection and native artifact identity checks. The shared JavaScript suite passed 102/102; the .NET 10 backend suite passed 364/364. The four real simulator jobs used same-run native artifacts; Android smoke used the standalone release APKs. These remain CI-only install/startup checks, not production auth/API, live Cosmos, Keychain/token persistence, signed distribution, physical-device, pilot or store evidence. The docs-only synchronization on top of the tested code head requires its own exact-head CI before PR #141 merges.

### RELEASE-SMOKE-008 — isolated Android emulator ownership

| ID | Scenario | Steps / input | Expected result | Type | Priority |
|---|---|---|---|---|---|
| RELEASE-SMOKE-023 | Android smoke owns only its temporary AVD and never globally stops emulators | Run the fake-SDK harness with a pre-existing user AVD directory and sentinel, omit an explicit AVD home, and instrument fake ADB's `emu` command; execute install/launch/cleanup | Default AVD home is isolated under the invocation's temporary runner directory; an existing user AVD and sentinel remain unchanged; cleanup removes only the AVD created by this invocation; no global `adb emu kill` command is issued; any harness failure remains a failure | Python unittest + Android emulator CI | P1 |

Implementation is covered by `scripts/tests/test_android_emulator_smoke.py`. The latest commit `98cd89ff0a74a6c4bc5feedda29c4b44b189b0f2` adds an explicit negative assertion against global emulator shutdown. Exact-head [CI run #42717698786](https://github.com/rajeevp727/turnova-platform/actions/runs/42717698786) passed all 12 jobs, including 30/30 Python tests and both real Android emulator smoke jobs.

### RELEASE-01 exact-head execution evidence — 2026-11-10

[CI run #42717698786](https://github.com/rajeevp727/turnova-platform/actions/runs/42717698786) passed 12/12 jobs on `98cd89ff0a74a6c4bc5feedda29c4b44b189b0f2`: 364/364 backend tests; 102/102 shared JS tests; 30/30 Python fake-SDK/native-artifact tests; Rider/Driver mobile validation and production JS bundle exports; four native Android/iOS builds; and four same-run Android/iOS simulator install/launch/login-surface smokes. Android reports verify stable processes, role-specific login UI and no stale-session error. Unsigned iOS simulator reports verify stable launch and role-specific login UI while explicitly recording SecureStore unavailable and not claiming Keychain persistence. See the four smoke artifacts linked in `docs/MVP_PROGRESS.md`. Physical-device, production signing, production API/authentication and store acceptance remain external.


## RELEASE-01 exact-head verification — CI run #42726122385 (2026-11-10)

PR #141 head 3316980ec59d1e865eb2bfbd2c9d0be55015fa09 passed all **12/12 expected jobs** in [workflow run #42726122385](https://github.com/rajeevp727/turnova-platform/actions/runs/42726122385) (run number 1095). The run executed .NET 10 backend tests **364/364**, shared mobile JavaScript tests **102/102**, deterministic Python fake-SDK/native-artifact tests **32/32**, Rider and Driver mobile validation/bundle exports, all four native Android/iOS builds, and all four same-run simulator install/launch/login-surface smokes.

- Android Driver smoke passed for com.turnova.driver, artifact [13722738316](https://github.com/rajeevp727/turnova-platform/actions/runs/42726122385/artifacts/13722738316); Android Rider smoke passed for com.turnova.rider, artifact [13722214388](https://github.com/rajeevp727/turnova-platform/actions/runs/42726122385/artifacts/13722214388). Both used the same-run standalone release APKs (Driver [13723352891](https://github.com/rajeevp727/turnova-platform/actions/runs/42726122385/artifacts/13723352891), Rider [13723197862](https://github.com/rajeevp727/turnova-platform/actions/runs/42726122385/artifacts/13723197862)); both install/launch checks passed and the process remained available for the smoke assertions.
- iOS Rider smoke passed for com.turnova.rider, artifact [13724815604](https://github.com/rajeevp727/turnova-platform/actions/runs/42726122385/artifacts/13724815604); iOS Driver smoke passed for com.turnova.driver, artifact [13724351483](https://github.com/rajeevp727/turnova-platform/actions/runs/42726122385/artifacts/13724351483). Both unsigned Simulator apps launched and the expected SecureStore-unavailable banner was detected. This does not prove Keychain persistence or signed-device authentication.
- The native build artifacts are Rider/Driver Android standalone APKs and unsigned iOS Simulator apps (Rider [13723174674](https://github.com/rajeevp727/turnova-platform/actions/runs/42726122385/artifacts/13723174674), Driver [13723199006](https://github.com/rajeevp727/turnova-platform/actions/runs/42726122385/artifacts/13723199006)). No production API/authentication, live Cosmos, GPS provider, SignalR, signed distribution or physical-device behaviour was exercised.
- The 32 Python tests include the new Android pre-existing SDK package preservation and Android/iOS CI-only invocation guards. Android smoke reuses a complete pre-existing API 35 image unchanged; an incomplete pre-existing package is preserved and causes immediate fail-closed refusal. Only a partial package created by the current invocation is eligible for cleanup/retry. Both native smoke scripts reject non-CI invocation before SDK mutation or simulator erasure.

This is engineering/build/simulator evidence only. RELEASE-01 remains Blocked for strict acceptance pending approved production app identifiers/EAS linkage, signed AAB/IPA, production HTTPS API configuration, physical Android/iOS security and functional tests, pilot acceptance and store submission. Strict acceptance remains **0/19 Verified** (15 In Progress, 4 Blocked); this is not an implementation-completion percentage. Production readiness is not established.

The documentation synchronization is a new commit on top of the tested code head and therefore requires its own exact-head 12-job CI run before merge. Submit a COMMENT-only review anchored to that resulting SHA; merge only if all required checks pass, no critical/high-risk issue or unresolved review thread remains, and GitHub policy permits it. Do not self-approve or bypass protections.


### RELEASE-01 exact-head all-platform simulator evidence — 2026-11-10

Exact-head CI [#1097 / 42736524343](https://github.com/rajeevp727/turnova-platform/actions/runs/42736524343) passed all 12/12 jobs on `a1494f906696b6e28c2aa688534cd069a52601ec`: .NET 10 backend 364/364; shared JavaScript 102/102; Python fake-SDK/native-artifact suite 35/35; Rider/Driver mobile validation and production JavaScript bundles; four Android/iOS native builds; two Android standalone release-APK emulator smokes; and two unsigned iOS Simulator smokes. Same-run artifacts inspected: Rider Android [13728123213](https://github.com/rajeevp727/turnova-platform/actions/runs/42736524343/artifacts/13728123213), Driver Android [13727234634](https://github.com/rajeevp727/turnova-platform/actions/runs/42736524343/artifacts/13727234634), Rider iOS [13728118398](https://github.com/rajeevp727/turnova-platform/actions/runs/42736524343/artifacts/13728118398), Driver iOS [13729585235](https://github.com/rajeevp727/turnova-platform/actions/runs/42736524343/artifacts/13729585235). Android result JSON/screenshots verify correct package, install/launch, stable process and role-specific login UI with no stale-session error; both had zero Pixel Launcher Wait attempts. iOS result JSON/screenshots verify erased simulator, correct bundle, install/launch, stable process and login UI plus the expected unsigned SecureStore-unavailable banner. iOS explicitly reports `secureStoreAvailable=false`, no Keychain access group and `productionApiOrAuthentication=not exercised`; no Keychain persistence or production auth/API is claimed. The iOS simulator harness now bounds each `simctl` operation (default 180 seconds, configurable 1–300) and keeps first-boot `bootstatus` separately bounded at 600 seconds. This is simulator CI evidence only, not signed release, physical-device, pilot or store acceptance.

### RELEASE-SMOKE-009 — iOS simulator timeout configuration preflight

| ID | Scenario | Steps / input | Expected result | Type | Priority |
|---|---|---|---|---|---|
| RELEASE-SMOKE-024 | Validate every iOS simulator timeout before simulator access | Exercise malformed, zero and above-maximum values for simulator-operation (1–300s), first-boot (1–600s), login-readiness (1–300s) and login polling (1–30s) timeout settings; also exercise valid lower and upper boundaries | Invalid values exit with configuration error before the first `simctl` command; no simulator enumeration, erase, boot, install, launch or screenshot occurs and no success artifact is written. Valid boundary values continue through the deterministic smoke fixture. | Python unittest + mobile CI | P1 |

Implementation is covered by `scripts/tests/test_ios_simulator_smoke.py`. All four timeout settings are now parsed and validated before the first simulator enumeration or mutation. This prevents malformed late-stage configuration from erasing or launching a simulator before the smoke fails closed.



### RELEASE-SMOKE-010 — iOS OCR checker compile-once preflight

| ID | Scenario | Steps / input | Expected result | Type | Priority |
|---|---|---|---|---|---|
| RELEASE-SMOKE-025 | Compile the iOS screenshot OCR checker once before simulator access and reuse it for readiness polling | Fake Swift compiler success/failure/timeout; force two not-ready OCR results before login text appears; exercise compile-timeout values outside 1–600 seconds and both valid boundaries | The checker is compiled exactly once before the first simctl command and reused for every screenshot poll. Compilation failure or timeout fails closed before simulator enumeration/erasure/install/launch and cannot write a success artifact. Invalid compile timeout values are rejected before simulator access. | Python unittest + iOS CI | P1 |

Implementation is covered by `scripts/tests/test_ios_simulator_smoke.py`. The script now compiles the Vision OCR checker to a temporary executable once, outside the login-readiness deadline, then reuses it for each screenshot. This prevents first-invocation Swift interpreter compilation from consuming the cold-start polling budget.


## RELEASE-01 exact-head execution evidence — CI #42750283649 (2026-11-10)

PR #141 code/evidence head `ce88ade0f6b265de72d9bb520e8e866ea71e88d1` passed all **12/12 expected jobs** in [workflow run #42750283649](https://github.com/rajeevp727/turnova-platform/actions/runs/42750283649) (run number 1101). The run executed .NET 10 backend tests **364/364**, shared mobile JavaScript tests **102/102**, deterministic Python fake-SDK/native-artifact tests **40/40**, Rider and Driver mobile validation/production bundle exports, all four native Android/iOS builds, and all four same-run simulator install/launch/login-surface smokes.

- Android Rider [artifact 13734170935](https://github.com/rajeevp727/turnova-platform/actions/runs/42750283649/artifacts/13734170935) and Driver [artifact 13732909631](https://github.com/rajeevp727/turnova-platform/actions/runs/42750283649/artifacts/13732909631) show correct package/activity, install/launch passed, stable process, role-specific login UI, no stale-session error and zero Pixel Launcher Wait attempts. Both used same-run standalone release APKs.
- iOS Rider [artifact 13733574238](https://github.com/rajeevp727/turnova-platform/actions/runs/42750283649/artifacts/13733574238) and Driver [artifact 13733349134](https://github.com/rajeevp727/turnova-platform/actions/runs/42750283649/artifacts/13733349134) show erased simulator, install/launch passed, stable process and role-specific login UI. Both explicitly report `secureStoreAvailable=false`, `secureStoreUnavailableBannerVerified=true`, `staleSessionExpiredErrorAbsent=true`, unsigned simulator artifact, no Keychain access group and production API/authentication not exercised. Screenshots visibly show the generic session-restore warning used as the expected unsigned-Simulator SecureStore-unavailable banner. The expired-session assertion passed; this is not Keychain persistence or signed-device authentication evidence.
- RELEASE-SMOKE-025 is covered in `scripts/tests/test_ios_simulator_smoke.py`: the Vision OCR checker compiles once before simulator access, is reused for readiness polling, and compiler failure/timeout fails closed before simulator enumeration or mutation.

This is engineering/build/simulator evidence only. RELEASE-01 remains Blocked for strict acceptance pending approved production identifiers/EAS linkage, signed AAB/IPA, production HTTPS API configuration, physical Android/iOS functional/security testing, pilot acceptance and store submission. Strict acceptance remains **0/19 Verified** (15 In Progress, 4 Blocked); this is not an implementation-completion percentage. Production readiness is not established. The docs synchronization is a new commit and requires a fresh exact-head 12-job CI run plus a final COMMENT-only review before merge; no self-approval or bypass.


## AI-01 exact-head execution evidence — 2026-11-10

PR #142 head `79c473661d7804f761bedebbfe871ecd9b0e94e2` passed all 12/12 jobs in [CI #1110 / 42769495825](https://github.com/rajeevp727/turnova-platform/actions/runs/42769495825). The backend suite executed 373/373 tests; shared JavaScript passed 102/102; deterministic Python fake-SDK/native tests passed 40/40. Both Rider/Driver mobile validation and bundle jobs, all four native Android/iOS builds and all four same-run simulator smoke jobs passed. Same-run artifacts 13742280744, 13742285530, 13741469890 and 13742567031 were downloaded and inspected. Android Rider/Driver login UI passed; unsigned iOS Rider/Driver login UI passed with SecureStore unavailable and no Keychain access group. This does not prove signed-device persistence or production authentication.

AI-026–AI-030 were included in the 373-test backend run and cover independent timeout when the provider ignores cancellation, late fault observation, synchronous/asynchronous timeout and HTTP failures, in-flight caller cancellation, caller-cancellation precedence and unexpected synchronous/asynchronous adapter exceptions. AI-01 remains supplemental/outside the 19 acceptance rows.
