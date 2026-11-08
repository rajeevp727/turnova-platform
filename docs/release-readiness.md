# TurnOva Rider/Driver release readiness

**Last audited:** 2026-11-09  
**Scope:** Rider and Driver Android/iOS MVP only. Fleet/Ops Phase 2 is excluded.

## Engineering preflight

\`scripts/release-preflight.cjs\` validates each app's checked-in \`app.json\` and \`eas.json\` after \`npm install\` has run the icon-generation postinstall hook. It checks:

- Rider/Driver app identity, semantic version and established Android package ID.
- iOS device-family setting and Android adaptive-icon background.
- Generated icon files exist and remain inside the app directory.
- EAS CLI minimum and development, preview and production profile shapes.
- iOS bundle identifier and EAS project ID syntax when supplied.
- Explicit production build-number auto-increment configuration.

The mobile CI jobs upload one machine-readable \`release-readiness.json\` report per app. Structural configuration errors fail CI. Missing product/release credentials and physical/store acceptance are reported as external gates; they are not silently treated as passed or used to block ordinary engineering CI.

## Current checked-in configuration

| Item | Rider | Driver |
|---|---|---|
| App version | \`0.1.0\` | \`0.1.0\` |
| Android package | \`com.turnova.rider\` | \`com.turnova.driver\` |
| iOS bundle identifier | Not configured | Not configured |
| EAS project ID | Not configured | Not configured |
| Production Android profile | App Bundle | App Bundle |
| Production iOS profile | Store distribution | Store distribution |
| Production auto-increment | Not configured | Not configured |

The current CI artifacts are Android debug APKs and unsigned iOS Simulator \`.app\` bundles. They prove native compilation only. They are not signed production AAB/IPA files and do not prove install/runtime behaviour on physical devices.

## External gates and owners

| Gate | Owner | Required input/evidence |
|---|---|---|
| Final iOS bundle identifiers | Product / App Owner | Approved reverse-DNS identifiers registered with Apple for Rider and Driver |
| EAS project linkage | Release Engineering | Approved EAS project IDs linked to both apps |
| Build-number strategy | Release Engineering | Approved local/remote version source and automatic increment policy |
| Android signing | Release Owner | Upload keystore and signing configuration stored in EAS secure credentials, never in Git |
| Apple signing | Release Owner | Distribution certificate and provisioning profiles stored in EAS secure credentials, never in Git |
| Physical-device acceptance | QA / Product Owner | Signed builds installed on agreed Android/iOS devices; functional, security, permissions, GPS, SignalR, negotiation and reconciliation checks recorded |
| Store submission | Release Owner / Legal | Store metadata, privacy disclosures, review submission and approval for both apps |

## Current decision

\`RELEASE-01\` remains **Blocked**. The preflight is engineering evidence and a guard against configuration drift; it does not replace signed production artifacts, physical-device testing, pilot acceptance or store approval.

No signing secret, bundle identifier, EAS project ID, provider credential or deployment target is invented or committed. No Docker/container build or deployment is used.
