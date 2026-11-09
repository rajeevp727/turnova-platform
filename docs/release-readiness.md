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
- iOS: keep each native Simulator .app unsigned (`CODE_SIGNING_ALLOWED=NO`), erase the selected iPhone Simulator, install the same-run artifact, verify bundle ID/process stability and required login text, and require the complete SecureStore-unavailable banner. Rider and Driver currently use slightly different markers (`Unable to restore session` versus `Unable to restore your session`); OCR accepts both only in the expected unsigned-Simulator mode while continuing to reject expired-session, token-cleanup and secure-save errors. Ad-hoc signing without an Apple-issued TeamIdentifier is rejected by this hosted iOS 26.2 simulator's launchd (`Codesigning issue`, error 162). This smoke does not claim Keychain access or clean-session restoration; signed-device validation remains external.

The jobs consume the artifacts from the native build matrices in the same workflow run, so the tested binary is the one whose package/bundle identity was verified during compilation. They do not call the production API or authentication provider and require no production credentials. Their evidence is limited to simulator installation and application startup. It does not prove physical-device compatibility, GPS hardware, notifications, SignalR, live authentication, production API reachability, signing, Apple registration, EAS linkage, security/pilot acceptance or store readiness. `RELEASE-01` therefore remains Blocked until the external gates above are completed.


## Android emulator AVD bootstrap and diagnostics

The Android smoke explicitly sets `ANDROID_AVD_HOME` to the runner's `$HOME/.android/avd` (unless the runner supplies a deliberate override) before both `avdmanager` and `emulator` execute. It verifies the named AVD's `.ini` and `.avd` directory before launch, captures AVD-manager output, checks that the emulator process remains alive while waiting for ADB registration and includes the emulator log in the always-uploaded diagnostic artifact. This guards against an AVD being created in a location the emulator does not search. A first-run failure on PR #140 [CI #42606662115](https://github.com/rajeevp727/turnova-platform/actions/runs/42606662115) exposed the missing explicit AVD-home setting; the corrected script requires a fresh exact-head run. No simulator smoke is counted as passing until the relevant job concludes successfully.


The Android smoke runner also has a standard-library Python fake-SDK harness in `scripts/tests/test_android_emulator_smoke.py`. It exercises a custom `ANDROID_AVD_HOME`, AVD file creation, install/launch/process assertions and report/screenshot output without network access or a real emulator. CI runs it alongside the existing native artifact identity verifier tests. The real emulator jobs remain the evidence for actual simulator install/launch.

The fake-device harness verifies log scoping as well: it passes when the React Native marker belongs to another PID and fails when the same marker is emitted by the target app PID. The Android smoke's PID filter must therefore explicitly return a nonzero awk status when no target-process marker is found.

## Fresh-install login UI smoke

After the process-stability check, Android captures the UI hierarchy and asserts that the login title, email/password hints and sign-in action are present. iOS runs a local Apple Vision OCR check against the simulator screenshot for the same login-surface text. These assertions verify that the app renders its unauthenticated entry screen rather than merely keeping a process alive. They do not enter credentials or call production API/authentication providers; existing API/authentication functional tests and physical-device/pilot gates remain separate.


The fresh-install UI smoke now checks the Android UI hierarchy for `Welcome back`, `Email address`, `Password` and `Sign in`; iOS uses the built-in Vision OCR framework against the simulator screenshot for the same strings. A failed fake-SDK test revealed that the mock ADB needed to implement `uiautomator dump` and `cat`; the fixture now returns deterministic hierarchy nodes and asserts them. Real emulator/Simulator jobs remain authoritative for actual platform UI output.


## Fresh simulator state and session-restore banner guard

The iOS install/launch smoke erases the selected iPhone Simulator before installing the same-run app artifact. The native CI artifact remains unsigned (`CODE_SIGNING_ALLOWED=NO`). The clean unsigned app is expected to surface `Unable to restore your session. Check your connection or secure storage and retry.` because Expo SecureStore cannot establish Keychain access in this unsigned simulator build; the restore path reads SecureStore before making any API request, so this CI-only banner is a storage failure rather than a backend response. Vision OCR accepts the complete Rider or Driver banner variant in this unsigned-artifact mode, while continuing to reject `Session expired`, token-cleanup errors and other unexpected session/storage failures. Android continues to boot with `-wipe-data` and rejects the equivalent unexpected banner in its UI hierarchy. Neither platform enters credentials or calls production API/authentication providers.

The earlier attempt to ad-hoc sign an ephemeral iOS simulator copy was invalid. Exact-head run [#1049 / 42645314471](https://github.com/rajeevp727/turnova-platform/actions/runs/42645314471) uploaded Rider and Driver diagnostics [Rider artifact 13688114910](https://github.com/rajeevp727/turnova-platform/actions/runs/42645314471/artifacts/13688114910) and [Driver artifact 13688154719](https://github.com/rajeevp727/turnova-platform/actions/runs/42645314471/artifacts/13688154719). Both unified logs state `runningboardd ... spawn failed, error=162: Codesigning issue`; both ad-hoc signatures report `TeamIdentifier=not set`. Therefore the ad-hoc signature—not SecureStore or app runtime—caused SpringBoard/launchd to reject the app before process startup. The smoke no longer ad-hoc signs; it runs the original unsigned artifact and reports the Keychain limitation honestly.

Simulator evidence remains limited to install, launch, process stability and the fresh-install login surface. It does not prove production authentication, physical-device compatibility, GPS, SignalR, notifications, signing, EAS linkage, pilot acceptance or store readiness. RELEASE-01 remains Blocked on the external gates above.


The iOS Simulator smoke consumes the original unsigned native `.app` artifact unchanged and rejects signed/ad-hoc copies. The earlier ad-hoc attempt was rejected by hosted launchd because no Apple-issued TeamIdentifier was present (error 162). Unsigned Simulator SecureStore failure is therefore surfaced as the expected Rider/Driver recoverable banner; this is not Keychain persistence or clean-session restoration. Signed physical-device validation remains external.


## Cold iOS Simulator first-boot readiness bound (2026-11-09)

A fresh simulator erase can trigger first-boot Data Migration/LaunchServicesMigrator work. PR #141 run [#42628714272](https://github.com/rajeevp727/turnova-platform/actions/runs/42628714272) showed the Driver iOS job waiting in this phase and exiting from `simctl bootstatus` before app install; the Rider iOS job remained in progress when superseded. This was a simulator readiness failure, not evidence of a SecureStore result or an app launch failure.

The smoke script now bounds `xcrun simctl bootstatus <UDID> -b` externally with Python `subprocess.run(timeout=600)`. `TURNOVA_SIMULATOR_BOOT_TIMEOUT_SECONDS` defaults to 600 and is validated to 1–600 seconds. The deterministic fake-SDK harness covers a nonzero bootstatus exit and an actual timeout using a sleeping fake command with a one-second test budget; both must stop before install and leave no success report. The outer bound prevents a wedged simulator from hanging the job indefinitely while allowing a cold simulator more time than the observed earlier wait.

Latest PR #141 head `dc5d21327a843d46ed507603e5eed605af1b3f6a` exact-head CI [#1035 / 42633933073](https://github.com/rajeevp727/turnova-platform/actions/runs/42633933073) is pending. Do not count a clean iOS login result until both Rider and Driver simulator jobs complete, their screenshots/OCR show the required login text and no stale-session/storage banner, and the final head passes all required checks. This remains simulator-only evidence, not physical-device, production signing, production authentication or store readiness.


## PR #141 iOS simulator failure diagnostics and extended cold boot (2026-11-09)

Exact-head run [#1038 / 42634056917](https://github.com/rajeevp727/turnova-platform/actions/runs/42634056917) on `03602ad2ad1348a17559213b10476ab205458361` passed 10/12 jobs: backend (364/364 tests), Rider/Driver mobile validation and bundles, shared mobile tests, all four native Android/iOS builds, and both Android emulator smoke jobs. Both iOS simulator smoke jobs failed independently:

- Rider: the erased simulator remained in first-boot Data Migration/System App startup beyond the 300-second bootstatus limit; the smoke exited 124 before install. This is runner readiness, not an app launch result.
- Driver: bootstatus reached `Finished`, but the smoke later exited 1 before writing `launch.txt` or a screenshot. The current script captured `simctl launch` stderr inside command substitution and `set -e` terminated before printing it; the precise failing simctl stage is therefore unknown. Do not infer a SecureStore or app-runtime defect from this incomplete diagnostic.

The follow-up keeps simulator erase and the stale-session OCR guard, raises the default bounded boot wait to 600 seconds (still validated 1–600), and prints captured install/container/launch failures with stage-specific messages. Fake-SDK tests cover the 600-second default, a real short timeout, nonzero bootstatus, install failure and launch failure. The next exact-head run must pass both iOS simulator smokes and produce inspectable screenshots before the clean Keychain/login assertion can be counted. This remains simulator-only evidence; physical-device, production signing and store gates remain external.


## RELEASE-01 simulator smoke diagnostic note (2026-11-09)

The exact-head CI run [#1041 / 42639619625](https://github.com/rajeevp727/turnova-platform/actions/runs/42639619625) passed both Android emulator install/launch/login UI checks and all native builds, but both iOS Simulator smoke jobs failed after successful ad-hoc signing, simulator reset, install and app-container lookup. SpringBoard denied app launch with `FBSOpenApplicationServiceDomain` code 1. No iOS screenshot or smoke artifact was produced; the run therefore does not prove iOS SecureStore or login-screen behaviour. The next CI increment captures filtered simulator unified logs, launchctl state, installed-app inventory, code-signature metadata and embedded entitlements on launch failure. Production signing, Apple registration, EAS linkage, physical-device and store gates remain separate and unresolved.


The first launch-diagnostic harness attempt [#1045 / 42644957570](https://github.com/rajeevp727/turnova-platform/actions/runs/42644957570) failed only because its fake-SDK test compared a shortened `log show` command as an exact string, despite the actual invocation including the filter predicate and time window. The test now matches the command prefix and verifies deterministic captured signature/entitlement content. A new exact-head run is required; no iOS simulator launch success is claimed.


## Android emulator SDK download resilience

The API 35 Google APIs x86_64 system image is installed separately from platform-tools/emulator/platform packages. The smoke retries a transient corrupt/truncated SDK archive up to three times, verifies non-empty package metadata and `system.img` before AVD creation, removes an incomplete extraction and fails before app installation when all attempts fail. The deterministic fake-SDK tests exercise both one-failure recovery and three-failure exhaustion; they do not replace the real emulator smoke.

## Unsigned iOS Simulator evidence boundary

The hosted simulator rejects ad-hoc signatures without an Apple-issued TeamIdentifier (launchd error 162). CI therefore keeps the original unsigned simulator artifact and requires the expected SecureStore-unavailable banner after a fresh erase, while rejecting expired-session and token-cleanup errors. This is launch and fail-closed presentation evidence only; it does not verify Keychain persistence, signed physical-device behaviour or production release readiness.


## Latest RELEASE-01 engineering handoff (2026-11-09)

Last code head in PR #141: `657e663feeb370efee61111fcb344294f1036f3b` (documentation-only synchronization may advance the PR head). Prior run [#1056 / 42656790721](https://github.com/rajeevp727/turnova-platform/actions/runs/42656790721) on `ecc1882...` passed 10/12 jobs. Both Android emulator smoke artifacts were inspected and confirm Rider/Driver login UI and stable process; no production API/authentication was exercised. Both iOS native builds passed, but the two iOS Simulator smoke jobs were cancelled after the branch advanced, so no iOS launch/OCR result is claimed. The OCR checker now accepts the Rider and Driver copy variants while keeping expired-session/token-cleanup/secure-save errors forbidden. Android retry cleanup removes any partial corrupt package directory, and iOS process-exit failures capture system/launchctl/process/installed-app/signature diagnostics. Exact-head CI after code/doc synchronization and inspection of both iOS screenshots/OCR remain mandatory. RELEASE-01 remains Blocked on signed production AAB/IPA, approved identifiers/EAS, production HTTPS API configuration, physical-device/pilot acceptance and store approval.


## RELEASE-01 latest Pixel Launcher ANR recovery and simulator evidence (2026-11-10)

Run [#1070 / 42663289638](https://github.com/rajeevp727/turnova-platform/actions/runs/42663289638) exposed an Android Rider emulator system-dialog issue. The APK installed and `am start -W` succeeded; `dumpsys activity` showed `com.turnova.rider/.MainActivity` top-resumed and the TurnOva process visible. The UI hierarchy instead showed `Pixel Launcher isn't responding` and its ANR modal, obscuring the login UI. This is not evidence that TurnOva crashed. The smoke now recognizes only the exact Pixel Launcher dialog, parses the Wait-button bounds from XML, requires the control to be enabled and clickable, taps its center and recaptures the hierarchy. Recovery is bounded to two attempts; persistent ANR, disabled/missing Wait controls and other app/system ANR dialogs fail closed. Fake-SDK tests cover transient recovery, exhaustion and disabled-control no-tap behaviour.

Run [#1072 / 42669755886](https://github.com/rajeevp727/turnova-platform/actions/runs/42669755886) on head `7f2f0431de339a971851b61b4b385eff51f5d2c6` recorded 11 jobs, all successful, but the expected Android Driver emulator-smoke job was not created/recorded and no Driver Android smoke artifact exists; GitHub reports the workflow conclusion as failure. Rider Android smoke passed with a stable process and login UI. Both iOS Simulator smoke jobs passed after simulator erase: Rider [artifact 13698668818](https://github.com/rajeevp727/turnova-platform/actions/runs/42669755886/artifacts/13698668818) and Driver [artifact 13699018200](https://github.com/rajeevp727/turnova-platform/actions/runs/42669755886/artifacts/13699018200) show install/launch, stable process, login text, expected unsigned SecureStore-unavailable banner and no stale-session error. These artifacts explicitly do not claim Keychain persistence or production API/authentication. Rider Android artifact [13698723012](https://github.com/rajeevp727/turnova-platform/actions/runs/42669755886/artifacts/13698723012) verifies install/launch/login UI; no Pixel Launcher ANR occurred in that attempt.

The latest code additionally requires the Android Wait control to be enabled/clickable before tapping. A fresh exact-head CI must create and pass all 12 jobs, including the Driver Android emulator smoke, before merge. RELEASE-01 remains Blocked on signed production AAB/IPA, approved identifiers/EAS linkage, production HTTPS API hosting/configuration, physical Android/iOS devices, pilot acceptance and store submission.
