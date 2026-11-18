---
applyTo: "**/*Tests*/**/*,**/*.test.*,**/*.spec.*"
---

# Testing-specific instructions
- Test observable behavior and business invariants, not private implementation details.
- Include positive, negative, boundary, authorization, idempotency and concurrency scenarios where relevant.
- Keep tests deterministic; inject clocks, IDs and external dependencies rather than relying on sleeps or live services.
- Isolate test data and clean up safely. Never point tests at production resources.
- A mock does not prove Cosmos DB partition, ETag or transaction semantics; use integration tests for those guarantees.
- Report commands and results accurately. Distinguish skipped, flaky, failed and passed tests.
