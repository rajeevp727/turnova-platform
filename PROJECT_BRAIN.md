# TurnOva Project Brain

> Compact working context for future development sessions. Read this before coding; update it in the same feature PR whenever project state changes.
> Full requirements remain in `PRD.md`, `docs/architecture.md`, and `docs/test-plan.md`.

**Last verified:** 2026-11-06  
**Repository:** https://github.com/rajeevp727/turnova-platform  
**Default branch:** `main`  
**Current main HEAD at start of this increment:** `373cdad94006c1b742d15b4a8207a8a9d50f5e20` (PR #26)

## Product in one line
Mobile-first ride-hailing platform where eligible drivers receive rides fairly through zone + vehicle-class FIFO queues.

## Stack and app scope
- Backend: .NET 10 LTS / ASP.NET Core Web API / C# 14.
- Mobile: React Native + Expo; Android and iOS.
- Data: Azure Cosmos DB; local in-memory fallbacks exist for demo/tests.
- Realtime: SignalR.
- Authentication: TurnOva API integrates with TriSend Auth; JWT + refresh-token rotation.
- MVP apps: TurnOva Driver and TurnOva Rider.
- Phase 2: TurnOva Fleet and TurnOva Ops. No driver/rider web apps.

## Current product rules
- Canonical service classes: `scooty`, `bike`, `auto`, `cab`.
- Rider selects service class; pickup coordinates resolve to a configured dispatch zone.
- Driver selects service class during onboarding; queue and union eligibility use that same value.
- Only approved drivers are eligible for queue/ride actions.
- FIFO order is server-owned within a zone + service class.
- Current API exposes actionable requested rides only to the FIFO head; accept/reject recheck head ownership.
- Rejecting at queue head moves that driver to the back.
- Union is an employer/operating association, not a dispatch queue. Driver transfers require reasons and are audited.
- Driver selfie is private until approved by the driver's current union; replacement or union transfer resets/hides approval.
- Monthly payslips are PDF only, for completed IST months; metrics are days worked, accepted, completed and explicitly rejected rides. No compensation amounts until policy is agreed.
- Driver and Rider apps support System/Light/Dark themes.

## Delivery status (verified)
- PRs #1–#19: merged; see PRD, architecture and test plan for detailed scope.
- PR #20: merged — driver onboarding aligned with Scooty/Bike/Auto/Cab.
- PR #21: merged — Copilot agent profiles and repository engineering guardrails.
- PR #22: merged — FIFO queue-head enforcement at available-rides, accept and reject API boundaries.
- PR #22 CI: GitHub Actions `TurnOva CI` completed successfully. Its PR description notes no local build/test was run by the author; CI is the validation evidence.
- PR #23: merged — added this project brain; its GitHub Actions `TurnOva CI` completed successfully.
- PR #24: merged — durable ride-offer aggregate, Cosmos/in-memory stores, migration and state/store tests. Merge commit `1c10ccdb5d191b3dd635135dedad954807a78a47`; GitHub Actions CI passed.
- PR #25: merged — server-owned offer expiry worker and idempotent FIFO recovery. Merge commit `332bd5df8e99d290db98403b08bdca8b704202ce`.
- PR #26: merged — driver Online/Offline availability and FIFO eligibility enforcement. Merge commit `373cdad94006c1b742d15b4a8207a8a9d50f5e20`.
- No open PR is being treated as approved/merged unless rechecked live.

## Highest-priority unfinished work
1. **Exclusive driver/ride reservation (P0):** PR #27 adds a durable per-driver reservation slot; ride ETags still arbitrate competing drivers for the same ride.
2. **Offer orchestration and delivery (P0):** create the next FIFO offer only after prior resolution; persist before SignalR/push delivery and fetch authoritative state after reconnect.
3. **Availability lifecycle:** Online/Offline is merged; temporary-unavailable and on-trip integration remain follow-up work.
4. **Offer notifications:** SignalR/push delivery only after durable state is written; reconnect must fetch authoritative state.
5. **Production hardening:** replace demo/in-memory boundaries, secure Ops/union-manager RBAC, audit, privacy/retention, observability and device/Cosmos integration tests.

Do not mistake PR #22 queue-head checks for a complete dispatch offer system. The timeout route is still a client acknowledgement, not a server-owned timer. Available ride/ride storage and multi-instance dispatch require further hardening.

## Known constraints and migration cautions
- Cosmos DB transactions are limited to one logical partition. Do not assume atomic writes across Rides, QueueEntries, Driver profiles or other containers.
- Ride model currently includes in-memory/demo paths; Cosmos-backed persistence exists for selected resources, not proof that all ride/dispatch state is production durable.
- Development zone `hyderabad-central` uses a placeholder bounding rectangle; replace with reviewed operational boundaries before pilot.
- Existing driver/union records with legacy `mini`, `sedan`, `suv` values need explicit migration/operations correction; never silently map them.
- Interim Ops endpoints use a backend-only admin API key. This is not production RBAC.
- Payslip work-activity append is not transactionally coupled to ride transitions; add outbox/reconciliation before production.
- No production compensation, priority tier or commission policy has been agreed.

## Parked product decision
Priority/Normal driver tiers and commission model are intentionally parked until the user's partner is available. Do not invent, implement, or reopen this policy unless the user asks.

## Working agreements
- Work in small, reviewable PRs; do not merge without explicit user approval.
- Before a new feature: read this brain, inspect current `main`, open PRs, relevant implementation and tests.
- After every feature: update this brain in the same PR with status, decisions, constraints, next step and validation evidence.
- Keep PRD/architecture/test-plan synchronized when product behaviour or architecture changes.
- Distinguish implemented, CI-validated, manually tested and production-ready; never claim one means another.
- Do not commit secrets or real personal data.
- If a decision depends on partner input, park it rather than guessing.

## Current implementation increment (merged)
Durable offer persistence foundation was introduced on `feature/durable-ride-offer-foundation`:
- One aggregate document per ride in `RideOffers` (`/rideId`), with current offer identity/deadline/status and retained attempt history.
- Create-if-absent prevents a second aggregate for the same ride.
- ETag-conditional replacement protects concurrent transitions.
- Bounded expired-offer scan and driver-pending-offer lookup are part of the store contract.
- Cosmos migration and in-memory adapter are included.
- PRD, architecture, test plan and this brain are updated in the same branch.

This stage is persistence groundwork, not end-to-end dispatch. Next: implement the server-owned offer state machine/orchestrator and expiry worker; then wire driver offer endpoints and SignalR notifications. Preserve one pending offer per ride and one pending offer per driver. Use conditional writes and explicit recovery for cross-partition ride/queue coordination.

## Server-owned expiry increment (merged in PR #25)

A hosted worker polls overdue durable offers in bounded batches in Cosmos and in-memory modes. Queue rotation is idempotent by offer ID; the Cosmos queue entry persists the last applied offer ID and uses ETag conditional replacement. The offer is marked Expired only after queue rotation succeeds. If the process stops between those operations, replay observes the marker and safely completes offer resolution. Missing queue membership leaves the offer pending for retry. This is not a cross-container transaction and the legacy ride accept/reject endpoints are not yet fenced by durable offer IDs; automatic next-driver dispatch and exclusive reservation remain future work.

## Driver availability increment (merged in PR #26)

- Driver profiles persist AvailabilityStatus (Offline by default for existing and new profiles).
- Approved drivers can go Online/Offline through authenticated driver endpoints.
- Going Online appends the driver to the back of their approved zone/service-class FIFO queue; repeating the request is idempotent.
- Going Offline removes queue membership and is blocked while the driver has an active ride.
- Queue join, available-rides, accept, reject and timeout paths require Online status.
- Driver app displays server-loaded availability and exposes Go Online/Go Offline controls.
- Cosmos driver-profile persistence uses the existing Drivers container; queue changes use the existing queue store. Cross-container writes are not atomic.
- CI is the validation gate; no local build/test is claimed.
- Next: exclusive driver reservation is in PR #27; then implement durable FIFO offer orchestration and delivery.

## FIFO offer delivery increment (in review)
- Initial ride offer is created for the current FIFO queue head in the ride-partitioned `RideOffers` store before the SignalR `RideOfferReceived` event is sent.
- Drivers can reconnect and retrieve their pending offer through `GET /api/v1/driver/offers/pending`; SignalR driver groups are joined only when the authenticated driver ID matches the requested group ID.
- Ride acceptance checks the persisted offer owner, pending state and expiry before applying the existing driver reservation and ride ETag protections.
- Offer creation and ride creation are separate Cosmos operations. A durable outbox/reconciliation workflow and automatic next-driver progression after rejection/expiry remain required before production.


## Idempotent durable offer rejection (in progress)
- When a ride has a persisted pending offer, rejection validates that the caller owns that offer and that it is still pending and unexpired.
- Queue rotation uses the offer ID as an idempotency key. If the API process stops after rotation but before the offer aggregate is marked Rejected, the driver can retry; the queue store recognizes the same offer ID and does not rotate the driver twice.
- The offer aggregate is conditionally updated with ETag protection and the matching attempt is marked Rejected.
- Legacy rides without an offer aggregate retain the previous queue-rotation behavior.
- This improves retry safety but does not yet provide automatic next-driver offer creation, durable notification outbox, or background reconciliation for a driver who never retries. Those remain production blockers.
