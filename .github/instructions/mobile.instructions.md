---
applyTo: "src/mobile/**/*.ts,src/mobile/**/*.tsx"
---

# Mobile-specific instructions
- Use the existing React Native + Expo architecture and TypeScript conventions.
- Treat API responses as untrusted and backend state as authoritative.
- Keep credentials in approved secure storage; never log tokens or sensitive user data.
- Request camera/location permissions only when needed and explain the purpose.
- Provide loading, empty, error, retry, offline and reconnect states.
- Preserve accessible touch targets, readable contrast, screen-reader labels and light/dark theme behavior.
- Keep canonical vehicle categories aligned with the API: scooty, bike, auto, cab.
- Run Expo config validation, typecheck and relevant tests; do not claim physical-device validation unless performed.
