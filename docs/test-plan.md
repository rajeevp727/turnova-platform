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
| AUTH-LOGIN-014 | App restart with valid tokens | Kill and relaunch app | Session restored using SecureStore and `/me`; no login prompt | Mobile | P0 |
| AUTH-LOGIN-015 | App restart with invalid access but valid refresh | Store expired access token and valid refresh token, relaunch | Refresh succeeds and rotated tokens are stored | Mobile | P0 |
| AUTH-LOGIN-016 | Concurrent refresh attempts | Trigger multiple 401 responses together | No stale refresh token overwrites newer token; one refresh flow is serialized or safely coordinated | Mobile | P0 |
| AUTH-LOGIN-017 | Logout succeeds | Sign out while online | TriSend session revoked; local tokens deleted; sign-in screen shown | API + mobile | P0 |
| AUTH-LOGIN-018 | Logout while offline | Sign out with network unavailable | Local tokens are deleted; app signs out; server revocation may remain pending until token/session expiry | Mobile | P1 |
| AUTH-LOGIN-019 | Secure storage failure | Simulate SecureStore read/write failure | User sees recoverable error; app does not silently claim authenticated state | Mobile | P1 |
| AUTH-LOGIN-020 | Session limit reached | Sign in when TriSend's session limit is reached; test both replaceOldest false/true | TriSend policy is respected; no unexpected session is evicted | API + mobile | P1 |
| AUTH-LOGIN-021 | Concurrent unauthorized API responses | Trigger multiple protected API requests that all receive 401 while refresh-token rotation is enabled | Both apps share one in-flight refresh operation, persist the rotated token pair once, and retry each original request with the new access token | Mobile | P0 |

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

Payslip output is PDF only. Monetary calculations are intentionally absent until the compensation model is approved. Cosmos persistence, migration and crash-window reconciliation require validation with a development Cosmos account.

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
| AVAIL-002 | Approved driver goes online | Availability is persisted as Online and driver joins the back of the configured zone/service-class FIFO queue | API | P0 |
| AVAIL-003 | Driver repeats online request | HTTP 200; no duplicate queue membership or position change | API | P0 |
| AVAIL-004 | Driver goes offline | Availability is persisted as Offline and queue membership is removed | API | P0 |
| AVAIL-005 | Offline driver attempts queue join | HTTP 409; no queue entry is created | API/security | P0 |
| AVAIL-006 | Offline driver requests available rides or accepts/rejects | Request is forbidden; offline driver cannot dispatch | API/security | P0 |
| AVAIL-007 | Driver goes offline with active ride | HTTP 409; availability and queue state remain unchanged | API | P0 |
| AVAIL-008 | Driver comes online after offline period | Driver rejoins at the back of the FIFO queue | API | P0 |
| AVAIL-009 | Driver app restarts | Availability is loaded from the server profile, not assumed from local UI state | Mobile/API | P1 |
| AVAIL-010 | Cosmos mode availability change | Availability is persisted in the Drivers profile document and queue changes use the configured queue store | Cosmos integration | P0 |

Online/offline availability is server-authoritative. Mobile controls are presentation only; dispatch and queue APIs enforce the persisted state. Cosmos cross-container writes are not transactional and require operational reconciliation before production.

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

In-memory store concurrency tests are included in CI. Multi-instance Cosmos races and crash-window recovery require a real development Cosmos account and are not claimed as executed by CI unless separately configured.

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

## 12. Nearby available vehicles and online driver location

| ID | Scenario | Expected result | Type | Priority |
|---|---|---|---|---|
| NEARBY-001 | Driver goes online without GPS coordinates or with coordinates outside the assigned zone | HTTP 400/409; driver remains Offline and is not added to the queue | API integration | P0 |
| NEARBY-002 | Rider searches for nearby vehicles | Only approved, online drivers with active queue membership in the pickup zone, matching service class and sufficient passenger capacity are returned, ordered by distance | API integration | P0 |
| NEARBY-003 | Nearby search privacy and stale location | Response omits driver ID, name, phone, registration and exact coordinates; locations older than the configured freshness window are excluded | API integration + security | P0 |
| NEARBY-004 | Driver leaves assigned zone while online | Driver is taken Offline, queue membership is removed and current location is cleared | API integration | P0 |
| NEARBY-005 | Driver app is online in foreground or an active ride begins | Foreground availability updates run only while Online, pause during Accepted/Arrived/InProgress rides and stop when Offline or signed out; active-trip location uses the trip tracking endpoint | Android/iOS functional | P0 |
| NEARBY-006 | Rider searches without location permission or outside configured zones | Clear recoverable permission/zone error; no nearby driver data is returned | Android/iOS + API | P0 |
| NEARBY-007 | Driver submits a location update with missing coordinates or has an active ride | HTTP 400 for missing coordinates; HTTP 409 during an active ride without changing Online state or trip location | API integration | P0 |

The API integration tests use the in-memory store and configured test zone. They do not establish Cosmos cross-partition query behaviour or physical-device permission/resume behaviour. Driver GPS is foreground-only and is not intended as background tracking.

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
| PLATFORM-AUDIT-004 | Queue join conflicts, missing membership removal, stale ETag or duplicate offer rotation occurs | No audit event is written for a failed/no-op state mutation; retrying the same offer does not duplicate its audit event | Unit + Cosmos failure injection | P0 |
| PLATFORM-AUDIT-005 | Audit events are queried for a zone/service-class queue | Only events for that partition are returned, newest first; payload contains no phone/email, exact location or fare | Unit + Cosmos integration/security | P0 |
| PLATFORM-LOG-001 | An unhandled API exception occurs | API returns a generic 500 Problem Details response with traceId; structured error log records traceId and exception type only, not request body, token, exception message or PII | API integration/security | P0 |
| PLATFORM-AUDIT-006 | A business-relevant dispatch exception occurs while processing an authenticated action | A durable exception audit event records traceId, actor reference, safe exception category/reason and UTC timestamp without request body, token, contact data or exact location | Cosmos integration/security | P0 |

The Cosmos implementation writes queue state and its audit event in one transactional batch within the existing `QueueEntries` container partition (`zoneServiceKey`). In-memory tests validate event semantics; real Cosmos transactional-batch atomicity, ETag conflict, process-restart and audit retention must be verified against the configured development Cosmos account.

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
| CAB-REC-001 | Agreed 500, reported 400 | Delta +100, driver-reported/unverified shortfall | Unit/API | P0 |
| CAB-REC-002 | Agreed 500, reported 500 | Delta 0 | Unit/API | P0 |
| CAB-REC-003 | Agreed 400, reported 500 | Delta -100 | Unit/API | P0 |
| CAB-REC-004 | Decimal/currency precision and invalid amount | Currency-safe validation/rounding | Unit/API | P0 |
| CAB-REC-005 | Unrelated union/driver requests reconciliation | Forbidden; authorised minimum data only | Security | P0 |
| CAB-REC-006 | Driver submits amount | Actor/time/status audited; not customer/bank verified | API | P0 |
| CAB-UNION-001 | Driver transferred unions | Prior selfie approval invalidated until new union approval | API/privacy | P0 |

Do not test or implement platform fare estimates, in-app payment collection, gateway/escrow or customer-facing FIFO ranking/automatic assignment as MVP behaviour. Dispute and non-zero-delta next-ride outcomes remain pending product decisions.


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
| CAB-DISC-006 | Rider discovers with multiple eligible unions | Only Active unions serving Cab in the pickup zone are returned; drivers are grouped by their current Active union assignment | RiderDiscoveryGroupsApprovedUnionsCapsThreeAndOrdersByPickupProximityWithoutQueueDependency | CI run 42237256233 passed all four jobs |
| CAB-DISC-007 | A union has more than three eligible drivers | At most three drivers are returned for that union | Same API integration test; deterministic pickup-distance order | CI run 42237256233 passed all four jobs |
| CAB-DISC-008 | Driver has pending/rejected/old-union selfie approval | Driver may remain eligible, but public photo is null unless approved by the current union | Same API integration test | CI run 42237256233 passed all four jobs |
| CAB-DISC-009 | Driver is stale, outside radius, under-capacity, unapproved, non-Cab, unassigned, or union inactive | Driver is excluded | Same API integration test | CI run 42237256233 passed all four jobs |
| CAB-DISC-010 | Driver has an active reservation | Driver is excluded from Rider discovery | RiderDiscoveryExcludesReservedDriversAndLegacyFifoRiderRoutesAreRetired | CI run 42237256233 passed all four jobs |
| CAB-DISC-011 | Driver is Online but has no FIFO queue membership | Driver can still appear when all union/profile eligibility rules pass | Discovery fixture seeds Online profiles directly without queue entries | CI run 42237256233 passed all four jobs |
| CAB-DISC-012 | Discovery response privacy | No phone, full registration or exact driver coordinates; only approved current-union selfie is returned | API integration assertions on serialized response | CI run 42237256233 passed all four jobs |
| CAB-DISC-013 | Nearby radius is absent/zero | API returns 503; no implicit radius fallback | RiderDiscoveryFailsClosedWhenNearbyRadiusIsNotConfigured | CI run 42237256233 passed all four jobs |
| CAB-DISC-014 | Legacy customer-facing FIFO discovery/request | Nearby endpoint returns 410; legacy ride creation is disabled by default | RiderDiscoveryExcludesReservedDriversAndLegacyFifoRiderRoutesAreRetired; CustomerFacingFifoRideCreationIsDisabledOutsideTestCompatibilityMode | CI run 42237256233 passed all four jobs |
| CAB-DISC-015 | A Driver or unauthenticated caller requests Rider discovery | Driver role is forbidden and unauthenticated requests are unauthorized; only Rider role may call discovery | RiderDiscoveryGroupsApprovedUnionsCapsThreeAndOrdersByPickupProximityWithoutQueueDependency (Driver and anonymous assertions) | CI run 42237256233 passed all four jobs |

Mobile acceptance still requires Android/iOS device checks for union grouping, selected-card state, approved-photo rendering, location permission denial, no-provider call/chat messaging and the distinct unavailable journey-distance label. The discovery selection is local to the screen in this increment; durable selected-driver reservation/negotiation is a subsequent acceptance slice. Live maps/routing, production radius approval and Cosmos multi-instance query/concurrency validation remain external gates.



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

The first increment includes the API and Rider/Driver negotiation controls for rides that already have an assigned Driver. It does not yet connect those controls to the selected-driver booking flow; that dependency is sequenced under RIDER-03. Optional stop persistence, Android/iOS device acceptance, live Cosmos ETag/multi-instance contention, and production data recovery remain open. The tests above are CI evidence only when the PR's final head has passed its required workflow.
