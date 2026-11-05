---
applyTo: "src/backend/**/*.cs"
---

# Backend-specific instructions
- Follow .NET 10 / C# 14 and existing nullable/formatting conventions.
- Keep Domain free of API, Cosmos SDK and provider dependencies.
- Keep use cases in Application; persistence/provider implementations in Infrastructure; transport concerns in API.
- Enforce authentication, authorization, actor ownership and server-side validation at the API boundary and use-case boundary as appropriate.
- Cosmos DB partition keys define transaction scope. Use ETags and idempotency for concurrent writes; do not assume cross-partition transactions.
- Add tests for validation, authorization, idempotency, concurrency and failure paths.
- Avoid sensitive data in logs and exception messages. Never commit secrets.
- Document migrations, API contract changes and operational limitations.
