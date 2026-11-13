# TurnOva Rider/Driver release readiness

**Last audited:** 2026-11-14  
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

Run [#1070 / 42663289638](https://github.com/rajeevp727/turnova-platform/actions/runs/42663289638) exposed an Android Rider emulator system-dialog issue. The APK installed and `am start -W` succeeded; `dumpsys activity` showed `com.turnova.rider/.MainActivity` top-resumed and the TurnOva process visible. The UI hierarchy instead showed `Pixel Launcher isn't responding` and its ANR modal, obscuring the login UI. This is not evidence that TurnOva crashed. The smoke now recognizes only the exact Pixel Launcher dialog, parses the Wait-button bounds from XML, requires the control to be enabled and clickable, taps its center and recaptures the hierarchy. Recovery is bounded to two attempts; persistent ANR, disabled/missing Wait controls and other app/system ANR dialogs fail closed. Fake-SDK tests cover transient recovery, exhaustion, disabled-control no-tap and a separate TurnOva app ANR that fails without tapping or emitting success.

Run [#1072 / 42669755886](https://github.com/rajeevp727/turnova-platform/actions/runs/42669755886) on head `7f2f0431de339a971851b61b4b385eff51f5d2c6` recorded 11 jobs, all successful, but the expected Android Driver emulator-smoke job was not created/recorded and no Driver Android smoke artifact exists; GitHub reports the workflow conclusion as failure. Rider Android smoke passed with a stable process and login UI. Both iOS Simulator smoke jobs passed after simulator erase: Rider [artifact 13698668818](https://github.com/rajeevp727/turnova-platform/actions/runs/42669755886/artifacts/13698668818) and Driver [artifact 13699018200](https://github.com/rajeevp727/turnova-platform/actions/runs/42669755886/artifacts/13699018200) show install/launch, stable process, login text, expected unsigned SecureStore-unavailable banner and no stale-session error. These artifacts explicitly do not claim Keychain persistence or production API/authentication. Rider Android artifact [13698723012](https://github.com/rajeevp727/turnova-platform/actions/runs/42669755886/artifacts/13698723012) verifies install/launch/login UI; no Pixel Launcher ANR occurred in that attempt.

The latest code additionally requires the Android Wait control to be enabled/clickable before tapping. Exact-head code run [#1079 / 42679676632](https://github.com/rajeevp727/turnova-platform/actions/runs/42679676632) on 51dbb81b6baf3a6e832889132b63f15b1923b310 passed all 12/12 jobs, including the Driver Android emulator smoke. The docs-sync commit advances the PR head; a fresh all-12 exact-head run is still required before merge. RELEASE-01 remains Blocked on signed production AAB/IPA, approved identifiers/EAS linkage, production HTTPS API hosting/configuration, physical Android/iOS devices, pilot acceptance and store submission.


The deterministic harness also supplies a separate `TurnOva Rider isn't responding` dialog with a visible Wait button. The script must not treat it as a Pixel Launcher recovery case: it fails closed without tapping any control and without emitting `result.json`. This guards against hiding an actual app ANR behind the system-launcher workaround.


The final code-only increment is commit [`94c07cbe1b368cc885329599f67e47121fb82b05`](https://github.com/rajeevp727/turnova-platform/commit/94c07cbe1b368cc885329599f67e47121fb82b05): the fake Android harness now also supplies a separate `TurnOva Rider isn't responding` dialog with an enabled Wait control and asserts the script fails without tapping it or emitting a success report. The current branch has a single documentation synchronization after that code commit; the resulting PR head requires a fresh exact-head run with all 12 expected jobs.

## RELEASE-01 latest all-platform simulator evidence (2026-11-10)

Code head 51dbb81b6baf3a6e832889132b63f15b1923b310 passed exact-head CI [#1079 / 42679676632](https://github.com/rajeevp727/turnova-platform/actions/runs/42679676632) with all 12/12 jobs: backend 364/364; shared JS 102/102; Python 28/28; both mobile validations/bundles; all four native Android/iOS builds; and both Android plus both iOS simulator smokes.

- Rider standalone Android release APK smoke: [artifact 13702114989](https://github.com/rajeevp727/turnova-platform/actions/runs/42679676632/artifacts/13702114989). Driver standalone Android release APK smoke: [artifact 13702836717](https://github.com/rajeevp727/turnova-platform/actions/runs/42679676632/artifacts/13702836717). Both show install/launch passed, stable process, expected login text, no stale-session error and zero Pixel Launcher Wait attempts. The workflow downloads the standalone-release-APK artifacts, not the debug APKs.
- Rider unsigned iOS Simulator smoke: [artifact 13705430717](https://github.com/rajeevp727/turnova-platform/actions/runs/42679676632/artifacts/13705430717). Driver unsigned iOS Simulator smoke: [artifact 13704571529](https://github.com/rajeevp727/turnova-platform/actions/runs/42679676632/artifacts/13704571529). Both show erased simulator, install/launch passed, stable process, login text, expected SecureStore-unavailable banner and no stale-session expired error.
- iOS result JSON explicitly records secureStoreAvailable=false, no Keychain access group and productionApiOrAuthentication not exercised. This is expected for the original unsigned Simulator artifact and is not Keychain/token persistence evidence.
- Android and iOS smokes do not exercise production auth/API, live Cosmos, SignalR, GPS providers, signed release distribution or physical devices. Production readiness remains blocked on approved identifiers/EAS, signed AAB/IPA, HTTPS API hosting/configuration, physical-device/pilot acceptance and store submission.
- The documentation synchronization advances the PR head; run fresh exact-head CI and a COMMENT-only final review before merge. No self-approval or bypass.


## RELEASE-01 final exact-head evidence — PR #141 (2026-11-10)

Final PR head 6d7e6776e27ad34d926eb454856d41c293776a2e passed exact-head CI [#1080 / 42686782043](https://github.com/rajeevp727/turnova-platform/actions/runs/42686782043), all 12/12 jobs: backend 364/364; shared JS 102/102; Python fake-SDK/native-artifact 28/28; Rider/Driver mobile validation and production bundles; all four native Android/iOS builds; both Android standalone-release-APK emulator smokes; and both unsigned iOS Simulator smokes. Inspected artifacts: Rider Android [13706635203](https://github.com/rajeevp727/turnova-platform/actions/runs/42686782043/artifacts/13706635203), Driver Android [13704609925](https://github.com/rajeevp727/turnova-platform/actions/runs/42686782043/artifacts/13704609925), Rider iOS [13707380822](https://github.com/rajeevp727/turnova-platform/actions/runs/42686782043/artifacts/13707380822), Driver iOS [13706204614](https://github.com/rajeevp727/turnova-platform/actions/runs/42686782043/artifacts/13706204614). Both Android screenshots show the correct Rider/Driver login role and clean fresh-install UI; both iOS screenshots show the login UI plus the expected unsigned SecureStore-unavailable banner. All four result JSON files report install/launch and stable process. No production auth/API, iOS Keychain persistence, signed store artifact, physical-device behaviour, pilot or store readiness is claimed. The docs synchronization that records this evidence advances the PR head and must receive its own fresh exact-head CI before merge. RELEASE-01 remains Blocked on signed AAB/IPA, approved identifiers/EAS, production HTTPS API configuration, physical-device/pilot acceptance and store submission.

## RELEASE-01 exact-head run #1085 — simulator smoke evidence (2026-11-10)

Exact-head CI [#1085 / 42708832985](https://github.com/rajeevp727/turnova-platform/actions/runs/42708832985) passed all 12/12 jobs on code/evidence head `076b37dd2c0d71524bb8424c639399ece494d6af`: .NET 10 backend 364/364; shared JavaScript 102/102; Python fake-SDK/native-artifact tests 30/30; Rider and Driver mobile configuration/typecheck/production bundle validation; all four Android/iOS native builds; both Android standalone release-APK emulator smokes; and both unsigned iOS Simulator smokes. The four smoke artifacts were downloaded and inspected: Rider Android [13716265571](https://github.com/rajeevp727/turnova-platform/actions/runs/42708832985/artifacts/13716265571), Driver Android [13714823999](https://github.com/rajeevp727/turnova-platform/actions/runs/42708832985/artifacts/13714823999), Rider iOS [13717028524](https://github.com/rajeevp727/turnova-platform/actions/runs/42708832985/artifacts/13717028524), Driver iOS [13714969794](https://github.com/rajeevp727/turnova-platform/actions/runs/42708832985/artifacts/13714969794). Both Android apps installed/launched, remained process-stable, displayed the correct role-specific login screen, had no stale-session error, and needed zero Pixel Launcher Wait attempts. Both iOS simulators were erased before install; both apps installed/launched, remained process-stable, displayed the correct login screen and the expected unsigned-Simulator SecureStore-unavailable banner, with no expired-session error. The iOS result JSON explicitly reports `secureStoreAvailable=false`, no Keychain access group and `productionApiOrAuthentication=not exercised`. This is CI-only startup evidence; it does not prove Keychain persistence, production auth/API, signed release distribution, physical-device behaviour, pilot or store readiness.

The Android smokes used the standalone release APK artifacts produced earlier in the same run; they do not exercise the production API or authentication provider. The iOS smokes used the original unsigned Simulator .app artifacts after simulator erase; the expected SecureStore-unavailable banner is an explicit test-boundary result, not successful Keychain access. The four screenshots/result JSON files are CI artifacts only. RELEASE-01 remains Blocked on approved app identifiers/EAS linkage, production-signed AAB/IPA, HTTPS production API configuration, physical-device security/functional tests, pilot acceptance and store submission. The docs-only synchronization must receive a fresh exact-head CI run before merge.

## Latest hosted simulator evidence — 2026-11-10

PR #141 exact tested code head `98cd89ff0a74a6c4bc5feedda29c4b44b189b0f2` passed [CI run #42717698786](https://github.com/rajeevp727/turnova-platform/actions/runs/42717698786), all 12/12 jobs. The run verified backend 364/364, shared JS 102/102, Python fake-SDK/native-artifact 30/30, Rider/Driver validation and production JS bundles, both Android standalone release APK builds, both unsigned iOS Simulator builds, and all four same-run install/launch/login-surface smokes.

Android Rider and Driver: APK installation and launch passed, app process remained stable, the correct role-specific login surface was visible, and stale-session errors were absent. iOS Rider and Driver: simulator state was erased before install, launch/process stability and role-specific login UI passed, and the unsigned-artifact SecureStore-unavailable banner was correctly detected. This is an explicit CI signing boundary: **it does not validate iOS Keychain persistence or signed production-device behaviour**.

The Android smoke harness additionally proves that it uses an isolated temporary AVD home, preserves unrelated AVD/user-data sentinels, cleans up only its own invocation-specific AVD, and never invokes global `adb emu kill`.

Artifacts: Android Rider [smoke](https://github.com/rajeevp727/turnova-platform/actions/runs/42717698786/artifacts/13719094276) / [release APK](https://github.com/rajeevp727/turnova-platform/actions/runs/42717698786/artifacts/13719412458); Android Driver [smoke](https://github.com/rajeevp727/turnova-platform/actions/runs/42717698786/artifacts/13718314881) / [release APK](https://github.com/rajeevp727/turnova-platform/actions/runs/42717698786/artifacts/13719437560); iOS Rider [smoke](https://github.com/rajeevp727/turnova-platform/actions/runs/42717698786/artifacts/13720236788); iOS Driver [smoke](https://github.com/rajeevp727/turnova-platform/actions/runs/42717698786/artifacts/13721391666).

Production release remains blocked by approved production bundle/package identifiers and EAS linkage, signed Android AAB/iOS IPA, verified HTTPS production API configuration, physical-device functional/security testing, pilot acceptance and store submission. No deployment or production readiness is claimed.


## RELEASE-01 exact-head verification — CI #42726122385 (2026-11-10)

PR #141 code head 3316980ec59d1e865eb2bfbd2c9d0be55015fa09 passed [CI run #42726122385](https://github.com/rajeevp727/turnova-platform/actions/runs/42726122385), all 12/12 jobs: backend 364/364; shared JS 102/102; Python fake-SDK/native-artifact 32/32; Rider/Driver validation and bundles; all four native builds; both Android standalone APK emulator smokes; and both unsigned iOS Simulator smokes. Android Rider [smoke 13722214388](https://github.com/rajeevp727/turnova-platform/actions/runs/42726122385/artifacts/13722214388) and Driver [smoke 13722738316](https://github.com/rajeevp727/turnova-platform/actions/runs/42726122385/artifacts/13722738316) passed install/launch. iOS Rider [smoke 13724815604](https://github.com/rajeevp727/turnova-platform/actions/runs/42726122385/artifacts/13724815604) and Driver [smoke 13724351483](https://github.com/rajeevp727/turnova-platform/actions/runs/42726122385/artifacts/13724351483) passed install/launch and verified the expected SecureStore-unavailable banner. No Keychain persistence, production auth/API or physical-device behaviour is claimed.

The 32 deterministic Python tests include preservation of pre-existing complete/incomplete Android SDK packages, cleanup limited to invocation-created partial downloads, and Android/iOS CI-only guards. The Android smoke cannot mutate a developer SDK/emulator and the iOS smoke cannot erase a developer simulator when invoked outside CI. No application runtime, API, JWT, Cosmos, dispatch, fare, payment or production deployment behaviour changed.

RELEASE-01 remains Blocked for strict acceptance pending approved production identifiers/EAS linkage, signed AAB/IPA, HTTPS API hosting/configuration, physical-device functional/security testing, pilot acceptance and store submission. Overall strict acceptance remains 0/19 Verified (15 In Progress, 4 Blocked); production readiness is not established.


## Exact-head simulator evidence — CI #1097 (2026-11-10)

PR [#141](https://github.com/rajeevp727/turnova-platform/pull/141), branch `test/release-01-clean-simulator-state`, code/evidence head `a1494f906696b6e28c2aa688534cd069a52601ec`, passed all 12/12 jobs in exact-head CI [#42736524343](https://github.com/rajeevp727/turnova-platform/actions/runs/42736524343). The run passed .NET 10 backend 364/364, shared JavaScript 102/102, Python fake-SDK/native-artifact tests 35/35, both app validation/production-bundle jobs, all four native builds and all four same-run simulator smoke jobs. The Rider/Driver Android release APK artifacts and Rider/Driver unsigned iOS Simulator artifacts were downloaded and inspected. Android confirms install/launch, stable app process, correct role-specific login surface, no stale-session error and no Pixel Launcher Wait recovery attempts. iOS confirms simulator erase, install/launch, stable process and correct login surface plus the expected SecureStore-unavailable banner for unsigned artifacts; results explicitly mark SecureStore unavailable, no Keychain access group and production API/authentication not exercised. This does not establish Keychain persistence, signed production AAB/IPA, physical-device behaviour, pilot acceptance or store readiness. The iOS smoke now bounds each `simctl` operation (default 180 seconds; configurable 1–300) and retains a separate 600-second first-boot bound. RELEASE-01 remains Blocked for strict acceptance until approved identifiers/EAS, production signing, HTTPS API configuration, physical-device/pilot acceptance and store submission are evidenced.


## RELEASE-01 exact-head CI #42746353993 — Driver iOS OCR readiness failure (2026-11-10)

PR #141 head `1f6220134853c6feba8ac1044ca0b35905a6973a` completed 11/12 jobs. Backend 364/364, shared JavaScript 102/102, Python 38/38, both mobile validation/bundle jobs, all four native builds, both Android emulator smokes and Rider iOS Simulator smoke passed. Driver iOS smoke failed because its final screenshot remained on the SpringBoard home screen; the artifact has no successful login result. Driver installation and process launch succeeded. The log records the app scene becoming ready at 03:23:02Z, while the first screenshot was written at 03:23:00Z and the 90-second readiness deadline failed at 03:25:44Z. The Swift OCR script is currently invoked via the interpreter inside that readiness budget; first-invocation compilation likely consumed the poll window. This is a test-harness timing diagnosis pending fresh exact-head validation, not a confirmed app-runtime defect.

The follow-up compiles the Vision OCR checker once before simulator enumeration using a separately bounded compile timeout, then reuses the executable for all readiness polls. Deterministic tests cover compile-once reuse, compile failure/timeout before any simulator command, and timeout configuration boundaries. Require a new 12-job exact-head CI and inspect all four same-run simulator artifacts. RELEASE-01 remains externally Blocked pending approved production identifiers/EAS, signed AAB/IPA, HTTPS API hosting, physical-device functional/security testing, pilot acceptance and store submission. Unsigned simulator evidence does not prove iOS Keychain persistence or production authentication.


## RELEASE-01 exact-head verification — CI #42750283649 (2026-11-10)

PR #141 code/evidence head `ce88ade0f6b265de72d9bb520e8e866ea71e88d1` passed [CI run #42750283649](https://github.com/rajeevp727/turnova-platform/actions/runs/42750283649), all 12/12 jobs: backend 364/364; shared JS 102/102; Python fake-SDK/native-artifact 40/40; Rider/Driver mobile validation and production bundles; all four native Android/iOS builds; both Android standalone-release-APK emulator smokes; and both unsigned iOS Simulator smokes.

Inspected same-run artifacts: Rider Android [13734170935](https://github.com/rajeevp727/turnova-platform/actions/runs/42750283649/artifacts/13734170935), Driver Android [13732909631](https://github.com/rajeevp727/turnova-platform/actions/runs/42750283649/artifacts/13732909631), Rider iOS [13733574238](https://github.com/rajeevp727/turnova-platform/actions/runs/42750283649/artifacts/13733574238), Driver iOS [13733349134](https://github.com/rajeevp727/turnova-platform/actions/runs/42750283649/artifacts/13733349134). Android Rider/Driver confirm correct package/activity, install/launch, stable process, role-specific login screen, no stale-session error and zero Pixel Launcher Wait attempts. Both iOS simulators were erased before install; Rider/Driver confirm correct bundle, install/launch, stable process and role-specific login screen. Their screenshots visibly include the generic session-restore warning detected as the expected unsigned-Simulator SecureStore-unavailable banner; result JSON records `secureStoreAvailable=false`, `secureStoreUnavailableBannerVerified=true`, `staleSessionExpiredErrorAbsent=true`, no Keychain access group and production API/authentication not exercised. This is not successful Keychain persistence or signed-device authentication.

The native smoke harness compiles the Vision OCR checker once before simulator access with a separate bounded timeout, then reuses it during readiness polling; deterministic fake-SDK tests cover success/reuse, compile failure, timeout and configuration boundaries. The 40/40 Python suite and all 12 CI jobs passed.

RELEASE-01 remains Blocked for strict acceptance pending approved production identifiers/EAS linkage, signed Android AAB/iOS IPA, verified HTTPS API/DNS/TLS/CORS/readiness, physical-device functional/security testing, pilot acceptance and store submission. No production deployment or production readiness is claimed. The documentation synchronization creates a new PR head and therefore requires a fresh exact-head CI run and final COMMENT-only review before merge.

## Final RELEASE-01 merge evidence — 2026-11-10

PR [#141](https://github.com/rajeevp727/turnova-platform/pull/141) merged at `33e438dba6e0a1af74959904ba132fd0e7273744`. The documentation-synchronized exact head `c14b28d33ecc45d36164b658435296ca256107c3` passed all 12/12 jobs in [CI #1102 / 42753704595](https://github.com/rajeevp727/turnova-platform/actions/runs/42753704595): backend 364/364, shared JavaScript 102/102, Python fake-SDK/native-artifact 40/40, both mobile validation/bundle jobs, four native builds and four same-run simulator smokes. Same-run Rider/Driver Android and iOS result JSONs and screenshots were inspected.

Android Rider/Driver both installed and launched from the same-run standalone release APKs, remained stable and displayed their role-specific login screen without stale-session errors or Pixel Launcher Wait attempts. Unsigned iOS Rider/Driver simulators were erased before install, installed/launched, remained stable and displayed their role-specific login screen. Both iOS screenshots visibly include the expected SecureStore-unavailable warning; results state `secureStoreAvailable=false`, no Keychain access group and production API/authentication not exercised. This is not Keychain persistence, signed-device authentication, physical-device or production readiness evidence.

RELEASE-01 remains Blocked for strict acceptance until approved production identifiers/EAS linkage, signed AAB/IPA, HTTPS API/auth configuration, physical-device testing, pilot acceptance and store submission are evidenced. Other external gates remain Product dispatch radius/zones/location freshness, Product/Operations support policy, live Cosmos ETag/transactional-batch/multi-instance/restart/retention, telemetry/alerts/CORS/load, maps/realtime providers, private document controls, reconciliation policy and legal partnership review.


## AI-01 provider failure boundary — 2026-11-10

Provider calls remain disabled by default and use the unavailable placeholder adapter. The service now returns deterministic local fallback for timeout, transient HTTP and unexpected provider-adapter exceptions while propagating caller cancellation. No provider/model/credential is selected, no sensitive data is sent, and no booking/fare/payment/dispatch/ride-state operation is exposed. Deterministic test-plan cases AI-026–AI-030 cover timeout, cancellation, late faults and unexpected adapter failures. Live provider sandbox and managed-secret validation remain external; AI-01 is supplemental and outside the 19-area denominator.


## AI-01 exact-head verification — 2026-11-10

PR #142 head `79c473661d7804f761bedebbfe871ecd9b0e94e2` passed [CI #1110 / 42769495825](https://github.com/rajeevp727/turnova-platform/actions/runs/42769495825) 12/12: backend 373/373, shared JavaScript 102/102, Python fake-SDK/native tests 40/40, both app validation/bundles, four native builds and four simulator smokes. Same-run Android Rider/Driver and unsigned iOS Rider/Driver artifacts were inspected. Android login surfaces passed with stable processes and no stale-session errors. iOS login surfaces passed with SecureStore unavailable, no Keychain access group and production API/authentication not exercised.

AI-01 provider resilience is supplemental and outside the 19-area denominator. Deterministic tests AI-026–AI-030 cover timeout, caller cancellation, late faults, HTTP failures and unexpected provider-adapter exceptions. No paid provider/model/credential is selected; AI remains disabled by default. Live provider sandbox, managed-secret wiring and data-processing terms remain external.


## AUTH-02 exact-head simulator evidence — 2026-11-10

CI [#1149 / 42814411667](https://github.com/rajeevp727/turnova-platform/actions/runs/42814411667) passed all 12 jobs on source/test head `65a1ea8eca5087a6ad64d88b47dce602938b3c94`, including Rider/Driver Android and unsigned iOS native builds plus all four simulator smoke jobs. Artifact result JSON and screenshots were inspected: Rider Android `13764416739`, Driver Android `13765480564`, Rider iOS `13764818112`, Driver iOS `13765668500`. Both Android apps installed/launched and displayed their correct login screens with stable processes and no stale-session errors. Both iOS apps installed/launched from erased simulator data and displayed their correct login screens; SecureStore was unavailable in the unsigned builds and no Keychain access group was present. Production API/authentication was not exercised. Signed-device Keychain/SecureStore, production identifiers/signing, HTTPS API configuration, physical-device/pilot and store acceptance remain external.

The iOS smoke harness uses a default 300-second bound per simulator operation (maximum 300) and a separate 600-second first-boot bound. RELEASE-SMOKE-026 is included in the 41/41 Python test run. No Docker/container build or deployment was introduced.


## Latest exact-head verification — AUTH-02 PR #143 (2026-11-10)

The documentation head `97c8528f56596537c9e46aec0277a4a83b4286a1` passed [CI #1153 / 42832334281](https://github.com/rajeevp727/turnova-platform/actions/runs/42832334281) with **12/12 jobs successful**. Exact-head results: .NET 10 backend **373/373**; shared mobile JavaScript **140/140**; Python native-artifact/simulator harness **41/41**; Rider and Driver TypeScript validation and production JS bundles; both Android native builds; both unsigned iOS Simulator native builds; and all four Android/iOS simulator smoke jobs.

Same-run artifacts were downloaded and inspected, including each result JSON and screenshot:
- Rider Android: `13773461057` — `com.turnova.rider`, install/launch passed, process stable, Rider login screen verified, no stale-session error, zero Pixel Launcher ANR recovery attempts.
- Driver Android: `13773895577` — `com.turnova.driver`, install/launch passed, process stable, Driver login screen verified, no stale-session error, zero Pixel Launcher ANR recovery attempts.
- Rider iOS: `13772979189` — `com.turnova.rider`, clean simulator data erased, install/launch passed, process stable, Rider login screen verified.
- Driver iOS: `13775055975` — `com.turnova.driver`, clean simulator data erased, install/launch passed, process stable, Driver login screen verified.

Both iOS result JSONs report `secureStoreAvailable=false`, the expected SecureStore-unavailable banner verified, no Keychain access group, unsigned simulator artifacts, and production API/authentication not exercised. The screenshots visibly show the expected warning. This is simulator startup/UI evidence only; it does not establish signed-device Keychain persistence, production authentication, or physical-device acceptance.

CI #1153 and its four inspected simulator artifacts belong to pre-documentation head `97c8528f56596537c9e46aec0277a4a83b4286a1`. Subsequent commits update documentation only and do not change runtime or test source. The current PR head is recorded in PR #143 metadata; exact-head CI #42841628009 is pending. The prior review is COMMENT-only on the pre-documentation head; a fresh exact-head COMMENT review and current thread check are required after CI completes. The repository rulesets endpoint returned an empty list, while branch-protection details were not readable by the connected integration (403). The expected-head merge operation must enforce any remaining GitHub policy; do not self-approve or bypass protections.

AUTH-02 engineering implementation and deterministic evidence are complete for this increment. Strict acceptance remains **0/19** (15 In Progress, 4 Blocked); this is a complete-evidence count, not an implementation-completion score. Production readiness is not established. External AUTH-02 gates remain live TriSend refresh rotation/reuse semantics and signed Android/iOS physical-device restore, expiry, logout, and SecureStore/Keychain failure acceptance.

Next sequential work after merge: re-read `main`, finish the #3 RIDER-01 audit, implement #4 RIDER-02 stable same-payload proposal Idempotency-Key reuse for double-tap/ambiguous retries in both apps, then #5 RIDER-03 server-side single-active Rider booking reservation across different keys. Maintain one active feature PR.


## CI #1159 simulator evidence (2026-11-10)

Previous AUTH-02 source/test head 639e6c1516693995997571983619f980dac9f75f passed [CI #1159 / 42842677468](https://github.com/rajeevp727/turnova-platform/actions/runs/42842677468) with 12/12 jobs successful: backend 373/373, shared JavaScript 141/141, Python native-artifact/simulator harness 41/41, Rider/Driver typechecks and bundles, four native builds and four simulator smoke jobs.

Same-run artifacts were downloaded and inspected:
- Rider Android smoke: 13777386979 — install/launch and Rider login surface verified.
- Driver Android smoke: 13778300986 — install/launch and Driver login surface verified.
- Rider iOS smoke: 13777927561 — install/launch and Rider login surface verified; SecureStore unavailable warning expected.
- Driver iOS smoke: 13778222906 — install/launch and Driver login surface verified; SecureStore unavailable warning expected.
- Rider iOS app bundle: 13778820239 — embedded identifier com.turnova.rider.
- Driver iOS app bundle: 13778236148 — embedded identifier com.turnova.driver.

Both iOS artifacts are unsigned simulator builds without a Keychain access group. These checks do not establish signed-device SecureStore/Keychain persistence, production authentication, physical-device acceptance, signing, hosting, pilot or store readiness. Latest exact-head CI and artifact evidence is recorded below; production readiness is not claimed.


## Exact-head build and smoke evidence — CI #1493 (2026-11-13)

The pre-synchronization documentation head `c9695ea97d299db8cedea54600f8befcdab11884` passed [GitHub Actions #1493 / 43384473766](https://github.com/rajeevp727/turnova-platform/actions/runs/43384473766) **12/12 jobs** after retrying only the initial Driver Android emulator smoke job.

| Artifact | App/platform | Verified result |
|---|---|---|
| `14018841676` | Rider Android emulator | Standalone release APK installed/launched; stable foreground process; expected login text; no stale-session error or system UI recovery |
| `14020282957` | Driver Android emulator (targeted retry) | Standalone release APK installed/launched; stable foreground process; expected login text; no stale-session error or system UI recovery |
| `14019186476` | Rider iOS Simulator | Unsigned app installed/launched; stable process; expected login text; SecureStore-unavailable recovery banner verified |
| `14019121356` | Driver iOS Simulator | Unsigned app installed/launched; stable process; expected login text; SecureStore-unavailable recovery banner verified |

The initial Driver Android attempt artifact `14018592304` showed successful installation, ActivityManager launch and the expected login screen but exited 255 before writing its final result JSON. Inspection found no Driver fatal exception, ANR or React Native startup exception. The exact failing ADB command is not known; a targeted retry passed. The retry is the accepted smoke evidence for this CI run.

The same run verified backend **570/570**, shared mobile/auth **179/179**, deterministic Python harness **46/46**, Rider/Driver Expo validation and production JS bundles, native non-container API publish, both Android debug and standalone release APKs, and both unsigned iOS Simulator builds.

These artifacts establish CI build and emulator/simulator install/launch/login-screen evidence only. They do not prove production API/authentication, live Cosmos, signed iOS Keychain persistence, production-signed Android AAB/iOS IPA, physical-device acceptance or store approval. The current release-readiness row remains Blocked until those external inputs and acceptance tests are supplied.

## PLATFORM-01 exact-head native build and smoke evidence — CI #1498 (2026-11-13)

Exact documentation HEAD `6c67c98b192df5ea8210dd47640eee76816ea578` passed [GitHub Actions #1498 / 43391119590](https://github.com/rajeevp727/turnova-platform/actions/runs/43391119590) **12/12 jobs**: backend **570/570**, shared mobile/auth **179/179**, Python harness **46/46**, Rider/Driver Expo validation/typechecks/production JS bundles, native non-container API publish, both Android debug and standalone release APKs, both unsigned iOS Simulator builds and all four emulator/simulator smoke jobs.

| Artifact | App/platform | Verified result |
|---|---|---|
| `14021706924` | Rider Android emulator | APK installed/launched; stable foreground process; Rider login screen verified; no stale-session error or system-UI recovery |
| `14020793191` | Driver Android emulator | APK installed/launched; stable foreground process; Driver login screen verified; no stale-session error or system-UI recovery |
| `14022022857` | Rider iOS Simulator | Unsigned app installed/launched; stable process; Rider login screen verified; expected SecureStore-unavailable recovery banner |
| `14022750045` | Driver iOS Simulator | Unsigned app installed/launched; stable process; Driver login screen verified; expected SecureStore-unavailable recovery banner |

These are CI build and emulator/simulator install/launch/login-screen results, not production release acceptance. Android APK signing/store readiness, signed iOS IPA and Keychain access group, physical-device testing, production API/authentication, live Cosmos and end-to-end ride flows remain external or unverified. No production deployment was performed.

## CI #1502 Android smoke harness correction — 2026-11-13

- Documentation-head CI #1502 / 43398973091 completed 11/12. Driver Android smoke job `131801620220` failed after APK installation and successful ActivityManager launch (`Status: ok`); the shell harness's `printf ... | grep -Fq` pipeline raised `Broken pipe` under `set -euo pipefail`. No application crash or ANR was observed. Artifact `14024159021` preserves the launch output.
- Corrected the launch-status assertion to use a Bash substring check and added a deterministic Python regression invariant plus test-plan case `PLATFORM-NATIVE-ANDROID-001`. The corrected branch requires fresh exact-head CI before any merge. This is a CI harness reliability correction; it does not change the release-readiness limitations below.


## Latest exact-head verification — PR #158 / CI #1508 (2026-11-13)

- Main/base HEAD: `b6564c7dc2959883c2910f5b6f13134f7052566f`. PR #158 branch `fix/platform-01-storage-readiness-failure-mapping`; exact tested HEAD `115d1914cb19e0c7a13df6765409521d31a4024b`.
- GitHub Actions [CI #1508 / run 43406713092](https://github.com/rajeevp727/turnova-platform/actions/runs/43406713092) completed successfully: **12/12 jobs passed**. Backend **570/570** (0 failed, 0 skipped); shared mobile/auth **179/179**; deterministic Python harness **47/47**, including `test_launch_status_check_avoids_pipefail_sigpipe_false_failure`; Rider/Driver Expo configuration, typechecks and production JavaScript bundles; native non-container .NET API publish; both Android debug and standalone release APKs; both unsigned iOS Simulator apps; all four same-run emulator/simulator smoke jobs.
- Driver iOS smoke initially hit a 300-second `xcrun simctl list devices available -j` timeout before app installation. Only that job was retried as job `131837472857`; the retry completed successfully. This is transient Simulator infrastructure failure followed by a successful targeted retry, not an application failure.
- Same-run smoke artifacts: Rider Android `14027824488`, Driver Android `14027884362`, Rider iOS `14029062725`, Driver iOS retry `14030386392`. Job logs confirm successful install/launch and role-specific login UI; unsigned iOS reports the expected SecureStore-unavailable recovery banner and does not claim Keychain success. Android release APKs: Rider `14028800834`, Driver `14028640774`; unsigned iOS app artifacts: Rider `14029435192`, Driver `14028438409`.
- The Android smoke harness correction is covered by the 47/47 Python suite: use a Bash substring check instead of `printf | grep -Fq` under `set -euo pipefail`, preventing a SIGPIPE false negative after successful ActivityManager launch. Test-plan case `PLATFORM-NATIVE-ANDROID-001`.
- This verifies CI builds plus emulator/simulator install, launch and login-screen presentation only. It does not establish production API/authentication, live Cosmos, signed iOS Keychain persistence, physical-device acceptance, store signing or end-to-end ride acceptance.
- This evidence-documentation commit creates a new PR head and requires its own exact-head CI. Keep PR #158 Draft until all required jobs pass on that exact new head and the latest base/head, review threads and GitHub merge enforcement are rechecked. No self-approval or bypass.
- Strict acceptance remains **0/19** (15 In Progress, 4 Blocked); this is complete-evidence accounting, not an implementation score. Production readiness is not established.


## Exact-head release build and smoke evidence — CI #1508 (2026-11-13)

[CI #1508 / run 43406713092](https://github.com/rajeevp727/turnova-platform/actions/runs/43406713092) passed **12/12 jobs** on PR #158 head `115d1914cb19e0c7a13df6765409521d31a4024b`. It produced Rider/Driver Android debug and standalone release APKs, both unsigned iOS Simulator apps, Rider/Driver production JavaScript bundles and native non-container .NET API publish. Backend 570/570, shared mobile/auth 179/179 and Python harness 47/47 passed.

| Artifact | App/platform | Result |
|---|---|---|
| `14027824488` | Rider Android emulator smoke | Standalone release APK installed/launched; ActivityManager status OK; Rider login screen asserted |
| `14027884362` | Driver Android emulator smoke | Standalone release APK installed/launched; ActivityManager status OK; Driver login screen asserted |
| `14029062725` | Rider iOS Simulator smoke | Unsigned app installed/launched; Rider login screen; SecureStore-unavailable recovery banner |
| `14030386392` | Driver iOS Simulator smoke (targeted retry) | Unsigned app installed/launched; Driver login screen; SecureStore-unavailable recovery banner |

The initial Driver iOS smoke attempt timed out while enumerating available Simulator devices before app installation. Only that failed smoke job was retried; the retry passed. This is transient runner/Simulator infrastructure evidence, not an application failure. Unsigned iOS artifacts do not establish signed Keychain persistence. CI smoke tests do not establish physical-device acceptance, production API/authentication, live Cosmos or end-to-end ride acceptance. Production readiness remains blocked on signed AAB/IPA, store identifiers/EAS linkage, certificates/profiles, physical-device testing, approved API hosting/configuration and pilot/store acceptance.


## Latest exact-head verification — PR #158 / CI #1509 (2026-11-13)

- Main/base HEAD: `b6564c7dc2959883c2910f5b6f13134f7052566f`. PR #158 branch `fix/platform-01-storage-readiness-failure-mapping`; exact tested HEAD `9fe2b884f9e0c2a566dd6e06b1b0fca731f65f82`.
- GitHub Actions [CI #1509 / run 43414466125](https://github.com/rajeevp727/turnova-platform/actions/runs/43414466125) completed successfully: **12/12 jobs passed**. Backend **570/570** (0 failed, 0 skipped); shared mobile/auth **179/179**; deterministic Python harness **47/47**; Rider/Driver Expo configuration, typechecks and production JavaScript bundles; native non-container .NET API publish; both Android debug and standalone release APKs; both unsigned iOS Simulator apps; all four same-run emulator/simulator smoke jobs.
- Same-run smoke artifacts were downloaded and inspected: Rider Android `14030984936`, Driver Android `14031119728`, Rider iOS Simulator `14033460518`, Driver iOS Simulator `14032367500`. Android results confirm install/launch, stable foreground process, role-specific login screen, no stale-session error and no system-UI recovery. iOS results confirm erased simulator data, install/launch, stable process, role-specific login screen, stale-session error absent and the expected SecureStore-unavailable recovery banner.
- Native artifacts: Rider Android release APK `14031468247`, Driver Android release APK `14030834941`, Rider unsigned iOS Simulator app `14032345534`, Driver unsigned iOS Simulator app `14031683932`, native API publish `14031511255`.
- These are CI build and emulator/simulator install/launch/login-screen results only. Unsigned iOS does not establish signed-device Keychain persistence. Production API/authentication, live Cosmos, physical-device acceptance, store signing or end-to-end ride acceptance are not claimed.
- This evidence synchronization creates a new PR head and requires its own exact-head CI before PR #158 is marked ready or merged. Recheck main/base, exact head, all required jobs, review threads and GitHub merge enforcement. No self-approval or bypass.
- Strict acceptance remains **0/19** (15 In Progress, 4 Blocked); this is complete-evidence accounting, not an implementation score. Production readiness is not established. Next after #158 merges: re-read main, implement explicit CORS wildcard-origin validation with deterministic configuration tests, then Union audit observability and remaining PLATFORM-01 work before RELEASE-01.


## Latest exact-head verification — PR #158 / CI #1512 (2026-11-13)

- Exact documentation HEAD `56f5f5573083f354ac1a3cc198f9b672655efcae`; GitHub Actions [CI #1512 / run 43421477453](https://github.com/rajeevp727/turnova-platform/actions/runs/43421477453) completed successfully: **12/12 jobs passed**.
- Backend: **570/570** (0 failed, 0 skipped). Shared mobile/auth: **179/179**. Deterministic Python harness: **47/47**. Rider and Driver Expo configuration, typechecks and Android/iOS production JavaScript bundles passed. Native non-container .NET API publish passed.
- Native builds: both Rider/Driver Android debug APKs and standalone release APKs passed; both unsigned iOS Simulator apps compiled. All four same-run emulator/simulator smoke jobs passed.
- Downloaded and inspected same-run smoke artifacts and result JSON/screenshots:
  - Rider Android emulator: `14035271929` — install/launch passed, stable foreground process, Rider login screen verified, no stale-session error, no system-UI recovery.
  - Driver Android emulator: `14034763708` — install/launch passed, stable foreground process, Driver login screen verified, no stale-session error, no system-UI recovery.
  - Rider iOS Simulator: `14036036290` — simulator data erased, install/launch passed, stable process, Rider login screen verified, SecureStore-unavailable recovery banner verified, stale-session error absent.
  - Driver iOS Simulator: `14037970012` — simulator data erased, install/launch passed, stable process, Driver login screen verified, SecureStore-unavailable recovery banner verified, stale-session error absent.
- Native build artifacts: Rider Android debug `14034092527`, Driver Android debug `14033948751`, Rider Android release APK `14034467813`, Driver Android release APK `14034093247`, Rider unsigned iOS Simulator app `14033739564`, Driver unsigned iOS Simulator app `14035391612`, native API publish `14034013011`.
- The screenshots confirm the expected role-specific login surfaces. The iOS apps are unsigned and have no Keychain access group; the SecureStore recovery banner is expected in this CI environment. This does not establish signed-device Keychain persistence.
- CI verifies build, install, launch and login-screen presentation only. Production API/authentication was not exercised; no live Cosmos, physical-device, signed-release, end-to-end ride or production-readiness claim is made.
- PR #158 remains the sole active feature PR. This evidence-only synchronization creates a new head and requires its own exact-head CI. Recheck current main/base, head SHA, review threads and GitHub merge enforcement; no self-approval or bypass.
- Strict acceptance remains **0/19** (15 In Progress, 4 Blocked); this is complete-evidence accounting, not an implementation score. Production readiness is not established.
- Next after PR #158 merges: re-read main, implement explicit CORS wildcard-origin validation with deterministic configuration tests, then privacy-safe Union audit metrics/ActivitySource tracing and remaining PLATFORM-01 diagnostics/native-publish/audit-path work before #19 RELEASE-01.


## Latest exact-head verification — PR #158 / CI #1513 (2026-11-13)

- Main/base HEAD: `b6564c7dc2959883c2910f5b6f13134f7052566f`. PR #158 branch `fix/platform-01-storage-readiness-failure-mapping`; tested documentation HEAD `89dc76f483ffa9a27dda1b67c5eaf50d78c362da`.
- GitHub Actions [CI #1513 / run 43431035440](https://github.com/rajeevp727/turnova-platform/actions/runs/43431035440) completed successfully on **attempt 2: 12/12 jobs passed**. Backend **570/570** (0 failed, 0 skipped); shared mobile/auth **179/179**; deterministic Python harness **47/47**; Rider/Driver Expo validation, typechecks and production JS bundles; native non-container .NET API publish; both Android debug/release APKs; both unsigned iOS Simulator builds; all four same-run Android/iOS smoke tests.
- The first attempt failed only Rider Android smoke job `131909886413` with exit 255 after APK installation and ActivityManager `Status: ok`. Its artifact `14040178925` contained the expected Rider login UI/foreground activity and no app fatal exception or ANR, but no final result JSON. GitHub's failed-jobs-only retry ran job `131930156233` and passed. Retry artifact `14045725928` confirms install/launch, stable foreground process, Rider login text, no stale-session error and no system-UI recovery.
- All four final smoke artifacts were downloaded and inspected:
  - Rider Android retry: `14045725928`.
  - Driver Android: `14041925228`.
  - Rider iOS Simulator: `14043137536`.
  - Driver iOS Simulator: `14041887826`.
- Android reports confirm successful install/launch, stable foreground process, role-specific login screen, no stale-session error and no system-UI recovery. iOS reports confirm simulator data erasure, install/launch, stable process, role-specific login screen, no stale-session-expired error and the expected SecureStore-unavailable recovery banner. Native artifacts: Rider Android debug `14038763284`, Driver Android debug `14039628739`, Rider Android release APK `14038654645`, Driver Android release APK `14039349859`, Rider unsigned iOS Simulator app `14040388207`, Driver unsigned iOS Simulator app `14040736292`, API publish `14039892373`.
- These are CI build and emulator/simulator install/launch/login-screen results only. Unsigned iOS does not establish signed-device Keychain persistence. Production API/authentication, live Cosmos, physical-device, signed-store release and end-to-end ride acceptance were not exercised.
- This evidence synchronization creates a new PR head and requires fresh exact-head CI before PR #158 is marked ready or merged. Recheck main/base, exact HEAD, all required jobs, review threads and GitHub merge enforcement. No self-approval or bypass.
- Strict acceptance remains **0/19** (15 In Progress, 4 Blocked); this is complete-evidence accounting, not an implementation score. Production readiness is not established.
- Next after PR #158 merges: re-read main, implement explicit CORS wildcard-origin validation with deterministic configuration tests, then privacy-safe Union audit metrics/ActivitySource tracing and remaining PLATFORM-01 diagnostics/native-publish/audit-path work before #19 RELEASE-01.



## Latest exact-head verification — PR #158 / CI #1514 (2026-11-13)

- Main/base HEAD: `b6564c7dc2959883c2910f5b6f13134f7052566f`. PR #158 branch `fix/platform-01-storage-readiness-failure-mapping`; exact tested documentation HEAD `0152a12789001e349b94da4e913160e1dd6acafc`.
- GitHub Actions [CI #1514 / run 43439229082](https://github.com/rajeevp727/turnova-platform/actions/runs/43439229082) completed successfully: **12/12 jobs passed**. Backend: **570/570** (0 failed, 0 skipped); shared mobile/auth: **179/179**; Python deterministic harness: **47/47**. Both Rider and Driver Expo validation, TypeScript checks and Android/iOS production JavaScript bundle exports passed. Both mobile dependency audits reported 0 high and 0 critical findings (11 moderate, 1 low each; informational audit output, not a release-security sign-off).
- Native non-container .NET API publish passed. Both Rider and Driver Android debug APKs and standalone release APKs built. Both unsigned iOS Simulator apps built. All four same-run Android emulator/iOS Simulator install-launch smoke jobs passed.
- Same-run smoke artifacts were downloaded and inspected:
  - Rider Android: `14044689106` — install/launch passed, process stable, Rider login text verified, no stale-session error or system-UI recovery.
  - Driver Android: `14045463266` — install/launch passed, process stable, Driver login text verified, no stale-session error or system-UI recovery.
  - Rider iOS Simulator: `14046196834` — install/launch passed, process stable, Rider login text verified, simulator data erased; SecureStore unavailable and the expected recovery banner was shown.
  - Driver iOS Simulator: `14046876982` — same simulator result for the Driver login surface.
- Native artifacts: Rider Android debug `14045911470`, Driver Android debug `14044657223`, Rider Android release APK `14045772222`, Driver Android release APK `14045528021`, Rider unsigned iOS Simulator app `14045669353`, Driver unsigned iOS Simulator app `14047120044`, API publish `14044902241`. Production JS bundles: Rider `14045556170`, Driver `14045366748`. Release-readiness reports: Rider `14044207450`, Driver `14044167454`.
- These results establish CI build and emulator/simulator install-launch/login-screen evidence only. Unsigned iOS does not establish signed-device Keychain persistence. Production API/authentication, live Cosmos transactional behavior, physical-device acceptance, signed-store release and end-to-end ride acceptance were not exercised.
- Review state at this checkpoint: PR #158 is marked ready for review; the latest COMMENT-only review covers the exact parent evidence head, and the review-thread list returned zero unresolved threads. This is not an approval or a claim that an independent approval exists. The base is not behind the tested head. Recheck live main/base, exact head, review threads and GitHub merge policy after the next documentation synchronization; merge only with exact-head CI green and no bypass.
- Strict acceptance remains **0/19** (15 In Progress, 4 Blocked). CI build/test evidence does not satisfy the remaining physical-device, live-provider, product-policy or legal acceptance gates. Production readiness is not established.
- Next sequential engineering: after PR #158 is safely merged, re-read main and continue #18 PLATFORM-01 with explicit CORS wildcard-origin rejection and deterministic startup/configuration tests, then privacy-safe Union audit metrics/ActivitySource tracing and remaining diagnostics/native-publish/audit-path work before #19 RELEASE-01. This documentation update creates a new PR head and requires its own exact-head CI.


## Exact-head build and smoke evidence — CI #1523 (2026-11-13)

[CI #1523 / run 43459668928](https://github.com/rajeevp727/turnova-platform/actions/runs/43459668928) passed **12/12 jobs** on PR #158 head `48474830f0c4a2d14733ee738f9109179dd07def` (main/base `b6564c7dc2959883c2910f5b6f13134f7052566f`). Backend **570/570**, shared mobile/auth **179/179**, and deterministic Python harness **47/47** passed. Rider/Driver Expo validation, typechecks, production JavaScript bundles, native non-container .NET API publish, Android debug/release APK builds, unsigned iOS Simulator builds and all four same-run emulator/simulator install-launch smoke jobs passed.

| Smoke artifact | App/platform | Evidence |
|---|---|---|
| `14057030887` | Rider Android emulator | ActivityManager `Status: ok`; install/launch passed |
| `14056906435` | Driver Android emulator | ActivityManager `Status: ok`; install/launch passed |
| `14059827183` | Rider iOS Simulator | Install/launch passed; login UI observed; expected unsigned SecureStore-unavailable banner |
| `14058624906` | Driver iOS Simulator | Install/launch passed; login UI observed; expected unsigned SecureStore-unavailable banner |

The tested source head includes the late-provider-fault completion-race correction and its deterministic regression coverage. These results establish CI build/publish and emulator/simulator install-launch evidence only. Unsigned iOS does not establish signed Keychain persistence. Production API/authentication, live Cosmos, physical-device acceptance, signed-store release, end-to-end ride acceptance and production readiness remain unverified. The release-readiness status remains blocked on the external gates above.


## Current PR #158 smoke follow-up — 2026-11-14

- Prior exact-head CI #1523 remains historical evidence for head `48474830f0c4a2d14733ee738f9109179dd07def`; it passed 12/12. It does not verify subsequent commits.
- CI #1527 / run 43488094224 completed 11/12. The Rider Android emulator job installed/launched the APK, confirmed the Rider activity was resumed and found the expected login UI, then exited 255 before the final screenshot and success JSON. The artifact contains the initial screenshot, activity snapshots, UI hierarchy and logcat. The previous script did not record the exact post-launch ADB command failure; this is not classified as an observed app crash.
- Follow-up changes add up to three attempts for final screenshot capture, capture ADB stderr/exit status, validate the PNG signature and emit device/emulator diagnostics on persistent failure. Deterministic harness cases cover a transient transport failure recovering and persistent failures remaining non-zero with no success report.
- Code/test head before documentation synchronization: `4e561c65a95d102d1c35d518e2469e065f883563`. CI #1532 was pending on that head and is superseded by the documentation commit. Fresh exact-head CI on the new documentation head must pass all 12 jobs before PR #158 can be considered merge-ready.
- Release readiness remains **not established**. These checks are CI emulator/simulator evidence only; they do not prove physical-device acceptance, signed Android/iOS artifacts, signed iOS Keychain persistence, production API/authentication, live Cosmos behavior, end-to-end ride acceptance or production readiness.
