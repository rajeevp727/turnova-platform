# TurnOva Project Brain

> Compact working context for future development sessions. Read this before coding; update it in the same feature PR whenever project state changes.
> Full requirements remain in `PRD.md`, `docs/architecture.md`, and `docs/test-plan.md`.

**Last verified:** 2026-11-06  
**Repository:** https://github.com/rajeevp727/turnova-platform  
**Default branch:** `main`  
**Current main HEAD:** `89854a7f088b131363c2ff44e71e0d29aa974152` (PR #22)

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
- No open PR is being treated as approved/merged unless rechecked live.

## Highest-priority unfinished work
1. **Durable server-controlled dispatch offers (P0):** persistent offer identity, one active offer per ride, expiry deadline, server-owned timeout worker, automatic retry/rotation, and recovery after process restart.
2. **Exclusive assignment/concurrency:** prevent one driver taking multiple active rides and one ride being assigned twice; use Cosmos ETags/partition-aware coordination.
3. **Driver availability:** explicit online/offline/temporarily-unavailable state must participate in eligibility.
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

## Next recommended increment
Design and implement durable server-owned ride offers. First inspect current ride and queue stores, Cosmos container partition keys, DI and test seams. Prefer an explicit offer state machine and concurrency design before editing endpoints. Ensure only the current driver's active offer is actionable; do not broadcast all requested rides to all drivers.
