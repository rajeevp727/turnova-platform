# TurnOva infrastructure placeholders

This folder documents deployment inputs; it does not provision infrastructure. Replace placeholders only when the owner supplies target accounts and approved values. Never commit credentials, tokens, connection strings or private keys.

## .NET API publish artifact

The API is published as a standard .NET application directory. No Dockerfile, container image, registry or container runtime is used.

```bash
dotnet restore src/backend/TurnOva/TurnOva.API/TurnOva.API.csproj
dotnet publish src/backend/TurnOva/TurnOva.API/TurnOva.API.csproj \
  --configuration Release \
  --no-restore \
  --output ./artifacts/turnova-api
dotnet ./artifacts/turnova-api/TurnOva.API.dll
```

The hosting provider is intentionally undecided until the owner supplies infrastructure details. Select only a host that supports a native .NET 10 application deployment; do not use a container-only hosting path. The API exposes `/health/live` and `/health/ready`. These currently report process health and do not independently prove downstream provider health.

Required server-side settings:
- `ASPNETCORE_ENVIRONMENT=Production`
- `Auth__Jwt__Issuer=<TRISEND_JWT_ISSUER>`
- `Auth__Jwt__Audience=<TRISEND_JWT_AUDIENCE>`
- `Auth__Jwt__SigningKey=<TRISEND_SHARED_SIGNING_KEY>` (secret store only; minimum 32 characters)
- `CosmosDb__Endpoint=<COSMOS_DB_ENDPOINT>`
- `CosmosDb__Key=<COSMOS_DB_KEY>` (secret store only)
- `CosmosDb__DatabaseName=TurnOvaDb`
- `TriSend__BaseUrl=<TRISEND_AUTH_BASE_URL>` (HTTPS)

Production startup rejects missing Cosmos and JWT configuration. Do not enable in-memory persistence in production.

## Cosmos DB

| Setting | Placeholder |
|---|---|
| Account endpoint | `<COSMOS_DB_ENDPOINT>` |
| Account key | `<COSMOS_DB_KEY>` (secret store only) |
| Database | `TurnOvaDb` |
| Region | `<COSMOS_DB_REGION>` |
| Containers | Created/managed by versioned migrations; verify partition keys and indexing before production |

Do not manually create containers unless migration ownership and schema are deliberately changed. Use least-privilege database permissions.

## Mobile API URL

Set `EXPO_PUBLIC_API_BASE_URL` independently for Rider and Driver build profiles:

- Development Android emulator: `http://10.0.2.2:5000`; physical devices need the host LAN URL.
- Preview: `https://<PREVIEW_API_HOST>`
- Production: `https://<PRODUCTION_API_HOST>`

This value is public and embedded in the app bundle. Never put signing keys, Cosmos credentials or server secrets in Expo public variables.

## Azure Static Web Apps

The MVP UI consists of native Expo Android/iOS applications, not browser SPAs. SWA cannot host or distribute native app binaries and is not a substitute for the .NET API host. Do not create a fake SWA deployment for mobile UI. A static companion site is outside the current MVP scope.

## External provider placeholders

- TriSend auth: `<TRISEND_AUTH_BASE_URL>` and matching issuer/audience/signing key.
- Maps/geocoding: `<MAPS_PROVIDER>` / `<MAPS_API_KEY>` (restricted platform keys only).
- Push notifications: `<PUSH_PROVIDER>` / credentials stored server-side.
- Payments: `<PAYMENT_PROVIDER>` / sandbox credentials first; provider selection and legal/commercial onboarding remain explicit owner decisions.

## Post-coding infrastructure gates

1. Select a native .NET 10 hosting provider and configure secrets.
2. Provision Cosmos and confirm migration/partition/index behaviour.
3. Configure Expo EAS project/build credentials and app signing.
4. Run Rider/Driver builds and test on physical Android and iOS devices.
5. Configure only the external providers required by the MVP and run their sandbox checks.
