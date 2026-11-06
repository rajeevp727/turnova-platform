# TurnOva infrastructure placeholders

This folder contains deployment templates, not provisioned infrastructure. Replace placeholders only when the owner supplies target accounts and approved values. Never commit credentials, tokens, connection strings or private keys.

## Render API

- Blueprint: `render.yaml`
- Dockerfile: `infrastructure/render/Dockerfile`
- Service name: `turnova-api` (placeholder)
- Region/plan: Singapore / Starter (provisional; confirm latency, availability and pricing)
- Health probe: `/health/ready`
- Required secret environment variables: `CosmosDb__Endpoint`, `CosmosDb__Key`, `Auth__Jwt__Issuer`, `Auth__Jwt__Audience`, `Auth__Jwt__SigningKey`
- Supply the real HTTPS TriSend auth base URL through `TriSend__BaseUrl`.
- Production startup rejects missing Cosmos configuration. JWT configuration is also validated at startup.

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

The MVP UI consists of native Expo Android/iOS applications, not browser SPAs. SWA cannot host or distribute native app binaries. Do not create a fake SWA deployment for mobile UI. If a static companion site or public API documentation site is approved later, its placeholders are `<SWA_RESOURCE_NAME>`, `<SWA_APP_LOCATION>`, `<SWA_OUTPUT_LOCATION>` and `<SWA_DEPLOYMENT_TOKEN>`; keep the deployment token in GitHub Secrets.

## External providers

- TriSend auth: `<TRISEND_AUTH_BASE_URL>` and matching issuer/audience/signing key.
- Maps/geocoding: `<MAPS_PROVIDER>` / `<MAPS_API_KEY>` (restricted platform keys only).
- Push notifications: `<PUSH_PROVIDER>` / credentials stored server-side.
- Payments: `<PAYMENT_PROVIDER>` / sandbox credentials first; provider selection and legal/commercial onboarding remain explicit decisions.

## Release gates

1. Provision Cosmos and confirm migration/partition/index behaviour.
2. Configure Render secrets and verify live/ready probes.
3. Configure Expo EAS project/build credentials and app signing.
4. Run Rider/Driver builds and test on physical Android and iOS devices.
5. Configure approved external providers and run sandbox-to-live checklists.
