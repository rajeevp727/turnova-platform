# Ride audit trail — MVP architecture

## Storage and atomicity

- Ride audit facts are stored as an optional append-only `AuditEvents` list on the existing `DemoRide` aggregate in the existing Rides container.
- The Rides partition key and document identity do not change. Existing ride documents without `AuditEvents` deserialize as an empty history on the next audited mutation; no destructive migration or new container is required.
- The initial `RideCreated`/`RideRequested` event is written with the initial ride document. Subsequent events are appended in the same `TryReplaceRideAsync` write as the state/agreement/reconciliation mutation. Cosmos ETag replacement therefore commits each subsequent mutation and its event together within the existing ride partition.
- Snapshot comparison includes the event list. A prefix guard rejects any update that removes, reorders or rewrites an existing event. The in-memory fixture path uses the same append-only guard.

## Event contract

The MVP records only the facts needed to reconstruct the Rider/Driver ride and direct-payment lifecycle:

- `RideCreated` / legacy `RideRequested`
- `AgreementProposed`
- `AgreementPartyConfirmed` (one event for each party's first confirmation of a version)
- `RideStatusChanged` (accept, arrive, start, complete, cancel, decline and system expiry)
- `PaymentReportedUnverified`

Each event includes an event ID, ride/Union linkage, actor ID and role, UTC timestamp, previous/new status, and agreement version/amount/currency/received amount/delta only when applicable. Idempotent retries return the existing state/report and do not append duplicate events.

## Privacy and authorization

- Audit events never contain pickup/drop-off/stops, precise coordinates, phone/email, KYC/document references, access tokens or private chat content.
- Existing ride participant responses remain participant-scoped. Driver history continues to expose only the Driver's own report. The UnionManager reconciliation view remains signed-`unionId` scoped and returns only its existing minimal reconciliation fields; it is not expanded into a general audit dump.
- Cross-Union disclosure of a preceding ride's reconciliation to a new Union for next-ride eligibility remains an unresolved product decision. This increment does not infer or implement that policy.
- No new admin role, general-purpose audit endpoint or Phase 2 Ops surface is introduced.

## Verification and external gates

Automated API tests cover event order and actor identity for the selected Cab lifecycle, agreement proposal/confirmation, cancellation, Driver decline, system expiry and unverified payment reporting; they also cover retry idempotency, preservation during non-audit location updates and omission of sensitive route/contact data. These deterministic tests do not prove live Cosmos multi-instance ETag conflict handling, restart recovery or retention. Those, plus production telemetry/alerts, origin configuration and load testing, remain PLATFORM-01 launch gates.
