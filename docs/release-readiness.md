# TurnOva Rider/Driver release readiness

**Last audited:** 2026-11-09  
**Scope:** Rider and Driver Android/iOS MVP only. Fleet/Ops Phase 2 is excluded.

## Engineering preflight

`scripts/release-preflight.cjs` validates each app's checked-in `app.json` and `eas.json` after `npm install` has run the icon-generation postinstall hook. It checks:

- Rider/Driver app identity, semantic version and established Android package ID.
- iOS device-family setting and Android adaptive-icon background.
- Generated icon files exist and remain inside the app directory.
- EAS CLI minimum and development, preview and production profile shapes.
- iOS bundle identifier and EAS project ID syntax when supplied.
- Explicit `cli.appVersionSource` (`local` or `remote`) plus production build-number auto-increment configuration.
- A CI-visible HTTPS API URL syntax check that rejects private/reserved IP literals and known internal hostnames; the URL itself is not copied into the report. DNS resolution and EAS remote environment values are not inspected.

The mobile CI jobs upload one machine-readable `release-readiness.json` report per app. The API check reads only `EXPO_PUBLIC_API_BASE_URL` available to that GitHub job; it does not query EAS's remote environment store or resolve DNS from EAS. A missing value therefore means the production URL is not verified by this CI run, not proof that an EAS remote value is absent. A syntactically valid URL passes only the CI-visible check; EAS remote configuration, public DNS, TLS, production CORS and health/readiness remain external. A valid iOS bundle-ID string is not proof of Apple registration, and a UUID-shaped EAS project ID is not proof that the app is linked to the approved remote project. Those gates remain in every report until their owners verify them. Structural configuration errors fail CI. Missing product/release credentials and physical/store acceptance are reported as external gates; they are not silently treated as passed or used to block ordinary engineering CI.

## Current checked-in configuration

| Item | Rider | Driver |
|---|---|---|
| App version | `0.1.0` | `0.1.0` |
| Android package | `com.turnova.rider` | `com.turnova.driver` |
| iOS bundle identifier | Not configured | Not configured |
| EAS project ID | Not configured | Not configured |
| Production API base URL | `.env.example` placeholder; EAS remote value not verifiable from GitHub CI | `.env.example` placeholder; EAS remote value not verifiable from GitHub CI |
| Production Android profile | App Bundle | App Bundle |
| Production iOS profile | Store distribution | Store distribution |
| Production auto-increment | Not configured | Not configured |

The current CI artifacts include Android debug APKs, standalone Android release-variant APKs (assembleRelease), and unsigned iOS Simulator .app bundles. After native compilation, CI inspects the embedded Android APK manifest with Android aapt and verifies its package ID against expo.android.package; the iOS job reads the built .app/Info.plist and verifies CFBundleIdentifier against the CI-only simulator identifier passed to xcodebuild. These checks prove artifact identity only. They do not produce a production-signed Android AAB or iOS IPA, establish Apple registration, or prove install/runtime behaviour on physical devices.

## External gates and owners

| Gate | Owner | Required input/evidence |
|---|---|---|
| Final iOS bundle identifiers | Product / App Owner | Approve the reverse-DNS identifiers and verify each is registered with Apple; syntax validation alone does not prove registration |
| Production API base URL | Platform / Infrastructure | Verify the approved HTTPS endpoint is configured in EAS production, resolves publicly from that environment and passes TLS, production CORS and health/readiness |
| EAS project linkage | Release Engineering | Verify each configured UUID resolves to the approved remote EAS project and the corresponding app is linked; UUID syntax alone does not prove linkage |
| Build-number strategy | Release Engineering | Approve `cli.appVersionSource` as local or remote and `production.autoIncrement=true`; verify actual version/build number in an EAS build |
| Android signing | Release Owner | Upload keystore and signing configuration stored in EAS secure credentials, never in Git |
| Apple signing | Release Owner | Distribution certificate and provisioning profiles stored in EAS secure credentials, never in Git |
| Physical-device acceptance | QA / Product Owner | Signed builds installed on agreed Android/iOS devices; functional, security, permissions, GPS, SignalR, negotiation and reconciliation checks recorded |
| Store submission | Release Owner / Legal | Store metadata, privacy disclosures, review submission and approval for both apps |

## Current decision

`RELEASE-01` remains **Blocked**. The preflight is engineering evidence and a guard against configuration drift; it does not replace signed production artifacts, physical-device testing, pilot acceptance or store approval.

No signing secret, bundle identifier, EAS project ID, provider credential or deployment target is invented or committed. No Docker/container build or deployment is used.


## Preflight trust boundary

The report always retains external gates for Apple bundle-ID registration, approved EAS project linkage, the EAS production API environment and DNS/TLS verification, build-number policy approval, signing credentials, physical-device/pilot acceptance and store submission. Local syntax/configuration checks are not evidence that remote ownership, DNS reachability, signing or release approval has been completed.


## Compiled native artifact identity

The native Android matrix verifies the standalone release-variant APK after `assembleRelease`: it requires a non-empty APK, confirms the embedded Metro bundle, runs Android SDK `aapt dump badging`, and compares the one parsed package ID with the checked-in `expo.android.package`. Missing/empty APKs, failed manifest inspection, absent/ambiguous package metadata, malformed IDs and mismatches fail the job.

The native iOS Simulator matrix verifies the built app's actual `Info.plist` with Python's standard-library `plistlib`, comparing `CFBundleIdentifier` with the explicit CI-only `PRODUCT_BUNDLE_IDENTIFIER` supplied to `xcodebuild`. XML and binary plist encodings are supported. This CI-only identifier is not a Product-approved production bundle identifier and does not remove the Apple registration gate.

The shared verifier is `scripts/verify_native_app_identity.py`; 12 deterministic standard-library unit tests live in `scripts/tests/test_verify_native_app_identity.py`. These tests validate parsing, exact identity matching, missing/ambiguous metadata, malformed/placeholder identifiers, tool failures and XML/binary plist handling. CI runs the tests and both platform artifact checks. No signing credential, final Apple identifier, EAS project, production API URL or store decision is invented.


## Native install/launch smoke

The native build artifacts are exercised after compilation by two CI matrix jobs:

- Android: install each standalone release APK into a clean API 35 Google APIs x86_64 emulator, resolve and start its embedded launch activity, verify the expected Rider/Driver package is installed, assert the same app process remains alive after startup, fail on app-scoped fatal native or unhandled React Native startup exceptions, and upload a screenshot plus emulator/install/launch/logcat diagnostics.
- iOS: install each unsigned Simulator .app into an available iPhone Simulator, verify the installed bundle ID, launch it with `simctl`, require a returned process ID and confirm process availability after startup, then upload a screenshot and launch diagnostics.

The jobs consume the artifacts from the native build matrices in the same workflow run, so the tested binary is the one whose package/bundle identity was verified during compilation. They do not call the production API or authentication provider and require no production credentials. Their evidence is limited to simulator installation and application startup. It does not prove physical-device compatibility, GPS hardware, notifications, SignalR, live authentication, production API reachability, signing, Apple registration, EAS linkage, security/pilot acceptance or store readiness. `RELEASE-01` therefore remains Blocked until the external gates above are completed.
