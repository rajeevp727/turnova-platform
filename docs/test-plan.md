# TurnOva Feature Test Plan

**Scope:** TurnOva MVP foundation and Feature 2 (TriSend email/password authentication).
**Test types:** automated unit/CI, API integration, mobile functional, security, and manual acceptance.
**Priority:** P0 = release blocker, P1 = high, P2 = normal.
**Status convention:** Planned means the case is specified but has not yet been executed. A green build/type-check is not evidence that a functional scenario passed.

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
| AUTH-LOGIN-014 | App restart with valid tokens | Kill and relaunch app | Session restored using SecureStore and `/me`; no login prompt | Mobile | P0 |
| AUTH-LOGIN-015 | App restart with invalid access but valid refresh | Store expired access token and valid refresh token, relaunch | Refresh succeeds and rotated tokens are stored | Mobile | P0 |
| AUTH-LOGIN-016 | Concurrent refresh attempts | Trigger multiple 401 responses together | No stale refresh token overwrites newer token; one refresh flow is serialized or safely coordinated | Mobile | P0 |
| AUTH-LOGIN-017 | Logout succeeds | Sign out while online | TriSend session revoked; local tokens deleted; sign-in screen shown | API + mobile | P0 |
| AUTH-LOGIN-018 | Logout while offline | Sign out with network unavailable | Local tokens are deleted; app signs out; server revocation may remain pending until token/session expiry | Mobile | P1 |
| AUTH-LOGIN-019 | Secure storage failure | Simulate SecureStore read/write failure | User sees recoverable error; app does not silently claim authenticated state | Mobile | P1 |
| AUTH-LOGIN-020 | Session limit reached | Sign in when TriSend's session limit is reached; test both replaceOldest false/true | TriSend policy is respected; no unexpected session is evicted | API + mobile | P1 |

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
| RID-IDEM-001 | Repeat request with same rider and Idempotency-Key | Original ride is returned; no second ride is created | API + Cosmos | P0 |
| RID-IDEM-002 | Same key used by different riders | Separate ride requests are allowed | API + Cosmos | P1 |
| RID-IDEM-003 | Missing or oversized idempotency key | HTTP 400; no ride is created | API | P0 |
| TRACK-007 | Assigned Driver posts location during active ride | Latest location is broadcast to the owning Rider's SignalR group | API + SignalR | P0 |
| TRACK-008 | Driver posts location after completion/cancellation | HTTP 404/409; no location update or broadcast | API security | P0 |
| TRACK-009 | Rider SignalR connection uses another Rider's ride ID | Connection is aborted before group membership | SignalR security | P0 |
| TRACK-010 | Rider reconnects after network loss | SignalR reconnects and subsequent location/status events resume | Mobile | P1 |
| TRACK-011 | Driver leaves Trips screen | Foreground GPS subscription is disposed | Mobile | P0 |

The lifecycle state-machine unit tests and in-memory API integration tests are automated. API integration coverage includes Rider booking/idempotency, role restrictions, ownership, lifecycle transitions, cancellation rules, terminal location-sharing shutdown, and concurrent Driver acceptance. Cosmos-specific concurrency, SignalR WebSocket authorization/reconnect, and physical-device scenarios still require their corresponding integration/device environments.
