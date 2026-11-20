# TurnOva Rider and Driver demo build

This branch adds the API-backed demo entry points to both mobile apps.

- Rider: `POST /api/v1/auth/demo/rider`
- Driver: `POST /api/v1/auth/demo/driver`

The API host must set `DemoAuth:Enabled=true` and valid JWT issuer, audience and signing key configuration. Demo sessions are short-lived and must be used only on an isolated demo deployment. Demo tokens cannot access normal authenticated application routes.

Set `EXPO_PUBLIC_API_BASE_URL` to the reachable HTTPS API base URL when building for physical Android devices. Do not embed API credentials or signing secrets in the mobile apps.

The Android CI workflow builds both Rider and Driver debug/release APKs and runs emulator install/launch smoke tests. A green build does not prove that a deployed API or Cosmos DB is reachable from a physical device.
