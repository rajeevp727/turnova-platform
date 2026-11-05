# TurnOva GitHub Copilot agent team

These custom agent profiles provide role-specific instructions for GitHub Copilot coding agent / agent mode. They do not create independent GitHub accounts, autonomous background workers, or grant repository permissions. Availability and execution depend on the GitHub Copilot plan, organization policies, repository settings and the user's chosen agent experience.

## Agents
- `product-owner`: outcomes, roadmap and acceptance criteria.
- `business-analyst`: journeys, requirements, business rules and edge cases.
- `solution-architect`: architecture, data, reliability and security design.
- `backend-developer`: .NET 10 API/domain/Cosmos implementation.
- `mobile-developer`: Rider/Driver React Native/Expo implementation.
- `code-reviewer`: independent review-only role.
- `qa-engineer`: test design, automation and release evidence.
- `security-reviewer`: threat modeling and security review.

## Recommended PR lifecycle
1. PO/BA define the user outcome and acceptance criteria.
2. Architect records design constraints and data/security risks.
3. Developer implements on a feature branch and adds tests/docs.
4. QA validates acceptance, regression and concurrency behavior.
5. Security reviewer examines trust boundaries and sensitive data.
6. Code reviewer performs an independent diff review.
7. Human owner reviews CI, decisions and release risks, then merges.

## Governance
- No agent merges to `main`, deploys production or changes repository protections.
- Agent profiles are instructions, not security boundaries. Enforce permissions through GitHub repository settings, branch protection/rulesets, required reviews, required status checks and Actions permissions.
- Keep parked product decisions parked. In particular, driver Priority/Normal tiers and commission policy require explicit owner approval before implementation.
- Do not treat agent output or green CI as a security certification.
