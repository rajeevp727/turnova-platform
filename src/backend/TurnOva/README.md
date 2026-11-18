# TurnOva backend

The backend is a .NET 10 / ASP.NET Core solution for the Cab-only Rider/Driver MVP.

## Projects

- `TurnOva.API` — HTTP API, JWT authorization, OpenAPI contract, SignalR hub, health probes and application composition.
- `TurnOva.Application` — dispatch use cases and repository contracts.
- `TurnOva.Domain` — ride, agreement, reservation, union and legacy queue invariants.
- `TurnOva.Infrastructure` — Cosmos DB repositories, migrations and deterministic in-memory adapters.
- `TurnOva.Tests` — API integration, domain, application and persistence-contract tests.

## Restore, build and test

From the repository root:

```bash
dotnet restore src/backend/TurnOva/TurnOva.sln
dotnet build src/backend/TurnOva/TurnOva.sln --configuration Release --no-restore
dotnet test src/backend/TurnOva/TurnOva.Tests/TurnOva.Tests.csproj --configuration Release
dotnet publish src/backend/TurnOva/TurnOva.API/TurnOva.API.csproj --configuration Release --no-restore --output ./artifacts/turnova-api
```

The publish is a native .NET application directory. No Dockerfile, container image, registry or container runtime is required.

## API contract

In Development and the automated Testing environment, the API exposes the generated OpenAPI document at:

`GET /openapi/v1.json`

The document describes JWT bearer requirements for authenticated Rider, Driver and UnionManager endpoints and the operations-only `X-Driver-Admin-Key` header for `/api/v1/ops/*`. The OpenAPI endpoint is not mapped in Production. See [docs/api/README.md](../../../docs/api/README.md) for the route catalogue and product/security boundaries.

## Configuration and persistence

Production startup requires:

- `Auth:Jwt:Issuer`
- `Auth:Jwt:Audience`
- `Auth:Jwt:SigningKey` (32+ characters, secret store only)
- `CosmosDb:Endpoint`
- `CosmosDb:Key` (secret store only)
- `TriSend:BaseUrl` (HTTPS)

Versioned Cosmos migrations create the database containers. In-memory stores are deterministic Development/Testing adapters only; Production refuses to start without Cosmos configuration.

The API exposes `/health/live` and `/health/ready`. Readiness performs a bounded storage dependency probe; it does not prove multi-instance, restart, provider or production-host acceptance.

## Local run

Set the required Development settings through environment variables or user secrets, then run:

```bash
dotnet run --project src/backend/TurnOva/TurnOva.API/TurnOva.API.csproj
```

Never commit JWT keys, Cosmos credentials, TriSend secrets or provider credentials. Rider payments are direct to the Driver; no payment gateway, escrow or in-app collection is part of this MVP.
