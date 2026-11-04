# TurnOva backend

Initial .NET 10 solution foundation. All projects target net10.0.

## Projects

- TurnOva.API — ASP.NET Core endpoints and health check.
- TurnOva.Application — queue use cases and storage contracts.
- TurnOva.Domain — FIFO queue model and invariants.
- TurnOva.Infrastructure — temporary in-memory queue adapter.
- TurnOva.Tests — domain tests.

## Run

Install the .NET 10 SDK, then from this directory:

    dotnet restore
    dotnet run --project TurnOva.API

Call /health to verify the API is running.

## Important

The in-memory queue is for local development only. It is not durable, is not shared between API instances, and is not suitable for production. Replace it with a Cosmos DB-backed implementation and enforce atomic queue/assignment operations before connecting real drivers.
