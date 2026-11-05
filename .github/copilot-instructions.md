# TurnOva engineering instructions

## Product and source of truth
- Read `PRD.md`, `README.md`, `docs/architecture.md`, and relevant feature documentation before changing behavior.
- The approved product scope and explicit decisions in repository documentation are authoritative. If docs conflict, stop and raise the conflict; do not invent policy.
- TurnOva MVP: Android/iOS Rider and Driver apps, shared .NET 10 API, Cosmos DB, SignalR, JWT authentication, zone/service-class FIFO dispatch.
- Canonical vehicle classes are `scooty`, `bike`, `auto`, `cab`. Do not reintroduce legacy values without a documented migration.
- Priority/Normal driver tiers and commission/compensation policy are intentionally parked pending product-owner/partner discussion. Do not implement or assume these rules.

## Architecture boundaries
- Backend targets .NET 10 / C# 14. Mobile apps use React Native + Expo. Persisted data uses Azure Cosmos DB.
- Keep Domain independent of API, Cosmos SDK, UI and provider SDKs. Keep Application use cases independent of transport and persistence implementations.
- API is an adapter: authentication/authorization, request validation, composition and transport. Infrastructure owns Cosmos and external provider adapters.
- Server is authoritative for identity, eligibility, queue order, dispatch, offer expiry, ride assignment, money and approvals. Never trust client-calculated permissions, prices, queue positions or statuses.
- Cosmos DB transactions are limited to one logical partition. Never claim cross-partition atomicity. Use ETags, idempotency, partition-aware design, bounded retries and reconciliation/outbox patterns where required.
- Do not put secrets, tokens, private keys, personal data samples or production connection strings in source, logs, fixtures or PR descriptions.
- Base64 selfies and identity data are sensitive. Minimize exposure, authorize every read, suppress unapproved images, and avoid logging payloads.

## Delivery workflow
- Work on a dedicated feature/chore branch. Never commit directly to `main`.
- Open a focused PR with rationale, scope, API/data changes, security considerations, tests and known limitations.
- Never merge, push directly to main, change branch protections, deploy production, run destructive migrations, or alter production data without explicit human approval.
- Do not mark a task complete merely because code was written. Report exact commands run and actual outcomes. Distinguish not run, failed, skipped and passed.
- Update PRD/architecture/test plan when behavior, data model, APIs, threat boundaries or acceptance criteria change.
- Prefer small, reviewable changes. Avoid unrelated refactors and dependency upgrades.
- Never invent test evidence, CI status, reviewer approval, deployment status or runtime behavior.

## Quality gates
- Add tests for positive, negative, authorization, validation, concurrency, idempotency, expiry/retry and failure paths as applicable.
- Backend: restore, Release build, full test suite; add Cosmos integration tests for persistence/ETag/partition behavior where possible.
- Mobile: Expo config validation, TypeScript typecheck, and relevant component/flow tests. Do not claim device testing unless performed.
- Security: deny by default, least privilege, server-side validation, ownership checks, safe errors, rate limiting for sensitive endpoints, secure secret handling and privacy-aware telemetry.
- Review migrations for repeatability, backward compatibility, rollout/rollback and production data impact.
- Flag demo/in-memory fallbacks and incomplete production controls clearly. Never describe demo behavior as production-ready.

## Agent collaboration
- PO/BA define outcomes and acceptance criteria; Architect checks design and risks; developers implement; QA and Security independently validate; Reviewer reviews the diff.
- Agents may hand off findings and propose changes but must not approve their own work or bypass independent review.
- When requirements are ambiguous or affect fare, commission, driver priority, safety, legal/compliance or personal-data policy, ask the human product owner before implementation.
