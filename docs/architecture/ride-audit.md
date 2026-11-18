# Ride audit trail — MVP architecture

## Storage and atomicity

- Ride audit facts are stored as an optional append-only `AuditEvents` list on the existing `DemoRide` aggregate in the existing Rides container.
- The Rides partition key and document identity do not change. Existing ride documents without `AuditEvents` remain compatible; the append helper treats a missing/null list as empty and persists the first event with the next audited mutation. No destructive migration or new container is required.
- The initial `RideCreated`/`RideRequested` event is written with the initial ride document. Subsequent events are appended in the same `TryReplaceRideAsync` write as the state/agreement/reconciliation mutation. Cosmos ETag replacement therefore commits each subsequent mutation and its event together within the existing ride partition.
- Snapshot comparison includes the event list. A prefix guard rejects any update that removes, reorders or rewrites an existing event. The in-memory fixture path uses the same append-only guard.
- Per-Driver reservation events are separate documents in the existing `DriverRideReservations` container, partitioned by `/driverId`. Each reservation create, ETag-protected replacement or ETag-protected delete is paired with an audit-event create in one same-partition transactional batch. A failed batch commits neither operation; deleting the active reservation leaves its audit documents intact. No new container or partition-key migration is introduced.
- The in-memory reservation store keeps the active lease and its immutable audit-event sequence in one compare-and-swap state value, so the deterministic test implementation cannot mutate the lease without appending the corresponding event.

## Event contract

The MVP records only the facts needed to reconstruct the Rider/Driver ride and direct-payment lifecycle:

- `RideCreated` / legacy `RideRequested`
- `AgreementProposed`
- `AgreementPartyConfirmed` (one event for each party's first confirmation of a version)
- `RideStatusChanged` (accept, arrive, start, complete, cancel, decline and system expiry)
- `PaymentReportedUnverified`
- `ReservationAcquired`, `ReservationMadePermanent`, `ReservationExpiryChanged` and `ReservationReleased` for selected ride leases and short-lived technical Driver-operation locks.

Ride events include event ID, ride/Union linkage, actor ID and role, UTC timestamp, previous/new status, and agreement version/amount/currency/received amount/delta only when applicable. Reservation events include event ID, Driver ID, opaque reservation ID, fixed event type, allow-listed actor role/reason, safe actor reference, UTC timestamp and previous/next lease expiry. Contact-like actor references are reduced to the system actor; free-text reason categories are never persisted. Idempotent reservation retries and no-op state requests do not append duplicate events.

## Privacy and authorization

- Ride audit events never contain pickup/drop-off/stops, precise coordinates, phone/email, KYC/document references, access tokens or private chat content. Reservation audit events contain no route, location, contact, KYC, token, fare or chat fields.
- Existing ride participant responses remain participant-scoped. Driver history continues to expose only the Driver's own report. The UnionManager reconciliation view remains signed-`unionId` scoped and returns only its existing minimal reconciliation fields; it is not expanded into a general audit dump.
- Cross-Union disclosure of a preceding ride's reconciliation to a new Union for next-ride eligibility remains an unresolved product decision. This increment does not infer or implement that policy.
- No new admin role, general-purpose audit endpoint or Phase 2 Ops surface is introduced.

## Verification and external gates

Automated API tests cover event order and actor identity for the selected Cab lifecycle, agreement proposal/confirmation, cancellation, Driver decline, system expiry and unverified payment reporting. Reservation-store tests cover concurrent acquire, idempotent same-ride retries, expiry updates, permanent promotion, matching-only release, event preservation after release and metadata minimisation. These deterministic tests exercise the in-memory state machine; they do not prove live Cosmos transactional-batch/ETag visibility, partition isolation, multi-instance/restart recovery or retention. Those, plus production telemetry/alerts, production CORS origins, load testing and physical-device acceptance, remain PLATFORM-01 launch gates.
