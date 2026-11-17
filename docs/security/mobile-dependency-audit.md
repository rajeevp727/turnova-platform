# Mobile dependency audit scope assessment

**Assessment date:** 2026-11-18  
**Apps:** TurnOva Rider and TurnOva Driver  
**Platform phase:** Android  
**Current dependency baseline:** Expo SDK 54 / React Native 0.81.4

## Current exact audit evidence

The PR #185 run [#44158843845](https://github.com/rajeevp727/turnova-platform/actions/runs/44158843845) generated fresh lockfiles and completed npm ci for both apps. Both audit artifacts report:

| App | Total package findings | High package nodes | Moderate | Critical |
|---|---:|---:|---:|---:|
| Rider | 34 | 27 | 7 | 0 |
| Driver | 34 | 27 | 7 | 0 |

The 27 high package nodes are a transitive cascade from one direct high advisory root: [GHSA-fc5q-39xc-vjpf](https://github.com/advisories/GHSA-fc5q-39xc-vjpf), affecting picomatch through 4.0.7. The lockfiles resolve picomatch 2.3.2 and 4.0.7 under Expo CLI, Expo Metro config, tinyglobby, anymatch, Jest and micromatch tooling. It is not a direct Rider or Driver dependency.

The GitHub advisory currently lists no patched picomatch version. The Rider npm audit JSON reports fixAvailable as react-native@0.88.1, while the Driver report reports expo@58.0.26; both have isSemVerMajor=true. These are app-specific major SDK migration suggestions, not patched picomatch releases. CI accepts only those two exact app-specific remediation metadata objects; any other package, version or major/minor classification forces a fresh review. The advisory is disputed by the package maintainer; Snyk's [amendment](https://security.snyk.io/vuln/SNYK-JS-PICOMATCH-21248802) records that it was deemed not a vulnerability. TurnOva does not treat that dispute as a patch or erase the finding.

The same audit reports seven moderate package nodes rooted in glob and minimatch advisories. They remain recorded for follow-up; they are not represented as fixed.

## TurnOva exposure assessment and CI controls

The affected picomatch copies are build/test tooling, not app source dependencies. The current apps do not import picomatch directly and do not accept user-supplied glob patterns. The Android validation job now performs all of these checks before it can pass:

1. Fail on any critical finding or npm audit execution error.
2. Resolve every high package node through the npm audit dependency graph; the only accepted high root must be the exact picomatch GHSA above.
3. Require picomatch to remain transitive, with no direct app dependency.
4. Require every picomatch dependency parent to remain within the reviewed Expo CLI/Metro/Jest build-tool set.
5. Search app source for direct picomatch imports.
6. Export the Android production bundle with source maps and fail if any source map contains picomatch code.
7. Remove verification-only source maps before uploading the production bundle artifact.
8. Fail on any new or unknown high advisory, any direct app use, any unreviewed parent, or any bundle inclusion.

The exception is deliberately narrow and fail-closed. It is a contextual build-tooling risk assessment, not a patched dependency and not a claim that npm audit is clean. The source-map check is required for each app and each exact CI head.

## Upgrade decision

No blind Expo/React Native major upgrade is included in this dependency-reproducibility PR. Expo SDK 58 upgrades React Native to 0.88 and documents TypeScript/native lifecycle and Android release-build changes; such a migration needs its own Rider/Driver regression work and platform evidence. Expo's [SDK 58 upgrade guide](https://expo.dev/blog/how-to-upgrade-to-sdk-58) and [SDK 58 release notes](https://expo.dev/changelog/sdk-58) are the compatibility references.

Revisit the picomatch advisory when the maintainer publishes a patched release or the advisory is withdrawn. Until then, preserve the explicit CI exception and the bundle-exclusion proof; do not claim the advisory is fixed.

## Android exact-head evidence and follow-up — 2026-11-18

PR #185 run [#44161191929](https://github.com/rajeevp727/turnova-platform/actions/runs/44161191929) completed successfully on prior source head `1e09053bb1ec1ba4d31ca0deb4b5b1dcc35808bd`: Rider and Driver npm ci, lockfile digest verification, Expo configuration, TypeScript checks and Android bundle export passed; both actual Android source maps passed the exact-advisory contextual gate; debug and standalone release APKs built and passed package-identity verification; both standalone release APKs installed and launched in CI Android emulators; backend Release build, 758/758 backend tests, native non-container API publish and artifact validation passed; mobile auth/native tests passed 227/227 Node and 48/48 Python; iOS jobs were correctly skipped.

Artifacts: Rider release APK [14364856295](https://github.com/rajeevp727/turnova-platform/actions/runs/44161191929/artifacts/14364856295), Driver release APK [14364336448](https://github.com/rajeevp727/turnova-platform/actions/runs/44161191929/artifacts/14364336448), Rider emulator smoke [14363499225](https://github.com/rajeevp727/turnova-platform/actions/runs/44161191929/artifacts/14363499225), Driver emulator smoke [14363638701](https://github.com/rajeevp727/turnova-platform/actions/runs/44161191929/artifacts/14363638701).

A subsequent security review found that the contextual verifier should reject malformed or internally inconsistent npm audit JSON before applying the advisory exception. The verifier now requires all severity counters, verifies record counts and package-node arrays, checks the audit exit code against the inventory, and allows only the two exact major-only remediation hints observed in CI (Rider react-native@0.88.1; Driver expo@58.0.26). Its deterministic test suite now has 20 cases, including acceptance of the Driver hint and rejection of unknown versions or non-major suggestions. These follow-up code commits are not yet covered by the prior green run; fresh exact-head CI is mandatory before merge.

The picomatch advisory remains unresolved and is not represented as patched. No Expo/React Native major upgrade was performed.

## Exact-head audit metadata discrepancy — 2026-11-18

PR #185 CI [#44166927981](https://github.com/rajeevp727/turnova-platform/actions/runs/44166927981) passed Rider's contextual audit gate but failed Driver at the remediation-metadata comparison. The two fresh reports had identical inventory counts (34 total, 27 high package records, 7 moderate, 0 critical) and the same sole high advisory root, GHSA-fc5q-39xc-vjpf. The Rider report suggested the major-only react-native@0.88.1 path; the Driver report suggested the major-only expo@58.0.26 path. Neither is a picomatch patch.

The verifier validates the package identity is one of the two known apps and allows only either exact major-only hint observed in npm metadata (react-native@0.88.1 or expo@58.0.26). The same Rider report changed from the former hint to the latter on a subsequent run while the vulnerability inventory stayed the same, so the hints are not stable per app. Unknown app identities, packages, versions and non-major metadata still fail closed. Malformed package/lock roots and static side-effect/require.resolve imports are rejected. The deterministic suite has 27 cases. The advisory remains unpatched and disclosed. Fresh exact-head CI must rerun both apps' audits, bundles, native builds and emulator smoke before PR #185 can merge.


## Per-app remediation identity hardening — 2026-11-18

The audit verifier validates `package.json.name` is `turnova-rider` or `turnova-driver`, then allows only either exact major-only npm hint observed for this advisory: `react-native@0.88.1` or `expo@58.0.26`. npm metadata is not stable per app: a later Rider report changed from the React Native hint to the Expo hint without changing the 34-record vulnerability inventory. Unknown app identities, other packages/versions and non-major suggestions fail. Static side-effect imports and `require.resolve` are rejected; malformed package/lock JSON roots fail closed. The deterministic suite has 27 cases. This does not patch picomatch or change the affected dependency; the high build-tooling advisory remains disclosed and contextually isolated.


## npm remediation metadata drift — 2026-11-18

The exact Rider npm audit artifact from CI #44169775823 reported the same inventory (34 total; 27 high package nodes; 7 moderate; 0 critical) and same GHSA root, but `fixAvailable` changed from `react-native@0.88.1` to `expo@58.0.26` with `isSemVerMajor=true`. This confirms npm's major-migration suggestion is not stable per app/run. The verifier therefore accepts only these two exact reviewed objects for either known app identity, while still rejecting unknown package/version, non-major metadata, unknown app identities, critical/unknown advisories and bundle inclusion. This is not a patched picomatch version.


## Third observed major-only remediation hint — 2026-11-18

The Rider audit artifact from CI #44170775823 reports `expo-asset@58.0.21` with `isSemVerMajor=true`; Driver reports `expo@58.0.26`; an earlier Rider report reported `react-native@0.88.1`. All reports retain the same 34-record inventory and sole GHSA root. The allow-list now contains only these three exact major-only hints for either known app identity. A fourth/unreviewed hint, non-major suggestion, unknown app, critical/unknown advisory or bundle inclusion fails. No SDK upgrade is performed and no picomatch patch is claimed. The deterministic audit suite now has 28 cases.
