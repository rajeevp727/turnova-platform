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
| AUTH-MOBILE-028 | App-specific profile role guard | Return a Rider profile to the Driver app or a Driver profile to the Rider app during login/register or /me restoration | The client rejects the mismatched profile before saving a new token pair or setting authenticated app state | Shared mobile unit + Rider/Driver typecheck | P0 |

| AUTH-LOGIN-031 | Logout during an epoch-guarded access-token write | Pause the access-key write for a refresh, advance the session epoch and begin logout before releasing the write | The stale pair is rolled back inside the serialized storage mutation; logout clears both keys; no stale refresh can restore authenticated state | Mobile unit | P0 |
| AUTH-LOGIN-032 | Logout during an epoch-guarded refresh-token write | Pause the refresh-key write after the access key has been written, advance the epoch and begin logout | Both token keys are removed before the storage queue completes; the stale refresh reports not accepted | Mobile unit | P0 |
| AUTH-LOGIN-033 | New login supersedes an in-flight refresh persistence | Pause an old refresh write, begin a new login (advancing epoch and queueing prior-token cleanup), then complete the new login pair | The old refresh cannot overwrite or clear the new pair; only the new access/refresh pair remains stored and authenticated | Mobile unit + Rider/Driver | P0 |
| AUTH-LOGIN-034 | New session does not reuse an old single-flight refresh | Keep an old refresh promise pending, advance the session epoch, then request a refresh for the new session before resolving the old promise | The new session starts its own refresh; late completion of the old promise cannot clear or replace the new in-flight promise | Mobile unit | P0 |
| AUTH-LOGIN-035 | Superseded login cannot restore authenticated state | Begin login/registration, advance the epoch through a newer login/logout while the request is in flight, then complete the stale response | The stale response is rejected before token persistence or in-memory profile/token updates; Driver preflight cleanup also stops before sending a superseded request | Mobile unit + Rider/Driver | P0 |
| AUTH-LOGIN-036 | Protected request during logout | Begin logout while old tokens remain in memory, then start a new protected API request before secure-store deletion completes | The request is rejected before network dispatch because the authenticated epoch no longer matches the active session epoch | Mobile unit + Rider/Driver | P0 |
| AUTH-LOGIN-037 | Protected request during account switch | Advance the session epoch for a replacement login while the previous token pair remains in memory; start a protected request before the new pair is accepted | The previous access token is not sent; the new epoch becomes authenticated only after guarded token persistence succeeds | Mobile unit + Rider/Driver | P0 |
| AUTH-LOGIN-038 | Restored session binds to a fresh epoch | Restore a valid stored token pair and authoritative profile, then exercise the protected-request guard; also cover invalid/missing profile | Only a fully restored session binds its tokens to the new epoch; failed restore remains unauthenticated | Mobile unit + Rider/Driver | P1 |
| AUTH-LOGIN-039 | SignalR token request after session invalidation | Pause SecureStore access-token read, advance the session epoch through logout/account switch, then release the read; also invoke the token factory after invalidation | SignalR receives an empty token; neither stored nor in-memory stale credentials are returned; current-epoch reads may use the current stored token or same-session fallback | Shared mobile unit + Rider/Driver | P0 |
| AUTH-LOGIN-040 | Logout/account switch stops active session resources | Register Rider and Driver SignalR transports and the Driver foreground location watcher, then advance the session epoch | All active transports/watchers stop immediately without blocking local credential cleanup; stop failures do not prevent invalidation | Shared mobile unit + Rider/Driver | P0 |
| AUTH-LOGIN-041 | Stale realtime callbacks are ignored | Capture an authenticated connection epoch, then advance the session epoch and deliver a late ride/location/offer/agreement event | Rider and Driver handlers ignore the event and do not update private UI state or start recovery requests | Shared mobile unit + Rider/Driver | P0 |
| AUTH-LOGIN-042 | Protected response completes after logout | Start an authenticated API request, advance the session epoch while its response body is being read, then release the body | The response is rejected after parsing and is not returned to callers for stale state updates; deterministic deferred-body tests cover the guard | Shared mobile unit + Rider/Driver | P0 |
| AUTH-LOGIN-043 | Driver foreground location watcher stops on session invalidation | Start foreground location sharing for an active Driver ride, then invalidate the session during permission lookup, watch startup or an active subscription | No new location subscription is started after invalidation; an active subscription and AppState listener are stopped immediately; late callbacks cannot publish coordinates | Shared mobile unit + Driver | P0 |
| AUTH-LOGIN-044 | SignalR start completes after session invalidation | Hold `HubConnection.start()` pending, invalidate the owning session, then resolve start successfully | The connection helper stops the just-started transport and reports it inactive; no reconnect recovery or private state update runs | Shared mobile unit + Rider/Driver | P0 |
| AUTH-LOGIN-045 | Startup profile restore completes after sign-in begins | Hold the initial /auth/me request during startup restore, begin a replacement login, then release the old profile response | The old profile/tokens are not applied and the replacement login remains authoritative | Shared mobile unit + Rider/Driver | P0 |
| AUTH-LOGIN-046 | Startup refresh completes after account switch | Hold the startup refresh request, advance the session epoch and persist a replacement login pair, then release the old rotated pair | The stale startup refresh does not persist or publish its token pair | Shared mobile unit + Rider/Driver | P0 |
| AUTH-LOGIN-047 | Stale startup 400/401 cannot clear a replacement login | Hold startup /auth/refresh, persist a replacement login pair after epoch advance, then reject the old refresh with 400/401 | The stale restore returns without deleting the replacement token pair | Shared mobile unit + Rider/Driver | P0 |
| AUTH-LOGIN-048 | Partial token cleanup rechecks its observed pair | Pause a token-pair write after the access key, start a partial-pair read, then finish the refresh-key write before queued cleanup executes | Cleanup re-reads under the mutation queue, sees the complete replacement pair differs from the observed partial snapshot, and preserves it | Shared mobile unit + Rider/Driver | P0 |

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

### DRIVER-03 payslip session concurrency

| ID | Scenario | Expected result | Automated evidence | Priority |
|---|---|---|---|---|
| PAY-028 | Account A starts a payslip download, then logs out or is replaced by Account B while Account B authentication is still loading; Account A's request finalizes afterward | The stale Account A finalizer must not clear Account B's loading state. Session reset clears prior-account loading, while a finalizer for the still-current initiating session clears its own loading state. If logout invalidates the session but secure-storage cleanup fails, loading is released only when that same logout epoch is still current; a stale logout failure cannot clear a newer account's loading state. Existing exact-file payslip cache cleanup remains unchanged. | `finishLoadingIfSessionCurrent` deterministic unit cases and `sessionStateReset.test.cjs` App.tsx source-integration assertions | P1 |

### RIDER-04 Driver payment-report session concurrency

| ID | Scenario | Expected result | Automated evidence | Priority |
|---|---|---|---|---|
| PAY-029 | Account A submits a Driver-reported direct-payment amount, then the authenticated session changes before the POST/history response completes | The stale request cannot refresh the new account's history, clear its payment-report input, set its notice/error or clear its loading state. A current-session report still refreshes history and clears only that ride's input. The API idempotency key and Driver-reported/unverified semantics remain unchanged. | Deferred-promise session regression and App.tsx source-integration assertions in `sessionStateReset.test.cjs` | P1 |

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
| PLATFORM-HEALTH-002 | Readiness probe calls `/health/ready` in demo/in-memory mode | HTTP 200 with `status: ready`; public response does not disclose the storage mode or database name | API integration | P0 |
| PLATFORM-HEALTH-003 | Readiness probe calls `/health/ready` in Cosmos mode while the database is unavailable | HTTP 503; readiness does not report healthy when the configured persistent store is unreachable | Cosmos integration/failure injection | P0 |

| PLATFORM-CORS-003 | Configured origin contains a wildcard hostname such as `https://*.turnova.test` | Startup validation rejects the origin before it is added to the CORS policy | Unit |
| PLATFORM-CORS-004 | Configured origin contains a wildcard character elsewhere in the origin string | Startup validation rejects the origin; no wildcard matching is enabled | Unit |
| PLATFORM-CORS-005 | Configured origin is an explicit HTTPS origin with no path, user info, query or fragment | Validation accepts the origin | Unit |
| PLATFORM-CORS-006 | Configured origin uses HTTP | Validation accepts HTTP only when the host environment is Development and rejects it otherwise | Unit |
| PLATFORM-CORS-007 | Configured origin is blank, malformed, has a URL path, user info, query or fragment | Validation rejects the origin with a generic configuration error | Unit |

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
| CAB-REC-016 | Stored payment report RideId differs from its containing DemoRide.RideId | Cosmos query and in-memory fallback exclude the mismatched report; a matching report remains visible and the stored aggregate is not mutated | `UnionReconciliationCosmosQueryRequiresReportRideIdToMatchAggregateId`; `UnionReconciliationExcludesReportWhoseRideIdDoesNotMatchAggregate` | API/privacy + deterministic query-contract test; live Cosmos SQL validation remains external | P0 |
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


### RIDER-02 mobile proposal-attempt idempotency

| ID | Scenario | Expected result | Automated evidence | Status |
|---|---|---|---|---|
| CAB-AGR-022 | Same Rider/Driver repeats the same ride/amount/currency after an ambiguous proposal POST failure | Same actor-prefixed Idempotency-Key is reused; backend returns the original immutable version rather than creating a duplicate | identical proposal retries reuse the same key after an ambiguous request failure | CI #1180 / 42882071110 (153/153 shared tests) |
| CAB-AGR-023 | Rider or Driver changes ride ID or amount after a prior attempt | New fingerprint and new key; the old attempt remains isolated | proposalAttempt helper fingerprint tests | CI #1180 / 42882071110 (153/153 shared tests) |
| CAB-AGR-024 | Rider or Driver double-taps proposal submission | Only one request is active; amount input is not editable while submitting | Rider proposal submission uses stable attempts and blocks concurrent taps; Driver proposal submission uses stable attempts and blocks concurrent taps | CI #1180 / 42882071110 (153/153 shared tests) |
| CAB-AGR-025 | Proposal POST succeeds but agreement-history refresh fails | Attempt key remains available; retry replays the same server version; clear only after history refresh succeeds | app source integration assertions in proposalAttempt.test.cjs | CI #1180 / 42882071110 (153/153 shared tests) |
| CAB-AGR-026 | A late completion for an old proposal attempt arrives after a newer attempt exists | Old completion cannot clear the newer attempt | a late completion cannot clear a newer proposal attempt | CI #1180 / 42882071110 (153/153 shared tests) |
| CAB-AGR-027 | Rider or Driver session reset occurs with a pending proposal attempt | Pending attempt ref is cleared for the previous session | Rider and Driver proposal submission uses stable attempts and blocks concurrent taps (session-reset source assertions) | CI #1180 / 42882071110 (153/153 shared tests) |
| CAB-AGR-028 | Actor or currency changes while ride and amount stay the same | A distinct key is generated for the changed actor/currency fingerprint | changing actor or currency creates a new proposal attempt key | CI #1180 / 42882071110 (153/153 shared tests) |
| CAB-AGR-029 | Logout, refresh expiry or account replacement occurs while a proposal POST is pending | Session reset clears the attempt and in-flight owner; a new account can submit without waiting for the old request, and the old finalizer cannot clear the new lock | Rider/Driver source-integration assertions plus `finishProposalSubmission` owner test | CI #1180 / 42882071110 (153/153 shared tests) |
| CAB-AGR-030 | The active ride changes while a proposal POST is pending | Old ride attempt and lock are reset; a late old completion cannot release a new ride's submission lock | Rider/Driver source-integration assertions plus `finishProposalSubmission` owner test | CI #1180 / 42882071110 (153/153 shared tests) |

PR #145 source/test head `56a363621029fbfb16e2ef7c72d14f1faad48c8d` and documentation head `9fbca10d57334167aa41018a9104096f9f85e877` passed [CI #1180 / 42882071110](https://github.com/rajeevp727/turnova-platform/actions/runs/42882071110) with **12/12 jobs successful**. Results: .NET 10 backend **373/373**, shared mobile JavaScript **153/153**, Python native-artifact/simulator harness **41/41**, Rider/Driver TypeScript validation and production bundles, both Android native builds, both iOS Simulator native builds, and all four Android/iOS simulator smoke jobs. The Driver Android smoke initially encountered a Pixel Launcher ANR; only that failed job was rerun, and the retry passed without app changes. The final workflow conclusion is success.

Same-run smoke artifacts were downloaded and inspected, including result JSON and screenshots:
- Rider Android `13795890654`: `com.turnova.rider`, install/launch passed, process stable, Rider login text and screenshot verified, stale-session error absent.
- Driver Android `13798054264`: `com.turnova.driver`, targeted retry after the first artifact `13795995296` recorded a Pixel Launcher ANR; retry install/launch passed, process stable, Driver login text and screenshot verified, stale-session error absent, zero launcher recovery attempts on the successful retry.
- Rider iOS `13796097620`: `com.turnova.rider`, clean simulator data erased, install/launch passed, process stable, Rider login screen verified.
- Driver iOS `13795844308`: `com.turnova.driver`, clean simulator data erased, install/launch passed, process stable, Driver login screen verified.

Both iOS result JSONs report `secureStoreAvailable=false`, the expected SecureStore-unavailable banner, no Keychain access group, unsigned simulator artifacts, and production API/authentication not exercised. These artifacts prove simulator install/launch/login-surface checks only; they do not establish signed-device Keychain persistence, production authentication, live Cosmos, or physical-device acceptance. Final-head COMMENT-only review [5847757020](https://github.com/rajeevp727/turnova-platform/pull/145#pullrequestreview-5847757020) found no critical/high issue; unresolved review threads: **0**. The subsequent documentation-only synchronization requires fresh exact-head CI before merge.

The client attempt is currently held in an in-memory ref. Startup agreement-history reload mitigates ambiguous outcomes after process restart, but persistent client-key replay across process death is not established. The submission lock is attempt-key-owned and reset on account/session or ride identity change; a stale request finalizer cannot release a replacement lock. CI #1180 / 42882071110 passed the exact prior documentation head with all CAB-AGR-022–030 mobile tests included in the 153/153 shared suite. This subsequent documentation synchronization requires fresh exact-head CI. Live Cosmos ETag/multi-instance and Android/iOS negotiation/reconnect acceptance remain external.

## RIDER-03 — selected-driver booking and reservation

- Verify only an active Cab Union, active membership, approved Cab Driver, approved driving licence/vehicle registration, Online state, fresh location, passenger capacity and configured pickup radius can be booked.
- Verify the selected Driver and Union are persisted with pickup, destination and optional stops; the agreement snapshot contains the same route.
- Verify Idempotency-Key retries return the same ride, while reusing a key with a different request fingerprint conflicts.
- Verify interrupted-write recovery reuses the original idempotency CreatedAtUtc for ride RequestedAt, the RideCreated audit timestamp and NegotiationExpiresAtUtc; the recovered ride deadline must equal the already-held reservation expiry and must not be extended by retry time.
- Verify two Riders cannot acquire overlapping reservations for one Driver; Rider cancellation, Driver decline, negotiation expiry and trip completion release only the matching reservation.
- Verify one Rider cannot hold two concurrent active selected bookings for different Drivers using different Idempotency-Keys, and a pre-existing active legacy ride without a Rider reservation also blocks a new selected booking or Driver acceptance; the second request conflicts without creating a second ride or Driver lease. The Rider lease is a fixed `active-selected-booking` document in the existing `RideIdempotency` container's `/riderId` partition. Create-if-absent acquisition and append-only audit creation are one partition transaction; Driver acceptance promotes the lease with an ETag-guarded replace plus audit transaction; cancellation, Driver decline, expiry and completion delete only the matching RideId with an ETag condition plus audit transaction. The API reconciles the authoritative ride before releasing an expired reservation; the store itself never steals an expired lease. When no Rider reservation exists, the API also checks the Rider's active ride projection so pre-deployment/legacy active rides remain exclusive.
- Verify an expired temporary lease is not stolen by the store: the API first transitions the linked Negotiating/AgreementConfirmed ride to Expired (or releases an expired orphan lease), then releases only the matching reservation so a new booking can acquire it. A live lease becomes permanent only after mutual agreement and Driver acceptance; active reservations prevent going offline.
- Verify Driver acceptance is rejected before both parties confirm the current agreement and the selected booking transitions Negotiating → AgreementConfirmed → Accepted.
- Verify expiry is configuration-gated: missing Dispatch:SelectedDriverNegotiationLifetimeMinutes returns 503; no default lifetime is invented. Lazy expiry is triggered on ride reads/history, Rider discovery and Driver availability/reservation checks. Reservation stores expose expired leases to the API and never delete a lease before ride-state reconciliation.
- Verify Rider and Driver history reads reconcile expired selected negotiations before returning them; each transition is recorded once as a System Expired event and releases only the matching Driver and Rider reservations.
- Automated tests added in RIDER-03 cover selected booking, route stops and display snapshots, exact-coordinate privacy, idempotent retry/payload mismatch and interrupted-write recovery, create-if-absent persistence, union mismatch, concurrent Driver reservation, Union transfer exclusion while a selected negotiation is active, cancellation/decline, terminal-reservation reconciliation, expiry/release during discovery, missing-lifetime fail-closed behaviour, agreement-gated Driver acceptance, and reservation promotion; and Rider/Driver history-triggered expiry reconciliation (`RiderAndDriverHistoryReconcileExpiredSelectedNegotiationsAndReleaseOnlyTheirReservations`). The new Rider-scoped reservation tests are listed below.

RIDER-03 backend tests are executed evidence: PR #64 final-head CI run [42272254259](https://github.com/rajeevp727/turnova-platform/actions/runs/42272254259) ran 189/189 tests successfully, including booking/reservation and Union-transfer coverage. Native non-container API publish, Rider/Driver Expo config/typechecks and shared mobile tests also passed. Live Cosmos ETag/multi-instance/restart and Android/iOS acceptance remain external gates. Automated reviews remain recorded in PR comments [6689967817](https://github.com/rajeevp727/turnova-platform/pull/57#issuecomment-6689967817) and [6690099440](https://github.com/rajeevp727/turnova-platform/pull/57#issuecomment-6690099440).

### RIDER-03 Rider-scoped single-active-booking regressions

| ID | Scenario | Steps / input | Expected result | Type | Priority |
|---|---|---|---|---|---|
| RIDER03-RES-001 | One Rider submits two different Drivers concurrently with different Idempotency-Keys | Start two selected-booking requests from the same Rider against two eligible Drivers | Exactly one request creates a ride; the other returns 409; one active Rider reservation and one active selected ride remain | API concurrency | P0 |
| RIDER03-RES-002 | Different Riders book different Drivers concurrently | Submit one request per Rider with independent Drivers and keys | Both bookings succeed; Rider partitions do not contend | API integration | P0 |
| RIDER03-RES-003 | Rider cancellation releases only the matching Rider reservation | Create a selected booking, cancel it, then select another Driver with a new key | First reservation is removed and the second booking succeeds; a stale release cannot delete the new RideId | API lifecycle + store unit | P0 |
| RIDER03-RES-004 | Driver acceptance promotes the Rider reservation and same-key replay remains idempotent | Create a booking, persist a mutually confirmed agreement, accept it, replay the original booking key, then submit a different key | The original ride replays with HTTP 200; the Rider reservation is permanent; a different booking returns 409 | API integration | P0 |
| RIDER03-RES-005 | Expired negotiation is reconciled before a new Rider booking | Expire the authoritative Negotiating ride, then request another Driver | The first ride transitions to Expired and its matching Rider/Driver reservations are released before the new booking is created | API lifecycle | P0 |
| RIDER03-RES-006 | Completed selected ride releases the Rider reservation | Complete a selected Cab ride after both parties confirm the latest agreement | The terminal transition releases only that ride's Rider reservation | API lifecycle | P0 |
| RIDER03-RES-007 | Driver decline releases the Rider reservation | Decline a selected negotiation as its assigned Driver | The ride is terminal and the matching Rider reservation is absent | API lifecycle | P0 |
| RIDER03-RES-008 | Reservation store enforces atomic acquisition and safe lifecycle | Race two different RideIds for one Rider; exercise same-key replay, permanent promotion, stale release, expired lease and audit serialization | One acquisition wins; same RideId/hash replays without duplicate audit; wrong/stale RideId cannot release a newer reservation; store does not steal expired leases; audit excludes route, GPS, contacts, tokens and fare | Store unit + concurrency | P0 |
| RIDER03-RES-009 | Rider app prevents a second booking while an active ride exists | Restore a Requested, Matching, Offered, Negotiating or InProgress ride at sign-in; attempt Book tab, discovery and selected-booking submission | App loads the server-owned active ride, redirects to Activity and blocks Book/search/request until terminal status; API remains authoritative | Shared mobile unit + Rider typecheck | P0 |
| RIDER03-RES-010 | Each legacy active ride status is protected when no Rider reservation exists | For each of Requested, Matching, Offered, Negotiating, AgreementConfirmed, Accepted, Arrived and InProgress, seed a pre-deployment ride for the Rider, then request a selected Cab Driver with a different key | API reads the authoritative active ride, returns 409 and creates neither a new ride nor a Driver reservation | Parameterized API integration | P0 |
| RIDER03-RES-011 | Driver acceptance cannot bypass any pre-existing active legacy ride for the same Rider | For each of the eight active statuses, create a selected negotiation, seed a separate legacy ride for the same Rider, then attempt Driver acceptance | API returns 409, does not promote the Rider reservation or transition the selected ride to Accepted, and preserves the temporary Driver lease | Parameterized API integration | P0 |

The Rider reservation is stored in the existing `RideIdempotency` container with partition key `/riderId`; no new Cosmos container or migration is introduced. If the Rider has no reservation, the API also checks existing active ride records so pre-deployment/legacy active rides block a second selected booking and cannot be bypassed at Driver acceptance. The in-memory store is deterministic test/demo evidence only. Live Cosmos transactional-batch/ETag/multi-instance/restart validation remains external. The selected-driver negotiation lifetime remains a Product-approved configuration gate; the 15-minute integration-test value is not a product default.


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

### DISPATCH-01 — selected-booking radius and location-freshness fail-closed gates

| ID | Scenario | Expected result | Automated evidence | Priority |
|---|---|---|---|---|
| DISPATCH-01-BOOKING-001 | Selected booking has no configured discovery radius | HTTP 503 before ride, idempotency, Driver reservation or Rider reservation writes | SelectedBookingFailsClosedWhenNearbyRadiusIsMissingOrInvalidBeforeAnyReservationWrites(null) | P0 |
| DISPATCH-01-BOOKING-002 | Selected-booking radius is zero, negative, NaN, positive infinity or negative infinity | HTTP 503 before ride, idempotency, Driver reservation or Rider reservation writes | SelectedBookingFailsClosedWhenNearbyRadiusIsMissingOrInvalidBeforeAnyReservationWrites theory cases | P0 |
| DISPATCH-01-BOOKING-003 | Selected booking has no configured maximum Driver location age | HTTP 503 before ride, idempotency, Driver reservation or Rider reservation writes | SelectedBookingFailsClosedWhenLocationFreshnessPolicyIsMissingOrNonPositive(null) | P0 |
| DISPATCH-01-BOOKING-004 | Selected-booking maximum Driver location age is zero or negative | HTTP 503 before ride, idempotency, Driver reservation or Rider reservation writes | SelectedBookingFailsClosedWhenLocationFreshnessPolicyIsMissingOrNonPositive theory cases | P0 |

These tests prove selected-booking has the same fail-closed configuration boundary as discovery and that invalid policy cannot leave partial booking or reservation state. They do not choose the production radius or freshness threshold; Product approval remains an external gate.

### DISPATCH-03 — selected-driver expiry and reservation reconciliation evidence

- `ExpiredSelectedNegotiationBecomesTerminalAndReleasesDriverReservation` verifies an expired Negotiating ride becomes terminal and releases only its selected Driver lease.
- `DiscoveryExpiresAnExpiredNegotiationBeforeReleasingItsDriverReservation` verifies discovery reconciles the ride lifecycle before returning the Driver to eligible results.
- `DiscoveryReleasesPermanentReservationWhenRideIsAlreadyTerminal` verifies a terminal ride's stale reservation is reconciled before another booking.
- `SelectedBookingReservationPreventsConcurrentBookingsAndRiderCancelReleasesIt` and `DriverDecliningSelectedBookingCancelsRideAndReleasesReservation` verify cancellation/decline cleanup; `ExpiredTemporaryReservationRemainsHeldUntilRideLifecycleReconciliation` and `ReleaseOnlyRemovesMatchingRide` verify expiry retention and matching-ride-only release. `RiderAndDriverHistoryReconcileExpiredSelectedNegotiationsAndReleaseOnlyTheirReservations` additionally verifies both history routes trigger one System Expired audit event and release only the matching reservation (PR #95, CI [42352928382](https://github.com/rajeevp727/turnova-platform/actions/runs/42352928382), 249/249 backend tests).
- The API's selected-booking expiry is lazy: ride reads/history, Rider discovery and Driver availability/reservation checks trigger the authoritative conditional transition. Cosmos ride replacement uses an IfMatch ETag after snapshot comparison; the reservation store returns expired leases to the API rather than deleting them independently.
- `ExpiredSelectedNegotiationDoesNotAdvanceLegacyFifoOffer` (PR #97, commit [`f8774f4`](https://github.com/rajeevp727/turnova-platform/commit/f8774f40b53b8f6d389c260adbc8f60fef8b313b)) injects a recording `IRideOfferAdvancer`, expires a customer-selected negotiation through the Rider read path and proves the selected flow never invokes FIFO advancement while releasing the matching reservation.
- `DriverDecliningSelectedBookingDoesNotAdvanceLegacyFifoOffer` (PR #148, source/test commit `c7a9d4649dfa5343f27515e12780e0cbdab166fc`; `DISPATCH-03-REJECT-011`) creates a Union-linked selected Cab booking, confirms both reservations are held, posts the assigned Driver's reject endpoint while Negotiating, then asserts HTTP 204, Negotiating → Cancelled, Driver-owned audit, exact Driver/Rider reservation release and zero calls to the recording `IRideOfferAdvancer`.
- `DriverRejectingExpiredSelectedNegotiationDoesNotAdvanceLegacyFifoOffer` (PR #148, same source/test commit; `DISPATCH-03-REJECT-012`) moves the selected negotiation expiry into the past and posts the Driver reject endpoint. It asserts authoritative expiry reconciliation runs first, the stale reject returns HTTP 409, Negotiating → Expired has System-owned audit, both exact reservations are released and FIFO advancement is never invoked.
- Exact-head CI [#1231 / 42950871464](https://github.com/rajeevp727/turnova-platform/actions/runs/42950871464), run attempt 3 on tested PR head `3ef0e10c953a4538e25162fcc015aea36d513541`, passed **12/12 jobs** after only the initially failed Driver Android and Driver iOS simulator jobs were each retried once. Backend **413/413** (18 warnings, zero errors); shared mobile **171/171**; Python native-artifact harness **41/41**; Rider/Driver TypeScript and bundles; four native builds; four simulator smokes. Same-run smoke artifacts Rider Android `13825815457`, Driver Android retry `13826556864`, Rider iOS `13826002670`, Driver iOS retry `13826194724` were downloaded and their result JSONs/screenshots inspected. iOS native artifacts Rider `13825272195` and Driver `13825786705` were inspected; both are unsigned simulator apps with expected bundle IDs and no provisioning profile.
- The first Driver Android smoke failed on a Pixel Launcher ANR dialog while the Driver process was foreground/resumed; its one retry passed. The first Driver iOS smoke timed out during `simctl install` after 300 seconds waiting for the System App; its one retry passed. Neither failed attempt was waived. Production API/authentication, live Cosmos and physical-device behaviour were not exercised.
- `RideOfferExpiryProcessorTests.ProcessBatchAsync_AdvancesAfterOfferIsMarkedExpired` remains the positive control proving the separate legacy offer-expiry worker advances only its own queue-offer path.
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
| PLATFORM-HEALTH-001 | API runs in demo in-memory mode | Liveness and readiness return HTTP 200; readiness returns only service/status and does not identify the storage provider | `PublicHealthEndpointsExposeOnlyGenericStatusMetadata` | P0 |
| PLATFORM-HEALTH-002 | Storage dependency probe fails | Readiness returns generic HTTP 503 while liveness remains HTTP 200; response omits provider exception and infrastructure metadata | `ReadinessReturns503WhenStorageIsUnavailableWhileLivenessRemainsHealthy` | P0 |
| PLATFORM-HEALTH-003 | Storage probe exceeds its configured bound | Probe cancels after three seconds in production wiring; unit test verifies timeout maps to a readiness failure | `CheckAsyncMapsProbeTimeoutToReadinessFailure` | P0 |
| PLATFORM-HEALTH-004 | Caller cancels the readiness request | Request cancellation propagates and is not misreported as a dependency timeout | `CheckAsyncPropagatesCallerCancellation` | P1 |
| PLATFORM-HEALTH-005 | Unauthenticated root and health endpoints are queried | `/` exposes only name/version/message; `/health` and `/health/ready` expose only service/status; storage mode, database name, environment and provider metadata are absent | `PublicHealthEndpointsExposeOnlyGenericStatusMetadata` | P0 |
| PLATFORM-HEALTH-006 | Storage readiness fails | Generic HTTP 503 problem details contain no storage mode, database, environment, endpoint, connection-string or raw provider exception metadata | `ReadinessReturns503WhenStorageIsUnavailableWhileLivenessRemainsHealthy` | P0 |
| PLATFORM-AUDIT-015 | Numeric actor reference contains allowed separators | Phone-like actor references with at least eight digits and only numeric/separator characters are reduced to the System actor before persistence | `ReservationAuditRedactsPhoneLikeActorIdsWithAllowedSeparators` | P0 |
| PLATFORM-AUDIT-016 | Opaque actor reference contains letters and digits | Opaque internal IDs containing letters remain intact; only phone-like numeric references are redacted | `ReservationAuditPreservesOpaqueActorIdsContainingLetters` | P1 |
| PLATFORM-AUDIT-017 | Reservation audit event uses a declared event type | The four declared reservation event types are accepted unchanged | `ReservationAuditAcceptsOnlyTheFourDeclaredEventTypes` | P1 |
| PLATFORM-AUDIT-018 | Reservation audit event uses an unknown or malformed event type | Factory rejects the event before an append-only audit fact can be created | `ReservationAuditRejectsUnknownEventTypesBeforeCreatingAnEvent` | P0 |
| PLATFORM-AUDIT-019 | Operations selfie rejection reason is missing, oversized or padded | Normalize once; reject null/blank or over-300-character input and persist the same trimmed value in profile and audit event | `RejectionReasonIsTrimmedToOneStableValue`, `RejectionReasonRejectsNullOrWhitespace`, `RejectionReasonRejectsMoreThanThreeHundredCharactersAndAcceptsTheLimit` | P1 |
| PLATFORM-AUDIT-020 | Operations selfie-rejection API must reject invalid reasons without mutation and persist one normalized reason consistently | Null, blank and 301-character reasons return BadRequest without changing profile/audit; padded valid reason is trimmed identically in response, profile and append-only Union audit event | `UnionManagerCanReviewOnlyCurrentAssignedCabDriverSelfiesAndReceivesMinimalData` | P1 |
| PLATFORM-AUDIT-021 | Identical selfie/reason retries use one privacy-safe event identity | Event identity is stable for the same Driver, current Union, selfie capture timestamp, review timestamp, selfie-content digest and normalized reason; a new review timestamp creates a distinct event identity | `SelfieRejectionEventIdentityIsStableAndDoesNotExposeInputs`, `SelfieRejectionEventIdentityChangesWhenPhotoOrReasonOrReviewTimestampChanges` | P0 |
| PLATFORM-AUDIT-022 | Operations repeats an already completed identical rejection | Preserve the original profile review timestamp and event ID; do not append a duplicate audit event | `UnionManagerCanReviewOnlyCurrentAssignedCabDriverSelfiesAndReceivesMinimalData` | P0 |
| PLATFORM-AUDIT-023 | Profile update succeeds but the audit write is absent when the client retries | Retry reconstructs the same deterministic audit event from the rejected profile and restores it without moving the review timestamp | `UnionManagerCanReviewOnlyCurrentAssignedCabDriverSelfiesAndReceivesMinimalData` | P0 |
| PLATFORM-AUDIT-024 | Audit create conflicts with an identical canonical event | Point-read the event by exact ID and Driver partition and treat the retry as successful only when every persisted field matches | `ConflictRecoveryAcceptsAnExactCanonicalEvent`, `ConflictRecoveryReadsByExactEventIdAndDriverPartition`, `ConflictRecoveryRetriesPointReadUntilCanonicalEventAppears` | P0 |
| PLATFORM-AUDIT-025 | Audit create conflicts with a different event at the same deterministic identity | Fail closed; do not report a mismatched audit record as a successful write | `ConflictRecoveryFailsClosedWhenCanonicalEventDiffers`, `ConflictRecoveryFailsClosedWhenCanonicalActorDiffers` | P0 |
| PLATFORM-AUDIT-026 | Audit create conflicts but the canonical point read remains missing | Perform bounded reads and fail closed when no canonical record becomes visible | `ConflictRecoveryFailsWhenCanonicalEventNeverBecomesReadable`, `ConflictRecoveryPropagatesCancellationBeforeReading` | P0 |
| PLATFORM-AUDIT-027 | Operations audit actor identity is resolved | Use `sub` only from an authenticated Ops-role principal; never use email-like `Identity.Name`; otherwise use the fixed Operations actor | `AuditActorUsesOpaqueSubjectInsteadOfEmailLikeIdentityName`, `AuditActorFallsBackWhenOnlyEmailLikeNameIsAvailable`, `AuditActorFallsBackWhenSubjectPrincipalIsNotOperations` | P0 |
| PLATFORM-AUDIT-028 | JWT subject resembles an email address or phone number | Do not persist contact-shaped subject values in the Union audit actor; use the fixed Operations fallback | `AuditActorFallsBackWhenSubjectLooksLikePersonalContactData` | P0 |
| PLATFORM-AUDIT-029 | Audit write is missing and a different Operations principal retries the same rejection | Preserve the original actor with the profile review; reconstruct the audit using the original actor; redact private actor metadata from Driver profile responses | `UnionManagerCanReviewOnlyCurrentAssignedCabDriverSelfiesAndReceivesMinimalData` | P0 |
| PLATFORM-AUDIT-030 | Operations rejects a selfie previously rejected by the current UnionManager with the same reason | Do not mistake the UnionManager review for an Operations retry; create a new Operations review timestamp and a separately attributed audit event | `OperationsRetryRequiresAnOperationsReviewMarker`, `OperationsRejectionAfterUnionManagerRejectionCreatesItsOwnReviewAndAudit` | P0 |
| PLATFORM-AUDIT-031 | A Rider/Driver JWT subject reaches an API-key-protected Operations audit route | Do not attribute the event to a non-Ops principal; use the fixed Operations actor unless an authenticated Ops-role principal supplies a safe opaque subject | `AuditActorFallsBackWhenSubjectPrincipalIsNotOperations` | P0 |
| PLATFORM-AUDIT-032 | A selfie is rejected by Operations, approved by UnionManager, then rejected again by Operations with the same reason and selfie | Treat the second Operations decision as a new review; include its review timestamp in the deterministic event identity so the prior event cannot cause a false canonical conflict | `SelfieRejectionEventIdentityChangesWhenPhotoOrReasonOrReviewTimestampChanges`, `OperationsRejectionAfterUnionManagerRejectionCreatesItsOwnReviewAndAudit` | P0 |
| PLATFORM-AUDIT-033 | Operations rejects a selfie while another booking, ride or Union transfer owns the Driver reservation | Serialize Operations rejection with the shared per-Driver reservation lease; reject the competing review without changing the profile, then allow it after lease release | `OperationsSelfieReviewRespectsAnExistingDriverReservationLease` | P0 |
| PLATFORM-AUDIT-034 | A transfer follows an Operations profile commit whose Union audit write is missing | Reconstruct and verify the Operations audit from private profile metadata before transfer clears the metadata; preserve original actor/event ID/timestamp | `UnionTransferResetsSelfieApprovalUntilTheNewUnionApprovesIt` | P0 |
| PLATFORM-AUDIT-035 | Operations review marker fields are present but blank/whitespace | Treat any non-null marker as an attempted review; reject incomplete snapshots before selfie replacement can clear them. Onboarding resubmission uses the same guard. | `BlankButPresentOperationsReviewMarkersAreTreatedAsIncompleteMetadata`, `DriverSelfieReplacementFailsClosedWhenOperationsReviewMetadataIsIncomplete` | P0 |
| PLATFORM-AUDIT-036 | UnionManager lists currently assigned Cab members | The scoped member-list projection omits phone/email, exact GPS, private document storage references, Rider identity and chat data while retaining minimum selfie/Cab review details | `UnionManagerCanReviewOnlyCurrentAssignedCabDriverSelfiesAndReceivesMinimalData` | P0 |
| PLATFORM-AUDIT-037 | The same UnionManager selfie review is retried by a different authorised manager | Preserve the original actor in the private profile snapshot; canonical audit read-back succeeds only when the persisted actor exactly matches that snapshot and every other canonical field matches. A retrying manager cannot replace or mask the original actor. | `UnionManagerReviewRetryUsesStoredOriginalActorForCanonicalMatch`, `UnionManagerReviewCanonicalMatchFailsClosedWhenActorChanges`, `UnionManagerSelfieAuditRetryReconstructsMissingEventWithOriginalActor`, `AuditActorUsesOpaqueSubjectForUnionManager`, `UnionManagerAuditActorFallsBackForContactLikeSubjects` | P0 |
| PLATFORM-AUDIT-038 | Operations performs a Union assignment/transfer audit with a contact-like JWT subject | Persist only the validated opaque Ops subject or the fixed Operations fallback; never persist email-like `Identity.Name` | `UnionTransferResetsSelfieApprovalUntilTheNewUnionApprovesIt`, `AuditActorFallsBackWhenSubjectLooksLikePersonalContactData` | P0 |
| PLATFORM-AUDIT-039 | An initial assignment or transfer must survive a profile/assignment write followed by an audit-write failure | Store a versioned deterministic event identity, event type, original opaque actor, previous Union snapshot and timestamp in the assignment record; reject incomplete, unsafe or hash-mismatched snapshots; do not invent legacy audit events | `AssignmentEventIdentityIsStableAndDoesNotExposeInputs`, `InitialAssignmentSnapshotReconstructsAssignedEventWithoutPreviousUnion`, `AssignmentSnapshotReconstructsCanonicalEventAndAllowsOnlySameRetry`, `LegacyAssignmentWithoutSnapshotIsNotInventedOrTreatedAsRetry`, `IncompleteUnsafeOrTamperedAssignmentSnapshotFailsClosed` | P0 |
| PLATFORM-AUDIT-040 | The assignment upsert succeeds but the separate Union audit write is missing; Operations retries the same target and reason | Reconstruct the exact original event from the persisted assignment snapshot, canonical-read verify duplicate/409, return idempotent success only for the same active target/reason and preserve original actor/timestamp | `UnionTransferRetryAndHistoryReadRepairMissingAssignmentAuditWithoutExposingSnapshotToDriver` | P0 |
| PLATFORM-AUDIT-041 | Operations reads Union history while the current assignment audit event is missing; Driver reads own Union membership | Repair the current assignment audit before returning history; redact event ID/type/actor/previous-Union/timestamp snapshot fields from the Driver membership response | `UnionTransferRetryAndHistoryReadRepairMissingAssignmentAuditWithoutExposingSnapshotToDriver` | P0 |
| PLATFORM-AUDIT-042 | A persisted assignment snapshot contains a contact-like actor or incomplete/corrupted fields | Fail closed before creating an audit event; preserve the assignment and require Operations resolution rather than writing unsafe actor data | `IncompleteUnsafeOrTamperedAssignmentSnapshotFailsClosed` | P0 |

| PLATFORM-AUDIT-043 | Operations/UnionManager selfie-review or assignment reason contains an obvious email address or phone-like sequence | Reject before profile/assignment mutation or audit creation; preserve ordinary non-contact text and ISO date/time text. The validator is bounded and fails closed on regex timeout; it is not represented as a general PII scanner. | `UnionAuditReasonsRejectObviousContactDetails`, `UnionAuditReasonAllowsOrdinaryDatesAndNonContactText`, `AuditIdentityFactoriesRejectContactLikeReasons`, `UnionAuditReasonsRejectContactDetailsAndRedactLegacyContactTextFromUnionMembers` | P0 |
| PLATFORM-AUDIT-044 | A legacy stored selfie-review reason contains an email address or phone-like sequence | Do not echo contact-like text in the UnionManager member projection; incomplete/unsafe recovery snapshots fail closed rather than creating an audit event | `UnionAuditReasonsRejectContactDetailsAndRedactLegacyContactTextFromUnionMembers`, `OperationsReviewRecoveryRequiresCompletePrivateSnapshot`, `UnionReviewSnapshotRequiresOriginalActorUnionAndReviewTimestamp` | P0 |
| PLATFORM-AUDIT-045 | A Union audit reason contains a nine-digit phone-like sequence | Reject the contact-like value before profile/assignment mutation or audit identity creation while preserving eight-digit ISO date text and ordinary ISO date/time text | `UnionAuditReasonsRejectObviousContactDetails`, `UnionAuditReasonAllowsOrdinaryDatesAndNonContactText` | P0 |
| PLATFORM-AUDIT-046 | A Union audit reason contains ASCII control characters or Unicode line/paragraph separators | Reject multiline/control-character audit text before mutation or persistence to prevent audit/UI line injection | `UnionAuditReasonRejectsControlAndLineSeparatorCharacters` | P0 |
| PLATFORM-AUDIT-047 | A stored Operations or UnionManager selfie-review snapshot contains a contact-like actor identifier | Treat the private recovery snapshot as incomplete and fail closed before reconstructing an audit event; never persist contact-shaped actor data | `ReviewRecoverySnapshotsRejectContactLikeActorIdentifiers` | P0 |

The readiness failure-path test uses a deterministic fake probe. Reservation-audit privacy and event-contract tests use deterministic domain fixtures and the in-memory reservation boundary. PLATFORM-AUDIT-020 and PLATFORM-AUDIT-022–023, PLATFORM-AUDIT-029 exercise the actual Operations API route against the deterministic in-memory test host. PLATFORM-AUDIT-024–026 exercise the reusable bounded canonical-read recovery helper with deterministic point-read fixtures; PLATFORM-AUDIT-028 exercises PII-shaped JWT subjects; PLATFORM-AUDIT-030 exercises UnionManager/Operations review separation; PLATFORM-AUDIT-031 proves non-Ops principals cannot supply the recorded actor; PLATFORM-AUDIT-032 covers a new Operations review after a UnionManager approval; PLATFORM-AUDIT-033 verifies shared reservation-lease exclusion; PLATFORM-AUDIT-034 verifies transfer repairs a pending Operations audit before profile reset; PLATFORM-AUDIT-035 verifies blank-but-present review markers fail closed instead of being silently cleared; PLATFORM-AUDIT-036 verifies UnionManager member-detail privacy for both list and individual detail projections; PLATFORM-AUDIT-037 verifies that the private profile snapshot preserves the original UnionManager actor and canonical audit read-back rejects any different actor, even when another authorised manager retries; PLATFORM-AUDIT-038 verifies Operations transfer audit never persists a contact-like identity; PLATFORM-AUDIT-039–042 verify deterministic assignment/transfer snapshot identity, legacy no-invention, fail-closed corrupted metadata, idempotent retry recovery after the assignment upsert, history read-repair and Driver response redaction; PLATFORM-AUDIT-043–046 verify obvious email and 9+-digit phone-like free-text reasons, ASCII control characters and Unicode line/paragraph separators are rejected before mutation, ordinary ISO dates/date-times remain accepted, and legacy contact-like review text is omitted from UnionManager projections; PLATFORM-AUDIT-047 verifies contact-like actor identifiers make stored Operations/UnionManager recovery snapshots incomplete and fail closed. PLATFORM-AUDIT-041 additionally verifies that history read-repair first point-reads the exact current assignment event and only attempts create when absent; normal history reads therefore do not issue duplicate/409 writes. A concurrent create race still uses the bounded canonical 409 read-back. The live Cosmos 409/ETag/partition/multi-instance/restart behavior remains an infrastructure gate. These tests do not claim live Cosmos transactional-batch, ETag, retention or telemetry-export validation.


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
| DISPATCH-03-REJECT-011 | Active selected Driver decline never advances legacy FIFO | Create a Union-linked selected Cab booking with a recording `IRideOfferAdvancer`; while the ride is Negotiating, POST the assigned Driver's reject endpoint | HTTP 204; ride becomes Cancelled with Driver actor audit; matching Driver and Rider reservations are released; `IRideOfferAdvancer` receives zero calls | API integration + reservation lifecycle + FIFO isolation | P0 |
| DISPATCH-03-REJECT-012 | Driver rejection of an expired selected negotiation preserves system expiry and never advances legacy FIFO | Create a selected Cab booking, move its negotiation expiry into the past, then POST the assigned Driver's reject endpoint with a recording `IRideOfferAdvancer` | API first reconciles the ride to Expired with System actor audit, returns conflict for the stale reject, releases matching Driver and Rider reservations, and makes zero FIFO advancer calls | API integration + expiry reconciliation + FIFO isolation | P0 |

### DRIVER-01 — conditional Driver profile writes and stale document review

| ID | Scenario | Expected result | Automated evidence | Status |
|---|---|---|---|---|
| DRIVER-PROFILE-001 | Driver resubmits onboarding while the current profile contains availability/location state | Submitted identity, Cab, selfie and document fields are updated; the latest availability, coordinates, location timestamp and original submission timestamp are preserved; Approved/Suspended and legacy non-Cab profiles cannot be replaced | OnboardingResubmissionPreservesLatestAvailabilityAndLocationFields plus existing onboarding eligibility regressions | CI [42455925931] passed 8/8 jobs |
| DRIVER-PROFILE-002 | Driver replaces the selfie while other profile fields have changed | Selfie approval is reset to Pending, but the latest unrelated availability/location fields remain intact; a concurrent non-Cab profile change is rejected | DriverSelfieReplacementPreservesLatestAvailabilityAndLocationFields; conditional in-memory/Cosmos profile update | CI [42455925931] passed 8/8 jobs |
| DRIVER-PROFILE-003 | Operations reviews a profile after its submitted document reference/status, vehicle identity, submission state or prior review state changes | The stale review is rejected with HTTP 409; no document or profile approval is applied to a different snapshot | DriverProfileReviewGuardTests.ChangedDocumentReferenceOrStatusRejectsStaleReviewSnapshot and ChangedVehicleIdentityOrReviewStatusRejectsStaleReviewSnapshot | CI [42455925931] passed 8/8 jobs |
| DRIVER-PROFILE-004 | Availability/location changes while Operations reviews the same unchanged document snapshot | Review applies to the current profile using compare-and-swap and preserves the latest unrelated fields | DriverProfileReviewGuardTests.UnrelatedAvailabilityAndLocationChangesDoNotInvalidateDocumentReviewSnapshot | CI [42455925931] passed 8/8 jobs |
| DRIVER-PROFILE-005 | Two initial onboarding requests race to create the same Driver profile | Only one create-if-absent succeeds; a losing create re-reads and conditionally updates the authoritative profile, never blindly upserts | ConcurrentInitialOnboardingSubmissionsLeaveOneCompleteProfile | CI [42455925931] passed 8/8 jobs |
| DRIVER-01-ONBOARD-014 | Driver submits malformed phone numbers or valid 10/15-digit values with/without leading '+' | Reject misplaced/repeated '+', embedded spaces, letters, 9/16 digits and non-ASCII numerals with HTTP 400; accept 10 and 15 ASCII digits with optional single leading '+' | `DriverOnboardingRejectsMalformedPhoneNumbersWithoutCreatingProfile` (nine invalid values) and `DriverOnboardingAcceptsPhoneNumbersAtBothE164LengthBoundaries` (four valid boundary values); fresh exact-head CI required | P1 |

Phone-number syntax is validated locally by the API; this does not prove ownership. OTP/SMS verification and upstream identity-provider assurance remain external.

| DRIVER-01-ONBOARD-015 | A malformed phone number is submitted when a Driver profile already exists | Return HTTP 400 and preserve the entire existing profile snapshot; do not partially update onboarding fields | `DriverOnboardingRejectsMalformedPhoneNumberWithoutReplacingExistingProfile` | API integration | P1 |

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


### TRIP-01 merged exact-head CI execution evidence (2026-11-08)

- PR #118 merged at `4a58ea6d5e4d0a516754f3500d8ef60a3ea48829`; exact source head `31d79773ea14115bb0f6f822f45814b271426204` passed [GitHub Actions run #747 / 42492757245](https://github.com/rajeevp727/turnova-platform/actions/runs/42492757245), 8/8 jobs.
- Backend Release suite executed **317/317 tests** with zero failures. Shared mobile auth test job, Rider/Driver typechecks and production JS exports, Android debug APKs, unsigned iOS Simulator apps and native non-container API publish all passed. This eight-job run did not include emulator/simulator smoke jobs.
- The implementation persists a per-ride GPS capture watermark, rejects future/out-of-order fixes before mutation/broadcast through the existing ride CAS guard, emits capture time in SignalR, sends Expo LocationObject.timestamp from the Driver foreground watcher, fences stale/mismatched Rider events and delayed history responses, and gates map follow/recenter on the matching MapView ready callback while respecting manual pan.
- Test-plan cases TRIP-LOC-001–009 and TRIP-MAP-001–005 cover these deterministic API/shared-mobile behaviours. No live Cosmos multi-instance/ETag/restart, live SignalR/backplane/reconnect, routing provider or physical-device GPS/map acceptance is claimed. The one-minute future allowance is clock-skew tolerance, not a Product freshness policy.


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
| RELEASE-SMOKE-027 | iOS app foreground/login readiness tolerates a delayed SpringBoard transition without weakening UI assertions | Keep a fake launched process visible while OCR reports the SpringBoard home screen for several polls, then expose the expected Rider/Driver login and unsigned-SecureStore recovery text; run without overriding the default readiness timeout | Default OCR readiness budget is 240 seconds and remains bounded by the existing 1–300-second validation; only OCR not-ready status is retried; missing login text, forbidden session errors and missing SecureStore recovery detail still fail closed with no success report | Python standard-library fake-SDK unittest + iOS Simulator CI | P0 |


CI #1546 / run [43515592418](https://github.com/rajeevp727/turnova-platform/actions/runs/43515592418) on PR #159 head `0d21acf86348de370b97de1ea51d25851f1d6ec5` completed 11/12 jobs. The only failure was Rider iOS Simulator smoke: install and launch returned a PID and the process remained present, but all four OCR screenshots still showed the SpringBoard home screen and the 90-second login-readiness deadline expired. The uploaded artifact [14081558373](https://github.com/rajeevp727/turnova-platform/actions/runs/43515592418/artifacts/14081558373) was downloaded and visually inspected; the simulator system log records `com.turnova.rider` entering the foreground at 22:27:20Z, after the smoke had failed at 22:26:36Z. This is a delayed simulator foreground transition, not evidence of a login-screen pass. The harness default is raised to 240 seconds (still capped at 300); OCR requirements, forbidden-session checks and fail-closed behaviour are unchanged. A deterministic delayed-SpringBoard regression test is added, and a fresh exact-head CI run is required before counting the smoke as passing.

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

| AUTH-LOGIN-049 | Final startup profile lookup completes after account switch | Let startup /auth/me return 401, rotate and persist the old pair, then hold the final /auth/me while a replacement login writes a new pair | The stale final profile is not published and the replacement token pair remains intact | Shared mobile unit + Rider/Driver | P0 |

## AUTH-02 SignalR access-token refresh regressions

| ID | Scenario | Steps / input | Expected result | Type | Priority |
|---|---|---|---|---|---|
| AUTH-LOGIN-050 | Parse JWT expiry safely | Exercise a valid NumericDate, missing/malformed expiry, non-numeric expiry and overflow | A finite NumericDate is converted to milliseconds; missing, malformed or overflowing values are treated as unknown and require server validation | Shared mobile unit | P0 |
| AUTH-LOGIN-051 | Reuse a valid SignalR JWT without an unnecessary REST request | Supply an unexpired JWT and an active session epoch | The token factory returns the stored token without calling the authenticated /me endpoint | Shared mobile unit + Rider/Driver wiring | P1 |
| AUTH-LOGIN-052 | Refresh an expired SignalR JWT through the existing authenticated path | Supply an expired JWT; make guarded /me receive 401, complete the existing refresh coordinator and rotate SecureStore; re-read the token | The token factory returns only the rotated token after /me succeeds and the captured session epoch remains current | Shared mobile unit + Rider/Driver wiring | P0 |
| AUTH-LOGIN-053 | Validate a JWT whose expiry cannot be decoded locally | Supply a token with missing/unknown exp; make guarded /me succeed | The token is returned only after server validation and a second SecureStore read | Shared mobile unit | P1 |
| AUTH-LOGIN-054 | Reject a token when logout/account switch occurs during /me validation | Expire the token, pause validation, advance the session epoch, then complete validation | The token factory returns an empty token and does not perform a post-validation token read | Shared mobile unit | P0 |
| AUTH-LOGIN-055 | Reject a token when logout/account switch occurs during SecureStore read | Pause the initial token read, advance the session epoch, then complete the read | The token factory returns an empty token and never calls the authenticated /me endpoint | Shared mobile unit | P0 |
| AUTH-LOGIN-056 | Fail closed when no access token is stored | Return an empty SecureStore value while the session predicate remains current | The token factory returns an empty token without server validation or use of a stale in-memory fallback | Shared mobile unit | P0 |
| AUTH-LOGIN-057 | Clear Rider session-scoped state on expiry and account replacement | Seed tokens, profile, ride/history, discovery, selected Driver, agreement, route and help state; invoke the Rider reset used by refresh expiry, sign-in start and successful logout | All prior-account state is cleared, route defaults are restored, and fresh collection instances are created | Shared mobile unit + Rider wiring | P0 |
| AUTH-LOGIN-058 | Clear Driver session-scoped state on expiry and account replacement | Seed tokens, profile, queue, availability, active ride/history, payment input, Union assignment, vehicle/document and selfie state; invoke the Driver reset used by refresh expiry, sign-in start and successful logout | All prior-account state is cleared, availability is locally Offline, private profile/document/selfie state is removed, and fresh collection instances are created | Shared mobile unit + Driver wiring | P0 |
| AUTH-LOGIN-059 | Clear all Driver session-scoped data when payslip refresh expires | Force the Driver payslip endpoint to return 401 and refresh to fail; execute the payslip expiry cleanup | After secure-token and cached-payslip cleanup, the Driver app invokes the complete shared session reset, clearing queue, membership, availability, active ride/history, agreements/payment inputs, onboarding, Union, vehicle, document, selfie and help state; no partial manual reset remains | Shared mobile source-integration + Driver typecheck | P0 |
| AUTH-LOGIN-060 | Clear prior auth form secrets while preserving only the current replacement-login attempt | Seed prior email/password/name/mode/error state; invoke session reset for logout/expiry and then for a replacement login with newly submitted credentials; complete a successful Rider/Driver login | Reset clears previous auth form values and returns mode to login; only the current attempt's captured email/password/name/mode is restored; successful authentication clears the password from React state | Shared mobile source-integration + Rider/Driver typecheck | P0 |
| AUTH-LOGIN-061 | Invalidate Rider pickup cache without releasing an unresolved booking guard | Capture selected-booking GPS coordinates; start a booking request; invalidate the session while the request is pending; inspect the next booking attempt state | Cached GPS is invalidated, booking Idempotency-Key rotates, stale GPS completion is rejected, and bookingInFlight remains held until the original request's finally path settles | Shared mobile source-integration + selected-booking location unit | P0 |
| AUTH-LOGIN-062 | Clear Driver SignalR offer deduplication on session reset | Seed the Driver session's receivedOfferIds Set with an offer ID; invoke the complete Driver session reset during logout/expiry or replacement login; inspect the Set and offer-handler bound | The session-scoped Set is cleared so a new account can receive an offer whose ID was seen by the prior account; the existing maximum 500-entry deduplication bound remains intact | Shared mobile source-integration + Driver typecheck | P0 |

Rider's selected-ride SignalR connection and all three Driver SignalR connections use the shared expiry-aware helper. The helper checks JWT exp locally; expired or unverifiable tokens are passed through the existing guarded apiFetch('/api/v1/auth/me') path, which owns 401 refresh and session invalidation. The helper then re-reads SecureStore and rechecks the captured epoch. No API/JWT/refresh-token provider contract is changed.

Both app roots now use role-specific session-state reset helpers before a replacement login and after refresh expiry; successful logout uses the same reset after secure-token deletion. The helper validates all required setter callbacks before mutating any state. This prevents prior-account ride, agreement, route, queue, payment, onboarding, Union, document-reference and selfie data from remaining visible after account replacement.

### RELEASE-SMOKE-011 — cold iOS install operation bound

| ID | Scenario | Steps / input | Expected result | Type | Priority |
|---|---|---|---|---|---|
| RELEASE-SMOKE-026 | Allow a slow first clean iOS install without an unbounded simulator command | Run the smoke with the default operation timeout and verify the announced bound; exercise explicit 1-second operation timeout through the existing erase/boot timeout fixtures | Default individual simulator operations are bounded at 300 seconds (maximum remains 300); explicit shorter bounds still fail closed and no success artifact is written after timeout | Python unittest + iOS Simulator CI | P1 |

The default operation timeout increased from 180 to 300 seconds after CI #1145 showed the Rider app install exceeded 180 seconds following a 2m41 first-boot migration. Every simctl operation remains independently bounded; first-boot bootstatus remains separately bounded at 600 seconds. The Python harness verifies the default and existing explicit-timeout fail-closed behaviour.


## Latest exact-head verification — AUTH-02 / PR #143 (2026-11-10)

Previous source/test head `65a1ea8eca5087a6ad64d88b47dce602938b3c94` passed [CI #1149 / 42814411667](https://github.com/rajeevp727/turnova-platform/actions/runs/42814411667) **12/12 jobs**: backend 373/373, shared mobile 136/136, Python 41/41, both app TypeScript/bundle jobs, four native builds and four simulator smoke jobs. All four same-run artifacts and screenshots were inspected. Android Rider/Driver login UI passed; unsigned iOS Rider/Driver login UI passed with SecureStore unavailable and no Keychain access group. This does not prove signed-device persistence or production authentication.

Source/test commit `b9f02acdc0d67ca5ecf70d4796ef40f933a90677` adds role-specific session-state reset helpers to both apps and four deterministic shared tests. AUTH-LOGIN-057/058 cover Rider/Driver prior-account state clearing on refresh expiry, replacement login and successful logout; the other two tests verify setter preflight/no partial mutation and fresh mutable collections. Expected shared JavaScript total is now 140/140; backend remains 373/373 and Python remains 41/41.

CI #1151 / 42831865877 on the source/test head was superseded by the documentation synchronization. Current documentation head `8dfaa27945a993d2e313fd83558f6d7e66066405` is queued in [CI #1152 / 42832058614](https://github.com/rajeevp727/turnova-platform/actions/runs/42832058614); it is not yet verified. Earlier documentation head `c10b19f...` CI #1150 / 42819171585 remains in progress after its first Rider iOS attempt failed during artifact upload with transient DNS `ENOTFOUND`; a targeted retry was still running when the source/test fix superseded that head. Do not count #1150 or #1151 as final evidence.

After the final documentation-only update in this commit, a fresh exact-head CI run is required. Merge PR #143 only when every required job passes on the final head, all four same-run simulator result JSON files and screenshots are inspected, the final COMMENT-only review is anchored to the exact SHA, no critical/high risk or unresolved review thread remains, and GitHub permits the expected-head merge. Unsigned iOS builds do not establish Keychain persistence or production authentication.


## Latest exact-head verification — AUTH-02 PR #143 (2026-11-10)

The documentation head `97c8528f56596537c9e46aec0277a4a83b4286a1` passed [CI #1153 / 42832334281](https://github.com/rajeevp727/turnova-platform/actions/runs/42832334281) with **12/12 jobs successful**. Exact-head results: .NET 10 backend **373/373**; shared mobile JavaScript **140/140**; Python native-artifact/simulator harness **41/41**; Rider and Driver TypeScript validation and production JS bundles; both Android native builds; both unsigned iOS Simulator native builds; and all four Android/iOS simulator smoke jobs.

Same-run artifacts were downloaded and inspected, including each result JSON and screenshot:
- Rider Android: `13773461057` — `com.turnova.rider`, install/launch passed, process stable, Rider login screen verified, no stale-session error, zero Pixel Launcher ANR recovery attempts.
- Driver Android: `13773895577` — `com.turnova.driver`, install/launch passed, process stable, Driver login screen verified, no stale-session error, zero Pixel Launcher ANR recovery attempts.
- Rider iOS: `13772979189` — `com.turnova.rider`, clean simulator data erased, install/launch passed, process stable, Rider login screen verified.
- Driver iOS: `13775055975` — `com.turnova.driver`, clean simulator data erased, install/launch passed, process stable, Driver login screen verified.

Both iOS result JSONs report `secureStoreAvailable=false`, the expected SecureStore-unavailable banner verified, no Keychain access group, unsigned simulator artifacts, and production API/authentication not exercised. The screenshots visibly show the expected warning. This is simulator startup/UI evidence only; it does not establish signed-device Keychain persistence, production authentication, or physical-device acceptance.

CI #1153 and its four inspected simulator artifacts belong to pre-documentation head `97c8528f56596537c9e46aec0277a4a83b4286a1`. Subsequent commits update documentation only and do not change runtime or test source. The current PR head is recorded in PR #143 metadata; exact-head CI #42841628009 is pending. The prior review is COMMENT-only on the pre-documentation head; a fresh exact-head COMMENT review and current thread check are required after CI completes. The repository rulesets endpoint returned an empty list, while branch-protection details were not readable by the connected integration (403). The expected-head merge operation must enforce any remaining GitHub policy; do not self-approve or bypass protections.

AUTH-02 engineering implementation and deterministic evidence are complete for this increment. Strict acceptance remains **0/19** (15 In Progress, 4 Blocked); this is a complete-evidence count, not an implementation-completion score. Production readiness is not established. External AUTH-02 gates remain live TriSend refresh rotation/reuse semantics and signed Android/iOS physical-device restore, expiry, logout, and SecureStore/Keychain failure acceptance.

Next sequential work after merge: re-read `main`, finish the #3 RIDER-01 audit, implement #4 RIDER-02 stable same-payload proposal Idempotency-Key reuse for double-tap/ambiguous retries in both apps, then #5 RIDER-03 server-side single-active Rider booking reservation across different keys. Maintain one active feature PR.


## AUTH-02 payslip-expiry state-reset follow-up (2026-11-10)

The final source review found a separate Driver payslip-download 401/refresh-failure branch that cleared only tokens, user, active ride/history, available rides, Union assignment and payment inputs. It did not invoke the complete Driver reset helper, so queue/membership, availability, onboarding, vehicle/document/selfie and help state could remain in memory. The branch now calls `clearDriverSessionState()` after secure-token and cached-payslip cleanup. AUTH-LOGIN-059 is a deterministic source-integration regression proving this path delegates to the complete reset and contains no partial manual state clearing. Fresh exact-head CI is required; the earlier CI #1153 does not cover this follow-up.


## AUTH-02 auth-form and Rider pickup-cache reset follow-up (2026-11-10)

Source/test head dd5b61efde3f730cddce434c3371c859b4f19001 adds AUTH-LOGIN-060/061. Role-specific session resets now clear old email/password/name/mode/error values. Replacement-login flows preserve only the current submitted fields while the request is active; successful authentication clears the password. Rider reset invalidates cached selected-booking GPS coordinates and rotates the request idempotency key. The reset deliberately does not clear bookingInFlight, so an unresolved previous request cannot be overlapped by a new session booking attempt.

The preceding head 639e6c1 passed [CI #1159 / 42842677468](https://github.com/rajeevp727/turnova-platform/actions/runs/42842677468) 12/12: backend 373/373, shared JS 141/141, Python 41/41, Rider/Driver typechecks and bundles, four native builds and four simulator smoke jobs. All four same-run result JSONs/screenshots were inspected. These results predate AUTH-LOGIN-060/061; expected shared count is 143/143 and fresh exact-head CI is required. Unsigned iOS simulator warnings remain expected and do not prove Keychain persistence or production authentication.


AUTH-LOGIN-060 clarification: replacement-login reset preserves the current login/register mode along with the just-submitted email/password/name, so a failed registration remains in registration mode for retry. On successful authentication, the password field is cleared. Implementation follow-up commit: 4e5ddfa924a445fa891356f99205726753e584e8. Fresh exact-head CI is required.


## AUTH-02 latest exact-head handoff — 2026-11-10

AUTH-LOGIN-062 verifies that `clearDriverSessionState()` clears the Driver app's session-scoped `receivedOfferIds` Set. This Set deduplicates at-least-once SignalR offer delivery and must not survive account replacement; the existing 500-entry cap remains unchanged. Latest source/test head 87129ce909ae39c1b3a42e0b175a93436201ee58; fresh exact-head CI is required. The preceding head fe3c029 passed [CI #1163 / 42852250391](https://github.com/rajeevp727/turnova-platform/actions/runs/42852250391) 12/12 (backend 373/373, shared JS 143/143, Python 41/41, both mobile validation/bundles, four native builds and four simulator smokes). Same-run smoke artifacts were inspected. Unsigned iOS results show SecureStore unavailable and do not prove Keychain persistence or production authentication.


### RIDER-02 exact-head CI and simulator evidence — 2026-11-11

PR #145 source/test head `56a363621029fbfb16e2ef7c72d14f1faad48c8d` and documentation head `9fbca10d57334167aa41018a9104096f9f85e877` passed [CI #1180 / 42882071110](https://github.com/rajeevp727/turnova-platform/actions/runs/42882071110) with **12/12 jobs successful**. Results: .NET 10 backend **373/373**, shared mobile JavaScript **153/153**, Python native-artifact/simulator harness **41/41**, Rider/Driver TypeScript validation and production bundles, both Android native builds, both iOS Simulator native builds, and all four Android/iOS simulator smoke jobs. The Driver Android smoke initially encountered a Pixel Launcher ANR; only that failed job was rerun, and the retry passed without app changes. The final workflow conclusion is success.

Same-run smoke artifacts were downloaded and inspected, including result JSON and screenshots:
- Rider Android `13795890654`: `com.turnova.rider`, install/launch passed, process stable, Rider login text and screenshot verified, stale-session error absent.
- Driver Android `13798054264`: `com.turnova.driver`, targeted retry after the first artifact `13795995296` recorded a Pixel Launcher ANR; retry install/launch passed, process stable, Driver login text and screenshot verified, stale-session error absent, zero launcher recovery attempts on the successful retry.
- Rider iOS `13796097620`: `com.turnova.rider`, clean simulator data erased, install/launch passed, process stable, Rider login screen verified.
- Driver iOS `13795844308`: `com.turnova.driver`, clean simulator data erased, install/launch passed, process stable, Driver login screen verified.

Both iOS result JSONs report `secureStoreAvailable=false`, the expected SecureStore-unavailable banner, no Keychain access group, unsigned simulator artifacts, and production API/authentication not exercised. These artifacts prove simulator install/launch/login-surface checks only; they do not establish signed-device Keychain persistence, production authentication, live Cosmos, or physical-device acceptance. Final-head COMMENT-only review [5847757020](https://github.com/rajeevp727/turnova-platform/pull/145#pullrequestreview-5847757020) found no critical/high issue; unresolved review threads: **0**. The subsequent documentation-only synchronization requires fresh exact-head CI before merge.


## RIDER-03 client-side active-ride hydration guard — 2026-11-11

- CAB-BOOK-001: After session restore or sign-in, keep Rider booking unavailable until the server-owned ride-history request and active-ride detail read complete successfully. A failed/in-flight read must not be treated as an empty ride history.
- CAB-BOOK-002: Once hydration succeeds, allow booking only when no active ride exists; keep Book disabled and route to Activity when an active legacy or selected ride is found.
- CAB-BOOK-003: Verify discovery and selected-booking handlers independently reject an unverified ride state, with the API remaining authoritative.
- CAB-BOOK-004: Verify session reset returns the hydration state to unverified and a new account must perform its own server read.


- CAB-BOOK-005: An expired temporary Rider reservation is not promoted by default; it may be made permanent only after the API has re-read the authoritative ride and confirmed it is Accepted, Arrived or InProgress. Verify the promotion appends its audit event and never permits a different Rider booking.
- CAB-BOOK-006: After successful sign-in/registration and after stored-session restoration, the authenticated session epoch is established before the user identity is published; the user-id hydration effect then reads server-owned ride history and active-ride details. A stale/replaced session must not trigger or complete hydration.


## RIDER-03 exact-head follow-up — 2026-11-11

- Source/test head `726d1b4356231d4993aa6292c09d5e5a33f635de` adds explicit authoritative active-ride promotion for an expired temporary Rider reservation. The default store operation still rejects expired promotion; only the API path that has re-read the same ride as Accepted/Arrived/InProgress opts in. New manual-clock test: `ExpiredReservationRequiresExplicitAuthoritativeActiveRidePromotion`.
- Rider UI hydration is fail-closed until both ride-history and active-ride detail reads succeed; failed/in-flight reads cannot be interpreted as an empty ride history. Session reset clears the hydration flag and the Book tab exposes a retry.
- CAB-BOOK-001–006 cover active-ride hydration, active-state gating, discovery/request guards, session reset, explicit expired-lease repair and authenticated sign-in/restoration epoch ordering.
- Previous tested head `18b8651d23970030f111310ad510103aea02957e` passed workflow [42904988755](https://github.com/rajeevp727/turnova-platform/actions/runs/42904988755) 12/12; all four same-run simulator artifacts were inspected. The new source/test head and this documentation sync require a fresh exact-head workflow; no latest-head result is claimed until it completes.


## RIDER-03 final hydration assertion — 2026-11-11

- CAB-BOOK-004 is now source-integration checked against the actual `clearRiderSessionState` block: session reset must set `activeRideStateLoaded` to false.
- The `loadRideHistory` source integration verifies the flag is cleared before `/api/v1/rides/mine` and becomes true only after the selected active-ride detail read completes. This prevents an in-flight/failed history read from being interpreted as an empty ride history.
- Source/test head `d8a052a2ccad3ee1a733b099505d2d5c9433eaee` requires fresh exact-head CI after the final documentation sync.


### RIDER-03 final exact-head verification — CI #1214 (2026-11-11)

PR #146 documentation head `a949f6e4f002721b17d5bd426f226416e109943a` passed [CI #1214 / 42920911797](https://github.com/rajeevp727/turnova-platform/actions/runs/42920911797) with 12/12 jobs successful: .NET backend 403/403, shared mobile 171/171, Python native-artifact/simulator harness 41/41, Rider/Driver TypeScript validation and production bundles, both Android native builds, both unsigned iOS Simulator native builds, and all four Android/iOS simulator smoke jobs. The source/test follow-up `d8a052a2ccad3ee1a733b099505d2d5c9433eaee` includes RIDER03-RES-010/011 across all eight active legacy statuses and CAB-BOOK-006 for sign-in/restored-session hydration ordering.

Same-run simulator artifacts inspected: Rider Android `13811579024`, Driver Android `13813150972`, Rider iOS `13812929395`, Driver iOS `13813746404`. Both Android apps installed/launched with role-specific login text, stable processes, no stale-session error and zero Pixel Launcher ANR recovery attempts. Both unsigned iOS apps installed/launched and displayed the expected SecureStore-unavailable banner; no signed-device Keychain or production authentication is claimed. Live Cosmos, physical-device booking and signed release acceptance remain external.

### DISPATCH-01 exact-head CI evidence — 2026-11-11

Source/test head `8cca1958f0e9d67d57ee69ee5327fd4449d9651a` passed exact-head CI [#1222 / 42929505358](https://github.com/rajeevp727/turnova-platform/actions/runs/42929505358) on run attempt 2, **12/12** checks:
- .NET backend: **411/411**.
- Shared mobile JavaScript: **171/171**.
- Python native-artifact and simulator harness: **41/41**.
- Rider/Driver TypeScript validation and production bundles: passed.
- Rider/Driver Android and iOS native builds: **4/4**.
- Rider/Driver Android emulator and iOS Simulator smoke: **4/4**.

The nine executed theory cases comprise six radius values (missing, zero, negative, NaN, positive infinity, negative infinity) and three freshness values (missing, zero, negative). This adds eight net backend tests versus the prior 403-test suite because one existing missing-freshness Fact was expanded to three cases. All cases assert HTTP 503 before ride, idempotency, Driver-reservation or Rider-reservation writes; Driver availability remains Online and profile state is unchanged.

Same-run simulator result JSONs and screenshots were inspected: Rider Android `13817215681`, Driver Android `13816635972`, Rider iOS `13818882794`, Driver iOS `13818155109`. Both Android apps installed/launched with stable processes, correct role-specific login screens, no stale-session error and zero Pixel Launcher ANR recovery attempts. Both unsigned iOS apps installed/launched, remained stable and displayed the expected SecureStore-unavailable banner. The first Rider iOS smoke attempt timed out during `simctl install` after the bounded 300-second operation timeout; only that failed job was retried and the retry passed without code changes. Native iOS app bundle IDs are `com.turnova.rider` and `com.turnova.driver`. These unsigned simulator results do not establish signed-device Keychain persistence, production authentication or physical-device acceptance. No production radius/freshness values are selected by these tests.


## Latest exact-head verification — DISPATCH-01 documentation CI #1225 (2026-11-11)

- PR #147 remains the sole active feature PR on `test/dispatch-01-selected-booking-config-guards`; source/test head `8cca1958f0e9d67d57ee69ee5327fd4449d9651a`, documentation head verified by CI `7bcb81cb9596fc9f2877bb6f79a7c502665f8e80`, base main HEAD `b1ea22f4e2d19e76f5b182b93e527960b110ad7a`.
- Documentation-synchronized CI [#1225 / 42938055684](https://github.com/rajeevp727/turnova-platform/actions/runs/42938055684) completed **12/12 jobs successfully**: .NET backend **411/411**, shared mobile **171/171**, Python native-artifact/simulator tests **41/41**, Rider/Driver TypeScript validation and production bundles, four native Android/iOS builds, and four Android/iOS simulator smoke jobs.
- Same-run simulator artifacts were downloaded and inspected: Rider Android `13818804661`, Driver Android `13819773601`, Rider iOS `13820856185`, Driver iOS `13821041213`. Both Android apps installed/launched, remained stable and displayed their role-specific login screens; stale-session errors were absent and Pixel Launcher ANR recovery attempts were zero. Both unsigned iOS apps installed/launched, remained stable and displayed role-specific login screens plus the expected SecureStore-unavailable banner. Their result JSONs report `secureStoreAvailable=false`, no Keychain access group, unsigned simulator artifacts and production API/authentication not exercised. This is not signed-device Keychain, production authentication or physical-device acceptance.
- Backend job steps confirm native .NET 10 restore/build/test and non-container Linux-x64 publish succeeded. No Docker/container build or deployment was added.
- Final review must be COMMENT-only on the latest post-sync head; the existing review [5851552534](https://github.com/rajeevp727/turnova-platform/pull/147) is anchored to the source/test head, not this documentation head. Recheck review threads and exact-head CI before marking ready. Merge only if GitHub permits the expected-head merge.
- DISPATCH-02 audit remains: capped three-per-union discovery, raw pickup-distance ordering, union-approved selfie/verified Cab projection, foreground GPS capture and same-process retry are implemented. Exact response replay across process death is not guaranteed; precise GPS is not persisted locally. No independent source gap identified.
- DISPATCH-03 next regression increment: prove active selected-driver decline and Driver rejection of an already-expired selected negotiation never invoke the legacy FIFO advancer. Preserve legacy FIFO compatibility; it must not rank, auto-assign or advance the customer-selected Rider flow.
- Strict acceptance remains **0/19** (15 In Progress, 4 Blocked); this is the full-evidence count, not an implementation-completion score. Product-approved radius/zones/freshness, live Cosmos, physical-device, signing, maps/realtime, support, reconciliation, legal and production-hosting gates remain. Production readiness is not established.


## REALTIME-01 — SignalR ride-group identity binding

| Test ID | Scenario | Expected result | Evidence | Priority |
|---|---|---|---|---|
| REALTIME-01-HUB-018 | An authenticated owning Rider or assigned Driver requests a ride group whose loaded aggregate has a different `RideId` | Authorization denies the group for both roles using ordinal exact-ID comparison, even when the caller owns the stored aggregate | `RiderOrAssignedDriverCannotJoinWhenStoredRideIdDiffersFromRequestedGroup` in `RideTrackingHubTests` | P0 |
| REALTIME-01-HUB-019 | `OnConnectedAsync` resolves the requested dictionary key to an aggregate whose `RideId` differs | Connection is aborted and no SignalR group membership is added | `OnConnectedAsyncAbortsWhenStoredRideIdDiffersFromRequestedGroup` in `RideTrackingHubTests` | P0 |

The guard binds the authorization decision to the exact aggregate used for group membership before role/owner checks. Existing owning Rider/assigned Driver positive cases, unrelated-user denial, UnionManager denial and Driver notification-group isolation remain regression controls. Automated in-memory tests do not validate live Cosmos reads, WebSocket delivery, multi-instance SignalR backplanes, long-outage reconnect or Android/iOS background/resume behaviour; those remain external gates.


## DRIVER-01 exact-head verification — PR #150 CI #1244 (2026-11-11)

- Tested PR head: `b1da63bb5987d27f26455119bd4e0e280a4c7428`; branch `fix/driver-01-phone-validation`; base main `9129b2bfbf174e8df07a9713f7e99f51d232cb45`.
- Exact-head CI [#1244 / 42971085313](https://github.com/rajeevp727/turnova-platform/actions/runs/42971085313) passed **12/12 jobs** on `b1da63bb5987d27f26455119bd4e0e280a4c7428`: backend **423/423**, shared mobile **171/171**, Python **41/41**, Rider/Driver TypeScript validation and production JS bundles, native API publish, four Android/iOS native builds and four simulator smoke jobs. Same-run simulator artifacts inspected: Rider Android `13833549494`, Driver Android `13833473650`, Rider iOS `13834578010`, Driver iOS `13834996824`. Android results report install/launch passed, stable processes, role-specific login text, no stale-session error and zero Pixel Launcher ANR recovery attempts. Unsigned iOS simulator results report install/launch passed, stable processes, role-specific login text and the expected SecureStore-unavailable banner; `secureStoreAvailable=false`, no Keychain access group, and production API/authentication not exercised. These are simulator/build checks, not signed-device, live-provider or production acceptance.
- Backend test output: **423 passed, 0 failed, 0 skipped**. Shared mobile: **171 passed**. Python: **41 passed**. Native API publish and both Rider/Driver TypeScript validation and Android/iOS production JS bundles passed.
- Android result JSONs: Rider `13833549494`, Driver `13833473650`; both report install/launch passed, stable process, role-specific login text, no stale-session error, zero Pixel Launcher ANR recovery attempts. iOS result JSONs: Rider `13834578010`, Driver `13834996824`; both report install/launch passed, stable process, role-specific login text, expected SecureStore-unavailable banner, `secureStoreAvailable=false`, unsigned simulator artifact and no Keychain access group. Production API/authentication was not exercised.
- This is phone syntax validation only. OTP/SMS ownership verification, provider assurance, signed-device SecureStore/Keychain, live Cosmos and physical-device onboarding remain external.
- Dependency audit reports per app: 11 moderate, 1 low, 0 high, 0 critical advisories. The complete Expo SDK 58 remediation is a major upgrade and must be separately compatibility/native-tested.


## Latest exact-head handoff — DRIVER-01 CI #1248 passed (2026-11-11)

- Last audited main/base HEAD: `9129b2bfbf174e8df07a9713f7e99f51d232cb45`. Sole active feature PR [#150](https://github.com/rajeevp727/turnova-platform/pull/150), branch `fix/driver-01-phone-validation`; runtime commit `6adf12d2ebaad3c532fa68a606ad95ade89b7987`, expanded source/test commit `e2c665a248b816f4e29de109e01ad430c4e5cfdb`, documentation/test-plan head `09f7be0c91ba64741c680ce477438920fdda9828`.
- Exact-head GitHub Actions [CI #1248 / 42977122031](https://github.com/rajeevp727/turnova-platform/actions/runs/42977122031) completed **12/12 jobs successfully** on `09f7be0c91ba64741c680ce477438920fdda9828`: .NET backend **430/430** (0 failed, 0 skipped), shared mobile **171/171**, Python native-artifact/simulator harness **41/41**, Rider/Driver TypeScript validation and production bundles, native API publish, two Android and two iOS native builds, and four Android/iOS simulator smoke jobs.
- Same-run simulator artifacts downloaded and inspected: Rider Android `13836830858`, Driver Android `13836218302`, Rider iOS `13837860133`, Driver iOS `13837290357`. Android result JSONs report install/launch passed, stable processes, correct role-specific login text, no stale-session error and zero Pixel Launcher ANR recovery attempts. Unsigned iOS result JSONs report install/launch passed, stable processes, correct role-specific login text, expected SecureStore-unavailable banner, `secureStoreAvailable=false`, no Keychain access group and production API/authentication not exercised. All four screenshots were visually inspected. This is simulator/build evidence only, not signed-device, live-provider or production acceptance.
- Release-readiness artifacts Rider `13836376099` and Driver `13835742296` report engineering checks passed but production readiness blocked. Both apps lack registered iOS bundle IDs, EAS project IDs, build-number policy and production HTTPS API URL; no signed AAB/IPA is produced. Release Owner, Product/App Owner, Platform/Infrastructure and QA must supply/verify those inputs, physical-device acceptance and store submission. Never commit signing secrets.
- DRIVER-01 phone validation accepts an optional single leading '+' followed by 10–15 ASCII digits. Nine malformed inputs (including misplaced/repeated plus, whitespace, letters, 9/16-digit values and Arabic-Indic digits) return HTTP 400 before profile creation; four valid 10/15-digit boundary forms with/without '+' are accepted; malformed resubmission preserves the complete existing profile. Test-plan IDs `DRIVER-01-ONBOARD-014/015`. This is syntax validation only; phone ownership, OTP/SMS and identity-provider assurance remain external.
- The iOS simulator applications are unsigned and SecureStore is unavailable in this environment. No signed-device Keychain, production authentication, live Cosmos, private document-storage, legal, or physical-device behaviour is claimed.
- Dependency audit artifacts remain a separate follow-up: the previously inspected Rider/Driver reports show 11 moderate, 1 low, 0 high and 0 critical advisories per app. Expo SDK 54 to SDK 58 is a major compatibility upgrade and is not mixed into this focused PR.
- Ordered acceptance rows: #12 DRIVER-02 is already implemented/merged (PR #117); next is #13 TRIP-01 (reconcile the progress/brain evidence with merged PR #118, then re-audit), followed by #14 DRIVER-03. Do not skip or renumber the matrix.
- Strict acceptance remains **0/19** (15 In Progress, 4 Blocked); this is a complete-evidence count, not an implementation-completion score. Production readiness is not established.

## DRIVER-01 final documentation-head verification — CI #1255 (2026-11-11)

- PR #150 (`fix/driver-01-phone-validation`) source/test commit: `e2c665a248b816f4e29de109e01ad430c4e5cfdb`; documentation head verified by CI: `af9548dadde101776d1568de6900ece53739aceb`; base main: `9129b2bfbf174e8df07a9713f7e99f51d232cb45`.
- Exact-head GitHub Actions [CI #1255 / 42985442306](https://github.com/rajeevp727/turnova-platform/actions/runs/42985442306) completed **12/12 jobs successfully** on `af9548dadde101776d1568de6900ece53739aceb`: .NET backend **430/430** (0 failed, 0 skipped), shared mobile **171/171**, Python native-artifact/simulator harness **41/41**, Rider/Driver TypeScript validation and production JS bundles, native non-container API publish, two Android and two iOS native builds, and all four Android/iOS simulator smoke jobs.
- Same-run simulator artifacts downloaded and inspected: Rider Android `13839539421`, Driver Android `13839718872`, Rider iOS `13840987499`, Driver iOS `13840917053`. Both Android result JSONs report install/launch passed, stable processes, role-specific login text, no stale-session error and zero Pixel Launcher ANR recovery attempts; both screenshots were visually inspected. Both unsigned iOS apps report install/launch passed, stable processes and role-specific login text; `secureStoreAvailable=false`, the expected SecureStore-unavailable banner is visible, no Keychain access group is available, and production API/authentication was not exercised. All four screenshots were visually inspected. These are simulator/build checks, not signed-device or production acceptance.
- Release-readiness artifacts Rider `13839790635` and Driver `13839381740` report engineering checks passed but production readiness blocked. Both apps still lack registered iOS bundle identifiers, EAS project IDs, approved build-number policy and production HTTPS API URL; signed AAB/IPA artifacts are not produced. Product/App Owner, Release Engineering, Platform/Infrastructure, QA and Release Owner/Legal must complete their listed inputs.
- Phone validation is syntax-only: optional single leading `+` followed by 10–15 ASCII digits; nine malformed inputs are rejected before profile creation, four valid 10/15-digit boundaries with/without `+` are accepted, and malformed resubmission preserves the existing profile. Phone ownership/OTP and upstream identity assurance remain external.
- The current dependency audit remains a separate hardening follow-up: the previously inspected Rider/Driver reports show 11 moderate, 1 low, 0 high and 0 critical advisories per app. Expo SDK 54 to SDK 58 is a major compatibility upgrade and is not mixed into this focused PR.
- The review on the exact head is COMMENT-only (not approval); there are zero unresolved inline review threads. Documentation-only consistency commits after this verified head require their own fresh exact-head CI before merge.
- After PR #150 merges, #12 DRIVER-02 is already implemented/merged (PR #117); the next ordered row is #13 TRIP-01 (reconcile the progress/Project Brain evidence with merged PR #118, then re-audit), followed by #14 DRIVER-03. Do not skip or renumber the matrix.
- Strict acceptance remains **0/19** (15 In Progress, 4 Blocked); this is a complete-evidence count, not an implementation-completion score. Production readiness is not established.


## DRIVER-01 latest exact-head verification — CI #1259 (2026-11-11)

- PR [#150](https://github.com/rajeevp727/turnova-platform/pull/150), branch `fix/driver-01-phone-validation`, remains the sole active feature PR. Main/base HEAD is `9129b2bfbf174e8df07a9713f7e99f51d232cb45`. Runtime commit `6adf12d2ebaad3c532fa68a606ad95ade89b7987` and source/test commit `e2c665a248b816f4e29de109e01ad430c4e5cfdb` are included in the exact documentation-head CI `a4a21d73be69301ccd8f8a71cf5fd1f6a6c1c14e`.
- GitHub Actions [CI #1259 / 42991278634](https://github.com/rajeevp727/turnova-platform/actions/runs/42991278634) completed successfully: **12/12 jobs**, backend **430/430** (0 failed, 0 skipped), shared mobile **171/171**, Python **41/41**, Rider/Driver TypeScript validation and production bundles, native non-container API publish, two Android and two iOS native builds, and all four simulator smoke jobs.
- Same-run artifacts downloaded and inspected: Rider Android smoke `13842058610`, Driver Android smoke `13843415264`, Rider iOS smoke `13844475295`, Driver iOS smoke `13843986871`. Both Android apps installed/launched, remained stable, displayed the correct role-specific login screen, had no stale-session error and zero Pixel Launcher ANR recovery attempts. Both unsigned iOS apps installed/launched, remained stable and displayed their role-specific login screens plus the expected SecureStore-unavailable banner; `secureStoreAvailable=false`, no Keychain access group, and production API/authentication were not exercised. All four screenshots and result JSONs were inspected.
- Release-readiness artifacts Rider `13841173754` and Driver `13840739230` report engineering checks passed but production readiness blocked. Both lack configured iOS bundle registration/EAS linkage/build-number policy/production API URL in the CI-visible configuration; no signed AAB or IPA was produced. Product/App Owner, Release Engineering, Platform/Infrastructure, QA and Release Owner/Legal must complete the listed inputs.
- The phone validator remains syntax-only: optional one leading '+' followed by 10–15 ASCII digits. Nine malformed values are rejected before profile creation, four valid boundary forms are accepted, and malformed resubmission preserves the full existing profile. Phone ownership/OTP and identity-provider assurance remain external.
- No source/test changes occurred after the verified source/test commit. This documentation synchronization is based on the completed CI #1259 evidence; the new documentation commit itself requires fresh exact-head CI before PR merge.
- Strict acceptance remains **0/19** (15 In Progress, 4 Blocked); this is the complete-evidence count, not an implementation-completion score. Production readiness is not established.


## DRIVER-01 merged exact-head verification — PR #150 (2026-11-11)

- PR #150 merged at `f7114f6c3df896b53b3ab4f502dce17c97982d6b` after final documentation-head [CI #1260 / 43000036463](https://github.com/rajeevp727/turnova-platform/actions/runs/43000036463) passed **12/12 jobs** on `da576ba4d4d63067c638a9d55a13118221c85cee`: backend **430/430**, shared mobile **171/171**, Python **41/41**, Rider/Driver validation and production bundles, native non-container API publish, two Android and two iOS native builds, and all four Android/iOS simulator smoke jobs.
- Same-run smoke artifacts downloaded and inspected: Rider Android `13846033280`, Driver Android `13846586793`, Rider iOS `13846678727`, Driver iOS `13847208793`. Android result JSONs show correct package IDs, install/launch passed, stable processes, role-specific login text and zero Pixel Launcher ANR recovery attempts. Unsigned iOS result JSONs show correct bundle IDs, install/launch passed, stable processes, role-specific login text and the expected SecureStore-unavailable banner; `secureStoreAvailable=false`, no Keychain access group, and production API/authentication not exercised. All four screenshots were visually inspected.
- Release-readiness artifacts Rider `13844579992` and Driver `13845108913` report engineering checks passed but production readiness blocked. Missing inputs: registered iOS bundle identifiers, EAS project IDs, build-number ownership policy, production HTTPS API URL, secure signing credentials, signed physical-device acceptance and store submission. No signed AAB or IPA was produced.
- DRIVER-01 syntax tests cover nine malformed inputs rejected before profile creation, four accepted 10/15-ASCII-digit boundaries with/without a leading plus, and malformed resubmission preserving the complete existing profile. Phone ownership/OTP and upstream identity assurance remain external.



## RIDER-04 exact-head verification — CI #1278 (2026-11-11)

- Active PR [#153](https://github.com/rajeevp727/turnova-platform/pull/153), branch `fix/rider-04-reconciliation-identity-session-guard`, base main `09e8f90eb64cf0112999e4b874511eb201e4158b`, source/test head `61f5cf783bd1f8acad2b8951d017bdb418667297`, documentation head tested by CI `877dffc559aba3e628445f824d0acabb159a5cf1`.
- Exact-head GitHub Actions [CI #1278 / 43034715352](https://github.com/rajeevp727/turnova-platform/actions/runs/43034715352) passed **12/12 jobs**: backend **431/431**, shared mobile **179/179**, Python harness **41/41**, Rider/Driver validation and production JS bundles, native non-container API publish, two Android native builds, two unsigned iOS Simulator builds, two Android emulator smoke jobs and two iOS Simulator smoke jobs.
- Same-run smoke artifacts downloaded and inspected: Rider Android `13863178192`, Driver Android `13863650955`, Rider iOS `13863523984`, Driver iOS `13864551988`. All four result JSONs report install/launch passed, stable process, role-specific login screen, no stale-session error and successful smoke completion. Both Android results report zero Pixel Launcher ANR recovery attempts. All four screenshots were visually inspected.
- Same-run native iOS app artifacts Rider `13864200268` and Driver `13863825959` were inspected: bundle IDs `com.turnova.rider` / `com.turnova.driver`, display names, version 0.1.0, build 1, minimum iOS 15.1. Both are unsigned simulator apps with no provisioning profile or app CodeSignature. Both iOS smoke results explicitly report SecureStore unavailable, expected secure-storage restore warning visible, no Keychain access group and production API/authentication not exercised.
- Release-readiness artifacts Rider `13862961522` and Driver `13862916835` report engineering checks passed but production readiness blocked: approved iOS bundle IDs, EAS project IDs, build-number policy, production HTTPS API URL/DNS/TLS/CORS/readiness, secure signing credentials, signed physical-device acceptance and store submission remain external. No signed AAB or IPA was produced.
- Same-run npm audit artifacts Rider `13862028118` and Driver `13862647074` each report 11 moderate, 1 low, 0 high and 0 critical advisories. The available Expo remediation resolves through Expo 57.0.32 and is a major SDK upgrade from the current app baseline; keep it as a separate compatibility/native-build hardening increment rather than mixing it into this PR.
- A prior backend build emitted a nullable warning in the Operations selfie-rejection audit path. PR #155 now validates and normalizes the reason once, reuses the same non-null value for profile and Union audit persistence, and adds deterministic boundary tests. Exact final-head CI [#1349 / 43209115012](https://github.com/rajeevp727/turnova-platform/actions/runs/43209115012) passed 12/12 on `1636e2731ca44dccf769a2f80aae8a7eb0279d69`; backend tests passed 500/500. The later docs-only synchronization is being checked separately and does not change the source/test evidence.
- PR #153's deterministic regression coverage includes `CAB-REC-016` (mismatched embedded report RideId excluded by Cosmos query contract and in-memory fallback without mutation) and `PAY-029` (stale/current Driver payment-report session completion plus App.tsx guard wiring). The existing idempotency key, Driver-reported/unverified label, signed delta, direct-payment semantics and privacy-minimized Union projection remain unchanged.
- Final documentation-only head `a7e58603ad0a342e20e44c385b96d32ebaccb450` passed exact-head CI #1279 / 43039769965 12/12. No source/test changes were introduced by this synchronization.
- External RIDER-04 gates remain live Cosmos SQL/index/partition/multi-instance/restart validation; Android/iOS reconciliation history/report-entry acceptance; customer dispute/confirmation, non-zero-delta next-ride and cross-Union visibility decisions. No gateway, escrow, customer confirmation or delta-based eligibility policy is introduced.
- Strict acceptance remains **0/19** (15 In Progress, 4 Blocked); this is a complete-evidence count, not an implementation-completion score. Production readiness is not established.


## RIDER-04 final documentation-head CI evidence — #1279 (2026-11-11)

- PR [#153](https://github.com/rajeevp727/turnova-platform/pull/153) source/test head `61f5cf783bd1f8acad2b8951d017bdb418667297`; final documentation head `a7e58603ad0a342e20e44c385b96d32ebaccb450`.
- Exact documentation-head CI [#1279 / 43039769965](https://github.com/rajeevp727/turnova-platform/actions/runs/43039769965) passed **12/12** on `a7e58603ad0a342e20e44c385b96d32ebaccb450`: backend **431/431**, shared mobile **179/179**, Python **41/41**, Rider/Driver validation and production bundles, native non-container API publish, two Android native builds, two unsigned iOS Simulator builds, two Android emulator smoke jobs and two iOS Simulator smoke jobs.
- Same-run artifacts inspected: Rider Android 13865940928, Driver Android 13865142765, Rider iOS 13865972836, Driver iOS 13867025038. All four result JSONs and screenshots confirm installation/launch and role-specific login screens. Both Android processes remained stable with zero Pixel Launcher ANR recovery attempts. Both unsigned iOS simulator apps reported SecureStore unavailable and displayed the expected recoverable warning; no signed-device Keychain or production authentication claim is made.
- Live Cosmos SQL/index/partition/multi-instance/restart validation, physical-device reconciliation/report-entry acceptance, customer dispute/confirmation, non-zero-delta next-ride and cross-Union visibility policies remain external/product gates. No payment gateway, escrow or in-app collection is introduced.


## RIDER-04 source/test and pre-final documentation verification — CI #1280 (2026-11-11)

- Sole active PR [#153](https://github.com/rajeevp727/turnova-platform/pull/153), branch `fix/rider-04-reconciliation-identity-session-guard`, base main `09e8f90eb64cf0112999e4b874511eb201e4158b`, exact tested source/test-and-documentation head `b7aae701904b6cc33b88d66a43c1aecae98180a7`.
- GitHub Actions [CI #1280 / 43054989137](https://github.com/rajeevp727/turnova-platform/actions/runs/43054989137) completed with **12/12 jobs successful** on that exact head: backend **431/431**, shared mobile **179/179**, Python **41/41**, Rider/Driver validation and production JS bundles, native non-container API publish, two Android native builds, two unsigned iOS Simulator builds, two Android emulator smoke jobs and two iOS Simulator smoke jobs.
- Same-run Android smoke artifacts Rider `13871921817` and Driver `13871363809`; both result JSONs and screenshots were inspected. Both installed/launched, remained stable, displayed the correct role-specific login, had no stale-session-expired error, and recorded zero Pixel Launcher ANR recovery attempts.
- Same-run iOS Simulator smoke artifacts Rider `13871979097` and Driver `13871899500`; both ZIPs, result JSONs, UI text and screenshots were inspected. Both report install/launch passed, stable process, simulator data erased, role-specific login verified and stale-session-expired error absent. Both explicitly report unsigned CI simulator artifacts, SecureStore unavailable and the expected recoverable secure-storage banner; production API/authentication was not exercised and no signed-device Keychain access group exists.
- Rider/Driver validation jobs also generated production Android/iOS JavaScript bundles; native iOS jobs compiled unsigned simulator apps. This does not establish signed Android AAB/iOS IPA or physical-device acceptance.
- Existing release gates remain: approved app identifiers/EAS linkage/build-number policy, production HTTPS API/DNS/TLS/CORS/readiness, signing credentials, signed physical-device testing, pilot acceptance and store submission. Live Cosmos SQL/index/partition/multi-instance/restart, reconciliation policy and legal gates remain external.
- No payment gateway, escrow, in-app collection, customer dispute/confirmation or delta-based next-ride policy is introduced. Strict acceptance remains **0/19** (15 In Progress, 4 Blocked); this is a complete-evidence count, not an implementation-completion score. Production readiness is not established.


## RIDER-04 final documentation-head verification — CI #1282 (2026-11-11)

- Sole active PR [#153](https://github.com/rajeevp727/turnova-platform/pull/153), branch `fix/rider-04-reconciliation-identity-session-guard`, base main `09e8f90eb64cf0112999e4b874511eb201e4158b`, final documentation head `6c3c529b04067046bf43533cff34f3c4f2419e89`.
- GitHub Actions [CI #1282 / 43063792555](https://github.com/rajeevp727/turnova-platform/actions/runs/43063792555) completed **12/12 jobs successfully on the exact final documentation head**: backend **431/431**, shared mobile **179/179**, Python **41/41**, Rider/Driver typecheck and production JS bundles, native non-container API publish, two Android native builds, two unsigned iOS Simulator builds, two Android emulator smoke jobs and two iOS Simulator smoke jobs. This is a documentation-only head after the fully verified source/test head; it introduces no runtime/test changes.
- Same-run smoke artifacts inspected: Rider Android `13875307910`, Driver Android `13876316269`, Rider iOS `13875853423`, Driver iOS `13876811434`. All four result JSONs and screenshots were downloaded and inspected. Both Android apps installed/launched, displayed their correct role-specific login, remained stable, had no stale-session-expired error and recorded zero Pixel Launcher ANR recovery attempts. Both unsigned iOS simulator apps installed/launched, displayed their correct role-specific login, remained stable, had simulator data erased, and showed the expected recoverable SecureStore-unavailable banner; `secureStoreAvailable=false`, no Keychain access group, and production API/authentication was not exercised.
- Release-readiness artifacts Rider `13875760194` and Driver `13874462726` report engineering checks passed but production readiness blocked. Neither has an approved iOS bundle identifier, EAS project ID, build-number policy or production API URL in CI-visible configuration; no signed Android AAB or iOS IPA was produced. Product/App Owner, Release Engineering, Platform/Infrastructure, QA and Release Owner/Legal own the outstanding inputs and acceptance.
- Same-run dependency audits Rider `13873704726` and Driver `13875265447` each report 11 moderate, 1 low, 0 high and 0 critical advisories. The automated full remediation points to Expo `58.0.24`, a major SDK upgrade; keep it as a separate compatibility/native-build hardening increment, not an unreviewed override.
- CI #1280 / 43054989137 remains the source/test verification on `b7aae701904b6cc33b88d66a43c1aecae98180a7`; CI #1282 verifies the final documentation head above. Live Cosmos SQL/index/partition/multi-instance/restart, signed-device Keychain, physical-device, production API/authentication and legal/product-policy gates remain external.
- No payment gateway, escrow, in-app collection, customer dispute/confirmation or delta-based next-ride policy is introduced. Strict acceptance remains **0/19** (15 In Progress, 4 Blocked); this is a complete-evidence count, not an implementation-completion score. Production readiness is not established.


## DISPATCH-04 legacy active-ride fallback follow-up (implemented in PR #154)

All eight cases DISPATCH-04-LEGACY-001–008 are implemented and passed in the code-bearing CI run #1330. Coverage includes all eight known active states (Requested, Matching, Offered, Negotiating, AgreementConfirmed, Accepted, Arrived and InProgress), terminal-history preservation, legacy non-Cab blocking, terminal/orphan reservation cleanup followed by a second historical-active check, same-ride idempotency, Driver acceptance, batched Rider discovery and authoritative expired-negotiation/ETag reconciliation. Detailed scenario-to-test mapping and query/batching/store/reconciler contracts are recorded in the later section “DISPATCH-04 legacy active-ride fallback — implemented regression coverage”.

- PR #154 code-bearing head: 02fbfa9121b62acf5ef8ec20287cc9dc0ea94db5.
- Exact-head CI [#1330 / 43178946882](https://github.com/rajeevp727/turnova-platform/actions/runs/43178946882): 12/12 jobs; backend 477/477, shared mobile 179/179, Python 46/46, Rider/Driver validation/bundles, native API publish, two Android release APK builds, two unsigned iOS Simulator builds and four emulator/simulator smokes.
- Remaining external gates: live Cosmos cross-partition query plan/RU/index/partition, multi-instance ETag/CAS/restart/crash recovery, Product-approved negotiation lifetime, radius/zones/location freshness, and Android/iOS concurrent-device acceptance. No production Cosmos or physical-device validation is inferred.

## RIDER-04 exact documentation-head verification — CI #1285 (2026-11-12)

- PR [#153](https://github.com/rajeevp727/turnova-platform/pull/153), exact tested documentation head `ecb526ae524be443df4eace659d3ad64a694da4c`.
- GitHub Actions [CI #1285 / 43073753705](https://github.com/rajeevp727/turnova-platform/actions/runs/43073753705) passed **12/12 jobs on exact documentation head `ecb526ae524be443df4eace659d3ad64a694da4c`**: backend 431/431, shared mobile 179/179, Python 41/41, Rider/Driver typechecks and production JS bundles, native non-container API publish, two Android native builds, two unsigned iOS Simulator builds, two Android emulator smoke tests and two iOS Simulator smoke tests.
- Same-run smoke artifacts: Rider Android `13882271982`, Driver Android `13879934066`, Rider iOS `13882698962`, Driver iOS `13883585816`. All four result JSONs, UI text and screenshots were inspected. Android install/launch and role-specific login passed with stable processes and zero Pixel Launcher ANR recovery attempts. Unsigned iOS simulator install/launch and role-specific login passed with stable processes; SecureStore was unavailable and the expected recoverable banner was visible; production API/authentication and signed-device Keychain were not exercised.
- Strict acceptance remains **0/19** (15 In Progress, 4 Blocked); production readiness is not established.


## RIDER-04 exact-head verification — CI #1286 (2026-11-12)

- PR [#153](https://github.com/rajeevp727/turnova-platform/pull/153), branch `fix/rider-04-reconciliation-identity-session-guard`, exact tested head `ba33caadfd33a87aed809c0ba4e148d9f213ba2d`, base main `09e8f90eb64cf0112999e4b874511eb201e4158b`.
- GitHub Actions [CI #1286 / 43084057288](https://github.com/rajeevp727/turnova-platform/actions/runs/43084057288) passed **12/12 jobs on exact head `ba33caadfd33a87aed809c0ba4e148d9f213ba2d`**. Backend **431/431**, shared mobile **179/179**, Python **41/41**, Rider/Driver typechecks and production JS bundles, native non-container API publish, two Android standalone release APK builds, two unsigned iOS Simulator builds, two Android emulator smoke tests and two iOS Simulator smoke tests all passed.
- Same-run smoke artifacts downloaded and inspected: Rider Android `13885114690`, Driver Android `13885967692`, Rider iOS `13886558695`, Driver iOS `13886339344`. Both Android apps installed/launched, displayed the correct role-specific login screen, remained stable, had no stale-session error and recorded zero Pixel Launcher ANR recovery attempts. Both unsigned iOS simulator apps installed/launched, displayed the correct role-specific login screen and remained stable; simulator data was erased, SecureStore was unavailable and the expected recoverable secure-storage warning was visible. Production API/authentication was not exercised; signed-device Keychain success is not claimed.
- Release-readiness artifacts Rider `13884494776` and Driver `13885995696` report engineering checks passed but production readiness blocked. Dependency audit artifacts Rider `13884244938` and Driver `13885747350` remain a separate Expo compatibility hardening follow-up.
- At the time of CI #1286, the separate DISPATCH-04 historical active-ride fallback was still open. It was subsequently implemented in PR #154; see the implemented regression coverage and CI #1330 evidence below.
- Strict acceptance remains **0/19** (15 In Progress, 4 Blocked); this is a complete-evidence count, not an implementation-completion score. Production readiness is not established.

## CI #1288 — RIDER-04 exact-head automated/native evidence (2026-11-12)

- Exact head: `0b7576b2c6da8a13eeac889c314f1e4a31e3d453`; [workflow run 43093306250](https://github.com/rajeevp727/turnova-platform/actions/runs/43093306250) passed 12/12 jobs.
- Test totals: backend 431/431, shared mobile 179/179, Python 41/41. Rider and Driver typechecks/production JS bundle exports and native non-container API publish passed. Rider/Driver Android native builds and unsigned iOS Simulator builds passed; both Android emulator and both iOS Simulator smoke tests passed.
- Smoke artifact IDs: Rider Android `13892642047`, Driver Android `13893215862`, Rider iOS `13892604439`, Driver iOS `13893277507`. All four result JSONs, UI text and screenshots were inspected. Android install/launch and role-specific login passed with stable process, no stale-session error and zero Pixel Launcher ANR recovery attempts. Unsigned iOS install/launch and role-specific login passed with stable process and erased simulator data; SecureStore unavailable and expected recoverable warning verified. Production API/authentication and signed-device Keychain are not exercised.
- Release readiness: Rider `13890031208`, Driver `13888833914`; engineering checks passed, production readiness blocked. No signed Android AAB or iOS IPA. Required external gates: approved/registered app identifiers, EAS project IDs, build-number policy, production HTTPS API/DNS/TLS/CORS/readiness, secure signing, signed physical-device acceptance and store submission/privacy approval.
- Dependency audit: Rider `13889986207`, Driver `13888604434`; each 11 moderate, 1 low, 0 high, 0 critical. Full automated fix proposes major Expo `58.0.24` upgrade; do not apply without a separate compatibility/native verification increment.
- The first native artifact-upload attempt returned HTML where the action expected JSON. The failed native jobs were rerun; all four native builds, artifact uploads and simulator smoke jobs then succeeded. This transient did not remain as a failed check in the final CI run.



## RIDER-04 final documentation-head verification — CI #1290 (2026-11-12)

- GitHub Actions [CI #1290 / 43108530927](https://github.com/rajeevp727/turnova-platform/actions/runs/43108530927) completed **12/12 jobs successfully** on exact documentation head `40421acf92f030c9737b83d987eb30d2b7e3eabe`: backend **431/431**, shared mobile **179/179**, Python **41/41**, Rider/Driver validation and production bundles, native non-container API publish, two Android standalone release APK builds, two unsigned iOS Simulator builds, two Android emulator smoke tests and two iOS Simulator smoke tests.
- Same-run smoke artifacts downloaded, extracted and inspected: Rider Android `13895439225`, Driver Android `13895109413`, Rider iOS `13897805633`, Driver iOS `13896847179`. All four result JSONs and screenshots were inspected. Android install/launch passed, package identity and role-specific login were verified, processes remained stable, stale-session errors were absent, and Pixel Launcher ANR recovery attempts were zero. Unsigned iOS simulator install/launch passed, bundle identity and role-specific login were verified, processes remained stable, simulator data was erased, SecureStore was unavailable, the expected recoverable secure-storage warning was visible, and stale-session-expiry errors were absent. Production API/authentication was not exercised; no signed-device Keychain access group is available.
- Same-run native iOS app artifacts Rider `13896092051` and Driver `13897190127` were inspected. Their Info.plists identify `com.turnova.rider` / `com.turnova.driver`, display names TurnOva Rider / TurnOva Driver, version `0.1.0`, build `1`, minimum iOS `15.1`, and `iPhoneSimulator` platform. These are unsigned simulator apps, not signed distribution IPAs.
- Release-readiness artifacts Rider `13895037964` and Driver `13895092535` report engineering checks passed but production readiness blocked. Both have no approved iOS bundle identifier, EAS project ID, build-number policy or production API URL in CI-visible configuration; neither signed Android AAB nor signed iOS IPA was produced. Owners/inputs: Product/App Owner (approve/register iOS bundle IDs); Release Engineering (EAS project IDs and build-number ownership/auto-increment); Platform/Infrastructure (production HTTPS API URL and DNS/TLS/CORS/readiness); Release Owner (secure signing credentials); QA/Product (signed physical-device functional/security/pilot acceptance); Release Owner/Legal (store metadata, privacy disclosures and submission).
- Same-run npm audit artifacts Rider `13894024579` and Driver `13893769604` each report **12 advisories: 11 moderate, 1 low, 0 high, 0 critical**. Full automated remediation requires the major Expo upgrade to `58.0.24` (with related Expo package alignment); keep this as a separate compatibility/native-build hardening increment, not an unreviewed override.
- CI #1290 verifies the documentation head that records CI #1288. This section itself is a subsequent documentation-only update and therefore requires a fresh exact-head CI and final COMMENT-only review before PR #153 can merge. RIDER-04 source/test code is unchanged from the previously verified head. The DISPATCH-04 historical active-ride fallback remains a separate P0 follow-up and is not implemented by PR #153.
- Strict acceptance remains **0/19** (15 In Progress, 4 Blocked); this is a complete-evidence count, not an implementation-completion score. Production readiness is not established. No payment gateway, escrow, in-app collection, customer dispute/confirmation or delta-based next-ride policy is introduced.


## DISPATCH-04 legacy active-ride fallback — implemented regression coverage (2026-11-12)

The P0 source-audit gap is implemented in PR [#154](https://github.com/rajeevp727/turnova-platform/pull/154). Historical active ride aggregates now block overlapping selected Cab work even when no Driver reservation document exists. The API reads a minimal projection with stored item ID, embedded RideId, DriverId, status and negotiation expiry; validates exact item/aggregate identity; batches distinct Driver IDs in chunks of at most 100; and batches reservation reads in the existing /driverId partition while excluding append-only audit facts with c.id = c.driverId. Booking, availability, Union transfer, Driver acceptance, idempotent selected-booking retry and Rider discovery share the guard. Expired Negotiating/AgreementConfirmed rides reconcile through the authoritative transition and latest aggregate reread; missing, mismatched or unrecognised latest state fails closed. Legacy active non-Cab rides continue to block and are never migrated/deleted.

| ID | Scenario | Executed regression coverage | Priority |
|---|---|---|---|
| DISPATCH-04-LEGACY-001 | Historical active Cab ride without a reservation blocks a new selected booking | HistoricalActiveCabRideWithoutReservationBlocksSelectedBooking covers Requested, Matching, Offered, Negotiating and AgreementConfirmed. | P0 |
| DISPATCH-04-LEGACY-002 | Historical accepted/active trip blocks booking, availability change and Union transfer | HistoricalAcceptedArrivedOrInProgressRideWithoutReservationBlocksBookingAvailabilityAndUnionTransfer covers Accepted, Arrived and InProgress. | P0 |
| DISPATCH-04-LEGACY-003 | Terminal historical ride does not block a new booking and is preserved | TerminalHistoricalRideWithoutReservationDoesNotBlockNewSelectedBookingAndPreservesHistory covers Completed, Cancelled and Expired. | P0 |
| DISPATCH-04-LEGACY-004 | Legacy non-Cab active ride continues to block without migration or rewrite | HistoricalNonCabActiveRideWithoutReservationBlocksBookingAvailabilityAndUnionTransferAndPreservesHistory. | P0 |
| DISPATCH-04-LEGACY-005 | Releasing a terminal/orphan reservation does not hide a second historical active ride | TerminalReservationCleanupStillBlocksWhenAnotherHistoricalActiveRideExists and ExpiredOrphanReservationCleanupStillBlocksWhenAnotherHistoricalActiveRideExists. | P0 |
| DISPATCH-04-LEGACY-006 | Same-idempotency retry may exclude its own ride only; a different active ride still blocks | SameIdempotencyRetryIsBlockedByAnotherHistoricalActiveRideWithoutReleasingItsReservation covers Negotiating and AgreementConfirmed; DriverCannotAcceptSelectedRideWhileAnotherHistoricalActiveRideExists verifies Driver acceptance. | P0 |
| DISPATCH-04-LEGACY-007 | Rider discovery excludes historical-active Drivers but retains terminal-only and eligible Drivers without FIFO ranking | RiderDiscoveryFiltersHistoricalActiveDriversAndKeepsTerminalOnlyAndEligibleDrivers; batching and query-contract tests cover 205 Driver IDs as 100/100/5. | P1 |
| DISPATCH-04-LEGACY-008 | Expired negotiation reconciles through the authoritative aggregate/ETag boundary before eligibility is restored | ExpiredHistoricalNegotiationReleasesMatchingReservationBeforeAnotherActiveRideBlocks; ExpiredNegotiationReconcilerTests cover successful transition, ETag conflict/latest reread, missing/mismatched aggregate, unknown status, identity mismatch and fail-closed reservation release. | P0 |

Additional regressions verify exact stored item ID/embedded RideId identity, no Rider/Driver history leakage through a mismatched alias, query projection minimisation, complete active lifecycle status coverage, reservation-query exclusion of audit documents, bounded batch validation and expired reservation visibility until lifecycle reconciliation.

Exact code-bearing head 02fbfa9121b62acf5ef8ec20287cc9dc0ea94db5 passed GitHub Actions [CI #1330 / 43178946882](https://github.com/rajeevp727/turnova-platform/actions/runs/43178946882), 12/12: backend 477/477, shared mobile 179/179, Python harness 46/46, Rider/Driver validation and production bundles, native non-container API publish, two Android standalone release APK builds, two unsigned iOS Simulator builds and four emulator/simulator smoke jobs. This deterministic evidence does not substitute for live Cosmos query-plan/RU/partition/multi-instance/restart validation or Android/iOS concurrent-device acceptance. Product-approved negotiation lifetime, radius/zones and Driver location freshness remain external prerequisites.

## RIDER-04 exact documentation-head verification — CI #1296 (2026-11-12)

- Active PR [#153](https://github.com/rajeevp727/turnova-platform/pull/153), branch `fix/rider-04-reconciliation-identity-session-guard`, base main `09e8f90eb64cf0112999e4b874511eb201e4158b`, exact tested head `885962b4a91d4e0f29fa2cb8734a1eafe80101b5`.
- GitHub Actions [CI #1296 / 43125266797](https://github.com/rajeevp727/turnova-platform/actions/runs/43125266797) completed **12/12 jobs successfully** on that exact head. Backend **431/431** (0 failed, 0 skipped); shared mobile-auth **179/179** (0 failed); Python harness **41/41**; Rider/Driver validation and production JS bundles; Android native builds 2/2; unsigned iOS Simulator native builds 2/2; Android emulator smoke 2/2; iOS Simulator smoke 2/2. All 12 jobs have conclusion `success`.
- Same-run smoke artifacts downloaded, extracted and inspected: Rider Android `13903496824`, Driver Android `13903208782`, Rider iOS `13904072953`, Driver iOS `13904651022`. All four result JSONs and screenshots were inspected. Android install/launch, package identity, role-specific login and process stability passed; stale-session error absent and Pixel Launcher ANR recovery attempts zero. Unsigned iOS install/launch, bundle identity, role-specific login and process stability passed; simulator data erased, SecureStore unavailable and expected recoverable secure-storage warning visible; production API/authentication was not exercised and signed-device Keychain is not established.
- Driver iOS native app artifact `13904265254` was extracted and inspected. It contains both `Frameworks/React.framework/React` and `Frameworks/ReactNativeDependencies.framework/ReactNativeDependencies`; the Driver iOS smoke launched PID 5857 and verified the Driver Partner login screen. The earlier CI #1295 missing-framework/dyld failure is superseded by this successful exact-head rebuild; no source change was made to produce the passing build. Treat the earlier incident as an intermittent native packaging/cache observation and add a separate fail-closed framework-presence assertion to the iOS build workflow when convenient; do not claim a root-cause fix from the successful retry alone.
- Same-run release-readiness artifacts Rider `13902422810` and Driver `13902703051` report engineering checks passed but production readiness blocked. No approved iOS bundle IDs, EAS project IDs, build-number policy or production API URL are configured in CI-visible settings; no signed Android AAB or iOS IPA was produced. Owners: Product/App Owner (approve/register bundle IDs); Release Engineering (EAS project IDs and build-number policy); Platform/Infrastructure (production HTTPS API URL, DNS/TLS/CORS/readiness); Release Owner (secure signing credentials); QA/Product (signed-device acceptance); Release Owner/Legal (store metadata, disclosures and submission).
- Same-run npm audit artifacts Rider `13901719495` and Driver `13902653265` each report **12 advisories: 11 moderate, 1 low, 0 high, 0 critical**. Complete automated remediation requires major Expo upgrade `58.0.24`; keep it as a separate compatibility/native-build hardening increment rather than applying dependency overrides.
- RIDER-04 source/test changes remain CAB-REC-016 and PAY-029; no payment gateway, escrow, in-app collection, customer dispute/confirmation or delta-based next-ride policy is introduced. No production API/authentication, live Cosmos, signed-device Keychain or physical-device acceptance is claimed.
- At the time of CI #1296, DISPATCH-04 remained a separate P0 follow-up. PR #154 subsequently implemented and tested all eight DISPATCH-04-LEGACY-001–008 cases; see the later implementation section.
- Strict acceptance remains **0/19** (15 In Progress, 4 Blocked); this is a complete-evidence count, not an implementation-completion score. Production readiness is not established.


## RIDER-04 exact-head automated/native evidence — CI #1306 (2026-11-12)

- Exact PR #153 head `4d1d2804f171a8b070458a0e9bfb9f5ef4dc3e0e`; [GitHub Actions run 43152744526](https://github.com/rajeevp727/turnova-platform/actions/runs/43152744526) passed **12/12 jobs**.
- Test totals: backend 431/431, shared mobile 179/179, Python harness 46/46. Rider/Driver typechecks and production JS bundles, native non-container API publish, two Android standalone release APK builds, two unsigned iOS Simulator builds, two Android emulator smoke jobs and two iOS Simulator smoke jobs all passed.
- Android APK artifacts: Rider `13915310843` (`com.turnova.rider`) and Driver `13915140697` (`com.turnova.driver`). They are standalone release-variant test APKs, not signed Play Store AABs.
- Smoke artifacts: Rider Android `13915031736`, Driver Android `13914589438`, Rider iOS `13915593606`, Driver iOS `13915623097`. All four result JSONs and screenshots were inspected. Android install/launch, package identity, role-specific login and process stability passed; stale-session error absent and Pixel Launcher ANR recovery attempts zero. Unsigned iOS install/launch, bundle identity, role-specific login and process stability passed; simulator data erased, SecureStore unavailable and expected recoverable warning visible. Production API/authentication was not exercised; signed-device Keychain is not established.
- Release-readiness artifacts Rider `13914646587` and Driver `13914283743` report engineering checks passed but production readiness blocked. Neither production API base URL nor approved iOS bundle/EAS project/build-number configuration is present in CI-visible settings; no signed Android AAB or iOS IPA was produced. Required owners/inputs: Product/App Owner (iOS bundle IDs); Release Engineering (EAS IDs and build-number policy); Platform/Infrastructure (production HTTPS API URL, DNS/TLS/CORS/readiness); Release Owner (secure signing); QA/Product (signed physical-device functional/security/pilot acceptance); Release Owner/Legal (store metadata, privacy disclosures and approval).
- Same-run npm audit artifacts Rider `13913754569` and Driver `13914283764` each report 12 advisories: 11 moderate, 1 low, 0 high, 0 critical. Major Expo SDK remediation remains a separate compatibility hardening task.
- RIDER-04 external gates remain live Cosmos SQL/index/partition/multi-instance/restart validation; Android/iOS report-entry/history acceptance; customer dispute/confirmation, non-zero-delta next-ride and cross-Union visibility decisions. No payment gateway, escrow or in-app collection is added.
- Next sequential engineering task after DISPATCH-04 integration: #18 PLATFORM-01; re-audit storage, health/readiness, telemetry and privacy-safe operational evidence for deterministic gaps before requesting infrastructure values.
- Strict acceptance remains **0/19** (15 In Progress, 4 Blocked); this is a complete-evidence count, not an implementation-completion score. Production readiness is not established.


### PLATFORM-01 reservation-audit privacy and Operations reason consistency — exact-head evidence (2026-11-12)

- PR [#155](https://github.com/rajeevp727/turnova-platform/pull/155), branch `fix/platform-01-reservation-audit-privacy-contract`; code/test head `3beb13e042820e88cb83e18a0d1a83f2d45424f8`, exact final code/documentation CI head `1636e2731ca44dccf769a2f80aae8a7eb0279d69`. Intermediate CI #1345 was cancelled after supersession. Exact final-head CI [#1349 / 43209115012](https://github.com/rajeevp727/turnova-platform/actions/runs/43209115012) passed **12/12 jobs on `1636e2731ca44dccf769a2f80aae8a7eb0279d69`**: backend **500/500**, shared mobile **179/179**, Python harness **46/46**, Rider/Driver validation and production JavaScript bundles, native non-container .NET API publish, two Android debug APKs and two standalone release APK builds, two unsigned iOS Simulator builds, and four Android emulator/iOS Simulator smoke jobs. Inspected smoke artifacts: Rider Android `13941620437`, Driver Android `13941890263`, Rider iOS `13942945816`, Driver iOS `13943110047`. Android package identity/login/install/launch/process stability passed with no stale-session error and zero launcher ANR recovery attempts; unsigned iOS bundle identity/login/install/launch/process stability passed, simulator data was erased, SecureStore was unavailable and the expected recoverable banner was visible. Production API/authentication was not exercised; signed-device Keychain is not established.
- Backend build completed with zero errors; 17 existing xUnit analyzer warnings remain. Backend tests passed 500/500 with zero failures or skips. Shared mobile-auth tests passed 179/179; deterministic Python native/smoke harness tests passed 46/46.
- Same-run release-readiness artifacts Rider `13940101921` and Driver `13938904191` report engineering checks passed but production readiness blocked. Approved iOS bundle IDs, EAS project IDs, build-number policy, production HTTPS API URL/DNS/TLS/CORS/readiness, secure signing credentials, signed physical-device acceptance and store submission/privacy approval remain owner-assigned external gates. No signed Android AAB or iOS IPA was produced.
- No production API/authentication, live Cosmos transactional-batch/ETag/retention, signed-device Keychain, or physical-device acceptance is claimed. Live Cosmos, telemetry export/alerts, production CORS origins, load testing and legal/release gates remain external.


### PLATFORM-01 final documentation-head CI — 2026-11-12

- The latest documentation head `98f7b81a29a74b8bc5e8493da608266593906260` was verified by GitHub Actions [CI #1354 / 43215907622](https://github.com/rajeevp727/turnova-platform/actions/runs/43215907622), which completed **12/12 jobs successfully on that exact commit**: backend, mobile-auth, Rider/Driver validation and bundles, four native Android/iOS builds, and four Android emulator/iOS Simulator smoke jobs. No job remained pending or failed.
- The source/test head remains `1636e2731ca44dccf769a2f80aae8a7eb0279d69`; its code-bearing CI [#1349 / 43209115012](https://github.com/rajeevp727/turnova-platform/actions/runs/43209115012) passed **12/12**, including backend **500/500**, shared mobile **179/179**, Python harness **46/46**, native non-container API publish, Android release APK builds, unsigned iOS Simulator builds and four platform smoke jobs. CI #1354 is a documentation-head rerun, not a new source change.
- The inspected smoke evidence remains limited to unsigned simulator/emulator workflows. Production API/authentication, live Cosmos transactional-batch/ETag/retention/multi-instance/restart, signed-device Keychain, physical-device acceptance, telemetry export/alerts, production CORS and load testing are not claimed and remain external gates. Strict acceptance remains **0/19** (15 In Progress, 4 Blocked); production readiness is not established.

### PLATFORM-01 PR #155 — final documentation-head verification (2026-11-12)

- Latest documentation head `ecbf08f3e9217ee08ac0abb633a23eb174ec6cdd` passed GitHub Actions [CI #1356 / 43222822397](https://github.com/rajeevp727/turnova-platform/actions/runs/43222822397) **12/12 jobs**: backend, Rider/Driver validation and production JS bundles, mobile-auth tests, four Android/iOS native builds and four Android emulator/iOS Simulator smoke jobs. No job remains pending or failed.
- The code-bearing head remains `3beb13e042820e88cb83e18a0d1a83f2d45424f8`; exact source/test CI [#1349 / 43209115012](https://github.com/rajeevp727/turnova-platform/actions/runs/43209115012) passed backend 500/500, shared mobile 179/179, Python harness 46/46 and native/build/smoke checks. CI #1356 verifies documentation only; it is not represented as new source/test coverage.
- PLATFORM-AUDIT-015–020 are implemented and passed. The latest SHA has a COMMENT-only engineering review with no critical/high-risk finding. PR #155 is ready for review; expected-head merge remains pending.
- External validation not covered by these deterministic tests: live Cosmos transaction/ETag/409 read-after-write/partition/multi-instance/restart/retention, telemetry export/alerts, production CORS/load, signed-device Keychain and physical Android/iOS acceptance. Strict acceptance remains 0/19 (15 In Progress, 4 Blocked); production readiness is not established.


## PLATFORM-01 PR #156 final exact-head evidence — 2026-11-13

- Main/base HEAD before merge: `4817351616d6f0d7f0c9a51a0863cb92ce4caea4`. PR [#156](https://github.com/rajeevp727/turnova-platform/pull/156) is the sole active feature PR on `fix/platform-01-union-audit-idempotent-recovery`.
- Source/test commit `966a1ec89bd88489522d4760f7f8042fa2fc83b0` passed backend **558/558** (0 failed, 0 skipped), shared mobile/auth **179/179**, and deterministic Python harness **46/46**. Rider and Driver Expo configuration/typecheck/production Android+iOS JS bundle validation passed. Native non-container .NET 10 Linux-x64 API publish passed and produced a 43,110,451-byte framework-dependent artifact with required native rendering assets.
- Exact documentation-head commit `33908e1f5d17a71346a1eb8653914c5175bfe1a2` passed GitHub Actions [CI #1453 / run 43307889289](https://github.com/rajeevp727/turnova-platform/actions/runs/43307889289) **12/12 jobs**. The run is completed/success on that exact SHA. Both Rider and Driver Android debug and standalone release APKs passed artifact verification; both unsigned iOS Simulator apps compiled; Rider/Driver Android emulator installation/launch and Rider/Driver iOS Simulator installation/launch smoke jobs passed. Artifacts include the API publish, both Android release APKs, both iOS Simulator apps and all four smoke reports.
- Backend log confirms: `Passed! Failed: 0, Passed: 558, Skipped: 0, Total: 558`. The earlier 554/554 and intermediate counts are superseded. No production API/authentication, live Cosmos, signed-device Keychain or physical-device acceptance is claimed.
- The final COMMENT-only security/privacy review is anchored to `33908e1f5d17a71346a1eb8653914c5175bfe1a2`; no unresolved critical/high-risk finding was identified. Inline review threads are empty. The review is not an approval. No self-approval or bypass was used.
- Implementation includes deterministic Operations/UnionManager selfie and assignment/transfer audit identities and retry snapshots, original actor/canonical-field equality, bounded exact Driver-partition point-read recovery after duplicate/409, per-Driver serialization, history read-repair, Driver response redaction, allow-listed UnionManager projections, and conservative contact/control-character/line-separator reason and actor validation. PLATFORM-AUDIT-021–047 are documented. Driver profile, assignment and audit remain separate Cosmos containers; no cross-container atomicity is claimed.
- External gates remain live Cosmos 409/ETag/partition/multi-instance/restart/retention validation, production telemetry export/alerts, approved production CORS origins and load testing, production auth-provider/Ops identity migration, signed-device Keychain, Android/iOS physical-device acceptance, legal review and remaining product/Operations policy decisions. Strict acceptance remains **0/19** (15 In Progress, 4 Blocked); this is complete-evidence accounting, not an implementation score. Production readiness is not established.
- After PR #156 merges, re-read main and continue remaining safe #18 PLATFORM-01 health/readiness/diagnostics/CORS/native-publish/audit-path work, then proceed to #19 RELEASE-01. Do not pause for external gates.


### PLATFORM-01 PR #156 follow-up regressions — 2026-11-13

- **PLATFORM-AUDIT-048:** Operations union-history returns HTTP 409 and no partial history when a pending assignment-audit snapshot contains an unsafe actor or otherwise cannot be reconciled; it does not synthesize or persist an event from invalid metadata.
- **PLATFORM-AUDIT-049:** Union audit reasons reject slash-separated phone-like groups and Unicode decimal-digit phone sequences (Devanagari and Arabic-Indic) while preserving ordinary ASCII and Devanagari ISO date/time text. Regex execution remains bounded and fails closed on timeout.
- Source/test head `2e2278e747c77ab2835b9ad439d8fd7038d4065f` passed backend **562/562**, shared mobile **179/179**, Python **46/46**, Rider/Driver validation and bundles, native non-container API publish, both Android native builds and both Android emulator smokes in exact-head CI #1466. The two iOS native builds and iOS smoke jobs were still running when the branch advanced. Follow-up head `2fc5772734c00ca86b5fc9dffee0b20ac6b589d5` validates persisted reasons with the normalized-safe contract and asserts unsafe/unnormalized snapshots throw `InvalidOperationException`. Current source/test head `0c783b729b3f9b4ff8c3406075cd8d06b17eef83` adds an Operations union-history integration theory for email-like and whitespace-padded stored reasons; both must return HTTP 409 with no partial history. Expected backend total is **564**; exact-head CI is pending and no 564/564 result is claimed yet. No production API/authentication, live Cosmos or signed-device validation is claimed.


## PLATFORM-01 audit recovery — additional regression (2026-11-13)

| Test ID | Criterion | Deterministic evidence | Priority |
|---|---|---|---|
| PLATFORM-AUDIT-050 | A pending assignment snapshot with a contact-like or unnormalized stored reason fails closed through the Operations history HTTP 409 path; it must not throw `ArgumentException` into the generic 500 handler. | `IncompleteUnsafeOrTamperedAssignmentSnapshotFailsClosed` asserts `InvalidOperationException` for an email-like reason and a whitespace-padded reason. `OperationsUnionHistoryReadReturnsConflictForUnsafePendingAssignmentReason` is a two-case API integration theory proving both stored-reason variants return HTTP 409 and append no audit event. `DriverUnionAssignmentAuditSnapshot.CreateExpectedEvent` calls `DriverUnionAuditReason.IsNormalizedSafe` before operation-key reconstruction. | P0 |


### PLATFORM-01 exact-head verification — CI #1471 (2026-11-13)

- Exact-head CI #1471 / 43325447712 passed **12/12 jobs** on documentation head `8a0199de41770c8e61421f12aa75d10950836084` (source/test head `0c783b729b3f9b4ff8c3406075cd8d06b17eef83`): backend **564/564**, shared mobile/auth **179/179**, Python harness **46/46**, Rider/Driver Expo validation and production JS bundles, native non-container .NET API publish, both Android native builds, both unsigned iOS Simulator builds, both Android emulator smoke tests and both iOS Simulator smoke tests. Same-run smoke artifacts inspected: Rider Android `13992094524`, Driver Android `13992717692`, Rider iOS `13994131365`, Driver iOS `13994001982`. Both Android apps installed/launched, remained stable, showed the expected login screen and had no stale-session error or system ANR recovery. Both unsigned iOS apps installed/launched, remained stable, showed the login screen and no stale-session-expired error; SecureStore was unavailable in the unsigned simulator environment and the expected recoverable session-storage banner was verified. Production API/authentication was not exercised. This is simulator/emulator evidence, not physical-device or signed-Keychain acceptance.
- Exact workflow: [CI #1471 / 43325447712](https://github.com/rajeevp727/turnova-platform/actions/runs/43325447712), 12/12 jobs successful on documentation head `8a0199de41770c8e61421f12aa75d10950836084`; source/test head `0c783b729b3f9b4ff8c3406075cd8d06b17eef83`.
- Backend job log confirms: `Passed! - Failed: 0, Passed: 564, Skipped: 0, Total: 564`. Shared mobile/auth tests: 179/179. Python deterministic native artifact/smoke harness: 46/46.
- Smoke report result JSONs and screenshots were inspected. Android Rider/Driver package IDs and process stability passed. Unsigned iOS Rider/Driver bundle IDs and process stability passed. iOS SecureStore-unavailable banner was expected in the unsigned simulator environment; signed Keychain success is not claimed. The smoke scripts do not exercise production API/authentication.
- CI emitted existing xUnit analyzer warnings (including xUnit2031/xUnit2029) but zero build errors and zero failed tests. These are non-blocking cleanup candidates, not test failures.
- PR #156 merged at `1fc57d0d32cc04e33825dc694321dbc6719466ad` after exact documentation-head CI #1472 passed 12/12. PR #157 is the sole active feature PR; its final evidence is recorded below. Strict acceptance remains 0/19 (15 In Progress, 4 Blocked); production readiness is not established.


## PLATFORM-01 PR #157 exact-head verification — CI #1475 (2026-11-13)

- Source/test commit `83063f6eaf929c71e5253cb8f3f2ab0910d66e35` implements minimal unauthenticated `/`, `/health` and `/health/ready` projections while retaining the real bounded storage readiness probe and generic HTTP 503 failure response. PLATFORM-HEALTH-005/006 assert exact response property allow-lists and ensure infrastructure/provider details are absent from failure Problem Details.
- Exact documentation head `8d071f51b9b8795108fc16c5ac70ac730233d6c7` passed GitHub Actions [CI #1475 / 43343585752](https://github.com/rajeevp727/turnova-platform/actions/runs/43343585752) **12/12 jobs**. Backend build/tests passed **564/564**; shared mobile/auth **179/179**; Python harness **46/46**; Rider/Driver Expo config, TypeScript validation and Android/iOS production JS bundle exports passed; native non-container .NET API publish passed; Rider/Driver Android debug and standalone release APK builds passed; both unsigned iOS Simulator builds passed; both Android emulator install/launch smoke tests and both iOS Simulator install/launch smoke tests passed.
- All 12 job records were independently fetched and showed `completed/success`; there were no failed or pending jobs on the exact documentation head. This is CI evidence only. Unsigned iOS Simulator success does not prove signed Keychain/SecureStore persistence; emulator/simulator smoke does not prove physical-device acceptance or production API/authentication. Live Cosmos, production hosting, telemetry export, approved CORS origins, load testing and store signing remain external.
- PR #157 remains Draft while this final evidence synchronization is committed. The new documentation head must receive a separate exact-head CI run before the PR is marked ready or merged. The final review is COMMENT-only, not an approval; no self-approval, bypass or force-push is used.
- Next sequential code task after merge: re-read main and verify/fix `StorageReadinessProbe` unexpected provider exception and cancellation classification; preserve generic HTTP 503 for unavailable storage, distinguish actual probe timeout from spontaneous provider cancellation, and propagate caller cancellation. Then continue remaining #18 diagnostics/CORS/native-publish/audit-path work before #19 RELEASE-01.
- Strict acceptance remains **0/19** (15 In Progress, 4 Blocked), based on complete acceptance evidence including external gates. Production readiness is not established.


## PLATFORM-01 storage readiness failure classification — 2026-11-13

| ID | Scenario | Steps / input | Expected result | Type | Priority |
|---|---|---|---|---|---|
| PLATFORM-HEALTH-007 | Unexpected provider exception | Inject a non-Cosmos/non-HTTP provider exception into the readiness probe and request `/health/ready` | Probe maps it to typed `unavailable`; API returns generic HTTP 503; exception details and infrastructure metadata are absent from Problem Details; liveness remains HTTP 200 | Unit + API integration | P0 |
| PLATFORM-HEALTH-008 | Provider cancellation unrelated to request/deadline | Probe delegate returns a faulted task containing `OperationCanceledException` while neither caller token nor probe timeout has fired | Failure kind is `unavailable`, not `timeout`; no raw provider detail is exposed | Unit | P1 |
| PLATFORM-HEALTH-009 | Distinguish caller cancellation from probe deadline | Cancel the caller token while the probe is actively waiting; separately allow the probe-owned deadline to expire | Caller cancellation propagates and records `cancelled`; probe-owned cancellation maps to typed `timeout` | Unit | P0 |


## PLATFORM-01 storage readiness CI evidence — CI #1479 (2026-11-13)

- Source/test head `40b7e373dd525b6b3985be0c36b72956ca280e9f` adds typed `unavailable` mapping for unexpected provider exceptions, classifies spontaneous provider cancellation as `unavailable` unless the caller or probe deadline was cancelled, preserves genuine caller cancellation, and keeps probe-owned deadline cancellation as `timeout`. PLATFORM-HEALTH-007–009 cover the unit and API outcomes.
- Exact source/test CI [#1479 / 43363366147](https://github.com/rajeevp727/turnova-platform/actions/runs/43363366147) passed **12/12 jobs** after a targeted retry of only the initial Driver Android emulator smoke job. Backend **567/567**, shared mobile/auth **179/179**, Python harness **46/46**; Rider/Driver validation and production JS bundles, native non-container .NET API publish, both Android native builds, both unsigned iOS Simulator builds and all four same-run emulator/simulator smoke jobs passed.
- Initial Driver Android smoke attempt installed/launched the app and captured the expected login screen and foreground activity but exited 255 before emitting its final result. Artifact `14010017033` was inspected: no Driver fatal exception, ANR or React Native unhandled exception was found. Targeted retry job `131699581542` passed; retry artifact `14011861121` confirms package `com.turnova.driver`, successful install/launch, stable process, required login text, no stale-session error and no system-UI recovery. Rider Android artifact `14008929013`, Rider iOS `14010953379` and Driver iOS `14010664772` were downloaded; result JSON and screenshots were inspected.
- Both iOS reports correctly state that SecureStore is unavailable in the unsigned simulator and that the expected recoverable banner is visible. This does not prove signed Keychain persistence. The smoke suite does not exercise production API/authentication. Live Cosmos, production telemetry, physical-device and production-hosting acceptance remain unverified.
- PR #158 remains Draft while progress/matrix/Project Brain/test-plan synchronization receives a new exact-head CI. No merge until that exact documentation head is green and expected-head/review checks are current.


## PLATFORM-01 exact documentation-head verification — CI #1483 (2026-11-13)

- Exact documentation HEAD `ff339ff068d09f7ca6d6a17278344ea138dd68c2`; workflow run [#1483 / 43369222847](https://github.com/rajeevp727/turnova-platform/actions/runs/43369222847) completed **12/12 jobs successfully**.
- Backend **567/567** (0 failed, 0 skipped), shared mobile/auth **179/179**, Python harness **46/46**. Rider/Driver Expo configuration/typechecks/production JS bundles and native non-container .NET API publish passed.
- Both Android debug APKs and standalone release APKs passed verification; both unsigned iOS Simulator builds passed. All four same-run install/launch smokes passed: Rider Android artifact `14011503947`, Driver Android `14012825007`, Rider iOS `14013400018`, Driver iOS `14012094464`. Job logs confirm the expected package IDs launched; both iOS logs explicitly verified the unsigned SecureStore-unavailable recovery banner.
- This does not prove signed-device Keychain persistence, physical-device acceptance, production API/authentication, live Cosmos or production hosting.
- The next evidence-only documentation synchronization creates a new PR head and requires its own exact-head CI. PR #158 remains Draft/unmerged until that run and final expected-head/review checks pass.


## PLATFORM-01 hard storage probe deadline regressions — 2026-11-13

| ID | Scenario | Steps / input | Expected result | Type | Priority |
|---|---|---|---|---|---|
| PLATFORM-HEALTH-010 | Provider-originated timeout exception | Provider throws `TimeoutException` before the probe-owned deadline and without caller cancellation | Classify as dependency `unavailable`, not probe `timeout`; public readiness remains generic HTTP 503 | Unit | P1 |
| PLATFORM-HEALTH-011 | Provider ignores cancellation token | Provider returns a task that remains incomplete after the probe deadline and does not observe the token | `CheckAsync` still returns the probe-owned `timeout` at its fixed upper bound; any later provider fault is observed without logging its detail | Unit | P0 |
| PLATFORM-HEALTH-012 | Caller cancels while provider ignores cancellation | Provider returns an incomplete task that ignores its token; cancel the request token while the probe is pending | Caller cancellation propagates promptly and records `cancelled`; it is not relabeled as timeout or unavailable | Unit | P0 |


## PLATFORM-01 bounded storage readiness — exact-head CI #1487 (2026-11-13)

- Exact source/test HEAD `e0997a8aad7d1d8a88722acf65d716016e6e30e5`; workflow [#1487 / 43376966539](https://github.com/rajeevp727/turnova-platform/actions/runs/43376966539) completed **12/12 jobs successfully**. Backend **570/570**, shared mobile/auth **179/179**, Python harness **46/46**. Rider/Driver validation, TypeScript, production JS bundles and native non-container API publish passed.
- Both Android debug and standalone release APKs passed verification; both unsigned iOS Simulator builds passed. Same-run smoke artifacts inspected: Rider Android `14015208959`, Driver Android `14015880431`, Rider iOS `14016166262`, Driver iOS `14016177184`.
- Android Rider/Driver result JSONs report install/launch passed, stable foreground process, login-screen text verified, stale-session error absent and no system UI recovery. Unsigned iOS Rider/Driver result JSONs report install/launch passed, stable process, login-screen text verified, stale-session-expired error absent and SecureStore-unavailable banner verified. Both iOS artifacts explicitly state unsigned simulator signing and unavailable Keychain access group.
- This does not establish signed-device Keychain persistence, physical-device acceptance, production API/authentication, live Cosmos or production hosting. The emulator/simulator smoke tests are installation/launch/login-screen checks, not end-to-end ride-flow acceptance.
- PLATFORM-HEALTH-010 covers a provider-originated `TimeoutException` classified as dependency `unavailable`, not as the probe-owned timeout. PLATFORM-HEALTH-011 proves the readiness wait returns at its fixed deadline when the provider ignores cancellation and observes any late fault without logging it. PLATFORM-HEALTH-012 proves genuine caller cancellation propagates promptly even when the provider ignores its token.
- PR #158 remains Draft pending exact documentation-head CI and final review/head/base checks. Strict acceptance remains 0/19 (15 In Progress, 4 Blocked); production readiness is not established.


## PLATFORM-01 exact documentation-head CI #1493 — 2026-11-13

- Pre-sync documentation HEAD `c9695ea97d299db8cedea54600f8befcdab11884`; workflow [#1493 / 43384473766](https://github.com/rajeevp727/turnova-platform/actions/runs/43384473766) completed **12/12 jobs successfully** after a targeted retry of only Driver Android emulator smoke job `131754149564` (retry `131767500587`).
- Backend tests **570/570**, shared mobile/auth tests **179/179**, Python harness **46/46**. Rider/Driver Expo config, TypeScript and production JS bundles, native non-container API publish, both Android debug/release APK builds and both unsigned iOS Simulator builds passed.
- Smoke artifacts inspected: Rider Android `14018841676`, Driver Android retry `14020282957`, Rider iOS `14019186476`, Driver iOS `14019121356`. Both Android apps installed/launched, remained stable in the foreground and showed expected login text. Both unsigned iOS apps installed/launched, remained stable and showed expected login text plus the recoverable SecureStore-unavailable banner. Production API/authentication was not exercised; signed-device Keychain and physical-device acceptance are not established.
- The initial Driver Android smoke artifact `14018592304` showed successful install/launch, foreground activity and expected login screenshot but exited 255 before its final report. No app fatal exception, ANR or React Native startup exception was present. The precise failing ADB command is unknown. The targeted retry passed and produced the complete report artifact `14020282957`.
- This evidence-only synchronization creates a new PR #158 head and requires a fresh exact-head CI run. Strict acceptance remains **0/19** (15 In Progress, 4 Blocked); production readiness is not established.

## PLATFORM-01 exact-head verification — CI #1498 (2026-11-13)

- Exact documentation HEAD `6c67c98b192df5ea8210dd47640eee76816ea578`; [workflow #1498 / 43391119590](https://github.com/rajeevp727/turnova-platform/actions/runs/43391119590) passed **12/12 jobs**. Backend **570/570** (0 failed, 0 skipped), shared mobile/auth **179/179**, deterministic Python harness **46/46**; Rider/Driver Expo config, TypeScript checks, production JS bundles and native non-container API publish passed.
- Both Android debug and standalone release APKs and both unsigned iOS Simulator builds passed. All four same-run smoke artifacts were downloaded and inspected: Rider Android `14021706924`, Driver Android `14020793191`, Rider iOS `14022022857`, Driver iOS `14022750045`.
- Android result JSON and screenshots confirm install/launch, stable foreground process, Rider/Driver login-screen text, no stale-session error and no system-UI recovery. iOS result JSON and screenshots confirm install/launch, stable process, role-specific login screen and expected SecureStore-unavailable recovery banner. iOS builds are unsigned simulator artifacts with no Keychain access group; signed-device Keychain validation remains external.
- These smoke tests verify installation, launch and login-screen presentation only. They do not exercise production API/authentication, live Cosmos, signed-device storage, physical devices or end-to-end ride flows. PR #158 remains unmerged pending a new exact-head CI for this documentation synchronization.

## CI harness regression — Android launch-status pipefail safety (2026-11-13)

| ID | Scenario | Steps / input | Expected result | Type | Priority |
|---|---|---|---|---|---|
| PLATFORM-NATIVE-ANDROID-001 | Launch-status check under `set -o pipefail` | Run the deterministic Android smoke harness and assert the launch-status check does not pipe `printf` into early-exit `grep -q` | A valid `Status: ok` launch response is accepted without a SIGPIPE/Broken-pipe false failure; invalid launch output still fails the smoke | Python harness + Android emulator smoke | P1 |

- Exact-head CI #1502 exposed a harness false-negative: the Driver Android emulator launched successfully (`Status: ok`, expected activity), but `printf ... | grep -Fq` caused Bash `printf: write error: Broken pipe` under `set -euo pipefail`, and the script incorrectly reported launch failure.
- The launch check now uses a Bash substring test rather than an early-exit grep pipeline. `scripts/tests/test_android_emulator_smoke.py` contains a regression invariant to prevent reintroducing that unsafe pipeline. The existing end-to-end fake-SDK smoke harness continues to exercise successful launch, process stability and UI checks.
- Source changes: `2ba1a20ce31baefa65a76a5b1cadb9a2554f8fa0` (script) and `fe193763adca3633e0cc202142f323f0188bf1bf` (test). CI #1502's Driver Android smoke artifact `14024159021` confirms the APK installed and ActivityManager reported `Status: ok`; the failure was the shell pipeline, not an observed app crash.
- The new source/test head requires fresh exact-head CI. No successful result is inferred from the failed #1502 attempt.


## Latest exact-head verification — PR #158 / CI #1508 (2026-11-13)

- Main/base HEAD: `b6564c7dc2959883c2910f5b6f13134f7052566f`. PR #158 branch `fix/platform-01-storage-readiness-failure-mapping`; exact tested HEAD `115d1914cb19e0c7a13df6765409521d31a4024b`.
- GitHub Actions [CI #1508 / run 43406713092](https://github.com/rajeevp727/turnova-platform/actions/runs/43406713092) completed successfully: **12/12 jobs passed**. Backend **570/570** (0 failed, 0 skipped); shared mobile/auth **179/179**; deterministic Python harness **47/47**, including `test_launch_status_check_avoids_pipefail_sigpipe_false_failure`; Rider/Driver Expo configuration, typechecks and production JavaScript bundles; native non-container .NET API publish; both Android debug and standalone release APKs; both unsigned iOS Simulator apps; all four same-run emulator/simulator smoke jobs.
- Driver iOS smoke initially hit a 300-second `xcrun simctl list devices available -j` timeout before app installation. Only that job was retried as job `131837472857`; the retry completed successfully. This is transient Simulator infrastructure failure followed by a successful targeted retry, not an application failure.
- Same-run smoke artifacts: Rider Android `14027824488`, Driver Android `14027884362`, Rider iOS `14029062725`, Driver iOS retry `14030386392`. Job logs confirm successful install/launch and role-specific login UI; unsigned iOS reports the expected SecureStore-unavailable recovery banner and does not claim Keychain success. Android release APKs: Rider `14028800834`, Driver `14028640774`; unsigned iOS app artifacts: Rider `14029435192`, Driver `14028438409`.
- The Android smoke harness correction is covered by the 47/47 Python suite: use a Bash substring check instead of `printf | grep -Fq` under `set -euo pipefail`, preventing a SIGPIPE false negative after successful ActivityManager launch. Test-plan case `PLATFORM-NATIVE-ANDROID-001`.
- This verifies CI builds plus emulator/simulator install, launch and login-screen presentation only. It does not establish production API/authentication, live Cosmos, signed iOS Keychain persistence, physical-device acceptance, store signing or end-to-end ride acceptance.
- This evidence-documentation commit creates a new PR head and requires its own exact-head CI. Keep PR #158 Draft until all required jobs pass on that exact new head and the latest base/head, review threads and GitHub merge enforcement are rechecked. No self-approval or bypass.
- Strict acceptance remains **0/19** (15 In Progress, 4 Blocked); this is complete-evidence accounting, not an implementation score. Production readiness is not established.


## CI harness regression evidence — PLATFORM-NATIVE-ANDROID-001

- Exact-head CI #1508 passed the Python harness **47/47**, including `test_launch_status_check_avoids_pipefail_sigpipe_false_failure`.
- The Android launch-status assertion uses a Bash substring condition, avoiding an early-exit `grep -Fq` pipeline under `set -euo pipefail`. The deterministic Python invariant prevents reintroduction of the pipe/SIGPIPE false negative. CI #1508 then passed both Rider and Driver Android emulator smoke jobs.


## Latest exact-head verification — PR #158 / CI #1509 (2026-11-13)

- Main/base HEAD: `b6564c7dc2959883c2910f5b6f13134f7052566f`. PR #158 branch `fix/platform-01-storage-readiness-failure-mapping`; exact tested HEAD `9fe2b884f9e0c2a566dd6e06b1b0fca731f65f82`.
- GitHub Actions [CI #1509 / run 43414466125](https://github.com/rajeevp727/turnova-platform/actions/runs/43414466125) completed successfully: **12/12 jobs passed**. Backend **570/570** (0 failed, 0 skipped); shared mobile/auth **179/179**; deterministic Python harness **47/47**; Rider/Driver Expo configuration, typechecks and production JavaScript bundles; native non-container .NET API publish; both Android debug and standalone release APKs; both unsigned iOS Simulator apps; all four same-run emulator/simulator smoke jobs.
- Same-run smoke artifacts were downloaded and inspected: Rider Android `14030984936`, Driver Android `14031119728`, Rider iOS Simulator `14033460518`, Driver iOS Simulator `14032367500`. Android results confirm install/launch, stable foreground process, role-specific login screen, no stale-session error and no system-UI recovery. iOS results confirm erased simulator data, install/launch, stable process, role-specific login screen, stale-session error absent and the expected SecureStore-unavailable recovery banner.
- Native artifacts: Rider Android release APK `14031468247`, Driver Android release APK `14030834941`, Rider unsigned iOS Simulator app `14032345534`, Driver unsigned iOS Simulator app `14031683932`, native API publish `14031511255`.
- These are CI build and emulator/simulator install/launch/login-screen results only. Unsigned iOS does not establish signed-device Keychain persistence. Production API/authentication, live Cosmos, physical-device acceptance, store signing or end-to-end ride acceptance are not claimed.
- This evidence synchronization creates a new PR head and requires its own exact-head CI before PR #158 is marked ready or merged. Recheck main/base, exact head, all required jobs, review threads and GitHub merge enforcement. No self-approval or bypass.
- Strict acceptance remains **0/19** (15 In Progress, 4 Blocked); this is complete-evidence accounting, not an implementation score. Production readiness is not established. Next after #158 merges: re-read main, implement explicit CORS wildcard-origin validation with deterministic configuration tests, then Union audit observability and remaining PLATFORM-01 work before RELEASE-01.


## Latest exact-head verification — PR #158 / CI #1512 (2026-11-13)

- Exact documentation HEAD `56f5f5573083f354ac1a3cc198f9b672655efcae`; GitHub Actions [CI #1512 / run 43421477453](https://github.com/rajeevp727/turnova-platform/actions/runs/43421477453) completed successfully: **12/12 jobs passed**.
- Backend: **570/570** (0 failed, 0 skipped). Shared mobile/auth: **179/179**. Deterministic Python harness: **47/47**. Rider and Driver Expo configuration, typechecks and Android/iOS production JavaScript bundles passed. Native non-container .NET API publish passed.
- Native builds: both Rider/Driver Android debug APKs and standalone release APKs passed; both unsigned iOS Simulator apps compiled. All four same-run emulator/simulator smoke jobs passed.
- Downloaded and inspected same-run smoke artifacts and result JSON/screenshots:
  - Rider Android emulator: `14035271929` — install/launch passed, stable foreground process, Rider login screen verified, no stale-session error, no system-UI recovery.
  - Driver Android emulator: `14034763708` — install/launch passed, stable foreground process, Driver login screen verified, no stale-session error, no system-UI recovery.
  - Rider iOS Simulator: `14036036290` — simulator data erased, install/launch passed, stable process, Rider login screen verified, SecureStore-unavailable recovery banner verified, stale-session error absent.
  - Driver iOS Simulator: `14037970012` — simulator data erased, install/launch passed, stable process, Driver login screen verified, SecureStore-unavailable recovery banner verified, stale-session error absent.
- Native build artifacts: Rider Android debug `14034092527`, Driver Android debug `14033948751`, Rider Android release APK `14034467813`, Driver Android release APK `14034093247`, Rider unsigned iOS Simulator app `14033739564`, Driver unsigned iOS Simulator app `14035391612`, native API publish `14034013011`.
- The screenshots confirm the expected role-specific login surfaces. The iOS apps are unsigned and have no Keychain access group; the SecureStore recovery banner is expected in this CI environment. This does not establish signed-device Keychain persistence.
- CI verifies build, install, launch and login-screen presentation only. Production API/authentication was not exercised; no live Cosmos, physical-device, signed-release, end-to-end ride or production-readiness claim is made.
- PR #158 remains the sole active feature PR. This evidence-only synchronization creates a new head and requires its own exact-head CI. Recheck current main/base, head SHA, review threads and GitHub merge enforcement; no self-approval or bypass.
- Strict acceptance remains **0/19** (15 In Progress, 4 Blocked); this is complete-evidence accounting, not an implementation score. Production readiness is not established.
- Next after PR #158 merges: re-read main, implement explicit CORS wildcard-origin validation with deterministic configuration tests, then privacy-safe Union audit metrics/ActivitySource tracing and remaining PLATFORM-01 diagnostics/native-publish/audit-path work before #19 RELEASE-01.


## Latest exact-head verification — PR #158 / CI #1513 (2026-11-13)

- Main/base HEAD: `b6564c7dc2959883c2910f5b6f13134f7052566f`. PR #158 branch `fix/platform-01-storage-readiness-failure-mapping`; tested documentation HEAD `89dc76f483ffa9a27dda1b67c5eaf50d78c362da`.
- GitHub Actions [CI #1513 / run 43431035440](https://github.com/rajeevp727/turnova-platform/actions/runs/43431035440) completed successfully on **attempt 2: 12/12 jobs passed**. Backend **570/570** (0 failed, 0 skipped); shared mobile/auth **179/179**; deterministic Python harness **47/47**; Rider/Driver Expo validation, typechecks and production JS bundles; native non-container .NET API publish; both Android debug/release APKs; both unsigned iOS Simulator builds; all four same-run Android/iOS smoke tests.
- The first attempt failed only Rider Android smoke job `131909886413` with exit 255 after APK installation and ActivityManager `Status: ok`. Its artifact `14040178925` contained the expected Rider login UI/foreground activity and no app fatal exception or ANR, but no final result JSON. GitHub's failed-jobs-only retry ran job `131930156233` and passed. Retry artifact `14045725928` confirms install/launch, stable foreground process, Rider login text, no stale-session error and no system-UI recovery.
- All four final smoke artifacts were downloaded and inspected:
  - Rider Android retry: `14045725928`.
  - Driver Android: `14041925228`.
  - Rider iOS Simulator: `14043137536`.
  - Driver iOS Simulator: `14041887826`.
- Android reports confirm successful install/launch, stable foreground process, role-specific login screen, no stale-session error and no system-UI recovery. iOS reports confirm simulator data erasure, install/launch, stable process, role-specific login screen, no stale-session-expired error and the expected SecureStore-unavailable recovery banner. Native artifacts: Rider Android debug `14038763284`, Driver Android debug `14039628739`, Rider Android release APK `14038654645`, Driver Android release APK `14039349859`, Rider unsigned iOS Simulator app `14040388207`, Driver unsigned iOS Simulator app `14040736292`, API publish `14039892373`.
- These are CI build and emulator/simulator install/launch/login-screen results only. Unsigned iOS does not establish signed-device Keychain persistence. Production API/authentication, live Cosmos, physical-device, signed-store release and end-to-end ride acceptance were not exercised.
- This evidence synchronization creates a new PR head and requires fresh exact-head CI before PR #158 is marked ready or merged. Recheck main/base, exact HEAD, all required jobs, review threads and GitHub merge enforcement. No self-approval or bypass.
- Strict acceptance remains **0/19** (15 In Progress, 4 Blocked); this is complete-evidence accounting, not an implementation score. Production readiness is not established.
- Next after PR #158 merges: re-read main, implement explicit CORS wildcard-origin validation with deterministic configuration tests, then privacy-safe Union audit metrics/ActivitySource tracing and remaining PLATFORM-01 diagnostics/native-publish/audit-path work before #19 RELEASE-01.



## Latest exact-head verification — PR #158 / CI #1514 (2026-11-13)

- Main/base HEAD: `b6564c7dc2959883c2910f5b6f13134f7052566f`. PR #158 branch `fix/platform-01-storage-readiness-failure-mapping`; exact tested documentation HEAD `0152a12789001e349b94da4e913160e1dd6acafc`.
- GitHub Actions [CI #1514 / run 43439229082](https://github.com/rajeevp727/turnova-platform/actions/runs/43439229082) completed successfully: **12/12 jobs passed**. Backend: **570/570** (0 failed, 0 skipped); shared mobile/auth: **179/179**; Python deterministic harness: **47/47**. Both Rider and Driver Expo validation, TypeScript checks and Android/iOS production JavaScript bundle exports passed. Both mobile dependency audits reported 0 high and 0 critical findings (11 moderate, 1 low each; informational audit output, not a release-security sign-off).
- Native non-container .NET API publish passed. Both Rider and Driver Android debug APKs and standalone release APKs built. Both unsigned iOS Simulator apps built. All four same-run Android emulator/iOS Simulator install-launch smoke jobs passed.
- Same-run smoke artifacts were downloaded and inspected:
  - Rider Android: `14044689106` — install/launch passed, process stable, Rider login text verified, no stale-session error or system-UI recovery.
  - Driver Android: `14045463266` — install/launch passed, process stable, Driver login text verified, no stale-session error or system-UI recovery.
  - Rider iOS Simulator: `14046196834` — install/launch passed, process stable, Rider login text verified, simulator data erased; SecureStore unavailable and the expected recovery banner was shown.
  - Driver iOS Simulator: `14046876982` — same simulator result for the Driver login surface.
- Native artifacts: Rider Android debug `14045911470`, Driver Android debug `14044657223`, Rider Android release APK `14045772222`, Driver Android release APK `14045528021`, Rider unsigned iOS Simulator app `14045669353`, Driver unsigned iOS Simulator app `14047120044`, API publish `14044902241`. Production JS bundles: Rider `14045556170`, Driver `14045366748`. Release-readiness reports: Rider `14044207450`, Driver `14044167454`.
- These results establish CI build and emulator/simulator install-launch/login-screen evidence only. Unsigned iOS does not establish signed-device Keychain persistence. Production API/authentication, live Cosmos transactional behavior, physical-device acceptance, signed-store release and end-to-end ride acceptance were not exercised.
- Review state at this checkpoint: PR #158 is marked ready for review; the latest COMMENT-only review covers the exact parent evidence head, and the review-thread list returned zero unresolved threads. This is not an approval or a claim that an independent approval exists. The base is not behind the tested head. Recheck live main/base, exact head, review threads and GitHub merge policy after the next documentation synchronization; merge only with exact-head CI green and no bypass.
- Strict acceptance remains **0/19** (15 In Progress, 4 Blocked). CI build/test evidence does not satisfy the remaining physical-device, live-provider, product-policy or legal acceptance gates. Production readiness is not established.
- Next sequential engineering: after PR #158 is safely merged, re-read main and continue #18 PLATFORM-01 with explicit CORS wildcard-origin rejection and deterministic startup/configuration tests, then privacy-safe Union audit metrics/ActivitySource tracing and remaining diagnostics/native-publish/audit-path work before #19 RELEASE-01. This documentation update creates a new PR head and requires its own exact-head CI.


## PLATFORM-01 exact-head verification — PR #158 / CI #1521 (2026-11-13)

- Exact tested head: `0b505631ba361a9f8df96858111eb2785d8f6dc6`; main/base: `b6564c7dc2959883c2910f5b6f13134f7052566f`.
- Exact-head CI [CI #1521 / run 43449298845](https://github.com/rajeevp727/turnova-platform/actions/runs/43449298845) passed **12/12 jobs** on `0b505631ba361a9f8df96858111eb2785d8f6dc6`: backend **570/570**, shared mobile/auth **179/179**, deterministic Python harness **47/47**; Rider/Driver Expo config, TypeScript validation and production JS bundles; native non-container .NET API publish; both Android debug and standalone release APKs; both unsigned iOS Simulator builds; and all four same-run emulator/simulator smoke jobs. Both mobile dependency audits reported **0 high / 0 critical** (11 moderate, 1 low each; not a release-security sign-off).
- New deterministic storage readiness cases PLATFORM-HEALTH-007–012 cover unexpected provider exceptions, spontaneous provider cancellation/timeout classification, caller cancellation, the probe-owned deadline when a provider ignores cancellation, late provider-fault observation and privacy-safe bounded diagnostics. API integration coverage confirms an unexpected storage exception produces generic HTTP 503 while liveness remains healthy and provider detail is absent from the public response.
- PLATFORM-NATIVE-ANDROID-001 protects the emulator smoke launch check against a `grep -q`/pipefail SIGPIPE false negative.
- Same-run smoke artifacts were downloaded and inspected: Rider Android `14051205443`, Driver Android `14051030643`, Rider iOS Simulator `14052726663`, Driver iOS Simulator `14052767198`.
- Android result JSONs confirm install and launch passed, stable foreground process, role-specific login text, no stale-session error and no system-UI recovery. iOS result JSONs confirm simulator data erased, install and launch passed, stable process, role-specific login text, no stale-session error and the expected SecureStore-unavailable recovery banner. Both iOS artifacts are unsigned simulator builds; Keychain access is unavailable and signed-device persistence is not established.
- Native artifacts: Rider Android debug `14049433157`, Driver Android debug `14050575202`, Rider Android release `14050451613`, Driver Android release `14052005270`, Rider unsigned iOS Simulator app `14051466634`, Driver unsigned iOS Simulator app `14050422210`, API publish `14049237810`, Rider production JS bundles `14049063217`, Driver production JS bundles `14048374702`.
- CI verifies build, install, launch and login-screen presentation only. Production API/authentication, live Cosmos transactional behaviour, signed-device Keychain, physical-device acceptance, store signing and end-to-end ride acceptance were not exercised.
- This is automated CI and emulator/simulator evidence, not production API/authentication, live Cosmos, signed-device Keychain, physical-device or full ride acceptance.


## Latest exact-head evidence — PR #158 / CI #1523 (2026-11-13)

- Main/base HEAD: `b6564c7dc2959883c2910f5b6f13134f7052566f`; PR #158 branch `fix/platform-01-storage-readiness-failure-mapping`; exact tested PR head: `48474830f0c4a2d14733ee738f9109179dd07def`.
- GitHub Actions [CI #1523 / run 43459668928](https://github.com/rajeevp727/turnova-platform/actions/runs/43459668928) completed successfully with **12/12 jobs** on the exact PR head. Backend tests: **570/570** (0 failed, 0 skipped). Shared mobile/auth tests: **179/179**. Deterministic Python native-artifact and Android/iOS smoke-harness tests: **47/47**.
- Rider and Driver Expo validation, TypeScript checks and production JavaScript bundle exports passed. Native non-container .NET 10 API publish and artifact verification passed. Both apps' Android debug and standalone release APK builds passed; both unsigned iOS Simulator app builds passed; all four same-run Android emulator/iOS Simulator install-launch smoke jobs passed.
- Same-run smoke artifacts: Rider Android `14057030887`, Driver Android `14056906435`, Rider iOS Simulator `14059827183`, Driver iOS Simulator `14058624906`. Android job logs show ActivityManager `Status: ok` and successful install/launch. Both iOS job logs show the expected login UI and explicitly verify the unsigned-simulator SecureStore-unavailable banner.
- The tested source head includes the late-provider-fault observation race correction: the fault observer is attached even if the provider task completed concurrently with the bounded wait. Deterministic tests cover unexpected provider exceptions, spontaneous provider cancellation/timeout classification, caller cancellation, non-cooperative providers, late faults and generic HTTP 503 privacy mapping.
- This evidence covers deterministic tests, native compilation/publish and emulator/simulator install-launch smoke only. The iOS apps are unsigned and do not establish signed Keychain persistence. Production API/authentication, live Cosmos transactional behavior, physical-device acceptance, signed-store release, end-to-end ride acceptance and production readiness were not exercised.
- PR #158 remains the sole active feature PR. Inline review threads were empty at the latest check; submitted reviews are COMMENT-only and do not constitute an approval. The GitHub branch-protection endpoint is not readable through the current integration, so merge enforcement must be left to GitHub's merge operation; do not bypass it. No self-approval, force-push or stale-head merge.
- Strict acceptance remains **0/19** (15 In Progress, 4 Blocked). CI evidence does not promote any row to Verified. Production readiness is not established.
- Next sequential work after a safe #158 merge: re-read main, implement explicit CORS wildcard-origin rejection with deterministic startup/configuration tests, then privacy-safe Union audit metrics/ActivitySource tracing and remaining PLATFORM-01 diagnostics/native-publish/audit-path hardening before #19 RELEASE-01.
- External gates remain live Cosmos ETag/partition/multi-instance/restart/retention validation; production authentication and Operations identity; telemetry export/alerts; approved CORS origins/load targets; product-approved dispatch radius/zones/location freshness; maps/routing and realtime providers; private document storage/scanning/consent/retention; support/escalation and reconciliation policies; Union legal approval; signed Android/iOS artifacts and physical-device acceptance; production API hosting/deployment and pilot/store acceptance.


## PLATFORM-NATIVE-ANDROID-002 — final emulator screenshot transport retry (2026-11-14)

| ID | Scenario | Steps / input | Expected result | Type | Priority |
|---|---|---|---|---|---|
| PLATFORM-NATIVE-ANDROID-002 | Final Android emulator screenshot survives a transient ADB transport failure but fails closed after persistent failure | Run the deterministic fake-SDK harness with the initial screenshot succeeding and the final `adb exec-out screencap -p` failing once with exit 255; repeat with three consecutive final-capture failures | One transient failure is retried and a PNG-signature-verified `launch.png` plus success JSON are produced; three failures return non-zero, include ADB/emulator diagnostics and do not produce success JSON | Python harness + Android emulator smoke | P1 |

- CI #1527 / run 43488094224 completed with 11/12 jobs successful. Rider Android smoke installed the standalone APK, ActivityManager reported `Status: ok`, the Rider activity was resumed, the login accessibility text was present and captured logcat had no detected Rider ANR/fatal/JS startup exception. The job exited 255 after these checks and before `launch.png` and `result.json` were written. Artifact [14069016945](https://github.com/rajeevp727/turnova-platform/actions/runs/43488094224/artifacts/14069016945) contains the initial screenshot, activity snapshots, UI hierarchy and logcat. The previous script did not identify the exact post-launch ADB command that returned 255; no narrower root cause is claimed.
- Follow-up source commit `16df7ec1eef6375261571a5cf3e00d9bee2ddd07` adds a bounded three-attempt final screenshot capture, stderr/exit-status capture, PNG signature validation, and ADB/emulator diagnostics after persistent failure. Deterministic fixture coverage was added in `56a57cbc9ad4bf8ac43bd534970e37abeb248b11` and corrected in subsequent fixture-escape commits through current code/test head `182102a217dad3ba9e5378eefd0b28d22556c180`.
- Exact-head CI [#1532 / run 43497151332](https://github.com/rajeevp727/turnova-platform/actions/runs/43497151332) was pending on code/test head `182102a217dad3ba9e5378eefd0b28d22556c180` when this documentation synchronization began. It is superseded by the new documentation head; do not treat it as successful evidence. A fresh exact-head CI run is required.
- The retry applies only to final evidence capture. APK install, app launch, foreground-process, UI, ANR and runtime-exception checks remain mandatory; persistent screenshot failure still fails the smoke. This remains emulator evidence, not physical-device or production API/authentication acceptance.


## Latest smoke-harness head — 2026-11-14

- Final assertion-diagnostic formatting cleanup commit: `182102a217dad3ba9e5378eefd0b28d22556c180`. It changes only deterministic Python harness failure-message formatting; no production/runtime behavior changes.
- CI #1538 / run 43497853938 was queued on that code/test head and is superseded by this documentation commit. No passing result is claimed. A fresh exact-head CI run must complete after documentation synchronization.
- PLATFORM-NATIVE-ANDROID-002 remains implemented in the deterministic harness: one transient final-screenshot ADB failure retries to a valid PNG and success report; three failures remain non-zero with ADB/emulator diagnostics and no success report.

## PLATFORM-NATIVE-ANDROID-002 execution evidence — CI #1541 (2026-11-14)

- Exact tested PR #158 head `447edbc4ce3dddab8edb762576da80d87f4db0a3`; GitHub Actions [CI #1541 / run 43498668217](https://github.com/rajeevp727/turnova-platform/actions/runs/43498668217) passed **12/12 jobs**.
- Backend 570/570, shared mobile/auth 179/179 and deterministic Python harness 47/47 passed. Rider/Driver Expo validation, typechecks and production JS bundles, native non-container .NET API publish, Android debug/release APK builds and unsigned iOS Simulator builds passed.
- Rider and Driver Android emulator smoke logs both show ActivityManager `Status: ok`, final screenshot capture on attempt 1 and successful install/launch. The harness cases for transient screenshot failure recovery and persistent failure fail-closed behavior ran in the 47/47 Python suite.
- Rider and Driver iOS Simulator smoke logs show the role-specific login surface and expected SecureStore-unavailable banner for unsigned simulator builds. They do not establish signed-device Keychain persistence.
- This is automated CI emulator/simulator evidence only. Physical-device, signed-store, production authentication, live Cosmos and end-to-end ride acceptance remain external.


## Exact-head CI #1547 — PR #159 (2026-11-14)

- Exact tested head: `a9a19b66d2d8dc6110095cd4a4a9f8f231ad8ca8`; GitHub Actions [CI #1547 / run 43523529694](https://github.com/rajeevp727/turnova-platform/actions/runs/43523529694) completed with **11/12 jobs successful**. Backend tests passed **585/585**; Rider/Driver Expo validation, Android debug/release builds, unsigned iOS Simulator builds and all four emulator/simulator smoke jobs passed.
- The only failed job was `mobile-auth-tests`. Its deterministic harness ran 48 tests; 47 passed and `test_release_smoke_test_ids_are_unique` failed because the new iOS delayed-SpringBoard case reused `RELEASE-SMOKE-023`, which was already assigned to the earlier Android isolated-AVD ownership case (line 1443 at this head). This was a test-plan identifier collision, not an application/runtime or simulator-smoke failure.
- Preserve the existing Android identifier `RELEASE-SMOKE-023`; assign the new iOS delayed-foreground case `RELEASE-SMOKE-027` (next unused identifier after 026). The uniqueness guard remains unchanged. The resulting table has 27 unique RELEASE-SMOKE rows. A fresh exact-head CI run is required after this documentation correction; no pass is claimed for the corrected head yet.


## PLATFORM-01 — Union audit observability (2026-11-14)

| ID | Scenario | Steps / input | Expected result | Type | Priority |
|---|---|---|---|---|---|
| UNION-OBS-001 | Union assignment, transfer and selfie review emit bounded telemetry | Execute initial Cab Union assignment, Union selfie approval, transfer to a second Union and selfie rejection with deterministic in-memory API fixtures and MeterListener/ActivityListener | Each persisted event emits `turnova.union.audit.write_attempts` and `union.audit.write`; event categories are limited to assigned, transferred, selfie_approved, selfie_rejected or other; only event_type/outcome metric tags and normalized event_type/outcome activity tags are present | API integration | P1 |
| UNION-OBS-002 | Idempotent duplicate Union audit event is classified as a canonical conflict | Repeat the same Union selfie approval with a second authorized manager after the first canonical event is persisted | The canonical event is verified without a duplicate record; metric outcome is conflict and the activity remains successful | API integration | P1 |
| UNION-OBS-003 | Mismatched canonical Union audit event fails closed | Seed the deterministic selfie-review event identity with a canonical record whose actor differs from the immutable profile recovery snapshot, then retry the review | Request does not succeed; metric outcome is failure and activity status is Error; no canonical event is overwritten and no actor/Union/Driver/reason data is emitted | API integration | P1 |
| UNION-OBS-004 | Unknown Union audit telemetry values collapse to safe defaults | Call the diagnostic recorder with an unknown event category and outcome | Metric emits event_type=other and outcome=failure; arbitrary caller values are never emitted | Unit | P1 |
| UNION-OBS-005 | Telemetry listener callbacks fail | Register deterministic MeterListener/ActivityListener callbacks that throw during metric, activity start and activity stop | Diagnostics failures are isolated; the telemetry boundary does not throw into the authoritative Union audit persistence path | Unit | P1 |
| UNION-OBS-006 | Union audit persistence survives throwing telemetry listeners | Exercise initial assignment with a throwing ActivitySource start listener and metric listener, then transfer with a throwing ActivityStopped callback and metric listener | Both API operations succeed; exactly one immutable Assigned and one Transferred event remain persisted; telemetry callback exceptions do not replace the authoritative audit result | API integration | P0 |

The Union audit writer retains create-if-absent persistence and canonical recovery. A conflict is classified only after the stored event is read and matches the expected immutable event; a mismatched or unavailable canonical event is a failure. The metrics use framework Meter/ActivitySource APIs only; no exporter, vendor, endpoint or credentials are added. Live Cosmos conflict/partition/multi-instance/restart behaviour and production telemetry export/alert configuration remain external validation gates.



## PLATFORM-01 — Union audit observability exact-head CI #1572 (2026-11-14)

- Exact tested PR #160 head: b0e59014a3019a79c079f87dd9be52752bfbbd2a; GitHub Actions [CI #1572 / run 43545955208](https://github.com/rajeevp727/turnova-platform/actions/runs/43545955208) completed **12/12 jobs passed**.
- Backend .NET Release build and tests: **589/589 passed**, 0 failed and 0 skipped. Shared mobile/auth Node tests: **179/179 passed**. Deterministic Python native/CI harness: **48/48 passed**.
- Rider/Driver Expo validation, typechecks and production JS bundles passed. Both Rider and Driver Android debug and standalone release APKs compiled and passed package-identity verification. Both unsigned iOS Simulator apps compiled. Native non-container .NET API Release publish and artifact verification passed.
- All four same-run smoke jobs passed. Rider Android smoke artifact `14093669688`; Driver Android `14094635476`; Rider iOS Simulator `14095426971`; Driver iOS Simulator `14094922688`. Android logs show successful install/launch and screenshot capture. iOS logs show the role-specific login surface and expected SecureStore-unavailable banner for unsigned simulator builds; this is not signed-device Keychain evidence.
- `UNION-OBS-001–005` exercise allow-listed low-cardinality metric/activity tags, initial assignment, transfer, selfie approval/rejection, duplicate canonical recovery (one persisted event), canonical actor mismatch fail-closed behavior, unknown-value normalization and telemetry listener failures. No IDs, reasons, selfie/document data or credentials are telemetry dimensions.
- The complete nine-file diff was reviewed. No authorization, audit identity, partition, ETag, persistence or canonical-recovery semantics changed. The current documentation synchronization creates a new PR head and requires fresh exact-head CI before ready/merge.
- Live Cosmos conflict/partition/multi-instance/restart validation, production telemetry exporter/alert configuration, signed mobile artifacts, physical-device acceptance and production hosting remain external gates. Strict acceptance remains **0/19** (15 In Progress, 4 Blocked); production readiness is not established.


## Latest execution evidence — PR #160 / CI #1573 (2026-11-14)

The Union audit observability implementation and deterministic integration cases UNION-OBS-001 through UNION-OBS-005 passed source/test CI #1572 and final documentation-head CI #1573. CI #1573 / run 43549944262 passed 12/12 jobs on PR head `d9632792494742f132dd21b6146e6de615f3591e`: backend 589/589, shared mobile/auth 179/179, deterministic Python harness 48/48, Rider/Driver Expo validation, Android debug/release APKs, unsigned iOS Simulator builds, native non-container .NET API Release publish, production JS bundles and four same-run emulator/simulator smoke jobs.

Smoke artifacts: Rider Android `14095977137`, Driver Android `14096095895`, Rider iOS Simulator `14096398079`, Driver iOS Simulator `14096323884`. Android logs confirm installation, launch and screenshot capture on attempt 1. Unsigned iOS Simulator logs confirm role-specific login surfaces and the SecureStore-unavailable banner. These are automated CI results only; no signed-device Keychain, physical-device, live Cosmos, production telemetry exporter or production readiness is claimed.


## PLATFORM-01 — ride and storage telemetry listener-failure isolation (2026-11-14)

| ID | Scenario | Steps / input | Expected result | Type | Priority |
|---|---|---|---|---|---|
| RIDE-OBS-001 | Ride creation remains authoritative when ActivityStarted and MeterListener callbacks throw | Register throwing callbacks only for `ride.audit.write` and `turnova.ride.audit.write_attempts`, then create a Rider ride through the API | HTTP create succeeds and the immutable RideCreated audit event is persisted; telemetry callback exceptions do not escape or alter the write result | API integration | P1 |
| RIDE-OBS-002 | Ride creation and cancellation remain authoritative when ActivityStopped and MeterListener callbacks throw | Register throwing stop/metric callbacks, create a ride, then cancel it as the owning Rider | Both create and state replacement succeed; the ride is Cancelled with exactly the expected append-only audit events | API integration | P1 |
| STORAGE-OBS-001 | Readiness succeeds when ActivityStarted and metric listeners throw | Execute a successful deterministic storage probe with throwing start and metric callbacks | CheckAsync completes successfully; listener exceptions do not become storage failures | Unit/integration | P1 |
| STORAGE-OBS-002 | Original storage failure classification survives ActivityStopped and metric listener exceptions | Execute a probe that fails with HttpRequestException while stop/metric callbacks throw | CheckAsync still throws StorageReadinessException with FailureKind unavailable and preserves the original exception type | Unit/integration | P1 |

- Telemetry is optional and best-effort. Activity creation, outcome tagging/status, disposal, counters and histograms must be isolated independently from ride persistence and storage readiness.
- Tests use deterministic in-process ActivityListener and MeterListener callbacks; they do not claim live exporter, live Cosmos, multi-instance or production readiness validation.


## PLATFORM-01 — ride/storage telemetry listener isolation execution evidence (2026-11-14)

RIDE-OBS-001/002 and STORAGE-OBS-001/002 passed exact source/test CI #1589 / run 43564291551 on PR #161 head `e625693c697c3903639c1135dd66075895643926`. The complete run passed 12/12 jobs: backend **594/594** (0 failed, 0 skipped), shared mobile/auth **179/179**, deterministic harness **48/48**, Rider/Driver Expo validation, Android debug/release APKs, unsigned iOS Simulator builds, native non-container .NET API Release publish/artifact verification and all four same-run smoke jobs.

- RIDE-OBS-001: a Rider-selected Cab booking persists its RideCreated audit event when the ride ActivityStarted and metric callbacks throw.
- RIDE-OBS-002: Rider-selected booking and owner cancellation both persist the expected append-only audit events when ActivityStopped and metric callbacks throw.
- STORAGE-OBS-001: a successful deterministic readiness probe remains successful when ActivityStarted and metric callbacks throw.
- STORAGE-OBS-002: an unavailable probe preserves StorageReadinessException FailureKind `unavailable` and the original HttpRequestException when ActivityStopped and metric callbacks throw.
- iOS Simulator smoke logs verified role-specific login surfaces and the expected unsigned-Simulator SecureStore-unavailable banner. Android smoke jobs verified install/launch and screenshot capture on attempt 1. No signed-device Keychain or physical-device acceptance is claimed.
- CI run: [#1589 / 43564291551](https://github.com/rajeevp727/turnova-platform/actions/runs/43564291551). This evidence is for the source/test head; documentation updates create a new head and require fresh exact-head CI before merge.
- Optional telemetry is best-effort. No live Cosmos, exporter, production host, signed release or physical-device validation is claimed.


## PLATFORM-01 — native API publish verifier hardening (2026-11-14)

| ID | Scenario | Steps / input | Expected result | Type | Priority |
|---|---|---|---|---|---|
| PLATFORM-PUBLISH-001 | Complete framework-dependent publish fixture is accepted | Provide non-empty API assembly, `.deps.json`, `.runtimeconfig.json`, Linux x64 SkiaSharp/HarfBuzz native assets and an artifact below 120 MiB | Verifier succeeds and reports both native assets | Bash fixture | P1 |
| PLATFORM-PUBLISH-002 | Missing dependency metadata is rejected | Remove `TurnOva.API.deps.json` | Verifier exits non-zero with the exact missing/empty metadata diagnostic | Bash fixture | P1 |
| PLATFORM-PUBLISH-003 | Empty runtime configuration is rejected | Truncate `TurnOva.API.runtimeconfig.json` to zero bytes | Verifier exits non-zero with the exact missing/empty metadata diagnostic | Bash fixture | P1 |
| PLATFORM-PUBLISH-004 | Empty API assembly is rejected | Truncate `TurnOva.API.dll` to zero bytes | Verifier exits non-zero with the exact missing/empty API assembly diagnostic | Bash fixture | P1 |
| PLATFORM-PUBLISH-005 | Missing SkiaSharp native asset is rejected | Remove `libSkiaSharp.so` | Verifier identifies the missing SkiaSharp asset | Bash fixture | P1 |
| PLATFORM-PUBLISH-006 | Empty SkiaSharp native asset is rejected | Truncate `libSkiaSharp.so` to zero bytes | Verifier identifies the empty SkiaSharp asset | Bash fixture | P1 |
| PLATFORM-PUBLISH-007 | Missing HarfBuzz native asset is rejected | Remove `libHarfBuzzSharp.so` | Verifier identifies the missing HarfBuzz asset | Bash fixture | P1 |
| PLATFORM-PUBLISH-008 | Empty HarfBuzz native asset is rejected | Truncate `libHarfBuzzSharp.so` to zero bytes | Verifier identifies the empty HarfBuzz asset | Bash fixture | P1 |
| PLATFORM-PUBLISH-009 | Nested forbidden font assembly is rejected | Place `PdfSharp.WPFonts.dll` under a nested runtime asset directory | Verifier rejects the forbidden assembly regardless of nesting depth | Bash fixture | P1 |
| PLATFORM-PUBLISH-010 | Unsupported runtime assets are rejected | Add an `osx-x64` runtime directory to the Linux x64 artifact | Verifier identifies the unsupported runtime directory | Bash fixture | P1 |
| PLATFORM-PUBLISH-011 | Oversized artifact is rejected | Make the fixture exceed the configured 120 MiB limit | Verifier reports the size-limit failure | Bash fixture | P1 |
| PLATFORM-PUBLISH-012 | Missing API assembly is rejected | Remove `TurnOva.API.dll` | Verifier exits non-zero with the exact missing/empty API assembly diagnostic | Bash fixture | P1 |

The fixture harness asserts the expected diagnostic for each negative case; a generic non-zero exit is not sufficient evidence. The local Bash fixture run passed the valid fixture and all 11 negative cases. Exact-head GitHub Actions evidence is pending. This validates the publish directory contract only; it does not establish production hosting, live Cosmos, signed release or production readiness.


## PLATFORM-01 — native API publish verifier exact-head execution evidence (2026-11-14)

PR #163 exact head `77cf39ff10268901e6a2229f3b65f21fdce34bb3` passed [GitHub Actions CI #1608 / run 43583820733](https://github.com/rajeevp727/turnova-platform/actions/runs/43583820733) **12/12 jobs**.

- Backend Release build and tests: **594/594 passed**, 0 failed, 0 skipped. Shared mobile/auth Node tests: **179/179 passed**. Deterministic Python CI harness: **48/48 passed**.
- Rider/Driver Expo validation, Android debug and standalone release APK builds, unsigned iOS Simulator builds, native non-container .NET API Release publish and all four same-run emulator/simulator smoke jobs passed.
- `scripts/test-api-publish-verifier.sh` passed one valid fixture plus 11 negative cases; negative cases assert the expected diagnostic. `scripts/verify-api-publish.sh` validated the actual CI publish directory at **43,111,979 bytes** against the 125,829,120-byte limit.
- Actual publish artifact `14109034542` contained non-empty `TurnOva.API.dll`, `TurnOva.API.deps.json`, `TurnOva.API.runtimeconfig.json`, `libSkiaSharp.so` and `libHarfBuzzSharp.so`; `PdfSharp.WPFonts.dll` was absent. The runtime directory allow-list remained Linux x64 plus the existing `unix` compatibility entry.
- Same-run smoke artifacts: Rider Android `14110196137`, Driver Android `14109384830`, Rider iOS Simulator `14109792158`, Driver iOS Simulator `14109599886`. Android apps installed/launched and screenshots were captured on attempt 1. Both unsigned iOS Simulator apps reached their role-specific login surface and verified the SecureStore-unavailable banner. No signed-device Keychain or physical-device acceptance is claimed.
- This evidence verifies CI build artifacts and deterministic verifier behavior only; it does not establish production API hosting, live Cosmos, signed releases or production readiness.


## RELEASE-01 — release preflight validation and CLI boundaries (2026-11-14)

| ID | Scenario | Steps / input | Expected result | Type | Priority |
|---|---|---|---|---|---|
| RELEASE-PREFLIGHT-001 | Expo version follows SemVer 2.0.0 | Exercise valid core/prerelease/build identifiers and invalid leading-zero core/numeric prerelease, empty identifiers and CR/LF/U+2028/U+2029 | Valid SemVer is accepted; malformed or line-terminated versions fail the `app-version` engineering check | Node unit | P1 |
| RELEASE-PREFLIGHT-002 | EAS CLI minimum is a strict supported range | Exercise `>= MAJOR.MINOR.PATCH` at 16+, below 16, abbreviated, malformed, prerelease, compound and line-terminated ranges | Only a strict minimum range at major 16 or newer passes; all malformed/unsupported forms fail `eas-cli-version` | Node unit | P1 |
| RELEASE-PREFLIGHT-003 | EAS development/preview/production profiles fail closed | Remove required development-client/internal distribution, preview APK/internal distribution, or production app-bundle/store distribution settings | Each invalid profile produces its corresponding engineering error | Node unit | P1 |
| RELEASE-PREFLIGHT-004 | Production API URL rejects credentials and delimiters | Supply username/password, query, fragment, empty `?`/`#` delimiters and surrounding whitespace | API URL is not reported as configured; the production URL remains an external gate | Node unit | P1 |
| RELEASE-PREFLIGHT-005 | Production API URL rejects reserved and internal destinations | Exercise loopback/private/link-local/CGNAT/reserved/multicast IPv4, non-global IPv6, internal suffixes, case and trailing-dot variants; include public boundary controls | Reserved/internal values fail closed; supported public-address syntax passes without claiming DNS reachability | Node unit | P1 |
| RELEASE-PREFLIGHT-006 | CLI writes a machine-readable readiness report | Run the real CLI against an isolated temporary Rider app fixture with `--app-dir` and `--output` | JSON report is parseable, engineering checks pass, seven external gates are listed, production readiness remains blocked, and exit code is zero | Node CLI integration | P1 |
| RELEASE-PREFLIGHT-007 | CLI records invalid metadata and exits non-zero | Run the real CLI with a leading-zero app version and an output path | Failure report is written, `app-version` is present, and exit code is one | Node CLI integration | P1 |
| RELEASE-PREFLIGHT-008 | CLI rejects missing, duplicate and unknown arguments | Exercise absent app directory, missing values, an option used as another option's value, duplicate flags and unknown flags | CLI emits a deterministic diagnostic and exits non-zero before reading app files | Node CLI integration | P1 |

### RELEASE-01 — final exact-head CI evidence (PR #164 / CI #1618, 2026-11-14)

PR #164 head `467400c65b99094b6714e9b23846f4507f41b438` passed [GitHub Actions CI #1618 / run 43594209819](https://github.com/rajeevp727/turnova-platform/actions/runs/43594209819) with **12/12 jobs successful**.

- Backend Release build/tests: **594/594 passed**, 0 failed, 0 skipped.
- Shared mobile/auth Node tests: **187/187 passed**, including all eight RELEASE-PREFLIGHT-001..008 cases.
- Deterministic Python CI harness: **48/48 passed**.
- Rider and Driver Expo validation, typecheck, Metro shared-module visibility, production Android/iOS JavaScript bundles and release preflight passed.
- Rider/Driver Android debug and standalone release APK builds passed; both unsigned iOS Simulator app builds passed; native non-container .NET API Release publish and publish-artifact verifier passed.
- Rider/Driver Android emulator install/launch smoke and Rider/Driver iOS Simulator install/launch smoke all passed. Android screenshots were captured on attempt 1. Both unsigned iOS apps reached their role-specific login surfaces and verified the SecureStore-unavailable banner; signed-device Keychain success is not claimed.
- Native API publish artifact: `14113468588`. Same-run smoke artifacts: Rider Android `14114280288`, Driver Android `14113828820`, Rider iOS Simulator `14114755885`, Driver iOS Simulator `14114186408`. Other native build, production JS bundle, release-readiness and dependency-audit artifacts are retained on the same workflow run.
- Both app release preflight reports continue to identify seven external gates. No signed Android AAB, signed iOS IPA, production API endpoint, EAS project linkage, signing credential or store submission is claimed.
- This CI evidence validates deterministic code/build/emulator/simulator workflows only. It does not establish production authentication, live Cosmos, signed-device Keychain, physical-device pilot acceptance, production API hosting or store approval. RELEASE-01 remains Blocked and strict acceptance remains 0/19 (15 In Progress, 4 Blocked).
