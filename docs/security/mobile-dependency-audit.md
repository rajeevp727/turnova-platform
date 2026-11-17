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

The verifier now allows only these two exact app-specific objects and rejects all other values. Deterministic tests cover the newly observed Expo suggestion, an unreviewed remediation version, a non-major suggestion and boolean/unknown metadata. The advisory remains unpatched and disclosed. Fresh exact-head CI must rerun both apps' audits, bundles, native builds and emulator smoke before PR #185 can merge.
